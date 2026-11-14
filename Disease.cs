namespace Dotburg;

// One illness. While someone is sick, severity and immunity both climb (per day of town time):
// immunity reaching 1 first means they recover, severity first means they die. Care slows severity.
// Spread scales the chance of passing it to someone in the same building. IncubDays is how long it hides
// before showing; Water marks a sickness clean water (a well or fountain) holds back. Tint colours the sick dot.
// Where weighs it by scheme (meadow, harbour, autumn, lavender, snow, pine, desert, cherry); When by season.
record Disease(string Key, string Name, double SevDay, double ImmDay, double Spread, double IncubDays, bool Water, int Tint,
               double[] Where, double[] When, string Start);

// The illness going round the town just now.
class Outbreak
{
    public int D;           // index into Diseases
    public int Cases, Deaths;
    public bool Told;       // the crier has announced it (when the first case shows)
    public bool Sealed;     // quarantine: guards keep the sick houses shut
}

// Stage 5 (part): illness. Only lasting results are saved (the dead stay dead); an outbreak in progress is not.
partial class Sim
{
    const int DisEvery = 30;               // ticks between illness steps (about 3 s)
    const double OutbreakEvery = 5;        // days between outbreaks on average, before crowding
    const double SpreadK = 0.04;         // per illness step, per contact, times the illness's Spread
    const double ImmuneDays = 4;           // how long beating an illness keeps it away

    public static readonly Disease[] Diseases =
    {
        //   key       name                sev   imm   spread incub water  tint      where: mea  har  aut  lav  sno  pin  des  che      when: spr  sum  aut  win
        new("marsh",  "marsh fever",       0.5,  0.75, 0.5, 0.3,  true,  0x9ab050, new[] { 1.0, 1.8, 1.0, 1.0, 0.3, 1.4, 0.2, 1.0 }, new[] { 1.0, 1.6, 1.0, 0.3 },
            "A fever rises from the wet ground in {0}."),
        new("cough",  "winter cough",      0.4,  0.75, 0.45,  0.2,  false, 0xa0b0b8, new[] { 1.0, 1.2, 1.0, 1.0, 1.8, 1.3, 0.4, 1.0 }, new[] { 0.6, 0.1, 1.0, 2.2 },
            "A cough goes round {0}."),
        new("sweat",  "sweating sickness", 0.6,  0.8,  0.4,   0.25, false, 0xd0a060, new[] { 1.0, 1.0, 1.0, 1.0, 0.4, 0.8, 1.6, 1.2 }, new[] { 1.0, 1.8, 0.8, 0.2 },
            "The sweating sickness comes to {0}."),
        new("dust",   "dust lung",         0.33, 0.4, 0.15,  0.5,  false, 0xc0a080, new[] { 0.3, 0.2, 0.5, 0.4, 0.2, 0.3, 2.5, 0.4 }, new[] { 1.0, 1.4, 1.0, 0.6 },
            "Dust lung settles in {0}."),
        new("plague", "grey plague",       0.67, 0.6,  0.5,   0.4,  false, 0x707870, new[] { 0.2, 0.4, 0.2, 0.2, 0.2, 0.2, 0.25, 0.2 }, new[] { 1.0, 1.0, 1.0, 1.0 },
            "Plague! The grey sickness reaches {0}."),
    };

    public Outbreak? Ill;

    static readonly string[] Victims = { "A neighbour", "An old friend", "A shopkeeper", "A grandmother", "A baker", "A young farmer", "A widower" };

    long DaysToTicks(double days) => (long)(days * DayMs / TickMs);
    bool HasWater => Built.Contains("well") || Built.Contains("fountain");

    // How well the sick are looked after: a church takes them in, a commune or council organises neighbours,
    // and the leader's character helps or hinders. 0 is no care; severity climbs that much slower.
    public double Care()
    {
        double c = 0;
        if (Built.Contains("church") || Built.Contains("cathedral")) c += 0.25;
        if (GovKey is "commune" or "council") c += 0.1;
        c += TraitSum(t => t.Care);
        return Clamp(c, 0, 0.7);
    }

    // Where a dot-man shares air with others: the building they are in or beside; -1 while walking.
    static int Room(Walker w) => w.Mode == Mode.Walk ? -1 : w.At;

