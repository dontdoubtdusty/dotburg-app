namespace Dotburg;

// Growth: units become newcomers, buildings, upgrades and new tiers.
partial class Sim
{
    // Units per growth step (later stages: rule and harvest change it).
    public int Cost => Math.Max(1, Round(Costs[Tier - 1] * G.CostMul * (Famine ? FamineCost : 1)));
    int HomeCap(string t) => Content.IsHome(t) ? Content.HomeCap[t] + G.HomeBonus : 0;
    public int Headcount => Walkers.Count(w => w.Kid == 0);
    // Newcomers only come while the stores can feed one more mouth until the next harvest (Food.cs).
    public bool FedEnough => Quiet || Walkers.Count(w => !w.Gone && w.Kind == "") < 4 || FoodTotal() >= (Eaters + 1) * NeedPerDay * (DaysToHarvest() + 3);

    // How many dot-men the town's homes and its tier allow.
    public int PopCap
    {
        get
        {
            int room = 0;
            foreach (var t in Built) room += HomeCap(t);
            return Math.Min(room, PopCaps[Tier - 1]);
        }
    }

    public void Earn(double n)
    {
        Pool += n;
        Lifetime += n;
    }

    // What the building on lot `rank` becomes at the current tier, if anything.
    string? UpgradeFor(int rank)
    {
        string t = Built[rank];
        if (t == "ruin") return null;
        if (Tier >= 4 && rank == 0) return t == "tower" ? null : "tower";
        if (Tier >= 2 && t == "house") return "house2";
        if (Tier >= 2 && t == "well") return "fountain";
        if (Tier >= 3 && t == "shop") return "market";
        if (Tier >= 3 && t == "house2" && rank > 0)
        {
            if (!Built.Contains("hall")) return "hall";
            if (!Built.Contains("school")) return "school";
            if (rank % 2 == 0) return "row";
        }
        if (Tier >= 4 && t == "house2") return "row";
        if (Tier >= 4 && t == "church") return "cathedral";
        if (Tier >= 4 && t == "inn") return "hotel";
        return null;
    }

    // The next upgrade, centre lot first.
    (int rank, string to)? NextUpgrade()
    {
        for (int r = 0; r < Built.Count; r++)
        {
            var to = UpgradeFor(r);
            if (to != null) return (r, to);
        }
        return null;
    }

    // What the next growth step will be, for the legend.
    public string NextThing()
    {
        if (Headcount < PopCap) return FedEnough ? "a newcomer" : "a newcomer, when there is food to spare";
        if (Built.Count < Lots.Count) return Content.Label(Content.TypeAt(Built.Count));
        var u = NextUpgrade();
        if (u != null) return $"{Content.Label(Built[u.Value.rank])} to {Content.Label(u.Value.to)}";
        if (Tier < 4) return $"becoming a {TierNames[Tier].ToLower()}";
        return "the charter";
    }

    // Spend whole units on newcomers, buildings and upgrades.
    public bool Grow()
    {
        bool changed = false;
        while (Pool >= Cost)
        {
            if (!GrowOnce()) break;
            Pool -= Cost;
            RegrowSisters();
            changed = true;
        }
        return changed;
    }

    // One growth event: a newcomer, a building, an upgrade or a new tier. False if there is nothing left to do.
    bool GrowOnce()
    {
        if (Headcount < PopCap)
        {
            if (!FedEnough) return false;   // settlers do not come to a hungry town: growth waits for food
            AddDot();
            return true;
        }
        if (Built.Count < Lots.Count)
        {
            string t = Content.TypeAt(Built.Count);
            Built.Add(t);
            StartWorks(Built.Count - 1, t, null);
            StaticDirty = true;
            return true;
        }
        var u = NextUpgrade();
        if (u != null)
        {
            string old = Built[u.Value.rank];
            Built[u.Value.rank] = u.Value.to;
            Rehome();
            StartWorks(u.Value.rank, u.Value.to, old);
            StaticDirty = true;
            return true;
        }
        if (Tier < 4)
        {
            Tier++;
            StaticDirty = true;
            Say($"{Name} is now {An(TierNames[Tier - 1].ToLower())}.");
            var fr = Sisters.Where(s => RelPeek(s) >= 0.25).ToList();
            if (fr.Count > 0) Tell(CPick(fr), "tierup");
            StartFestival("Bunting goes up across the square.");
            MaybeChooseGov();
            return true;
        }
        Charter();
        return true;
    }

    // The home with the most spare room; -1 if there is none.
    int EmptiestHome()
    {
        var count = new Dictionary<int, int>();
        for (int i = 0; i < Built.Count; i++) if (Content.IsHome(Built[i])) count[i] = 0;
        foreach (var w in Walkers) if (count.ContainsKey(w.H)) count[w.H]++;
        int home = -1, best = int.MinValue;
        foreach (var (i, n) in count)
        {
            int spare = HomeCap(Built[i]) - n;
            if (spare > best) { best = spare; home = i; }
        }
        return home;
    }

    // Dot-men whose home was turned into something else move to the emptiest home.
    void Rehome()
    {
        foreach (var w in Walkers) if (!Content.IsHome(w.H < Built.Count ? Built[w.H] : "")) w.H = Math.Max(0, EmptiestHome());
    }

    // A newcomer takes the emptiest home and walks in from the west gate.
    void AddDot()
    {
        var nc = NewcomerColour();
        Say(nc.from != null ? $"Someone moves in from {nc.from}." : Pick(new[] { "A newcomer walks in.", "A traveller settles down.", "A new face in town." }));
        SettleColour(nc.c);
        Provisions();
    }

    // ---------- scaffolding ----------
    void StartWorks(int rank, string to, string? up)
    {
        if (Quiet || rank >= Lots.Count) { Say(BuiltLine(to, up)); return; }
        if (WorksList.Count >= 4) FinishWorks(WorksList[0]);
        WorksList.RemoveAll(w => w.Rank == rank);
        WorksList.Add(new Works { Rank = rank, To = to, Up = up, Until = TickNo + Secs(WorksMs[0] / 1000 * StoneK, WorksMs[1] / 1000 * StoneK) });   // stone from the rocks speeds it
        StaticDirty = true;
    }

    void FinishWorks(Works w)
    {
        WorksList.Remove(w);
        StaticDirty = true;
        Say(BuiltLine(w.To, w.Up));
        // The first of each kind of public building is a milestone for the chronicle.
        if (w.Up != "ruin" && !Content.IsHome(w.To) && Built.Count(t => t == w.To) == 1) Note($"{Name} has its first {Content.Label(w.To)}.");
        // A finished public building (not a house) is worth a festival, held round the new building itself.
        if (!Quiet && w.Up == null && !Content.IsHome(w.To)) StartFestival($"{Name} celebrates its new {Content.Label(w.To)}.", null, w.Rank);
    }

    string BuiltLine(string to, string? up)
    {
        if (up == "ruin") return $"The {Content.Label(to)} is rebuilt.";
        if (up != null) return $"The {Content.Label(up)} is now {(Content.Label(to).EndsWith('s') ? Content.Label(to) : An(Content.Label(to)))}.";
        if (Content.IsHome(to)) return Pick(new[] { "A new house goes up.", "Another family has a roof." });
        return $"{Name} has a new {Content.Label(to)}.";
    }
}
