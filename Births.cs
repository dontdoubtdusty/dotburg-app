namespace Dotburg;

// Weddings and births. A wedding is owed a baby; babies cost no growth units and are extra to the cap.
partial class Sim
{
    const int MaxExtraKids = 6;      // children allowed beyond the population cap

    // Two neighbours who are free marry, at the church if there is one; a birth follows.
    void Wedding()
    {
        var ps = Walkers.Where(w => !w.Gone && w.Kind == "" && w.Kid == 0 && !w.Sick && !w.Lead && w.Mode == Mode.Wait).ToList();
        if (ps.Count < 2) return;
        int church = Built.FindIndex(t => t == "church" || t == "cathedral");
        var a = Pick(ps);
        var b = Pick(ps.Where(x => x != a).ToList());
        if (church >= 0 && church < Lots.Count) { Visit(a, church); Visit(b, church); }
        Weddings = Math.Min(3, Weddings + 1);
        Nudge(0.02, "a wedding");
        Say(church >= 0 ? "Wedding bells at the church." : "Two neighbours marry.");
    }

    // A baby is born into a parent's home, of a parent's colour, and counts as grown after a day.
    void Birth()
    {
        var adults = Walkers.Where(x => !x.Gone && x.Kind == "" && x.Kid == 0).ToList();
        if (adults.Count < 2 || Lots.Count == 0) return;
        int kids = Walkers.Count(x => !x.Gone && x.Kid != 0);
        if (kids >= MaxExtraKids || Headcount + kids >= PopCap + MaxExtraKids) return;
        Weddings = Math.Max(0, Weddings - 1);
        var parent = Pick(adults);
        int home = HomeOf(parent);
        var w = NewWalker(Chance() < 0.5 ? parent.C : Pick(adults).C, home >= 0 ? parent.H : Math.Max(0, EmptiestHome()));
        w.Kid = TickNo + Round(DayMs / TickMs);
        Walkers.Add(w);
        int hr = HomeOf(w);
        w.At = hr;
        (w.X, w.Y) = hr >= 0 ? DoorOf(hr) : Entrance();
        w.Mode = Mode.Inside;
        w.Until = TickNo + Secs(5, 20);
        if (Chance() < 0.05) Weddings++; // twins: another baby is due
        if (G.Curfew && Chance() < 0.4)
            Say(Pick(new[] { "Another curfew baby. The midwife is run off her feet.", "A baby is born nine months after the curfew came in. Nobody is surprised.", "Curfew babies, people call them. Another one today." }));
        else Say(Pick(new[] { $"A baby is born in {Name}.", "A new baby cries in the night.", "A family welcomes a dotlet." }));
    }

    // Wedding and birth dice, rolled with the other events; children grow up when their day is over.
    void Population()
    {
        // Curfew babies: with everyone shut in at dusk, a curfew town marries half again as often.
        if (Odds(0.12 * (0.5 + Mood) * (G.Curfew ? 1.5 : 1) * (FoodDays < 3 ? 0.3 : 1))) Wedding(); // few weddings when the larder is thin
        if (Weddings > 0 && Odds(1.6)) Birth();
        foreach (var w in Walkers) if (w.Kid != 0 && TickNo >= w.Kid) GrowUp(w);
    }
}

// The youth brigade: under a rule with patrols (junta, party state), children out in the day fall in behind a
// guard and march the roads with them; at curfew the guard walks them home to their door.
partial class Sim
{
    const int BrigadeMax = 2;       // children behind one guard
    long brigadeSaid = -1, escortSaid = -1;

    // A child deciding what to do may join a patrol instead. True if they did.
    bool JoinBrigade(Walker w, double d)
    {
        if (G.Guards == 0 || d < 0.4 || Chance() > 0.4) return false;
        var free = guards.Where(g => !g.Gone && g.Goal == Goal.Stroll && Walkers.Count(k => k.Follow == g) < BrigadeMax).ToList();
        if (free.Count == 0) return false;
        var guard = free.OrderBy(g => Math.Abs(g.X - w.X) + Math.Abs(g.Y - w.Y)).First();
        w.Follow = guard;
        w.At = -1;
        w.Mode = Mode.Walk;
        w.Goal = Goal.Stroll;
        w.Path.Clear();
        if (DayNumber != brigadeSaid)
        {
            brigadeSaid = DayNumber;
            Say(Pick(new[] { "Dotlets fall in behind the patrol. The youth brigade, they call it.", "A dotlet marches behind the guards, chin up.", "The youth brigade drills on the square." }));
        }
        return true;
    }

