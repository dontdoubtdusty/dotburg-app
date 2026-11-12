namespace Dotburg;

// A chartered town: only its people, its rule, its relation to us and its treasury are kept.
class Sister
{
    public string Name = "";
    public int Scheme;
    public int[] Counts = new int[Content.DotColors.Length];   // dot-men by colour
    public int Cap;                                            // how many it can regrow to
    public string Gov = "council";
    public double? Rel;                                        // -1..1 toward this town; null until first looked at
    public double? Gold;                                       // treasury 0..100; null until first looked at
    public int Total => Counts.Sum();
}

// Stage 4: the region. Charter and sister towns, migration, treasury and relations, envoys, aid, letters,
// refugees, fairs, spies, absorption and collapse. Visitors (envoys, teams, spies, refugees, carts) live here too.
partial class Sim
{
    const int SisterFloor = 5;              // a sister town never drops below this many dot-men
    const double RegrowChance = 0.08;       // per growth event, a shrunken sister gains a native
    const double MigrantPerSister = 0.15;
    const double MigrantMax = 0.6;
    const double PassOn = 0.5;              // chance a sister that lost someone takes one from another sister
    const double FateGapMs = DayMs * 100;   // rare fates (collapse) are at least this far apart

    public static readonly int[] CircusColors = { 0xd9432f, 0xf5f5f5, 0xf2c14e, 0x2f62c9 };
    static readonly Dictionary<string, int> VisitorColors = new()
    {
        ["pedlar"] = 0x9a7448, ["bard"] = 0xd070c0, ["clown"] = 0xf0f0f0, ["thief"] = 0x3a3a4a, ["cart"] = 0x8a6a3a,
        ["spy"] = 0x4a3a5a, ["hero"] = 0x30b0d0, ["prophet"] = 0xf0e8d0, ["hermit"] = 0x6a7a4a,
    };
    static readonly Dictionary<string, int> VisitorHead = new()
    {
        ["envoy"] = 0xf5f5f5, ["team"] = 0xf5f5f5, ["refugee"] = 0x8a7a5a, ["spy"] = 0xd9432f, ["pedlar"] = 0x6a4a2a,
        ["hero"] = 0xd9432f, ["prophet"] = 0xf2c14e, ["hermit"] = 0x8a7a5a,
    };
    static readonly string[] Goods = { "cloth", "salt", "spices", "tools", "wine", "wool", "pottery", "news" };

    // ---------- state ----------
    public List<Sister> Sisters = new();
    public double Gold = 12;                // this town's treasury, 0..100 (not growth units)
    public double EvClock;                  // ms of event time, region-wide (a charter does not reset it)
    public double LastFate;                 // EvClock of the last rare fate
    public int Weddings;                    // births owed to recent weddings (used by stage 5)
    public List<Walker> Visitors = new();   // envoys, teams, spies, refugees, carts: not citizens, not saved

    // ---------- small helpers ----------
    public static string WealthWord(double g) => g < 8 ? "broke" : g < 25 ? "modest" : g < 60 ? "well-off" : "rich";
    double BaseRel(Sister s) => Clamp(0.4 - 0.8 * Math.Abs(Govs.Get(s.Gov).Harsh - G.Harsh) + (s.Gov == GovKey ? 0.15 : 0), -1, 1);
    public double RelOf(Sister s) { s.Rel ??= BaseRel(s); return s.Rel.Value; }
    double RelPeek(Sister s) => s.Rel ?? BaseRel(s);   // the same, without fixing it (the Messenger must not change the sim)
    public double GoldOf(Sister s) { s.Gold ??= 15; return s.Gold.Value; }
    void Bump(Sister s, double d) => s.Rel = Clamp(RelOf(s) + d, -1, 1);
    void Purse(Sister s, double d) => s.Gold = Clamp(GoldOf(s) + d, 0, 100);
    void Pay(double d) => Gold = Clamp(Gold + d, 0, 100);
    int DotOf(Sister s) => Content.Schemes[s.Scheme].Dot;
    Sister? SisterNamed(string name) => Sisters.FirstOrDefault(s => s.Name == name);
    string RandomSisterGov() => Weighted(Govs.All.Where(g => g.Key != "elders").ToList(), g => g.Lean).Key;

