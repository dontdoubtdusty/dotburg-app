namespace Dotburg;

// Stage 3: how the town is ruled, its leader, its mood, and the politics that follow.
// Region parts (sister towns to flee to, migrants) come with stage 4; here exiles just walk out the west gate.
partial class Sim
{
    const double PolStepMs = DayMs / 4;   // how often politics rolls its dice
    const double RevoltAt = 0.2;          // mood below which harsh rule can fall
    const double RevoltPerDay = 0.32;     // revolts a day while angry enough, before fear (about one every 3 days)
    const double Fear = 0.85;             // how much harshness damps those odds
    const double Collapse = 0.15;         // chance a Party state dies with its Protector
    static readonly double[] SeasonMood = { 0.03, 0.05, 0, -0.05 };

    // ---------- state ----------
    public string GovKey = "elders";
    public double Mood = 0.6;                       // 0 furious .. 1 joyful
    public Leader? Leader;
    public double LastElection;                     // Age of the last election or change of rule
    public int Honoured;                            // statues of well-remembered leaders, one per park
    public Dictionary<string, int> Regnal = new();  // how many sovereigns have borne each name
    double polClock;
    readonly List<Walker> guards = new();           // patrols and escorts: not citizens, not saved

    public Gov G => Govs.Get(GovKey);
    // An event moves the mood at once; why is kept for the mood tooltip.
    void Nudge(double d, string why)
    {
        Mood = Clamp(Mood + d, 0, 1);
        MoodEvents.Add((why, d, Age));
        MoodEvents.RemoveAll(e => Age - e.At > 2 * DayMs);
        if (MoodEvents.Count > 30) MoodEvents.RemoveAt(0);
    }

    // ---------- odds over time ----------
    // Timed events are written as how often they happen in town days, not as a chance per check, so changing
    // PolStepMs (how often politics and events roll) leaves every event just as frequent.
    // PerDay(r): the chance that something happening r times a day (on average) happens within one check.
    // OncePer(n): true about once every n days while the condition holds.
    const double CheckDays = PolStepMs / DayMs;
    static double PerDay(double rate) => rate <= 0 ? 0 : 1 - Math.Exp(-rate * CheckDays);
    bool Odds(double rate) => Chance() < PerDay(rate);
    bool OncePer(double days) => Odds(1 / days);

    public Walker? LeaderWalker => Walkers.FirstOrDefault(w => w.Lead && !w.Gone);
    // A leader earns an epithet from their first trait after a day in office ("Mayor Ada the Honest").
    public string LeaderTitle => Leader != null
        ? $"{G.Title} {Leader.Name}{(GovKey == "crown" ? " " + Govs.RomanOf(Leader.Gen) : "")}{(Age - Leader.Since >= DayMs && Leader.Traits.Length > 0 && Traits.Exists(Leader.Traits[0]) ? " the " + Traits.Get(Leader.Traits[0]).Name : "")}"
        : "no leader";

    // What the current rule means in play, one line each, read from the rule's numbers and the harshness
    // thresholds the sim checks (0.5, 0.7, 0.8), for the government tooltip.
    public List<string> RuleLines()
    {
        var g = G;
        var food = FoodRule;
        string harsh = g.Harsh < 0.3 ? "gentle" : g.Harsh < 0.5 ? "firm" : g.Harsh < 0.8 ? "harsh" : "iron-fisted";
        static string Pct(double mul) => $"{Math.Abs(mul - 1) * 100:F0}%";
        var l = new List<string> { $"{g.Name}: {harsh} rule", Govs.Blurb.TryGetValue(GovKey, out var b) ? b : "", "" };

        l.Add($"Mood settles around {g.Mood * 100:F0}% when nothing else is going on.");
        if (GovKey == "none") l.Add($"No rule yet: once {RuleAt} grown dot-men live here, they settle on one.");
        else if (Govs.IsHamlet(GovKey)) l.Add("A hamlet rule: the town picks a new one when it becomes a Village.");
        if (GovKey == "commune") l.Add($"A new speaker every {g.Term} days.");
        else if (g.Term > 0) l.Add($"Elections every {g.Term} days. If mood is low, a strongman may win and found a Party state.");
        else if (GovKey == "crown") l.Add("No elections: the throne passes to an heir.");
        else if (!Govs.IsHamlet(GovKey)) l.Add("No elections.");
        if (g.CostMul != 1) l.Add($"Buildings cost {Pct(g.CostMul)} {(g.CostMul < 1 ? "less" : "more")}.");
        if (g.HomeBonus > 0) l.Add($"Each home holds {g.HomeBonus} more dot-man.");
        if (g.Welcome != 1) l.Add($"{Pct(g.Welcome)} {(g.Welcome > 1 ? "more" : "fewer")} newcomers from sister towns.");
        if (g.Fest != 1) l.Add($"Festivals last {(g.Fest > 1 ? "longer" : "shorter")} and cheer {(g.Fest > 1 ? "more" : "less")}.");
        if (g.Curfew) l.Add("Curfew: everyone is indoors from dusk. More weddings, more babies.");
        if (g.Guards > 0) l.Add($"{g.Guards} guards patrol the roads; dotlets may tag along.");

        l.Add($"Food: holds back {food.reserve:F0} days of grain{(food.open ? "; the granary opens to the hungry" : "; the granary stays shut, even in famine")}.");
        l.Add(food.hoard == 0 ? "Panic buying is stopped: food is rationed or shared." : food.hoard > 1 ? "Dot-men hoard hard when food runs short." : food.hoard < 1 ? "Dot-men hoard little when food runs short." : "Dot-men hoard when food runs short.");

        if (!Govs.IsHamlet(GovKey)) l.Add(g.Harsh >= 0.5 ? "Assassination plots grow as mood falls." : "Few plots against the leader.");
        if (g.Harsh >= 0.5) l.Add("Low mood brings revolts, arrests and dot-men fleeing.");
        else if (!Govs.IsHamlet(GovKey)) l.Add("Very low mood can bring a coup.");
        if (g.Harsh >= 0.7) l.Add("Sickness: houses are sealed. Refugees are turned away. Matches are rigged.");
        if (g.Harsh >= 0.8) l.Add("Festivals become rallies, war lifts spirits, fires are blamed on saboteurs, attacks on the leader end in a crackdown.");
        l.Add("Sister towns under a rule like this one get on better with us.");
        return l;
    }

