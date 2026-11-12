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
    void Nudge(double d) => Mood = Clamp(Mood + d, 0, 1);

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

    // ---------- the leader's character ----------
    IEnumerable<Trait> LeaderTraits => Leader == null ? Enumerable.Empty<Trait>() : Leader.Traits.Where(Traits.Exists).Select(Traits.Get);
    double TraitMul(Func<Trait, double> f) => LeaderTraits.Aggregate(1.0, (a, t) => a * f(t));
    double TraitSum(Func<Trait, double> f) => LeaderTraits.Sum(f);
    public bool HasTrait(string key) => Leader?.Traits.Contains(key) == true;
    public string TraitWords => string.Join(", ", LeaderTraits.Select(t => t.Name));
    public string TraitBlurb => string.Join("; ", LeaderTraits.Select(t => t.Blurb));

    public string[] TestAppoint() => RollTraits();

    // Two traits that do not clash, leaning by how harsh the rule is. An heir often takes after a parent.
    string[] RollTraits(string[]? parent = null)
    {
        var got = new List<string>();
        if (parent != null && parent.Length > 0 && Chance() < 0.6) got.Add(Pick(parent));
        while (got.Count < 2)
        {
            var ok = Traits.All.Where(t => !got.Any(k => Traits.Clashes(k, t.Key))).ToList();
            got.Add(Weighted(ok, t => Traits.Weight(t, G.Harsh)).Key);
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

    // A new town starts under its elders, led by its first dot-man.
    public void ResetGov()
    {
        GovKey = "elders";
        Mood = G.Mood;
        Leader = null;
        LastElection = Age;
        Honoured = 0;
        Regnal = new();
        guards.Clear();
        foreach (var w in Walkers) w.Lead = false;
        Appoint(null);
    }

    // How a town chooses to be ruled, leaning by its scheme.
    string ChooseGov()
    {
        var lean = Govs.Lean[Scheme];
        return Weighted(Govs.All.Where(g => g.Key != "elders").ToList(), g => g.Lean * (lean.TryGetValue(g.Key, out var m) ? m : 1)).Key;
    }

    // After a load: keep the saved leader if exactly one dot-man carries the flag, else appoint one.
    public void RestoreLeader(string name, int gen, double since, string[]? traits = null)
    {
        var leads = Walkers.Where(w => w.Lead).ToList();
        foreach (var w in leads.Skip(1)) w.Lead = false;
        if (leads.Count == 0 || name == "")
        {
            Appoint(null);
            if (GovKey == "elders" && Tier >= 2) GovKey = ChooseGov();
            return;
        }
        Leader = new Leader { Name = name, Gen = Math.Max(1, gen), Since = since };
        // An older save has no traits: give the sitting leader some now.
        var kept = (traits ?? Array.Empty<string>()).Where(Traits.Exists).Take(2).ToArray();
        Leader.Traits = kept.Length == 2 && !Traits.Clashes(kept[0], kept[1]) ? kept : RollTraits();
        if (GovKey == "elders" && Tier >= 2) { GovKey = ChooseGov(); StaticDirty = true; }
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

    // A village outgrows its elders and chooses how it will be ruled.
    void MaybeChooseGov()
    {
        if (Tier != 2 || GovKey != "elders") return;
        string k = ChooseGov();
        SetGov(k, $"{Name} chooses {An(Govs.Get(k).Lower)}.", false);
    }

    // Make a dot-man the leader (a sovereign's heir shares their colour when one can be found); returns the new title.
    string Appoint(int? prefer, string[]? parent = null)
    {
        foreach (var w in Walkers) w.Lead = false;
        var pool = Walkers.Where(w => !w.Gone && w.Goal != Goal.Leave && w.Goal != Goal.Taken).ToList();
        if (pool.Count == 0) { Leader = null; return ""; }
        var same = prefer == null ? new List<Walker>() : pool.Where(w => w.C == prefer).ToList();
        Pick(same.Count > 0 ? same : pool).Lead = true;
        string nm = Pick(Govs.First);
        int gen = 1;
        if (GovKey == "crown") Regnal[nm] = gen = (Regnal.TryGetValue(nm, out var n) ? n : 0) + 1;
        Leader = new Leader { Name = nm, Gen = gen, Since = Age };
        Leader.Traits = RollTraits(parent);
        StaticDirty = true; // the seat's banner follows the leader's home
        return LeaderTitle;
    }

    // Change how the town is ruled; keep keeps the leader under a new title.
    void SetGov(string k, string line, bool keep)
    {
        GovKey = k;
        LastElection = Age;
        if (!keep || LeaderWalker == null) Appoint(null);
        else if (k == "crown" && Leader != null) Regnal[Leader.Name] = Leader.Gen = (Regnal.TryGetValue(Leader.Name, out var n) ? n : 0) + 1;
        guards.RemoveAll(g => g.Goal != Goal.Taken);
        StaticDirty = true;
        Say(line);
    }

    // Remove the leader from office (and from town if gone).
    void Unseat(Walker w, bool gone)
    {
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

    // Mood settles toward what the rule, the weather, the season and crowding allow.
    void UpdateMood(double dt)
    {
        double target = G.Mood;
        if (Stormy) target -= 0.08;
        else if (Weather == Wx.Clear && Daylight() > 0.3) target += 0.04;
        if (PopCap > 0 && Headcount >= PopCap) target -= 0.05;
        target += SeasonMood[SeasonNo];
        target += TraitSum(t => t.Mood);
        int sick = Walkers.Count(w => w.Sick && !w.Gone);
        if (sick > 0) target -= 0.03 + 0.25 * sick / Math.Max(1, Walkers.Count);
        if (Ill?.Sealed == true) target -= 0.04;
        if (WarOn != null) target += G.Harsh >= 0.8 ? 0 : -0.06; // only harsh rule thrives on war
        if (Famine) target -= 0.08;
        target -= Math.Min(0.1, (Ruins.Count + Fires.Count) * 0.02); // burnt lots and live fires weigh on the town
        Mood = Ease(Mood, Clamp(target, 0, 1), dt, DayMs * 2);
    }

    // Roll the dice of politics, once every PolStepMs: at most one big event.
    void Politics()
    {
        if (Lots.Count == 0 || Walkers.Count == 0) return;
        foreach (var sis in Sisters.ToList()) if (OncePer(25)) SisterChange(sis);
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
        if (g.Harsh < 0.8 && GovKey != "elders" && Mood < 0.3 && Odds(0.2 * revolt)) { Coup(lw); return; }
        if (GovKey != "elders" && many && Odds((0.016 + 0.12 * g.Harsh * (1 - Mood)) * TraitMul(t => t.Kill))) { Assassination(lw); return; }
        if (many && Odds(Math.Min(0.48, 0.032 + 0.024 * tenure))) { Death(lw, tenure); return; }
        if (g.Term > 0 && Age - LastElection >= g.Term * DayMs) { Election(); return; }
        if (elected && OncePer(17)) { StepDown(lw, tenure); return; }
        if (elected && Odds(0.04 * TraitMul(t => t.Scandal)))
        {
            Nudge(-0.15);
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

    void Death(Walker lw, double tenure)
    {
        string t = LeaderTitle;
        int c = lw.C;
        var parent = Leader?.Traits;
        bool proud = HasTrait("proud");
        Unseat(lw, true);
        Nudge(-0.03);
        if (GovKey != "party" && GovKey != "junta" && (GovKey == "crown" || tenure >= 2 || proud)) Honour(); // the proud see to their own statue
        if (GovKey == "party" && Chance() < Collapse)
        {
            SetGov(Chance() < 0.5 ? "council" : "commune", $"{t} is dead, and the Party state collapses.", false);
            return;
        }
        string n = Appoint(GovKey == "crown" ? c : null, GovKey == "crown" ? parent : null);
        LastElection = Age;
        string line = $"{t} has died. {n} {Govs.Succeed[GovKey]}.";
        if (GovKey == "crown") StartFestival(line); // a coronation
        Say(line);
    }

    void Assassination(Walker lw)
    {
        string t = LeaderTitle;
        int c = lw.C;
        var parent = Leader?.Traits;
        Unseat(lw, true);
        Nudge(-0.1);
        string line = $"{t} is assassinated.";
        if (GovKey == "crown") { Say($"{line} {Appoint(c, parent)} takes the throne."); return; }
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
        Nudge(0.25);
        string line = $"Revolution! The {old} falls; {t} flees{(dest != null ? " to " + dest.Name : "")}.";
        Npc("ringleader", "won", false, old);
        SetGov(Chance() < 0.5 ? "commune" : "council", line, false);
        StartFestival(line);
    }

    void Coup(Walker lw)
    {
        string t = LeaderTitle;
        Unseat(lw, false);
        Nudge(-0.1);
        SetGov("junta", $"A coup: the army deposes {t}.", false);
    }

    void Election()
    {
        LastElection = Age;
        string t = LeaderTitle;
        if (GovKey == "commune")
        {
            Appoint(null);
            Say($"{(Leader != null ? Leader.Name : "Someone")} takes a turn as speaker.");
            return;
        }
        if (Mood < 0.35 && Chance() < 0.3) { SetGov("party", $"A strongman wins the vote. {Name} becomes a Party state.", false); return; }
        if (Chance() < 0.25 + Mood * 0.5)
        {
            Nudge(0.03);
            Say($"{t} is re-elected.");
            return;
        }
        Nudge(0.05);
        Say($"Election day: {Appoint(null)} wins.");
    }

    void StepDown(Walker lw, double tenure)
    {
        string t = LeaderTitle;
        Unseat(lw, false);
        if (tenure >= 2) Honour();
        LastElection = Age;
        Say($"{t} steps down. {Appoint(null)} {Govs.Succeed[GovKey]}.");
    }

    // Someone gives up on harsh rule and slips out of town.
    void Emigrate()
    {
        var cand = Walkers.Where(w => !w.Lead && !w.Gone && w.Mode != Mode.Walk).ToList();
        if (cand.Count == 0) return;
        var gentler = Sisters.Where(s => Govs.Get(s.Gov).Harsh < G.Harsh).ToList();
        var dest = gentler.Count > 0 ? Weighted(gentler, s => 1.3 - Govs.Get(s.Gov).Harsh) : null;
        SendAway(Pick(cand), Goal.Leave, dest?.Name ?? "");
        Say(dest != null ? $"Someone slips away to {dest.Name}." : "Someone flees into the hills.");
    }

    // Guards lead someone out of town; they do not come back.
    void Arrest()
    {
        var cand = Walkers.Where(w => !w.Lead && !w.Gone && w.Mode == Mode.Wait).ToList();
        if (cand.Count == 0 || Lots.Count == 0) return;
        var w = Pick(cand);
        SendAway(w, Goal.Taken);
        var g = NewWalker(0, 0);
        g.X = w.X;
        g.Y = w.Y;
        g.Path = w.Path.ToList();
        g.Goal = Goal.Taken;
        g.Mode = Mode.Wait;
        g.Period = w.Period;
        g.Until = TickNo + w.Period * 2; // a step behind
        guards.Add(g);
        Nudge(-0.05);
        Say(Pick(new[] { "Someone is taken away.", "Guards lead someone out of town.", "A neighbour is taken in for questioning." }));
        if (G.Harsh >= 0.5 && chatRng.NextDouble() < 0.5) Npc("ringleader", "arrest");
    }

    // Patrols walk the roads under harsh rule; escorts leave with their prisoner.
    void StepGuards()
    {
        int want = Lots.Count > 0 ? G.Guards : 0;
        var patrol = guards.Where(g => g.Goal != Goal.Taken).ToList();
        for (int i = want; i < patrol.Count; i++) patrol[i].Gone = true;
        for (int i = patrol.Count; i < want; i++)
        {
            var g = NewWalker(0, 0);
            (g.X, g.Y) = Entrance();
            g.Mode = Mode.Wait;
            g.Until = TickNo + Secs(1, 4);
            g.Period = 2;
            g.Goal = Goal.Stroll;
            guards.Add(g);
        }
        foreach (var g in guards)
        {
            if (g.Gone) continue;
            if (g.Mode == Mode.Wait)
            {
                if (TickNo >= g.Until)
                {
                    if (g.Goal == Goal.Taken) g.Mode = Mode.Walk;
                    else StrollSomewhere(g);
                }
                continue;
            }
            if (TickNo % g.Period != 0) continue;
            if (WalkStep(g))
            {
                if (g.Goal == Goal.Taken) g.Gone = true;
                else { g.Mode = Mode.Wait; g.Until = TickNo + Secs(2, 6); }
            }
        }
        guards.RemoveAll(g => g.Gone);
    }
}