    // How a sister town regards this one, as a word and a colour for the legend.
    public (string word, int col) Attitude(Sister s)
    {
        double r = RelOf(s);
        if (r >= 0.6) return ("allied", 0x58c8b0);
        if (r >= 0.25) return ("friendly", 0x6cc070);
        if (r > -0.1) return ("cordial", 0xa8c878);
        if (r > -0.45) return Govs.Get(s.Gov).Harsh > G.Harsh ? ("wary", 0xe0983a) : ("scornful", 0xd07070);
        return ("hostile", 0xe05040);
    }

    // The newest sisters as 4 x 4 icons: a roof line in the town's colour over 12 dot pixels coloured by its
    // population mix, and a flag pixel in the rule's banner colour at the corner.
    public const int IconSize = 4;
    public int[] IconPixels(Sister s)
    {
        var px = new int[IconSize * IconSize];
        Array.Fill(px, -1);
        var sch = Content.Schemes[s.Scheme];
        for (int x = 1; x < IconSize - 1; x++) px[x] = sch.Roofs[0][0];
        px[0] = Govs.Get(s.Gov).Banner;
        int tot = s.Total, slots = IconSize * (IconSize - 1);
        var order = new List<int> { sch.Dot };
        for (int c = 0; c < Content.DotColors.Length; c++) if (c != sch.Dot) order.Add(c);
        // Split the 12 pixels among the colours by largest remainder, home colour first.
        var share = order.Select(c => tot > 0 ? s.Counts[c] * (double)slots / tot : 0).ToArray();
        var whole = share.Select(v => (int)Math.Floor(v)).ToArray();
        int left = slots - whole.Sum();
        foreach (int i in Enumerable.Range(0, order.Count).OrderByDescending(i => share[i] - whole[i]))
            if (left > 0 && share[i] > 0) { whole[i]++; left--; }
        var fill = new List<int>();
        for (int i = 0; i < order.Count; i++) for (int j = 0; j < whole[i]; j++) fill.Add(Content.DotColors[order[i]]);
        while (fill.Count < slots) fill.Add(sch.Wall);
        for (int j = 0; j < slots; j++) px[(1 + j / IconSize) * IconSize + j % IconSize] = fill[j];
        return px;
    }

    // Rebuild a sister from a save, clamping anything odd.
    public Sister SisterFromSave(string name, int scheme, int[] counts, int cap, string gov, double? rel, double? gold)
    {
        var s = new Sister
        {
            Name = string.IsNullOrEmpty(name) ? "Old town" : name,
            Scheme = Clamp(scheme, 0, Content.Schemes.Length - 1),
            Cap = Math.Max(0, cap),
            Gov = Govs.Exists(gov) && gov != "elders" ? gov : RandomSisterGov(),
            Rel = rel.HasValue ? Clamp(rel.Value, -1, 1) : null,
            Gold = gold.HasValue ? Clamp(gold.Value, 0, 100) : null,
        };
        for (int c = 0; c < s.Counts.Length; c++) s.Counts[c] = c < counts.Length ? Math.Max(0, counts[c]) : 0;
        return s;
    }