    // ---------- the leader's character ----------
    IEnumerable<Trait> LeaderTraits => Leader == null ? Enumerable.Empty<Trait>() : Leader.Traits.Where(Traits.Exists).Select(Traits.Get);
    double TraitMul(Func<Trait, double> f) => LeaderTraits.Aggregate(1.0, (a, t) => a * f(t));
    double TraitSum(Func<Trait, double> f) => LeaderTraits.Sum(f);
    public bool HasTrait(string key) => Leader?.Traits.Contains(key) == true;
    public string TraitWords => string.Join(", ", LeaderTraits.Select(t => t.Name));
    public string TraitBlurb => string.Join("; ", LeaderTraits.Select(t => t.Blurb));

    // For the soak: the traits of the dot-man who would be chosen from this town now (or a roll if it is empty).
    public string[] TestAppoint()
    {
        var pool = Walkers.Where(w => !w.Gone && w.Kind == "").ToList();
        return pool.Count == 0 ? RollTraits() : CitizenTraits(Weighted(pool, w => LeaderFit(w, null, false)));
    }

    // How likely this dot-man is to be chosen: their traits under this rule, ambition and charm, an heir who takes
    // after the parent, and a dotlet hardly ever.
    double LeaderFit(Walker w, string[]? parent, bool heir)
    {
        var tr = CitizenTraits(w);
        double wt = 1;
        foreach (var k in tr) wt *= Traits.Weight(Traits.Get(k), G.Harsh);
        if (tr.Contains("ambitious")) wt *= 2.5;
        if (tr.Contains("charmer")) wt *= 3;
        if (heir && parent != null && tr.Any(parent.Contains)) wt *= 3;
        return w.Kid != 0 ? wt * 0.05 : wt;
    }

    // One trait from each of the three groups (and now and then a wildcard), none clashing, leaning by how harsh
    // the rule is. An heir often takes after a parent in a group. Used for sister rulers and older saves.
    string[] RollTraits(string[]? parent = null, double? harsh = null)
    {
        var got = new List<string>();
        for (int cat = 0; cat <= 3; cat++)
        {
            if (cat == 3 && Chance() >= Traits.WildChance) break;
            var ok = Traits.InCat(cat).Where(t => !got.Any(k => Traits.Clashes(k, t.Key))).ToList();
            var from = parent?.FirstOrDefault(p => ok.Any(t => t.Key == p));
            got.Add(from != null && Chance() < 0.4 ? from : Weighted(ok, t => Traits.Weight(t, harsh ?? G.Harsh)).Key);
        }
        return got.ToArray();
    }

    T Weighted<T>(IList<T> items, Func<T, double> weight)
    {
        double r = Chance() * items.Sum(weight);
        foreach (var t in items) { r -= weight(t); if (r < 0) return t; }
        return items[^1];
    }

    // The seat of government: the town hall, else the clock tower; -1 before either exists.
    public int SeatRank()
    {
        int v = Math.Min(Built.Count, Lots.Count);
        int h = Built.IndexOf("hall");
        if (h >= 0 && h < v) return h;
        int t = Built.IndexOf("tower");
        return t >= 0 && t < v ? t : -1;
    }

    // A new town starts with no rule and no leader (0.71): a rule is formed once RuleAt grown dot-men live here.
    public void ResetGov()
    {
        GovKey = "none";
        Mood = G.Mood;
        Leader = null;
        LastElection = Age;
        Honoured = 0;
        Regnal = new();
        guards.Clear();
        Bodies.Clear(); // the old town's people of note stay with the old town
        foreach (var w in Walkers) w.Lead = false;
    }

    // How a town chooses to be ruled (key only), for a load that finds a village still under a hamlet rule.
    string ChooseGov() => ChooseGovWhy().key;

    // Which townsfolk draw a village toward each rule (0.84): the traits, and what those people want, for the reason.
    static readonly Dictionary<string, (string[] traits, string want)> RuleDraw = new()
    {
        ["commune"] = (new[] { "generous", "humble", "tolerant", "merciful", "cheerful" }, "would rather share than rule"),
        ["council"] = (new[] { "honest", "calm", "careful", "tolerant" }, "want a say in things"),
        ["mayor"] = (new[] { "able", "honest", "thrifty", "curious" }, "want the place run well"),
        ["guild"] = (new[] { "greedy", "cunning", "thrifty", "ambitious" }, "count every coin"),
        ["temple"] = (new[] { "pious", "doomsayer", "gloomy" }, "keep the old prayers"),
        ["crown"] = (new[] { "loyal", "proud", "brave" }, "want someone to follow"),
        ["junta"] = (new[] { "brave", "hotheaded", "cruel", "reckless" }, "want order kept by force"),
        ["party"] = (new[] { "cruel", "ambitious", "jealous", "spiteful", "vengeful" }, "want their enemies named"),
    };

    // How strongly the townsfolk draw toward rule k: their share with its traits against the share a town of random
    // dot-men would have, squared so a clear lean counts (0.1 to 4); n is how many of the grown dot-men have them.
    (double k, int n, int of) PeopleDraw(string rule)
    {
        if (!RuleDraw.TryGetValue(rule, out var d)) return (1, 0, 0);
        var adults = Walkers.Where(w => !w.Gone && w.Kind == "" && w.Kid == 0 && !w.Lead).ToList();
        if (adults.Count < 3) return (1, 0, adults.Count);
        int n = adults.Count(w => CitizenTraits(w).Any(d.traits.Contains));
        double none = 1;
        for (int cat = 0; cat <= 3; cat++)
        {
            var g = Traits.InCat(cat);
            double p = g.Count(t => d.traits.Contains(t.Key)) / (double)g.Length * (cat == 3 ? Traits.WildChance : 1);
            none *= 1 - p;
        }
        double ratio = n / (double)adults.Count / Math.Max(0.05, 1 - none);
        return (Clamp(Math.Pow(ratio, 1.5), 0.1, 4), n, adults.Count);
    }

