namespace Dotburg;

// A feud between two families (by family name, Walker.Last): how hot it runs, when and how it began. Saved.
class Feud
{
    public string A { get; set; } = "";
    public string B { get; set; } = "";
    public int Day { get; set; }
    public double Heat { get; set; } = 1;   // 0..2: cools a little each day, flares with every quarrel
    public string Why { get; set; } = "";
}

// Feuds (0.85): the other half of friends marrying. A grudge that runs deep between dot-men of two families, or an
// attempt on the leader by a named dot-man, can turn into a feud between the families, which the children carry on
// since they carry the name. Feuding families quarrel in the street (more on market day), sour each other's
// meetings, and do not make friends. How a quarrel ends follows the rule: guards haul someone away, an elected
// council or the elders hear both sides and may end it, elsewhere it comes to fists and now and then a knife.
// A wedding between the two families ends it; so does time, or the last of one name leaving town.
partial class Sim
{
    const int MaxFeuds = 3;
    const double FeudOdds = 0.5;            // a grudge past FeudAt across families becomes a feud this often (more for the vengeful)
    const double FeudAt = 0.45;             // how deep a grudge must run before it can set two families against each other
    const double FeudCool = 0.12;           // heat lost per day
    const double QuarrelsPerDay = 0.8;      // street quarrels a day per feud, at heat 1
    public List<Feud> Feuds = new();
    public int FeudsBegun, FeudsSettled, FeudsWed, FeudsCooled, FeudQuarrels, FeudStabbings, FeudTaken;   // soak counters

    // "the Hales", "the Joneses": a family by its name.
    static string Fam(string last) => "the " + (System.Text.RegularExpressions.Regex.IsMatch(last, "(s|x|z|sh|ch)$") ? last + "es" : last + "s");

    Feud? FeudOf(string a, string b) => a == "" || b == "" || a == b ? null : Feuds.FirstOrDefault(f => (f.A == a && f.B == b) || (f.A == b && f.B == a));
    bool Feuding(Walker a, Walker b) => FeudOf(a.Last, b.Last) != null;
    int FamilySize(string last) => last == "" ? 0 : Walkers.Count(w => !w.Gone && w.Kind == "" && w.Last == last && w.Goal is not (Goal.Leave or Goal.Taken));

    // A deep wrong between a (the wronged) and b may set their families against each other. Force: always, if it can.
    void MaybeFeud(Walker a, Walker b, string why, bool force = false)
    {
        if (a.Last == "" || b.Last == "" || a.Last == b.Last || a.Spouse == b.Id || Feuds.Count >= MaxFeuds || FeudOf(a.Last, b.Last) != null) return;
        // Two lone dot-men have a grudge, not a feud: one side at least needs kin (dotlets count). After an attempt on
        // the leader (force), anyone left on each side will do.
        int na = FamilySize(a.Last), nb = FamilySize(b.Last);
        if (na == 0 || nb == 0 || (!force && na + nb < 3)) return;
        if (!force && Chance() >= FeudOdds * (Is(a, "vengeful") || Is(a, "spiteful") ? 2 : 1)) return;
        Feuds.Add(new Feud { A = a.Last, B = b.Last, Day = DayNumber, Heat = 1, Why = why });
        FeudsBegun++;
        string lead = LeaderWalker is { } lw && (lw.Last == a.Last || lw.Last == b.Last) ? $" The leader is one of {Fam(lw.Last)}." : "";
        Say($"Bad blood between {Fam(a.Last)} and {Fam(b.Last)} since {why}: it is a feud now.{lead}");
    }

    // Rolled with the events: feuds cool, end when a family is gone, and flare into quarrels in the street.
    void StepFeuds()
    {
        foreach (var f in Feuds.ToList())
        {
            if (FamilySize(f.A) == 0 || FamilySize(f.B) == 0)
            {
                Feuds.Remove(f);
                string left = FamilySize(f.A) == 0 ? f.A : f.B;
                Say($"With the last of {Fam(left)} gone, their feud with {Fam(left == f.A ? f.B : f.A)} is over.", false);
                continue;
            }
            f.Heat -= FeudCool * CheckDays;
            if (f.Heat <= 0)
            {
                Feuds.Remove(f);
                FeudsCooled++;
                Say($"{Cap(Fam(f.A))} and {Fam(f.B)} let their old feud rest.");
                continue;
            }
            if (Odds(QuarrelsPerDay * f.Heat * (MarketOn ? 2 : 1))) Quarrel(f);
        }
    }