    // ---------- the charter ----------
    // The finished City becomes a sister town; a new hamlet is founded in the next scheme.
    void Charter()
    {
        var counts = new int[Content.DotColors.Length];
        foreach (var w in Walkers) if (!w.Gone) counts[w.C]++;
        Sisters.Add(new Sister { Name = Name, Scheme = Scheme, Counts = counts, Cap = Walkers.Count, Gov = GovKey, Gold = Gold });
        Gold = 10;
        string old = Name;
        Founded++;
        Scheme = (Founded - 1) % Content.Schemes.Length;
        Name = Content.TownName(Rng, Sisters.Select(s => s.Name).Append(old).ToHashSet(), Founded);
        Tier = 1;
        Built = new() { "house" };
        Walkers = new() { NewWalker(Sc.Dot, 0) };
        Age %= DayMs;
        WorksList.Clear();
        ResetFires();
        puffs.Clear();
        cat = null;
        festUntil = 0;
        festWait = false;
        Visitors.Clear();
        Ill = null; // the old town keeps its sickness
        WarOn = null; // and its soldiers
        ResetGov();
        PlaceAll();
        StaticDirty = true;
        Say($"{old} is chartered. {Name} is founded.");
        RetireNotables();
        Tell(SisterNamed(old), "charter");
    }

    // Sisters that shrank from migration slowly refill with their own natives.
    void RegrowSisters()
    {
        foreach (var s in Sisters)
            if (s.Total < s.Cap && Chance() < RegrowChance) s.Counts[Content.Schemes[s.Scheme].Dot]++;
    }

    // Take one dot-man out of a sister town, weighted by colour; returns the colour.
    int TakeFrom(Sister s)
    {
        double r = Chance() * s.Total;
        for (int c = 0; c < s.Counts.Length; c++)
        {
            r -= s.Counts[c];
            if (r < 0) { s.Counts[c]--; return c; }
        }
        return Content.Schemes[s.Scheme].Dot;
    }

    // How readily people move from sister `s` to this town: like-minded towns trade more, and people flee
    // harsher rule for gentler.
    double Pull(Sister s)
    {
        double a = Govs.Get(s.Gov).Harsh, b = G.Harsh;
        return (1.2 - Math.Abs(a - b) * 0.6) * (1 + Math.Max(0, a - b) * 2);
    }

    // A newcomer's colour: a native, or a migrant from a sister town, who may be replaced there by someone
    // from yet another sister.
    (int c, string? from) NewcomerColour()
    {
        var able = Sisters.Where(s => s.Total > SisterFloor).ToList();
        double chance = Math.Min(MigrantMax, MigrantPerSister * able.Count * G.Welcome);
        if (able.Count == 0 || Chance() >= chance) return (Sc.Dot, null);
        var from = Weighted(able, Pull);
        int c = TakeFrom(from);
        var others = able.Where(o => o != from && o.Total > SisterFloor).ToList();
        if (others.Count > 0 && Chance() < PassOn) from.Counts[TakeFrom(Pick(others))]++;
        return (c, from.Name);
    }

    // A newcomer of colour `c` takes the emptiest home and walks in at the west gate.
    void SettleColour(int c)
    {
        var w = NewWalker(c, Math.Max(0, EmptiestHome()));
        Walkers.Add(w);
        if (Lots.Count == 0) return;
        (w.X, w.Y) = Entrance();
        w.At = -1;
        w.Mode = Mode.Wait;
        w.Until = TickNo;
        int hr = HomeOf(w);
        if (hr >= 0)
        {
            var (dx, dy) = DoorOf(hr);
            GoTo(w, dx, dy, Goal.Home, hr);
        }
    }

    // A sister town changes how it is ruled (rare, rolled with politics).
    void SisterChange(Sister s)
    {
        var k = Weighted(Govs.All.Where(g => g.Key != "elders" && g.Key != s.Gov).ToList(), g => g.Lean);
        s.Gov = k.Key;
        Say($"News from {s.Name}: {An(k.Lower)} takes power.");
        Tell(s, "rule");
    }

    // ---------- visitors ----------
    // A visitor walks in at the west gate and stays a while, then leaves.
    public Walker? Visitor(string kind, int stay)
    {
        if (Lots.Count == 0) return null;
        var v = NewWalker(0, 0);
        v.Kind = kind;
        (v.X, v.Y) = Entrance();
        v.Mode = Mode.Wait;
        v.Until = TickNo + Secs(0, 2);
        v.Stay = TickNo + stay;
        v.Period = kind == "cart" ? 3 : 2;
        Visitors.Add(v);
        return v;
    }