    // How well the sitting leader's traits suit a rule this harsh (1 is neutral; a merciful leader in a party state
    // is near 0.1, under a council over 2), the same weights that choose leaders.
    double LeaderSuits(double harsh) => LeaderTraits.Aggregate(1.0, (a, t) => a * Traits.Weight(t, harsh));

    // How a village chooses to be ruled (0.84), like the settlers' choice: the old lean of the land and the hamlet rule
    // it grew up under, then who lives there and who leads them (a well-liked leader's leanings count for more).
    // Returns the rule and the strongest reason, in words.
    (string key, string why) ChooseGovWhy()
    {
        var lean = Govs.Lean[Scheme];
        var grows = Govs.Grows.TryGetValue(GovKey, out var gr) ? gr : new();
        string hamlet = G.Lower;
        string leader = Leader != null ? LeaderTitle : "";
        var parts = new Dictionary<string, (double w, string why)>();
        foreach (var g in Govs.All.Where(g => !Govs.IsHamlet(g.Key)))
        {
            double land = lean.TryGetValue(g.Key, out var m) ? m : 1, habit = grows.TryGetValue(g.Key, out var h) ? h : 1;
            var (folk, n, of) = PeopleDraw(g.Key);
            double lead = Leader == null ? 1 : Math.Pow(LeaderSuits(g.Harsh), 0.25 + 0.5 * Mood);
            // The reason told is the factor that pushed hardest for this rule.
            var reasons = new List<(double k, string why)>
            {
                (land, "it is the way of towns in these parts"),
                (habit, $"old habits from the {hamlet} carry over"),
                (folk, $"{n} of its {of} grown dot-men {(RuleDraw.TryGetValue(g.Key, out var rd) ? rd.want : "want it")}"),
                (lead, $"{leader} has long argued for it"),
            };
            var best = reasons.OrderByDescending(r => r.k).First();
            parts[g.Key] = (g.Lean * land * habit * folk * lead, best.k > 1.05 ? best.why : "nobody can say quite why");
            if (RuleFactors != null) { var f = RuleFactors.TryGetValue(g.Key, out var ff) ? ff : RuleFactors[g.Key] = new double[3]; f[0] += folk; f[1] += lead; f[2]++; }
        }
        string k = Weighted(parts.Keys.ToList(), x => parts[x].w);
        return (k, parts[k].why);
    }

    // After a load: keep the saved leader if exactly one dot-man carries the flag, else appoint one.
    public void RestoreLeader(string name, int gen, double since, string[]? traits = null)
    {
        var leads = Walkers.Where(w => w.Lead).ToList();
        foreach (var w in leads.Skip(1)) w.Lead = false;
        if (GovKey == "none") { foreach (var w in leads) w.Lead = false; Leader = null; return; }   // settlers: nobody leads yet
        if (leads.Count == 0 || name == "")
        {
            Appoint(null);
            if (Govs.IsHamlet(GovKey) && Tier >= 2) GovKey = ChooseGov();
            return;
        }
        Leader = new Leader { Name = name, Gen = Math.Max(1, gen), Since = since };
        // An older save has no traits: give the sitting leader some now.
        var kept = (traits ?? Array.Empty<string>()).Where(Traits.Exists).ToArray();
        Leader.Traits = kept.Length >= 2 ? kept : RollTraits();
        if (Govs.IsHamlet(GovKey) && Tier >= 2) { GovKey = ChooseGov(); StaticDirty = true; }
    }

    // For screenshot tests: switch to a rule at once.
    public void ForceGov(string key)
    {
        if (!Govs.Exists(key)) return;
        GovKey = key;
        Mood = G.Mood;
        Honoured = 2;
        StaticDirty = true;
    }

    // A village outgrows its hamlet rule and chooses how it will be ruled (0.84). The sitting leader stays on under
    // the new title when the rule suits them, likelier the better liked the town is (a founder can be crowned);
    // otherwise they step aside and the new rule finds its own leader.
    public int RuleKept, RuleNew;   // soak counters
    public static Dictionary<string, double[]>? RuleFactors;   // soak only: summed people and leader factors per rule
    void MaybeChooseGov()
    {
        if (GovKey == "none") FormRule();   // settlers who grew fast form their hamlet rule first
        if (Tier != 2 || !Govs.IsHamlet(GovKey) || GovKey == "none") return;
        var (k, why) = ChooseGovWhy();
        var lw = LeaderWalker;
        string old = LeaderTitle;
        bool keep = lw != null && lw.Kid == 0 && Chance() < Clamp(0.2 + 0.6 * Mood, 0, 0.8) * Math.Min(1, LeaderSuits(Govs.Get(k).Harsh));
        if (keep) RuleKept++; else RuleNew++;
        if (lw != null && !keep) FallHow = "stepdown";
        SetGov(k, "", keep);
        // The new leader by first name, or full name when it is the old leader's first name too.
        string nu = Leader == null ? "someone" : lw != null && !keep && old.Contains($" {Leader.Name}") && LeaderWalker is { } nw ? CitizenName(nw) : Leader.Name;
        string rest = lw == null ? $"{nu} {Govs.Succeed[k]}"
            : keep && k == "crown" ? $"{old} is crowned: {LeaderTitle}"
            : keep ? $"{old} stays on as {G.Title.ToLowerInvariant()}"
            : $"{old} {(G.Harsh >= 0.8 ? "is pushed aside" : "steps aside")}; {nu} {Govs.Succeed[k]}";
        Say($"{Name} chooses {An(Govs.Get(k).Lower)}: {why}. {rest}.");
    }

