namespace Dotburg;

// The dot-men: where they live, where they go, and when they sleep.
partial class Sim
{
    public Walker NewWalker(int c, int home)
    {
        var w = new Walker { C = c, H = home, X = -1, Y = 0 };
        SetId(w, NextId++);
        Christen(w);
        return w;
    }

    // Give a dot-man their Id and the habits that come from it, so they are the same person after a reload:
    // walking pace, night owl or not, which side of the road they keep to.
    public static void SetId(Walker w, int id)
    {
        w.Id = id;
        w.Period = Trait01(id, 1) < 0.5 ? 2 : 3;
        w.Owl = Trait01(id, 2) < 0.15;
        w.Lane = Trait01(id, 3) < 0.5 ? 0 : 1;
    }

    // A number 0..1 fixed for this Id and this question (salt): the same every time it is asked.
    // Draws nothing from Rng, so it never shifts the random stream.
    public static double Trait01(int id, int salt)
    {
        uint x = unchecked((uint)id * 0x9E3779B1u ^ (uint)salt * 0x85EBCA77u);
        x ^= x >> 16; x = unchecked(x * 0x7FEB352Du);
        x ^= x >> 15; x = unchecked(x * 0x846CA68Bu);
        x ^= x >> 16;
        return x / 4294967296.0;
    }

    // The lot of a dot-man's home (falls back to the first visible home).
    public int HomeOf(Walker w)
    {
        int v = Math.Min(Built.Count, Lots.Count);
        if (w.Lead && SeatRank() >= 0) return SeatRank(); // the leader lives at the seat of government
        if (w.H < v && Content.IsHome(Built[w.H])) return w.H;
        for (int i = 0; i < v; i++) if (Content.IsHome(Built[i])) return i;
        return -1;
    }

    // Put every dot-man inside their home (after a load or a rebuild).
    void PlaceAll()
    {
        if (Lots.Count == 0) return;
        bool night = Daylight() < 0.25;
        foreach (var w in Walkers)
        {
            int home = HomeOf(w);
            w.At = home;
            w.Path.Clear();
            w.Mode = Mode.Inside;
            w.Sleep = night;
            w.Until = TickNo + RandInt(20, 200);
            (w.X, w.Y) = home >= 0 ? DoorOf(home) : Entrance();
        }
    }

    // A road route between two points that are both on a horizontal road row.
    List<(int x, int y)> Route(int x1, int y1, int x2, int y2, int lane)
    {
        var pts = new List<(int, int)>();
        if (y1 == y2) { pts.Add((x2, y2)); return pts; }
        int bestX = lane, best = 1_000_000_000;
        for (int i = 0; i <= Cols; i++)
        {
            int X = i * TileW + lane;
            int cost = Math.Abs(x1 - X) + Math.Abs(X - x2);
            if (cost < best) { best = cost; bestX = X; }
        }
        if (x1 != bestX) pts.Add((bestX, y1));
        pts.Add((bestX, y2));
        pts.Add((x2, y2));
        return pts;
    }

    void GoTo(Walker w, int x2, int y2, Goal goal, int dest)
    {
        if (w.InLine >= 0) LeaveLine(w);
        w.Path = Route(w.X, w.Y, x2, y2, w.Lane);
        w.Goal = goal;
        w.Dest = dest;
        w.Mode = Mode.Walk;
    }

    void GoHome(Walker w)
    {
        int home = HomeOf(w);
        if (home < 0 || Burning(home)) { StrollSomewhere(w); return; }
        if (w.At == home) { w.Mode = Mode.Inside; w.Sleep = true; return; }
        var (dx, dy) = DoorOf(home);
        GoTo(w, dx, dy, Goal.Home, home);
    }

    void StrollSomewhere(Walker w) => GoTo(w, RandInt(0, Cols) * TileW, RandInt(0, RowsT) * TileH, Goal.Stroll, -1);