    // Visitors in a sister town's colours.
    List<Walker> SisterVisitors(Sister s, string kind, int n, int stay)
    {
        var o = new List<Walker>();
        for (int i = 0; i < n; i++)
        {
            var v = Visitor(kind, stay);
            if (v != null) { v.C = DotOf(s); o.Add(v); }
        }
        return o;
    }

    // Each visitor heads for what suits them, or roams; when the stay is over they leave by the west gate.
    void StepVisitors()
    {
        foreach (var v in Visitors)
        {
            if (v.Gone) continue;
            if (v.Mode == Mode.Wait)
            {
                if (TickNo < v.Until) continue;
                if (TickNo >= v.Stay) { SendAway(v, Goal.Leave); continue; }
                int shop = Built.FindIndex(t => t == "market" || t == "shop");
                int inn = Built.FindIndex(t => t == "inn" || t == "hotel");
                if (v.Kind == "cart" && shop >= 0 && shop < Lots.Count) Visit(v, shop);
                else if (v.Kind == "bard" && inn >= 0) Visit(v, inn);
                else if (v.Kind == "clown" || v.Kind == "team") JoinFestival(v);
                else if (v.Kind == "envoy" && SeatRank() >= 0) Visit(v, SeatRank());
                else StrollSomewhere(v);
                continue;
            }
            if (TickNo % v.Period != 0) continue;
            int x = v.X;
            if (WalkStep(v))
            {
                if (v.Goal == Goal.Leave) v.Gone = true;
                else { v.Mode = Mode.Wait; v.Until = TickNo + (v.Kind == "cart" ? Secs(20, 40) : Secs(2, 8)); }
            }
            if (v.Kind == "cart" && v.X != x) v.Lane = v.X > x ? 1 : 0; // remembers which way the horse faces
        }
        Visitors.RemoveAll(v => v.Gone);
    }

    // ---------- aid, envoys, letters, refugees, fairs, spies ----------
    // A friendly, solvent sister sends aid when this town is struck; true if one did.
    bool AskAid(bool speak)
    {
        var givers = Sisters.Where(s => RelOf(s) >= 0.25 && GoldOf(s) >= 15 && WarOn?.Foe != s.Name).ToList();
        if (givers.Count == 0 || Chance() > 0.7)
        {
            if (speak) Say("Letters go out asking for help; nobody answers.");
            return false;
        }
        var s = Weighted(givers, g => RelOf(g) + 0.5);
        Purse(s, -8);
        Pay(8);
        Bump(s, 0.05);
        Say(Pick(new[] { $"{s.Name} sends grain and timber.", $"Wagons of aid arrive from {s.Name}." }));
        Tell(s, "aid");
        return true;
    }

    // Envoys walk in to the hall; what they bring depends on how the town regards us.
    void EnvoyVisit(Sister s)
    {
        double r = RelOf(s);
        var vs = SisterVisitors(s, "envoy", 2, Secs(40, 70));
        if (r >= 0.25)
        {
            Say($"Envoys from {s.Name} bring gifts.");
            Tell(s, "gifts");
            Pay(4); Purse(s, -2); Bump(s, 0.04);
        }
        else if (r > -0.1)
        {
            Say($"Envoys from {s.Name} come to talk trade.");
            Tell(s, "trade");
            Pay(2); Purse(s, 1); Bump(s, 0.03);
        }
        else if (G.Harsh >= 0.6 && r < -0.45)
        {
            foreach (var v in vs) v.Stay = TickNo + Secs(3, 6);
            Say($"Guards turn {s.Name}'s envoys away at the gate.");
            Tell(s, "turned");
            Bump(s, -0.08);
        }
        else
        {
            Say($"Envoys from {s.Name} deliver a stiff note.");
            Tell(s, "note");
            Nudge(-0.01);
            Bump(s, -0.02);
        }
    }