    // What has the town on edge, for the line when a crisis turns its rule (0.84): plague, hunger, a lost war,
    // empty coffers or ruins, or "" when nothing stands out. Starts with a comma.
    string CrisisCause()
    {
        if ((Ill is { Told: true } && Walkers.Count(w => !w.Gone && w.Sick) >= 2) || (BlightOn?.Kind == "plague" && BlightOn.Towns.Contains(Name))) return ", with sickness in every street";
        int pop = Walkers.Count(w => !w.Gone && w.Kind == "");
        if (Famine || HungryCount > 0.3 * pop) return ", with the bread running out";
        if (EvClock - lastDefeat < 4 * DayMs) return ", after the defeat";
        if (Ruins.Count >= 2) return ", with ruins still standing";
        if (Gold < EmptyCoffers) return ", with the coffers empty";
        return "";
    }

    // Which way a falling rule turns when either would do (a revolution, the Party's collapse): the townsfolk lean it.
    string TurnTo(params string[] keys) => Weighted(keys, k => PeopleDraw(k).k);

    // Test only (--rules): grow a fresh region quickly to a Village and report the choice: the rule, whether the
    // leader stayed, the line, the old leader's mean trait harshness, and the odds the pre-0.84 roll gave each rule.
    public (string rule, bool kept, string line, double leaderHarsh, Dictionary<string, double> oldOdds)? TestRuleChoice()
    {
        FreshRegion();
        SayLog = new();
        Dictionary<string, double>? old = null;
        double lh = 0;
        for (int i = 0; i < 80_000 && Tier == 1; i++)
        {
            if (i % 15 == 0)
            {
                // Just before the step to Village, note what the old roll would have given and the leader's leaning.
                if (Govs.IsHamlet(GovKey) && GovKey != "none")
                {
                    var lean = Govs.Lean[Scheme];
                    var grows = Govs.Grows.TryGetValue(GovKey, out var gr) ? gr : new();
                    var w = Govs.All.Where(g => !Govs.IsHamlet(g.Key)).ToDictionary(g => g.Key,
                        g => g.Lean * (lean.TryGetValue(g.Key, out var m) ? m : 1) * (grows.TryGetValue(g.Key, out var h) ? h : 1));
                    double sum = w.Values.Sum();
                    old = w.ToDictionary(kv => kv.Key, kv => kv.Value / sum);
                    lh = LeaderTraits.Any() ? LeaderTraits.Average(t => t.Harsh) : 0;
                }
                Earn(1);
                Grow();
            }
            Tick(TickMs);
        }
        var line = SayLog.FirstOrDefault(l => l.Contains(" chooses "));
        if (line == null || old == null) return null;
        return (GovKey, line.Contains("stays on") || line.Contains("is crowned:"), line, lh, old);
    }

    // Make a dot-man the leader (a sovereign's heir shares their colour when one can be found); returns the new title.
    string Appoint(int? prefer, string[]? parent = null, bool heir = false)
    {
        if (LeaderWalker is { } old) Retire(old); // the outgoing leader stays in town under their old title
        foreach (var w in Walkers) w.Lead = false;
        var pool = Walkers.Where(w => !w.Gone && w.Goal != Goal.Leave && w.Goal != Goal.Taken).ToList();
        if (pool.Count == 0) { Leader = null; return ""; }
        var same = prefer == null ? new List<Walker>() : pool.Where(w => w.C == prefer).ToList();
        // Harsh rule raises the cruel and proud, gentle rule the merciful and tolerant: the candidates' own traits
        // are weighed (0.68), an heir who takes after the parent is likelier, and a dotlet hardly ever rules.
        var chosen = Weighted(same.Count > 0 ? same : pool, w => LeaderFit(w, parent, heir));
        chosen.Lead = true;
        // A former leader called back keeps their own name; anyone else rules under the name the town knows them by.
        string nm = BodyOf(chosen) is { } back ? back.Name : CitizenFirst(chosen);
        int gen = 1;
        if (GovKey == "crown") Regnal[nm] = gen = (Regnal.TryGetValue(nm, out var n) ? n : 0) + 1;
        Leader = new Leader { Name = nm, Gen = gen, Since = Age };
        Leader.Traits = CitizenTraits(chosen);
        EnsureBody(chosen, nm, heir);
        StaticDirty = true; // the seat's banner follows the leader's home
        return LeaderTitle;
    }

    // Change how the town is ruled; keep keeps the leader under a new title.
    void SetGov(string k, string line, bool keep)
    {
        if (!keep && LeaderWalker is { } old) Retire(old); // under the title they held, before the rule changes
        GovKey = k;
        LastElection = Age;
        if (!keep || LeaderWalker == null) Appoint(null);
        else if (k == "crown" && Leader != null) Regnal[Leader.Name] = Leader.Gen = (Regnal.TryGetValue(Leader.Name, out var n) ? n : 0) + 1;
        guards.RemoveAll(g => g.Goal is not (Goal.Taken or Goal.Fetch));
        StaticDirty = true;
        if (line != "") Say(line);   // "": the caller says it, once the new leader is known
    }

    // Remove the leader from office (and from town if gone).
    void Unseat(Walker w, bool gone)
    {
        if (!gone) Retire(w);
        w.Lead = false;
        if (gone) w.Gone = true;
        Leader = null;
    }

    void Honour()
    {
        Honoured = Math.Min(6, Honoured + 1);
        StaticDirty = true;
    }

    // Send a dot-man out through the west gate, after any walk already under way.
    void SendAway(Walker w, Goal goal, string to = "")
    {
        if (w.Mode == Mode.Inside && w.At >= 0 && w.At < Lots.Count) (w.X, w.Y) = DoorOf(w.At);
        var (ex, ey) = w.Mode == Mode.Walk && w.Path.Count > 0 ? w.Path[^1] : (w.X, w.Y);
        var (gx, gy) = Entrance();
        var path = w.Mode == Mode.Walk ? w.Path : new List<(int x, int y)>();
        path.AddRange(Route(ex, ey, gx, gy, w.Lane));
        w.Path = path;
        w.Goal = goal;
        w.To = to;
        w.Mode = Mode.Walk;
        w.Sleep = false;
        w.Lead = false;
    }

