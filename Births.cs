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
        Nudge(0.02);
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
        Say(Pick(new[] { $"A baby is born in {Name}.", "A new baby cries in the night.", "A family welcomes a child." }));
    }

    // Wedding and birth dice, rolled with the other events; children grow up when their day is over.
    void Population()
    {
        if (Odds(0.12 * (0.5 + Mood))) Wedding();
        if (Weddings > 0 && Odds(1.6)) Birth();
        foreach (var w in Walkers) if (w.Kid != 0 && TickNo >= w.Kid) w.Kid = 0;
    }
}
