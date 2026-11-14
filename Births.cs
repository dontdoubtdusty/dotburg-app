namespace Dotburg;

// Weddings and births. A wedding is owed a baby; babies cost no growth units and are extra to the cap.
partial class Sim
{
    const int MaxExtraKids = 6;      // children allowed beyond the population cap

    const double WedFriendsAt = 0.25;   // a friendship this strong (either way) can end in a wedding
    const double MatchShare = 0.4;      // with no friends free to marry, how often a neighbour makes a match instead
    public int FriendWeddings, ArrangedWeddings, MatchWeddings;   // soak counters

    // Free to marry: a grown citizen in town, well, not leading, and unwed or widowed.
    bool FreeToWed(Walker w) => !w.Gone && w.Kind == "" && w.Kid == 0 && !w.Sick && !w.Hurt && !w.Lead
                                && w.Goal is not (Goal.Leave or Goal.Taken) && SpouseOf(w) == null;
    public Walker? SpouseOf(Walker w) => w.Spouse == 0 ? null : ById(w.Spouse);
    bool Grudge(Walker a, Walker b) => BondOf(a, b.Id, "grudge") != null || BondOf(b, a.Id, "grudge") != null;

    // A wedding (0.78): friends marry, the closer the likelier. Under a strict rule (temple, crown, junta, party)
    // many matches are arranged instead, friends or not. With no friends free, a neighbour sometimes makes a match.
    // The couple share a home if one has room, their friendship becomes a bond that does not fade, and a birth follows.
    void Wedding()
    {
        var free = Walkers.Where(FreeToWed).ToList();
        if (free.Count < 2) return;
        var pairs = new List<(Walker a, Walker b, Bond bond)>();
        foreach (var a in free)
            foreach (var bd in a.Bonds)
                if (bd.Kind == "friend" && bd.Str >= WedFriendsAt && free.FirstOrDefault(x => x.Id == bd.Who) is { } b && !Grudge(a, b))
                    pairs.Add((a, b, bd));
        Walker x1, x2;
        string kind;
        Bond? met = null;
        if (G.Harsh >= 0.5 && Chance() < G.Harsh * 0.6)
        {
            kind = "arranged";
            x1 = Pick(free);
            x2 = Pick(free.Where(x => x != x1).ToList());
        }
        else if (pairs.Count > 0)
        {
            kind = "friends";
            var p = Weighted(pairs, q => q.bond.Str);
            (x1, x2, met) = p;
        }
        else if (Chance() < MatchShare)
        {
            kind = "match";
            x1 = Pick(free);
            var ok = free.Where(x => x != x1 && !Grudge(x1, x)).ToList();
            if (ok.Count == 0) return;
            x2 = Pick(ok);
        }
        else return;
        Marry(x1, x2, kind, met);
    }

    // Two dot-men wed: they walk to the church if there is one, share a home if they can, and a birth is owed.
    void Marry(Walker a, Walker b, string kind, Bond? met)
    {
        int church = Built.FindIndex(t => t == "church" || t == "cathedral");
        if (church >= 0 && church < Lots.Count) { if (!a.Sleep) Visit(a, church); if (!b.Sleep) Visit(b, church); }
        a.Spouse = b.Id;
        b.Spouse = a.Id;
        bool sour = Grudge(a, b);
        AddBond(a, b, "friend", kind == "friends" ? 0.6 : 0.3, $"they married on day {DayNumber}");
        AddBond(b, a, "friend", kind == "friends" ? 0.6 : 0.3, $"they married on day {DayNumber}");
        MoveIn(a, b);
        Weddings = Math.Min(3, Weddings + 1);
        Nudge(kind == "arranged" ? 0.01 : 0.02, "a wedding");
        string an = CitizenName(a), bn = CitizenName(b), at = church >= 0 ? " at the church" : "";
        switch (kind)
        {
            case "friends":
                FriendWeddings++;
                Say(Pick(new[] {
                    $"{an} and {bn}, friends since day {met!.Day}, marry{at}.",
                    $"Wedding bells{at}: {an} and {bn}. They have been friends since day {met!.Day}.",
                    $"{an} marries {bn}{at}. Everyone saw it coming." }));
                break;
            case "arranged":
                ArrangedWeddings++;
                Say(sour ? $"{an} and {bn} are married{at} by arrangement. Neither looks happy."
                    : Pick(new[] { $"{an} and {bn} are married{at}; the match was arranged.", $"A match is made: {an} weds {bn}{at}." }));
                break;
            default:
                MatchWeddings++;
                Say(Pick(new[] { $"{an} and {bn} marry{at}, matched by a neighbour.", $"Two neighbours, {an} and {bn}, marry{at}." }));
                break;
        }
    }

    // A newly wed couple share a home: one moves in with the other if there is room (the emptier home first).
    void MoveIn(Walker a, Walker b)
    {
        int ha = HomeOf(a), hb = HomeOf(b);
        if (ha < 0 || hb < 0 || ha == hb) return;
        int spareA = HomeCap(Built[ha]) - Residents(ha), spareB = HomeCap(Built[hb]) - Residents(hb);
        if (spareA <= 0 && spareB <= 0) return;
        if (spareA >= spareB) b.H = ha; else a.H = hb;
    }

    // A baby is born to a married couple when there is one (else to any parent), in their home, of a parent's
    // colour and with a parent's family name, and counts as grown after a day.
    void Birth()
    {
        var adults = Walkers.Where(x => !x.Gone && x.Kind == "" && x.Kid == 0).ToList();
        if (adults.Count < 2 || Lots.Count == 0) return;
        int kids = Walkers.Count(x => !x.Gone && x.Kid != 0);
        if (kids >= MaxExtraKids || Headcount + kids >= PopCap + MaxExtraKids) return;
        Weddings = Math.Max(0, Weddings - 1);
        var couples = adults.Where(x => SpouseOf(x) is { } s && x.Id < s.Id && s.Kid == 0).ToList();
        var parent = couples.Count > 0 ? Pick(couples) : Pick(adults);
        var other = SpouseOf(parent) ?? Pick(adults.Where(x => x != parent).ToList());
        int home = HomeOf(parent);
        var w = NewWalker(Chance() < 0.5 ? parent.C : other.C, home >= 0 ? parent.H : Math.Max(0, EmptiestHome()));
        Christen(w, Chance() < 0.5 || other.Last == "" ? parent.Last : other.Last);
        bornTo = SpouseOf(parent) == other ? $"{CitizenName(parent)} and {CitizenName(other)}" : "";
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
        else if (bornTo != "" && Chance() < 0.7)
            Say(Pick(new[] { $"A dotlet is born to {bornTo}: {CitizenFirst(w)}.", $"{bornTo} name their dotlet {CitizenFirst(w)}.", $"A new {w.Last} in {Name}: {CitizenFirst(w)}, born to {bornTo}." }));
        else Say(Pick(new[] { $"A baby is born in {Name}.", "A new baby cries in the night.", "A family welcomes a dotlet." }));
    }
    string bornTo = "";

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