    // Drift the mood toward where the town's conditions put it (MoodTarget), half the scale a day. The reasons
    // change slowly, so they are summed about once a second rather than every tick.
    double moodGoal = -1;
    void UpdateMood(double dt)
    {
        if (moodGoal < 0 || TickNo % 9 == 0) moodGoal = MoodTarget;
        Mood = Ease(Mood, moodGoal, dt, DayMs * 2);
    }

    // Where the mood is heading: the sum of the standing reasons, from 0 to 1.
    public double MoodTarget => Clamp(MoodParts().Sum(p => p.D), 0, 1);

    // Every standing reason for the town's mood with its amount, starting with the rule's base level. The mood step
    // and the mood tooltip both read this, so the tooltip's numbers are the real ones.
    public List<(string Why, double D)> MoodParts()
    {
        var l = new List<(string, double)> { (GovKey == "none" ? "No rule yet: settlers getting along" : $"The rule: {G.Name}", G.Mood) };
        if (Stormy) l.Add(("Stormy weather", -0.08));
        else if (Weather == Wx.Clear && Daylight() > 0.3) l.Add(("A clear, sunny day", 0.04));
        if (PopCap > 0 && Headcount >= PopCap) l.Add(("Crowded: every home is full", -0.05));
        if (LandCount(Grove) > 0 && SeasonNo <= 1 && !Stormy) l.Add(("Walks in the grove", 0.02));
        if (SeasonMood[SeasonNo] != 0) l.Add(($"It is {SeasonNow.Name.ToLowerInvariant()}", SeasonMood[SeasonNo]));
        foreach (var t in LeaderTraits) if (t.Mood != 0) l.Add(($"The leader is {t.Name.ToLowerInvariant()}", t.Mood));
        int sick = Walkers.Count(w => w.Sick && !w.Gone);
        if (sick > 0) l.Add(($"{sick} sick in bed", -(0.03 + 0.25 * sick / Math.Max(1, Walkers.Count))));
        if (Ill?.Sealed == true) l.Add(("Sick houses sealed by guards", -0.04));
        if (WarOn != null && G.Harsh < 0.8) l.Add(("At war", -0.06)); // only harsh rule thrives on war
        l.AddRange(HungerMoodParts()); // the hungry, the starving and the panic (Food.cs)
        if (Comet) l.Add(CometOmen ? ("A comet: an ill omen, they say", -0.04) : ("A comet in the night sky", 0.02));
        if (Flooded) l.Add(("Floodwater in the low streets", -0.04));
        if (Feuds.Any(f => f.Heat >= 1.2)) l.Add(($"A feud between {Fam(Feuds.First(f => f.Heat >= 1.2).A)} and {Fam(Feuds.First(f => f.Heat >= 1.2).B)}", -0.02));
        int burnt = Ruins.Count + Fires.Count; // burnt lots and live fires weigh on the town
        if (burnt > 0) l.Add(($"{(Fires.Count > 0 ? "Fires and ruins" : "Ruins")} ({burnt})", -Math.Min(0.1, burnt * 0.02)));
        // The treasury: an empty one worries the town, a full one reassures it (0.77).
        if (Gold < EmptyCoffers) l.Add(("The coffers are empty", -0.04));
        else if (Gold >= FullCoffers) l.Add(("The coffers are full", 0.03));
        return l;
    }

    // Recent events that moved the mood at once (Nudge), newest last; the drift then carries it back toward the
    // target. Kept for two town days for the mood tooltip; not saved.
    public readonly List<(string Why, double D, double At)> MoodEvents = new();

    public const int RuleAt = 5;   // grown dot-men a settlement needs before it forms a rule

    // Settlers with RuleAt grown dot-men settle on a hamlet rule that fits who they are: many old heads make
    // elders likelier, strong wills a headman, the pious and the doomsayers shrine keepers, a web of friendships a
    // founding family. Each starts at 1, so any rule can happen. Then the leader is chosen as usual (Appoint).
    void FormRule()
    {
        var adults = Walkers.Where(w => !w.Gone && w.Kind == "" && w.Kid == 0).ToList();
        if (adults.Count < RuleAt) return;
        int Has(params string[] keys) => adults.Count(w => CitizenTraits(w).Any(keys.Contains));
        var ids = adults.Select(w => w.Id).ToHashSet();
        int old = adults.Count(w => YearsOfDot(w) >= 50);
        int strong = Has("ambitious", "brave", "proud", "hotheaded");
        int pious = Has("pious", "doomsayer");
        int friends = adults.Sum(w => w.Bonds.Count(b => b.Kind == "friend" && ids.Contains(b.Who))) / 2;
        var why = new Dictionary<string, (double weight, string reason)>
        {
            ["elders"] = (1 + 1.5 * old, old > 1 ? $"{old} of them are past fifty" : "the oldest are listened to"),
            ["headman"] = (1 + 1.2 * strong, strong > 1 ? $"{strong} strong wills want one voice" : "someone has to decide"),
            ["shrine"] = (1 + 1.5 * pious, pious > 0 ? $"{pious} keep the old prayers" : "the shrine draws them together"),
            ["founders"] = (1 + 1.2 * friends, friends > 1 ? $"{friends} friendships bind the first households" : "the first family knows the place best"),
        };
        GovKey = Weighted(why.Keys.ToList(), k => why[k].weight);
        Mood = Clamp(Mood + (G.Mood - 0.6), 0, 1);
        LastElection = Age;
        StaticDirty = true;
        string t = Appoint(null);
        Say($"With {adults.Count} grown dot-men, {Name} settles on {An(G.Lower)}: {why[GovKey].reason}. {t} leads.");
    }