    // A letter with something in it: a proposal, a plea or a demand.
    void Letter(Sister s)
    {
        double r = RelOf(s);
        if (r >= 0.4)
        {
            Say($"A letter from {s.Name} proposes a wedding between the towns.");
            Tell(s, "wedding");
            Weddings = Math.Min(3, Weddings + 1);
            Bump(s, 0.03);
        }
        else if (r > -0.1 && GoldOf(s) < 10)
        {
            if (Gold >= 15)
            {
                Pay(-8); Purse(s, 8); Bump(s, 0.1);
                Say($"{Name} answers {s.Name}'s plea with grain.");
                Tell(s, "plea.yes");
            }
            else
            {
                Bump(s, -0.03);
                Say($"A letter from {s.Name} begs for grain; the treasury is bare.");
                Tell(s, "plea.no");
            }
        }
        else if (r < -0.3)
        {
            if (G.Harsh >= Govs.Get(s.Gov).Harsh || Gold < 6)
            {
                Bump(s, Gold < 6 ? -0.08 : -0.12);
                Say($"{s.Name} demands tribute and is refused.");
                Tell(s, "tribute.refused");
            }
            else
            {
                Pay(-6); Purse(s, 6); Bump(s, 0.03); Nudge(-0.02);
                Say($"The town pays tribute to {s.Name}.");
                Tell(s, "tribute.paid");
            }
        }
    }

    // A harsh sister falls to revolution: its refugees arrive, and are welcomed or turned away.
    void SisterRevolt(Sister s)
    {
        var k = Weighted(Govs.All.Where(g => g.Key != "elders" && g.Harsh < 0.5).ToList(), g => g.Lean);
        s.Gov = k.Key;
        Tell(s, "revolution");
        if (!Refugees(s, $"{s.Name} falls to revolution")) Say($"{s.Name} falls to revolution: {An(k.Lower)} takes power.");
    }

    // A fallen sister's people reach the gate: taken in if the rule is gentle and there is room, else turned back.
    // False if none could leave.
    bool Refugees(Sister s, string fall)
    {
        int n = Math.Min(4, (int)Math.Floor(s.Total * 0.1));
        var cs = new List<int>();
        for (int i = 0; i < n && s.Total > SisterFloor; i++) cs.Add(TakeFrom(s));
        if (cs.Count == 0) return false;
        if (G.Harsh < 0.7 && Headcount + cs.Count <= PopCap)
        {
            foreach (int c in cs) SettleColour(c);
            Say($"{fall}; refugees reach {Name} and are taken in.");
            Tell(s, "refugees.in");
        }
        else
        {
            foreach (int c in cs)
            {
                var v = Visitor("refugee", Secs(8, 14));
                if (v != null) v.C = c;
            }
            Say($"Refugees from {s.Name} reach the gate; guards turn them back.");
            Tell(s, "refugees.out");
        }
        return true;
    }

    // A visiting team: a festival in the sister's colours, and a result (rigged under harsh rule).
    void Fair(Sister s)
    {
        SisterVisitors(s, "team", 3, Secs(30, 45));
        int c = Content.DotColors[DotOf(s)];
        StartFestival($"A team from {s.Name} arrives for the match.", new[] { c, 0xf5f5f5, c, 0xf5f5f5 });
        Pay(3);
        Purse(s, 2);
        if (G.Harsh >= 0.7)
        {
            Bump(s, -0.1);
            Say($"The match against {s.Name} is rigged; their team goes home bruised.");
            Tell(s, "rigged");
        }
        else if (Chance() < 0.5)
        {
            Nudge(0.04);
            Say($"Our team beats {s.Name}'s; the crowd roars.");
            Tell(s, "fairwon");
        }
        else
        {
            Bump(s, 0.05);
            Say($"{s.Name}'s team wins; the crowd cheers anyway.");
            Tell(s, "fairlost");
        }
    }

