namespace Dotburg;

// Sister rulers (0.57.1): every sister town has a ruler, a lighter version of our own leader: a name, an age,
// two traits, an epithet after a day, a regnal number under a crown. No body or wounds; death is rolled from
// age, war, blight and the knife. The Messenger shows a sister as "title.name@town", so a new rule or a new
// ruler is plain on the contact list; deaths go to the crier and the sister's chat.
// Times are on EvClock (region time), which a charter does not reset.
partial class Sim
{
    const double SisterOldK = 0.004;   // age hazard per day at 30, rising e-fold every 10 years (a ruler of 50 lasts about 15 days)

    // A short lowercase word for addresses: letters and digits only.
    public static string Slug(string s) => new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    // The sister's ruler. Every sister gets one when made or loaded, so this never rolls anything.
    public Leader RulerOf(Sister s) => s.Lord ??= new Leader { Name = Govs.First[(s.Name.Sum(c => c) * 7 + 3) % Govs.First.Length], Traits = new[] { "able" } };

    // The sister's chat address: "mayor.ada@oakford".
    public string HandleOf(Sister s) => $"{Slug(Govs.Get(s.Gov).Title)}.{Slug(RulerOf(s).Name)}@{Slug(s.Name)}";

    public int RulerYears(Sister s) => (int)Math.Floor((EvClock - RulerOf(s).Born) / YearMs);

    // "Sovereign Ada II the Proud", like LeaderTitle.
    public string RulerTitle(Sister s)
    {
        var l = RulerOf(s);
        string ep = EvClock - l.Since >= DayMs && l.Traits.Length > 0 && Traits.Exists(l.Traits[0]) ? " the " + Traits.Get(l.Traits[0]).Name : "";
        return $"{Govs.Get(s.Gov).Title} {l.Name}{(s.Gov == "crown" ? " " + Govs.RomanOf(l.Gen) : "")}{ep}";
    }

    // One line for tooltips: who rules, how old, their character, how long.
    public string RulerLine(Sister s)
    {
        var l = RulerOf(s);
        int days = (int)Math.Floor((EvClock - l.Since) / DayMs);
        string tr = string.Join(", ", l.Traits.Where(Traits.Exists).Select(t => Traits.Get(t).Name.ToLower()));
        return $"{RulerTitle(s)}, {RulerYears(s)}, {tr}; {(days < 1 ? "new to office" : days == 1 ? "in office 1 day" : $"in office {days} days")}";
    }

    // A new ruler for a sister. An heir (a crown passing on) is younger, keeps the number going and often a parent's trait.
    void NewRuler(Sister s, bool heir = false)
    {
        var old = s.Lord;
        double harsh = Govs.Get(s.Gov).Harsh;
        s.Lord = new Leader
        {
            Name = Pick(Govs.First.Where(n => n != old?.Name).ToList()),
            Gen = heir && old != null ? old.Gen + 1 : 1,
            Since = EvClock,
            Born = EvClock - StartAge(s.Gov, heir) * YearMs,
            Traits = RollTraits(heir ? old?.Traits : null, harsh),
        };
    }

    // How a sister's ruler shades her feelings toward everyone, us included.
    static readonly Dictionary<string, double> Warmth = new()
    {
        ["merciful"] = 0.08, ["tolerant"] = 0.08, ["honest"] = 0.04, ["cruel"] = -0.1, ["proud"] = -0.05, ["cunning"] = -0.03,
        ["generous"] = 0.06, ["humble"] = 0.04, ["charmer"] = 0.1, ["cheerful"] = 0.04, ["calm"] = 0.03, ["spiteful"] = -0.06,
        ["vengeful"] = -0.08, ["jealous"] = -0.04, ["greedy"] = -0.05, ["hotheaded"] = -0.05, ["ambitious"] = -0.03,
    };
    IEnumerable<Trait> RulerTraits(Sister s) => RulerOf(s).Traits.Where(Traits.Exists).Select(Traits.Get);

    // Rolled once per sister with the region: age, illness, war, blight and the knife under harsh rule.
    void StepRuler(Sister s)
    {
        bool war = WarOn?.Foe == s.Name || (SisterWarOn != null && (SisterWarOn.A == s.Name || SisterWarOn.B == s.Name));
        bool hit = BlightOn?.Towns.Contains(s.Name) == true;
        double harsh = Govs.Get(s.Gov).Harsh;
        double kill = RulerTraits(s).Aggregate(1.0, (a, t) => a * t.Kill);
        double care = Math.Clamp(RulerTraits(s).Sum(t => t.Care), -0.2, 0.3);
        var causes = new List<(string key, double rate)>
        {
            ("age", SisterOldK * AgeRate * Math.Exp((RulerYears(s) - 30) / 10.0) * (1 - care)),
            ("fever", 1 / 90.0 * (1 - care)),
            ("fall", (RulerYears(s) >= 65 ? 1 / 40.0 : 1 / 200.0) * AgeRate),
            ("battle", war ? 1 / 10.0 : 0),
            (BlightOn?.Kind == "plague" ? "plague" : "hunger", hit ? (BlightOn!.Kind == "plague" ? 1 / 6.0 : 1 / 12.0) * (1 - care) : 0),
            ("knife", harsh >= 0.5 ? harsh / 25.0 * kill : 0),
        };
        if (!Odds(causes.Sum(c => c.rate))) return;
        RulerDies(s, Weighted(causes.Where(c => c.rate > 0).ToList(), c => c.rate).key);
    }

    static readonly Dictionary<string, string> DeathWords = new()
    {
        ["age"] = "dies of old age", ["fever"] = "dies of a fever", ["fall"] = "dies after a fall",
        ["battle"] = "falls in battle", ["plague"] = "dies of the plague", ["hunger"] = "dies in the famine",
        ["knife"] = "is assassinated",
    };

    // For the soak: is this crier line a sister ruler's death?
    public static bool RulerDeathLine(string l) => l.StartsWith("News from") && DeathWords.Values.Any(l.Contains);

    // The ruler dies: the crier gives the news (not the chronicle: with several sisters there would be many),
    // a successor takes over, and the sister writes about it from the new ruler's address.
    void RulerDies(Sister s, string cause)
    {
        string dead = RulerTitle(s);
        int age = RulerYears(s), days = (int)Math.Floor((EvClock - RulerOf(s).Since) / DayMs);
        NewRuler(s, s.Gov == "crown");
        string reign = days < 1 ? "" : days == 1 ? " after a day in office" : $" after {days} days in office";
        Say($"News from {s.Name}: {dead}, {age}, {DeathWords[cause]}{reign}. {RulerOf(s).Name} {Govs.Succeed[s.Gov]}.", false);
        Tell(s, "mourn." + cause, false, dead);
    }

    // People a sister lost to war or blight, for the line that ends it: "Oakford buried 3".
    static string Buried(Dictionary<string, int> dead) =>
        string.Join(", ", dead.Where(kv => kv.Value > 0).Select(kv => $"{kv.Key} buried {kv.Value}"));

    // Take up to n people out of a sister (war, blight), never below floor; returns how many went.
    int Bury(Sister s, int n, int floor)
    {
        int k = 0;
        for (; k < n && s.Total > floor; k++) TakeFrom(s);
        return k;
    }
}