    // Roll the dice of politics, once every PolStepMs: at most one big event.
    void Politics()
    {
        if (Lots.Count == 0 || Walkers.Count == 0) return;
        foreach (var sis in Sisters.ToList()) if (OncePer(25)) SisterChange(sis);
        if (GovKey == "none") { FormRule(); return; }   // no leader and no politics until a rule is formed
        var g = G;
        var lw = LeaderWalker;
        if (lw == null || Leader == null)
        {
            string t = Appoint(null);
            if (t != "") Say($"{t} takes charge.");
            return;
        }
        double tenure = (Age - Leader.Since) / DayMs;
        bool elected = GovKey is "council" or "mayor" or "guild";
        bool many = Walkers.Count(w => !w.Gone) > 2;
        // Fear holds harsh rule together: the harsher it is, the deeper the anger must run.
        double revolt = TraitMul(t => t.Revolt);
        // Rates are per day: a gentle leader in a content town is assassinated about once in 60 days, a long-reigning
        // one dies more often each day they hold on (up to about one death every 2 days).
        if (g.Harsh >= 0.5 && Mood < RevoltAt && Odds(RevoltPerDay * (1 - g.Harsh * Fear) * revolt)) { Revolution(lw); return; }
        if (g.Harsh < 0.8 && !Govs.IsHamlet(GovKey) && Mood < 0.3 && Odds(0.2 * revolt)) { Coup(lw); return; }
        // A dot-man with a deep grudge against the leader adds their own reason (0.68), even in a hamlet.
        var (foe, motive) = MotiveAgainstLeader();
        double grudge = motive != null && motive.Str >= GrudgeAt ? MotiveK * motive.Str : 0;
        double plots = Govs.IsHamlet(GovKey) ? 0 : (0.016 + 0.12 * g.Harsh * (1 - Mood)) * TraitMul(t => t.Kill);
        if (many && Odds(plots + grudge)) { Assassination(lw, motive != null && motive.Str >= 0.3 ? foe : null, motive); return; }
        // Natural death now comes from the leader's body (Health.cs); poor health makes an elected leader step down sooner.
        if (g.Term > 0 && Age - LastElection >= g.Term * DayMs) { Election(); return; }
        bool ailing = LeaderBody is { } lb && HealthWord(lb) != "in good health";
        if (elected && OncePer(ailing ? 6 : 17)) { StepDown(lw, tenure); return; }
        if (elected && Odds(0.04 * TraitMul(t => t.Scandal)))
        {
            Nudge(-0.15, "a scandal at the hall");
            Say(Pick(new[] { "A scandal at the hall.", "Bribes are found in the ledgers.", "The treasury comes up short." }));
            return;
        }
        if (GovKey == "crown" && Mood > 0.7 && OncePer(25)) { SetGov("council", $"{LeaderTitle} grants {Name} a council.", false); return; }
        if (GovKey == "temple" && Mood < 0.3 && OncePer(8)) { SetGov("council", $"{Name} turns from the temple to a council.", false); return; }
        if (GovKey == "junta" && OncePer(17)) { SetGov("party", $"{LeaderTitle} declares a Party state.", true); return; }
        if (g.Harsh >= 0.5 && Mood < 0.5 && many && Odds(2 * g.Harsh * (1 - Mood) * TraitMul(t => t.Flee))) Emigrate();
        if (GovKey == "party" && many && Odds((0.12 + 0.2 * (1 - Mood)) * TraitMul(t => t.Arrest))) Arrest();
        else if (GovKey != "party" && g.Harsh >= 0.5 && HasTrait("cruel") && many && OncePer(12)) Arrest(); // a cruel ruler finds enemies anywhere
    }

    // The leader dies of a named cause (line says so); murdered means an assassin's wounds did it, maybe days later.
    void Death(Walker lw, string line0, bool murdered)
    {
        if (murdered) { Murdered(lw, line0); return; }
        double tenure = Leader != null ? (Age - Leader.Since) / DayMs : 0;
        int c = lw.C;
        var parent = Leader?.Traits;
        bool proud = HasTrait("proud");
        Unseat(lw, true);
        Nudge(-0.03, "the leader died");
        if (GovKey != "party" && GovKey != "junta" && (GovKey == "crown" || tenure >= 2 || proud)) Honour(); // the proud see to their own statue
        if (GovKey == "party" && Chance() < Collapse)
        {
            SetGov(TurnTo("council", "commune"), $"{line0} The Party state collapses.", false);
            return;
        }
        string n = Appoint(GovKey == "crown" ? c : null, GovKey == "crown" ? parent : null, GovKey == "crown");
        LastElection = Age;
        string line = $"{line0} {n} {Govs.Succeed[GovKey]}.";
        if (GovKey == "crown") StartFestival(line); // a coronation
        Say(line);
    }

    // An assassin strikes. The attempt may kill outright, wound (and perhaps kill days later), or be survived.
    void Assassination(Walker lw, Walker? by = null, Bond? why = null)
    {
        string? who = by == null ? null : CitizenName(by);
        string title = LeaderTitle;
        var (line, dead) = Attempt(lw, who);
        // The leader's family and a named assassin's are enemies from now on (0.85), said after the attempt.
        void Feud() { if (by != null && why != null) MaybeFeud(lw, by, $"{who} went for {title}", true); }
        // A named assassin: the story behind it, then caught (most often) or fled.
        if (by != null && why != null)
        {
            AssassinsKnown++;
            line += $" It went back to day {why.Day}: {WhyFor(by, why)}.";
            var dest = Sisters.Count > 0 ? Pick(Sisters) : null;
            if (Chance() < 0.7 || dest == null) { Arrest(by, true); line += $" {who} is seized and led away."; }
            else { SendAway(by, Goal.Leave, dest.Name); line += $" {who} flees to {dest.Name}."; }
        }
        if (dead) { Murdered(lw, line); Feud(); return; }
        Nudge(-0.05, "an attempt on the leader");
        Say(line);
        Note(line);
        Feud();
        Npc("physician", "wound", false, LeaderTitle);
        if (G.Harsh >= 0.8)
        {
            // A named assassin's friends are the first to be rounded up.
            var friends = by == null ? new List<Walker>() : Walkers.Where(w => !w.Gone && !w.Lead && w != by && w.Goal is not (Goal.Leave or Goal.Taken)
                && (BondOf(w, by.Id, "friend") != null || BondOf(by, w.Id, "friend") != null)).Take(2).ToList();
            Say(friends.Count > 0 ? $"Guards round up {string.Join(" and ", friends.Select(CitizenName))}, friends of {who}. A crackdown follows." : "Guards hunt the assassin's friends. A crackdown follows.");
            foreach (var f in friends) Arrest(f, true);
            for (int i = friends.Count; i < 2; i++) Arrest();
        }
    }