    // A harsh, unfriendly sister sends a spy: caught by guards, or it steals or burns.
    void Spy(Sister s)
    {
        var vs = SisterVisitors(s, "spy", 1, Secs(25, 40));
        double caught = guards.Count > 0 ? 0.7 : 0.3;
        if (Chance() < caught)
        {
            foreach (var v in vs) v.Stay = TickNo + Secs(4, 8);
            Bump(s, -0.1);
            Nudge(0.01);
            Say($"Guards catch a spy from {s.Name}.");
            Tell(s, "spycaught");
        }
        else if (Chance() < 0.5)
        {
            Pay(-3);
            Bump(s, -0.05);
            Say($"Papers go missing; a spy from {s.Name} is suspected.");
            Tell(s, "spypapers");
        }
        else
        {
            Arson($"A fire is traced to a saboteur from {s.Name}.");
            Tell(s, "spyfire");
            Bump(s, -0.05);
        }
    }

    // ---------- fates ----------
    bool FateReady => EvClock - LastFate > FateGapMs;

    // A sister town is gone. If the region is thin a new settlement is founded.
    void DropSister(Sister s)
    {
        ForgetSister(s);
        Sisters.Remove(s);
        LastFate = EvClock;
    }

    void FoundColony()
    {
        if (Sisters.Count >= 2) return;
        string nm = Content.TownName(Rng, Sisters.Select(s => s.Name).Append(Name).ToHashSet(), Founded);
        int sch = Founded % Content.Schemes.Length;
        var counts = new int[Content.DotColors.Length];
        counts[Content.Schemes[sch].Dot] = 6;
        Sisters.Add(new Sister { Name = nm, Scheme = sch, Counts = counts, Cap = 12, Gov = RandomSisterGov(), Gold = 8 });
        Founded++;
        Say($"A new settlement, {nm}, is founded in the hills.");
        SignIn(Sisters[^1]);
    }

    void Absorb(Sister win, Sister lose)
    {
        for (int c = 0; c < win.Counts.Length; c++) win.Counts[c] += (int)Math.Floor(lose.Counts[c] * 0.6);
        win.Cap += (int)Math.Floor(lose.Cap * 0.5);
        Purse(win, GoldOf(lose) * 0.5);
        DropSister(lose);
        Say($"{win.Name} absorbs {lose.Name}.");
        Gossip(win, lose, "absorb");
        SignOff(lose, "absorbed", win.Name);
        FoundColony();
    }

    void CollapseSister(Sister s)
    {
        var cs = new List<int>();
        for (int i = (int)Math.Ceiling(s.Total * 0.3); i > 0 && s.Total > 0; i--) cs.Add(TakeFrom(s));
        DropSister(s);
        if (G.Harsh < 0.7) foreach (int c in cs) if (Headcount < PopCap) SettleColour(c);
        Say($"{s.Name} collapses; its people scatter.");
        SignOff(s, "collapse");
        FoundColony();
    }