    void Visit(Walker w, int rank)
    {
        var (dx, dy) = DoorOf(rank);
        GoTo(w, dx, dy, Goal.Visit, rank);
    }

    // Choose what to do next.
    void Decide(Walker w, double d)
    {
        int v = Math.Min(Built.Count, Lots.Count);
        var places = new List<int>();
        var social = new List<int>();
        // Beating a fire: stay at the blaze while it burns.
        if (w.Goal == Goal.Fight && w.Dest >= 0 && Burning(w.Dest)) { w.Until = TickNo + 20; return; }
        // Still in a queue when patience ran out: give up on that counter for a while.
        if (w.InLine >= 0) { LeaveLine(w); w.NoFoodUntil = TickNo + Secs(15, 30); lastQueueTick = TickNo; CountTurned(); }
        // Their time has come: home to die there (or here, if this is home or there is none).
        if (w.Fading != "") { int h = HomeOf(w); if (h < 0 || w.At == h || Burning(h)) PassAway(w, w.Fading); else GoHome(w); return; }
        if (w.Sick || w.Hurt) { GoHome(w); return; } // the sick and the wounded go home to bed
        if (w.Bag > 0) { GoHome(w); return; }        // shopping goes straight home
        // A carrier with a full sack finishes the delivery; one whose trip was interrupted gives the job up.
        if (w.Good >= 0) { if (w.Load > 0) { Deliver(w); return; } if (w.Goal != Goal.Haul) EndJob(w); }
        bool curfew = G.Curfew;
        bool dark = d < 0.25 || (curfew && d < 0.4);
        if (GoEat(w, dark)) return; // the hungry look for food before anything else
        if (Festival && Chance() < 0.75) { JoinFestival(w); return; }
        if (!dark && !Stormy && GoToCouncil(w)) return;   // the council meets once a day (Council.cs)
        if (KeepBuilding(w, dark)) return;   // a builder at a scaffold carries on (Builders.cs)
        if (MarketOn && !dark && Chance() < 0.4) { JoinMarket(w); return; }   // market day (0.85)
        if (w.Kid != 0 && JoinBrigade(w, d)) return;
        bool storm = Stormy;
        for (int i = 0; i < v; i++)
        {
            if (i == w.At || Built[i] == "ruin" || Burning(i)) continue;
            if (storm && Content.Outdoor.Contains(Built[i])) continue; // nobody sits in the park in the rain
            if (!Content.IsHome(Built[i])) places.Add(i);
            if (Content.Social.Contains(Built[i])) social.Add(i);
        }
        // At night owls may go out where there is company; everyone else goes home.
        if (dark)
        {
            if (!curfew && w.Owl && social.Count > 0 && Chance() < 0.5) { Visit(w, Pick(social)); return; }
            GoHome(w);
            return;
        }
        if (storm && Chance() < 0.35) { GoHome(w); return; }
        // Work first: lend a hand at a building site, or carry food along the chain (not in a storm).
        if (!storm && JoinWorks(w)) return;
        if (!storm && Chance() < 0.6 && TakeJob(w)) return;
        double r = Chance();
        if (r < 0.7 && places.Count > 0) { Visit(w, Pick(places)); return; }
        if (r < 0.85 && v > 1)
        {
            var homes = new List<int>();
            for (int i = 0; i < v; i++) if (Content.IsHome(Built[i]) && i != w.At) homes.Add(i);
            if (homes.Count > 0) { Visit(w, Pick(homes)); return; }
        }
        if (storm) { GoHome(w); return; }
        StrollSomewhere(w);
    }