    bool CanCatch(Walker w, int d) => !w.Gone && w.Dis < 0 && w.Goal != Goal.Leave && w.Goal != Goal.Taken && !(w.ImmDis == d && TickNo < w.ImmUntil);

    void Infect(Walker w, int d)
    {
        w.Dis = d;
        w.Incub = TickNo + DaysToTicks(Diseases[d].IncubDays * (0.6 + 0.8 * Chance()));
        w.Sev = 0;
        w.Imm = 0;
        w.Resist = 0.6 + 0.8 * Trait01(w.Id, 100 + d); // a dot-man's constitution: always the same against the same illness
        if (BodyOf(w) is { } b) w.Resist *= Wear(b);   // the old, frail or wounded fight it off slower
        if (Ill != null) Ill.Cases++;
    }

    // Rolled with the events every quarter day: perhaps an illness arrives.
    void MaybeOutbreak()
    {
        if (Ill != null) return;
        if (Walkers.Count(w => !w.Gone) < 4) return;
        double crowd = 0.6 + 0.8 * Headcount / (double)Math.Max(1, PopCap);
        if (!Odds(crowd / OutbreakEvery)) return;
        StartOutbreak(PickDisease());
    }

    int PickDisease()
    {
        bool wet = Weather == Wx.Rain || Weather == Wx.Storm;
        return Weighted(Enumerable.Range(0, Diseases.Length).ToList(), i =>
        {
            var dz = Diseases[i];
            double w = dz.Where[Scheme] * dz.When[SeasonNo];
            if (dz.Water && HasWater) w *= 0.4;
            if (dz.Water && wet) w *= 1.5;
            return w;
        });
    }

    // The first case (two for plague) catches it; nobody notices until it shows.
    public void StartOutbreak(int d, bool showNow = false)
    {
        var pool = Walkers.Where(w => CanCatch(w, d)).ToList();
        if (pool.Count == 0) return;
        Ill = new Outbreak { D = d };
        int first = Diseases[d].Key == "plague" ? 2 : 1;
        for (int i = 0; i < first && pool.Count > 0; i++)
        {
            var w = Pick(pool);
            pool.Remove(w);
            Infect(w, d);
            if (showNow) w.Incub = TickNo;
        }
    }

    // For screenshot and soak tests.
    public void ForceOutbreak(string key)
    {
        int d = Array.FindIndex(Diseases, x => x.Key == key);
        if (d < 0) return;
        Ill = null;
        StartOutbreak(d, true);
        // A few more already ill, so there is something to see.
        foreach (var w in Walkers.Where(w => CanCatch(w, d)).Take(3)) { Infect(w, d); w.Incub = TickNo; }
    }

    // The first case shows: the crier tells, and the town answers by its rule and its leader.
    void Announce(Disease dz)
    {
        Ill!.Told = true;
        string line = string.Format(dz.Start, Name);
        string answer;
        if (G.Harsh >= 0.7 || TraitSum(t => t.Quarantine ? 1 : 0) > 0)
        {
            Ill.Sealed = true;
            Nudge(-0.04, "sick houses sealed");
            answer = "Guards seal the houses of the sick.";
        }
        else if (HasTrait("merciful")) { Nudge(0.02, "the leader visits the sick"); answer = $"{LeaderTitle} visits the sick."; }
        else if (HasTrait("idle")) { Nudge(-0.02, "the leader ignores the sick"); answer = $"{LeaderTitle} does nothing."; }
        else if (Built.Contains("church") || Built.Contains("cathedral")) answer = "The church takes in the sick.";
        else if (GovKey is "commune" or "council") answer = "Neighbours take turns nursing the sick.";
        else answer = "Doors are shut and windows hung with herbs.";
        Say($"{line} {answer}", false);
        if (dz.Key == "plague") Note(line);   // a common illness is chronicled once, when it passes, with its toll
        Npc("physician", "start", dz.Key == "plague", $"{line} {answer}");
    }