    // ---------- the region's business ----------
    // Rolled once every quarter day, with politics: wealth, relations, envoys, letters, fairs, spies, pacts
    // and rare fates. A sister "struck" by war or blight (War.cs) loses gold faster and may get our aid.
    void Region()
    {
        if (Sisters.Count == 0) return;
        double Rnd() => Chance();
        int shops = Built.Count(t => t is "shop" or "market" or "bakery" or "inn" or "hotel");
        // Income from trade, upkeep by size, and the rich spend down toward about 40.
        Pay(0.6 + 0.2 * shops + (Mood - 0.5) * 0.8 - 0.3 - Headcount * 0.015 - (WarOn != null ? 2 : 0) - Math.Max(0, Gold - 40) * 0.06 + TraitSum(t => t.Gold));
        foreach (var s in Sisters.ToList())
        {
            // Note: the gold and relation drifts here are amounts per check (every PolStepMs), not per day.
            double struck = Struck(s);
            Purse(s, 0.6 - 0.3 - s.Total * 0.012 - struck - Math.Max(0, GoldOf(s) - 40) * 0.04 + (Rnd() - 0.5) * 0.9 + (OncePer(12) ? RandInt(-12, 12) : 0));
            // This town helps a friendly sister in trouble.
            if (struck > 0 && RelOf(s) >= 0.25 && Gold >= 15 && Odds(1))
            {
                Pay(-8); Purse(s, 8); Bump(s, 0.12);
                Say($"{Name} sends aid to {s.Name}.");
                Tell(s, "thanks");
            }
            s.Rel = RelOf(s) + (BaseRel(s) - RelOf(s)) * 0.03;
            // A harsh sister can fall to revolution.
            if (Govs.Get(s.Gov).Harsh >= 0.6 && OncePer(60)) SisterRevolt(s);
        }
        if (Sisters.Count == 0) return;
        // Per day: envoys about once in 12 days per sister (up to four), letters once in 8, fairs once in 20 per friend.
        if (WarOn == null && Odds(0.08 * Math.Min(4, Sisters.Count))) EnvoyVisit(Weighted(Sisters, s => 0.3 + Math.Max(0, RelOf(s) + 0.5)));
        if (OncePer(8)) Letter(Pick(Sisters));
        var friends = Sisters.Where(s => RelOf(s) >= 0.25).ToList();
        if (friends.Count > 0 && WarOn == null && Daylight() > 0.4 && TickNo >= festUntil && Odds(0.048 * friends.Count)) Fair(Pick(friends));
        var foes = Sisters.Where(s => RelOf(s) < 0 && Govs.Get(s.Gov).Harsh >= 0.6).ToList();
        if (foes.Count > 0 && Odds(0.04 * foes.Count)) Spy(Pick(foes));
        // Sister news: two sisters make a pact or fall out.
        if (Sisters.Count >= 2 && OncePer(6))
        {
            var a = Pick(Sisters);
            var b = Pick(Sisters.Where(x => x != a).ToList());
            if (Math.Abs(Govs.Get(a.Gov).Harsh - Govs.Get(b.Gov).Harsh) < 0.35)
            {
                Purse(a, 3); Purse(b, 3);
                Say($"{a.Name} and {b.Name} sign a trade agreement.");
                Gossip(a, b, "pact");
            }
            else { Say($"{a.Name} and {b.Name} quarrel over a border."); Gossip(a, b, "quarrel"); }
        }
        // A ruined or tiny sister can collapse (rare).
        if (FateReady)
        {
            var weak = Sisters.Where(s => GoldOf(s) <= 2 && (s.Total <= 8 || BlightOn?.Towns.Contains(s.Name) == true)).ToList();
            if (weak.Count > 0 && OncePer(12)) CollapseSister(Pick(weak));
        }
    }

    // Events that tie the region to the town, rolled with politics (stage 5 adds the rest of the mod's events()).
    void Events()
    {
        if (Lots.Count == 0) return;
        // A caravan from a sister town: a horse and wagon to the market, a little trade.
        if (Sisters.Count > 0 && WarOn == null && BlightOn == null && Odds(0.048 * Math.Min(4, Sisters.Count) * (0.5 + Math.Min(1.5, Gold / 30))))
        {
            var from = Weighted(Sisters, s => Pull(s) * (0.5 + GoldOf(s) / 40));
            Visitor("cart", Secs(60, 90));
            Earn(2);
            Grow();
            Pay(3);
            Purse(from, 1);
            Bump(from, 0.02);
            Say($"A caravan from {from.Name} arrives with {Pick(Goods)}.");
            Tell(from, "caravan");
        }
        Population();
        FireDice();
        RebuildRuins();
        MaybeOutbreak();
        MaybeWar();
        StepWar();
        StepSisterWar();
        StepBlight();
        Chatter2();
        Region();
    }
}