    // What a dot-man does on reaching the end of the route.
    void Arrive(Walker w, double d)
    {
        double linger = Stormy ? 1.8 : 1; // bad weather keeps people indoors longer
        // A building that caught fire on the way is not entered.
        if ((w.Goal == Goal.Home || w.Goal == Goal.Visit) && w.Dest >= 0 && Burning(w.Dest)) { w.At = -1; w.Mode = Mode.Wait; w.Until = TickNo + Secs(1, 3); return; }
        if (w.Held) { w.Mode = Mode.Wait; w.Until = long.MaxValue; return; }   // arrested: waits here for the guard
        if (w.Goal == Goal.Haul) { ArriveHaul(w); return; }
        if (w.Goal == Goal.Eat) { ArriveEat(w); return; }
        if (w.Goal == Goal.Home)
        {
            w.At = w.Dest;
            // The shopping they carried goes into the pantry now.
            if (w.Bag > 0 && w.Dest >= 0 && w.Dest < Visible) Stock[w.Dest, (int)Good.Meals] = Math.Min(RoomFor(w.Dest, Good.Meals), Stock[w.Dest, (int)Good.Meals] + w.Bag);
            w.Bag = 0;
            if (w.Fading != "") { PassAway(w, w.Fading); return; }
            SupperAtHome(w, w.Dest);
            w.Mode = Mode.Inside;
            w.Sleep = d < 0.25;
            w.Until = TickNo + Round(Secs(2, 8) * linger);
        }
        else if (w.Goal == Goal.Fest)
        {
            w.At = -1;
            w.Mode = Mode.Wait;
            w.Until = TickNo + Secs(3, 8); // then mingle: Decide likely sends them to another spot
        }
        else if (w.Goal == Goal.Visit)
        {
            w.At = w.Dest;
            if (Content.Outdoor.Contains(Built[w.Dest]))
            {
                w.Mode = Mode.Wait;
                w.Until = TickNo + Secs(5, 10);
            }
            else
            {
                w.Mode = Mode.Inside;
                w.Until = TickNo + Round((d < 0.25 ? Secs(20, 40) : Secs(4, 12)) * linger);
            }
        }
        else if (w.Goal == Goal.Leave || w.Goal == Goal.Taken)
        {
            // Out of the west gate: an emigrant joins a sister town; the taken are simply gone.
            w.Gone = true;
            var sis = SisterNamed(w.To);
            if (w.Goal == Goal.Leave && sis != null) sis.Counts[w.C]++;
        }
        else
        {
            w.At = -1;
            w.Mode = Mode.Wait;
            w.Until = TickNo + Secs(1, 3);
        }
    }

    // Take one step along the route; true once the route is used up.
    static bool WalkStep(Walker w)
    {
        while (w.Path.Count > 0 && w.X == w.Path[0].x && w.Y == w.Path[0].y) w.Path.RemoveAt(0);
        if (w.Path.Count == 0) return true;
        var p = w.Path[0];
        if (w.X < p.x) w.X++;
        else if (w.X > p.x) w.X--;
        else if (w.Y < p.y) w.Y++;
        else if (w.Y > p.y) w.Y--;
        return false;
    }

    void StepWalker(Walker w, double d)
    {
        if (w.Gone || w.Follow != null) return;   // a child in the youth brigade is moved by StepBrigade
        if (w.Mode == Mode.Inside)
        {
            if (w.Sleep)
            {
                if (d > 0.32) { w.Sleep = false; w.Until = TickNo + RandInt(0, 300); }
                return;
            }
            if ((w.Sick || w.Hurt) && w.At == HomeOf(w)) return; // in bed until better
            if (TickNo >= w.Until)
            {
                if (w.At >= 0 && w.At < Lots.Count) (w.X, w.Y) = DoorOf(w.At);
                w.Mode = Mode.Wait;
                w.Until = TickNo + Secs(0.4, 1.5);
            }
            return;
        }
        if (w.Mode == Mode.Wait)
        {
            if (TickNo >= w.Until) Decide(w, d);
            return;
        }
        if (TickNo % (w.Food < WeakAt ? w.Period + 1 : w.Period) != 0) return; // the starving walk slowly
        if (WalkStep(w)) Arrive(w, d);
    }
}