    // Moves each child in the brigade one step behind their guard, and sends them home at curfew.
    void StepBrigade(double d)
    {
        foreach (var w in Walkers)
        {
            var g = w.Follow;
            if (g == null) continue;
            int home = HomeOf(w);
            bool taken = w.Goal == Goal.Taken || w.Goal == Goal.Leave;
            if (w.Gone || taken || g.Gone || g.Goal == Goal.Taken || w.Kid == 0 || w.Sick || w.Hurt || Festival || G.Guards == 0 || home < 0)
            {
                Release(w, g);
                if (!w.Gone && !taken) { w.Mode = Mode.Wait; w.Until = TickNo + Secs(1, 3); }
                continue;
            }
            var (hx, hy) = DoorOf(home);
            if (d < (G.Curfew ? 0.4 : 0.25))
            {
                // Curfew: the guard turns for the child's door; once there, the child goes in.
                if (g.Goal != Goal.Home) GoTo(g, hx, hy, Goal.Home, home);
                else if (g.Mode == Mode.Wait && g.Dest == home && Math.Abs(w.X - hx) + Math.Abs(w.Y - hy) <= 3)
                {
                    Release(w, g);
                    w.At = home;
                    (w.X, w.Y) = (hx, hy);
                    w.Mode = Mode.Inside;
                    w.Goal = Goal.Home;
                    w.Sleep = d < 0.25;
                    w.Until = TickNo + Secs(4, 10);
                    if (DayNumber != escortSaid) { escortSaid = DayNumber; Say("A guard walks a dotlet home before curfew."); }
                    continue;
                }
            }
            // Keep up: walk to the guard if left behind, else step into the guard's last footprint.
            int far = Math.Abs(w.X - g.X) + Math.Abs(w.Y - g.Y);
            if (far > 2) { w.Path = Route(w.X, w.Y, g.X, g.Y, w.Lane); WalkStep(w); }
            else if (far > 0) (w.X, w.Y) = (g.PX, g.PY);
        }
    }

    // A child leaves the brigade; a guard who was walking them home goes back to patrolling.
    void Release(Walker w, Walker g)
    {
        w.Follow = null;
        if (g.Goal == Goal.Home && !Walkers.Any(k => k.Follow == g)) { g.Goal = Goal.Stroll; if (g.Mode != Mode.Wait) { g.Mode = Mode.Wait; g.Until = TickNo + Secs(1, 3); } }
    }
}

partial class Sim
{
    // --brigade: a party state at mid-morning with three children born, run until they march. Returns a report line.
    public string TestBrigade()
    {
        ForceGov("party");
        Age = Math.Floor(Age / DayMs) * DayMs + DayMs * 0.42;
        for (int i = 0; i < 3; i++) { Weddings++; Birth(); }
        foreach (var w in Walkers.Where(k => k.Kid != 0)) { w.Mode = Mode.Wait; w.Until = TickNo; }
        int most = 0;
        for (int i = 0; i < 400; i++) { Tick(TickMs); most = Math.Max(most, Walkers.Count(k => k.Follow != null)); }
        string noon = $"brigade: {Walkers.Count(k => k.Kid != 0)} children, {guards.Count} guards, {Walkers.Count(k => k.Follow != null)} marching now, {most} at most";
        // On to curfew and a little past it: everyone should be walked home.
        while (Daylight() >= 0.4) Tick(TickMs);
        for (int i = 0; i < 300; i++) Tick(TickMs);
        var kids = Walkers.Where(k => k.Kid != 0).ToList();
        return noon + $"; after curfew: {kids.Count(k => k.Follow != null)} still marching, {kids.Count(k => k.Mode == Mode.Inside && k.At == HomeOf(k))} of {kids.Count} at home, escorted {(escortSaid >= 0 ? "yes" : "no")}";
    }
}