    // The leader is dead by an assassin's hand: succession, a crackdown under harsh rule, or the army steps in.
    void Murdered(Walker lw, string line)
    {
        int c = lw.C;
        var parent = Leader?.Traits;
        Unseat(lw, true);
        Nudge(-0.1, "the leader murdered");
        if (GovKey == "crown") { Say($"{line} {Appoint(c, parent, true)} takes the throne."); return; }
        if (G.Harsh >= 0.8)
        {
            Say($"{line} {Appoint(null)} {Govs.Succeed[GovKey]}. A crackdown follows.");
            Arrest();
            Arrest();
            return;
        }
        if (Chance() < 0.4) { SetGov("junta", $"{line} The army takes over.", false); return; }
        LastElection = Age;
        Say($"{line} {Appoint(null)} {Govs.Succeed[GovKey]}.");
    }

    // The people rise: harsh rule falls to a commune or a council; the fallen ruler flees.
    void Revolution(Walker lw)
    {
        string old = G.Lower, t = LeaderTitle;
        var dest = Sisters.Count > 0 ? Weighted(Sisters, s => Govs.Get(s.Gov).Harsh + 0.1) : null;
        if (dest != null) SendAway(lw, Goal.Leave, dest.Name);
        Unseat(lw, dest == null);
        Nudge(0.25, "the revolution");
        string line = $"Revolution! The {old} falls; {t} flees{(dest != null ? " to " + dest.Name : "")}.";
        Npc("ringleader", "won", false, old);
        SetGov(TurnTo("commune", "council"), line, false);
        StartFestival(line);
    }

    void Coup(Walker lw)
    {
        string t = LeaderTitle;
        FallHow = "coup";
        Unseat(lw, false);
        Nudge(-0.1, "a coup");
        SetGov("junta", $"A coup: the army deposes {t}{CrisisCause()}.", false);
    }

    void Election()
    {
        LastElection = Age;
        string t = LeaderTitle;
        if (GovKey == "commune")
        {
            FallHow = "turn";
            Appoint(null);
            Say($"{(Leader != null ? Leader.Name : "Someone")} takes a turn as speaker.");
            return;
        }
        if (Mood < 0.35 && Chance() < 0.3)
        {
            // The strongman comes from the angry: the Party's leader is chosen as usual (cruel and ambitious first).
            SetGov("party", "", false);
            Say($"A strongman wins the vote{CrisisCause()}: {LeaderTitle}. {Name} becomes a Party state.");
            return;
        }
        if (Chance() < 0.25 + Mood * 0.5)
        {
            Nudge(0.03, "the leader re-elected");
            Say($"{t} is re-elected.");
            return;
        }
        Nudge(0.05, "a new leader elected");
        FallHow = "election";
        Say($"Election day: {Appoint(null)} wins.");
    }

    void StepDown(Walker lw, double tenure)
    {
        string t = LeaderTitle;
        FallHow = "stepdown";
        Unseat(lw, false);
        if (tenure >= 2) Honour();
        LastElection = Age;
        Say($"{t} steps down. {Appoint(null)} {Govs.Succeed[GovKey]}.");
    }

    // Someone leaves a harsh town. Those who hate the leader go first (0.77), and a close friend may go with them.
    void Emigrate()
    {
        var cand = Walkers.Where(w => !w.Lead && !w.Gone && w.Mode != Mode.Walk).ToList();
        if (cand.Count == 0) return;
        var gentler = Sisters.Where(s => Govs.Get(s.Gov).Harsh < G.Harsh).ToList();
        var dest = gentler.Count > 0 ? Weighted(gentler, s => 1.3 - Govs.Get(s.Gov).Harsh) : null;
        int lid = LeaderWalker?.Id ?? -1;
        double Hate(Walker w) => BondOf(w, lid, "grudge")?.Str ?? 0;
        var who = Weighted(cand, w => 1 + 4 * Hate(w));
        string to = dest != null ? $"to {dest.Name}" : "into the hills";
        SendAway(who, Goal.Leave, dest?.Name ?? "");
        // A close friend (FollowFriend) who is free to go follows half the time.
        var pal = who.Bonds.Where(b => b.Kind == "friend" && b.Str >= FollowFriend).Select(b => ById(b.Who))
            .FirstOrDefault(f => f != null && !f.Lead && f.Kind == "" && f.Mode != Mode.Walk && f.Goal is not (Goal.Leave or Goal.Taken));
        bool both = pal != null && Chance() < 0.5;
        if (both) SendAway(pal!, Goal.Leave, dest?.Name ?? "");
        string names = both ? $"{CitizenName(who)} and {CitizenName(pal!)}" : CitizenName(who);
        if (Hate(who) >= 0.3) Say($"{names} {(dest != null ? "slip" : "flee")}{(both ? "" : "s")} away {to}, cursing the {G.Title.ToLowerInvariant()}.");
        else if (both) Say($"{names} slip away {to} together.");
        else Say(dest != null ? $"Someone slips away {to}." : "Someone flees into the hills.");
    }

    // Guards lead someone out of town; they do not come back.
    // Who is given (an assassin) or picked; a cruel leader takes those who hate them first. Their friends remember.
    void Arrest(Walker? who = null, bool quiet = false)
    {
        var cand = who != null ? new List<Walker> { who } : Walkers.Where(w => !w.Lead && !w.Gone && w.Mode == Mode.Wait).ToList();
        if (cand.Count == 0 || Lots.Count == 0) return;
        if (who == null && HasTrait("cruel") && LeaderWalker is { } lw)
        {
            var foes = cand.Where(w => BondOf(w, lw.Id, "grudge") != null).ToList();
            if (foes.Count > 0) cand = foes;
        }
        var w = Pick(cand);
        if (who == null) TakenNews(w);
        Hold(w);   // StepGuards sends a guard to fetch them
        Nudge(-0.05, "an arrest");
        if (quiet) return;
        Say(Pick(new[] { "Someone is taken away.", "Guards lead someone out of town.", "A neighbour is taken in for questioning." }));
        if (G.Harsh >= 0.5 && chatRng.NextDouble() < 0.5) Npc("ringleader", "arrest");
    }