    // Two of the feuding families meet in the street; what happens depends on who rules.
    void Quarrel(Feud f)
    {
        bool Free(Walker w) => !w.Gone && w.Kind == "" && !w.Lead && !w.Sick && !w.Hurt && !w.Sleep && w.Fading == ""
                               && w.Goal is not (Goal.Leave or Goal.Taken or Goal.Fight) && w.Follow == null;
        var sideA = Walkers.Where(w => w.Last == f.A && Free(w)).ToList();
        var sideB = Walkers.Where(w => w.Last == f.B && Free(w)).ToList();
        if (sideA.Count == 0 || sideB.Count == 0 || Lots.Count == 0) return;
        // The hot-headed are first into it; grown dot-men before dotlets.
        double Keen(Walker w) => (Is(w, "hotheaded") ? 3 : Is(w, "calm") ? 0.3 : 1) * (w.Kid != 0 ? 0.3 : 1);
        var a = Weighted(sideA, Keen);
        var b = Weighted(sideB, Keen);
        FeudQuarrels++;
        f.Heat = Math.Min(2, f.Heat + 0.3);
        // They meet at the market stalls on market day, else in the street outside a's door.
        foreach (var w in new[] { a, b }) if (w.Mode == Mode.Inside && w.At >= 0 && w.At < Lots.Count) (w.X, w.Y) = DoorOf(w.At);
        var (x, y) = MarketOn ? MarketSpot() : (a.X, a.Y);
        GoTo(a, x, y, Goal.Stroll, -1);
        GoTo(b, Clamp(x + 1, 0, Cols * TileW + 1), y, Goal.Stroll, -1);
        string an = CitizenName(a), bn = CitizenName(b), feud = $"{Fam(f.A)} and {Fam(f.B)}";
        string where = MarketOn ? " at the market" : " in the street";
        // The young carry it on in their own way: mud and names, nothing the guards or the knife come into.
        if (a.Kid != 0 || b.Kid != 0)
        {
            string young = a.Kid != 0 && b.Kid != 0 ? $"Dotlets of {feud} throw mud at each other{where}." : $"{(a.Kid != 0 ? bn : an)} chases off a dotlet of {Fam(a.Kid != 0 ? f.A : f.B)}{where}.";
            AddBond(a, b, "grudge", 0.1 * GrudgeGain(a), $"{bn} threw mud at them{where} when they were dotlets");
            AddBond(b, a, "grudge", 0.1 * GrudgeGain(b), $"{an} threw mud at them{where} when they were dotlets");
            Say(young, false);
            return;
        }
        AddBond(a, b, "grudge", 0.08 * GrudgeGain(a), $"{bn} cursed their family{where}");
        AddBond(b, a, "grudge", 0.08 * GrudgeGain(b), $"{an} cursed their family{where}");
        var g = G;
        if (g.Harsh >= 0.6 && g.Guards > 0)
        {
            Nudge(-0.02, "guards broke up a feud");
            if (Chance() < 0.4 && Walkers.Count(w => !w.Gone && w.Kind == "") > MinPop + 2)
            {
                // The guards know which side the leader's family is on.
                var lw = LeaderWalker;
                var taken = lw != null && lw.Last == a.Last ? b : lw != null && lw.Last == b.Last ? a : Chance() < 0.5 ? a : b;
                FeudTaken++;
                TakenNews(taken);
                Arrest(taken, true);
                Say($"Guards haul {CitizenName(taken)} away after a brawl{where}: the feud between {feud}.");
                return;
            }
            Say($"Guards break up a quarrel between {an} and {bn}{where} and fine both families.");
            Pay(0.5);
            return;
        }
        // Rules that sit in judgement: an elected council or mayor, a commune, the elders, the shrine keepers.
        if (GovKey is "council" or "mayor" or "commune" or "elders" or "shrine")
        {
            bool calm = Is(a, "calm") || Is(b, "calm") || Is(a, "humble") || Is(b, "humble");
            if (Chance() < (calm ? 0.6 : 0.35))
            {
                Feuds.Remove(f);
                FeudsSettled++;
                foreach (var (p, q) in new[] { (a, b), (b, a) }) if (BondOf(p, q.Id, "grudge") is { } gb) gb.Str = Math.Max(0.03, gb.Str - 0.4);
                Nudge(0.02, "a feud settled");
                Say($"{Judges(GovKey)} hear{(GovKey is "mayor" or "commune" ? "s" : "")} {feud} out; {an} and {bn} shake hands, and the feud is over.");
                return;
            }
            Say(Pick(new[] { $"{an} and {bn} trade words{where}. {Judges(GovKey)} will hear the feud between {feud}.", $"{an} will not let {bn} pass{where}; the feud between {feud} goes on.", $"{an} and {bn} have to be pulled apart{where}. {Judges(GovKey)} will hear them yet." }), false);
            return;
        }
        // Everywhere else it comes to blows, and now and then to a knife.
        double roll = Chance();
        if (roll < 0.05 && BodyOf(b) == null && Walkers.Count(w => !w.Gone && w.Kind == "") > MinPop + 2)
        {
            FeudStabbings++;
            f.Heat = 2;
            b.Gone = true;
            Mourn(b, "stabbed in a feud");
            Nudge(-0.05, "a stabbing");
            var dest = Sisters.Count > 0 ? Pick(Sisters) : null;
            if (g.Guards > 0 || dest == null) Arrest(a, true); else SendAway(a, Goal.Leave, dest.Name);
            Say($"The feud between {feud} ends in a stabbing: {bn} is dead, and {an} {(g.Guards > 0 || dest == null ? "is led away" : $"flees to {dest.Name}")}.");
            // The dead one's family will not forget.
            foreach (var w in Walkers) if (!w.Gone && w.Last == f.B && w.Kind == "") AddBond(w, a, "grudge", 0.5 * GrudgeGain(w), $"{an} stabbed their kin {bn}");
            return;
        }
        if (roll < 0.3)
        {
            Nudge(-0.02, "a brawl in the street");
            Say($"Fists fly between {an} and {bn}{where}: {feud} again.");
            return;
        }
        Say($"{an} and {bn} shout at each other{where}; {feud} will not let it lie.", false);
    }
    // Who sits in judgement over a quarrel under a rule that hears both sides.
    static string Judges(string gov) => gov switch { "elders" => "The elders", "shrine" => "The shrine keepers", "commune" => "The commune", "mayor" => "The mayor", _ => "The council" };

    // A wedding across a feud ends it.
    void WeddingEndsFeud(Walker a, Walker b)
    {
        var f = FeudOf(a.Last, b.Last);
        if (f == null) return;
        Feuds.Remove(f);
        FeudsWed++;
        Nudge(0.03, "a wedding ended a feud");
        Say($"The wedding of {CitizenName(a)} and {CitizenName(b)} ends the feud between {Fam(f.A)} and {Fam(f.B)}.");
    }

    // A new town (a fresh region or a charter) starts without the old town's feuds, market day or floodwater.
    // The comet is in the region's sky and stays.
    void ResetTownEvents()
    {
        Feuds.Clear();
        marketUntil = 0;
        lastMarket = -1e12;
        flood = 0;
        floodVer = -1;
    }

    // After a load: keep only feuds whose families still have someone in town.
    public void RestoreFeuds(List<Feud>? saved)
    {
        Feuds = (saved ?? new()).Where(f => f.A != "" && f.B != "" && f.A != f.B && FamilySize(f.A) > 0 && FamilySize(f.B) > 0).Take(MaxFeuds).ToList();
        foreach (var f in Feuds) f.Heat = Clamp(f.Heat, 0.05, 2);
    }
}