    void DieOfIllness(Walker w, Disease dz)
    {
        w.Gone = true;
        w.Dis = -1;
        Ill!.Deaths++;
        Mourn(w, $"of the {dz.Name}");
        Nudge(-0.03, "a death from illness");
        string t = BodyOf(w) is { } b ? NameOf(b) : LeaderTitle;
        string? line = BodyCause(w, $"the {dz.Name}");
        if (w.Lead)
        {
            Unseat(w, true);
            Say(line ?? $"{t} dies of the {dz.Name}.");
            Npc("physician", "leader", false, t);
        }
        else Say(line ?? $"{Pick(Victims)} succumbs to the {dz.Name}.", false);   // crier only; the toll goes in the chronicle when it passes
    }

    void Recover(Walker w, int d)
    {
        w.Dis = -1;
        w.Sick = false;
        w.ImmDis = d;
        w.ImmUntil = TickNo + DaysToTicks(ImmuneDays);
        w.Until = TickNo; // up and about again
    }

    void StepDisease()
    {
        if (Ill == null) return;
        int d = Ill.D;
        var dz = Diseases[d];
        double dtDays = DisEvery * TickMs / DayMs;

        // Spread: everyone sharing a building with the infected may catch it. The hidden cases walk about,
        // so they carry it from house to house; the sick in bed pass it to their household.
        double baseP = dz.Spread * SpreadK * (Ill.Sealed ? 0.4 : 1) * (dz.Water && HasWater ? 0.5 : 1);
        var rooms = new Dictionary<int, List<Walker>>();
        foreach (var w in Walkers)
        {
            if (w.Gone) continue;
            int r = Room(w);
            if (r < 0) continue;
            if (!rooms.TryGetValue(r, out var list)) rooms[r] = list = new();
            list.Add(w);
        }
        foreach (var list in rooms.Values)
        {
            double load = list.Sum(w => w.Dis != d ? 0 : w.Sick ? 1 : 0.5);
            if (load == 0) continue;
            double p = 1 - Math.Pow(1 - baseP, load);
            foreach (var w in list) if (CanCatch(w, d) && Chance() < p) Infect(w, d);
        }

        // Progress: severity against immunity.
        double care = Care();
        foreach (var w in Walkers)
        {
            if (w.Gone || w.Dis != d || TickNo < w.Incub) continue;
            if (!w.Sick)
            {
                w.Sick = true;
                w.Until = TickNo; // stop what they are doing and go to bed
                if (!Ill.Told) Announce(dz);
            }
            w.Sev += dz.SevDay * (1 - care) * dtDays;
            w.Imm += dz.ImmDay * w.Resist * (w.Food < HungryAt ? 0.7 : 1) * dtDays; // a hungry body fights it off slower
            if (w.Imm >= 1) Recover(w, d);
            else if (w.Sev >= 1) DieOfIllness(w, dz);
        }

        // Over: nobody carries it any more.
        if (!Walkers.Any(w => !w.Gone && w.Dis == d))
        {
            if (Ill.Told)
            {
                string toll = Ill.Deaths == 0 ? "no lives were lost" : Ill.Deaths == 1 ? "a life was lost" : $"{Ill.Deaths} lives are lost";
                string line = $"The {dz.Name} has passed: {Ill.Cases} fell ill and {toll}.";
                Say(line);
                Note(line);
                Npc("physician", "over", false, line);
            }
            Ill = null;
        }
    }

    // A line for the legend while an illness is about, else "".
    public string IllnessLine()
    {
        if (Ill == null || !Ill.Told) return "";
        int sick = Walkers.Count(w => !w.Gone && w.Sick);
        string care = Care() switch { >= 0.4 => "well cared for", >= 0.2 => "some care", > 0 => "little care", _ => "no care" };
        return $"{Cap(Diseases[Ill.D].Name)}: {sick} ill, {Ill.Deaths} dead, {care}{(Ill.Sealed ? ", sealed" : "")}";
    }

    // Test only: run one outbreak of illness d to its end. Returns cases, deaths, town size before, days it lasted.
    public (int cases, int deaths, int pop, double days) TestOutbreak(int d)
    {
        int pop = Walkers.Count(w => !w.Gone);
        StartOutbreak(d);
        var o = Ill;
        if (o == null) return (0, 0, pop, 0);
        double start = Age;
        for (int i = 0; i < 200_000 && Ill == o; i++) Tick(TickMs);
        return (o.Cases, o.Deaths, pop, (Age - start) / DayMs);
    }

    public int SickTint(Walker w) => w.Dis >= 0 ? Diseases[w.Dis].Tint : 0x8ab060;
}