    // An arrested dot-man stops where they are (one walking stops at the next corner) and waits for the guard who fetches them.
    void Hold(Walker w)
    {
        if (w.InLine >= 0) LeaveLine(w);
        if (w.Mode == Mode.Inside && w.At >= 0 && w.At < Lots.Count) (w.X, w.Y) = DoorOf(w.At);
        if (w.Mode != Mode.Walk || w.Path.Count == 0) { w.Mode = Mode.Wait; w.Path.Clear(); }
        else if (w.Path.Count > 1) w.Path.RemoveRange(1, w.Path.Count - 1);   // stops at the next corner (always on a road)
        w.Until = long.MaxValue;
        w.Goal = Goal.Taken;
        w.To = "";
        w.Sleep = false;
        w.Lead = false;
        w.Held = true;
    }

    // Where a held dot-man will stand: the end of the walk they were on, or where they are.
    static (int x, int y) HeldSpot(Walker w) => w.Mode == Mode.Walk && w.Path.Count > 0 ? w.Path[^1] : (w.X, w.Y);

    // A guard walking in at the west gate, not yet doing anything.
    Walker NewGuard()
    {
        var g = NewWalker(0, 0);
        (g.X, g.Y) = Entrance();
        g.Mode = Mode.Wait;
        g.Until = TickNo + Secs(1, 4);
        g.Period = 2;
        g.Goal = Goal.Stroll;
        guards.Add(g);
        return g;
    }

    // Rationing: under a rule that bans hoarding, while people panic, a guard stands at each food counter by day.
    bool Rationing(double d) => d > 0.25 && Panic > 0.5 && FoodRule.hoard == 0 && G.Guards > 0;

    // Every guard on the picture is a real walker: patrols walk the roads under harsh rule, are posted at the food
    // counters while rationing, and fetch the arrested (a patrol if one is free, else one from the gate), then lead
    // them out of the west gate.
    void StepGuards()
    {
        int want = Lots.Count > 0 ? G.Guards : 0;
        var patrol = guards.Where(g => g.Goal is Goal.Stroll or Goal.Visit).ToList();
        for (int i = want; i < patrol.Count; i++) patrol[i].Gone = true;
        for (int i = patrol.Count; i < want; i++) NewGuard();

        // Each held prisoner needs a guard on the way: the nearest free patrol, or a new one from the gate.
        foreach (var w in Walkers)
        {
            if (!w.Held || w.Gone || guards.Any(g => g.Prisoner == w)) continue;
            var (hx, hy) = HeldSpot(w);
            var g = guards.Where(p => !p.Gone && p.Goal == Goal.Stroll && !Walkers.Any(k => k.Follow == p))
                          .OrderBy(p => Math.Abs(p.X - hx) + Math.Abs(p.Y - hy)).FirstOrDefault() ?? NewGuard();
            g.Prisoner = w;
            GoTo(g, hx, hy, Goal.Fetch, -1);
        }

        // Post free patrols at the counters while rationing; stand them down when it ends.
        double d = Daylight();
        bool ration = Rationing(d);
        foreach (var g in guards)
            if (g.Goal == Goal.Visit && (!ration || !SellsFood(g.Dest))) { g.Goal = Goal.Stroll; g.Mode = Mode.Wait; g.Until = TickNo; }
        if (ration)
            for (int r = 0; r < Visible; r++)
            {
                if (!SellsFood(r) || guards.Any(g => g.Goal == Goal.Visit && g.Dest == r)) continue;
                var (dx, dy) = DoorOf(r);
                var g = guards.Where(p => !p.Gone && p.Goal == Goal.Stroll && !Walkers.Any(k => k.Follow == p))
                              .OrderBy(p => Math.Abs(p.X - dx) + Math.Abs(p.Y - dy)).FirstOrDefault();
                if (g == null) break;   // fewer guards than counters: the rest go unguarded
                GoTo(g, dx + 1, dy, Goal.Visit, r);
            }

        foreach (var g in guards)
        {
            if (g.Gone) continue;
            if (g.Mode == Mode.Wait)
            {
                if (g.Goal == Goal.Fetch) { TakePrisoner(g); continue; }
                if (TickNo >= g.Until)
                {
                    if (g.Goal == Goal.Taken) g.Mode = Mode.Walk;
                    else if (g.Goal == Goal.Visit) g.Until = TickNo + 20;   // stays at the counter
                    else StrollSomewhere(g);
                }
                continue;
            }
            if (TickNo % g.Period != 0) continue;
            (g.PX, g.PY) = (g.X, g.Y);
            if (WalkStep(g))
            {
                if (g.Goal == Goal.Taken) g.Gone = true;
                else { g.Mode = Mode.Wait; g.Until = TickNo + Secs(2, 6); }
            }
        }
        guards.RemoveAll(g => g.Gone);
    }

    // A guard who reached the prisoner's spot: once the prisoner stands there, both walk to the west gate, the guard
    // a step behind. A prisoner who died meanwhile leaves the guard to walk out alone.
    void TakePrisoner(Walker g)
    {
        var w = g.Prisoner;
        var (gx, gy) = Entrance();
        if (w == null || w.Gone || !w.Held)
        {
            g.Prisoner = null;
            g.Path = Route(g.X, g.Y, gx, gy, g.Lane);
            g.Goal = Goal.Taken;
            g.Mode = Mode.Walk;
            return;
        }
        if (w.Mode == Mode.Walk) return;   // still finishing their walk to where they will be held
        w.Held = false;
        w.Path = Route(w.X, w.Y, gx, gy, w.Lane);
        w.Mode = Mode.Walk;
        g.Prisoner = null;
        (g.X, g.Y) = (w.X, w.Y);
        g.Path = w.Path.ToList();
        g.Goal = Goal.Taken;
        g.Period = w.Period;
        g.Until = TickNo + w.Period * 2;   // a step behind
    }
}
