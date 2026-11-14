namespace Dotburg;

// Builders (0.86): scaffolds are put up by the townsfolk themselves. Free grown-ups in working hours walk to the
// nearest site that needs hands (up to three a site, the able more often, the idle less) and pace along its front;
// the works advance only while someone is there. With nobody free (night, a festival, storms, sickness, hunger)
// the sites stand idle, and once every open site is taken growth waits for one to be finished.
partial class Sim
{
    public const int MaxSites = 4;         // open building sites at once
    const int BuildersPerSite = 3;
    public bool SitesFull => WorksList.Count >= MaxSites;
    public long SiteTicks, IdleSiteTicks, SitesDone, GrowWaits;   // soak counters: site-ticks, of those with nobody at work, sites finished, growth steps held back

    // The front of a lot: the road row below it, and the pixel span of the building above that row.
    (int x0, int x1, int y) SiteFront(int rank) => (Lots[rank].tx * TileW + 2, Lots[rank].tx * TileW + TileW - 1, (Lots[rank].ty + 1) * TileH);

    // True for a builder standing or pacing at their site right now (the works advance for them).
    bool AtWork(Walker w)
    {
        if (w.Goal != Goal.Build || w.Gone || w.Dest < 0 || w.Dest >= Lots.Count) return false;
        var (x0, x1, y) = SiteFront(w.Dest);
        return w.Y == y && w.X >= x0 && w.X <= x1 && WorksList.Any(k => k.Rank == w.Dest);
    }

    // How many dot-men are on their way to, or working at, the site on this lot.
    int BuildersFor(int rank) => Walkers.Count(w => !w.Gone && w.Mode != Mode.Inside && w.Goal == Goal.Build && w.Dest == rank);

    // Every tick: each site advances by the builders at it (two make the old pace), and finishes when the work is done.
    void StepWorks()
    {
        if (WorksList.Count == 0) return;
        foreach (var wk in WorksList.ToList())
        {
            int n = 0;
            foreach (var w in Walkers) if (w.Dest == wk.Rank && AtWork(w)) n++;
            wk.Done += Math.Min(n, BuildersPerSite) / 2.0;
            SiteTicks++;
            if (n == 0) IdleSiteTicks++;
            if (wk.Done >= wk.Need) { SitesDone++; FinishWorks(wk); }
        }
    }

    // A free grown-up may go and lend a hand at the nearest site that needs one. True if they set off.
    bool JoinWorks(Walker w)
    {
        if (WorksList.Count == 0 || w.Kid != 0 || w.Lead || w.Kind != "" || w.Food < WeakAt) return false;
        int best = -1, bestN = 0, bestD = 0;
        foreach (var wk in WorksList)
        {
            if (wk.Rank >= Lots.Count || LotFlooded(wk.Rank)) continue;
            int n = BuildersFor(wk.Rank);
            if (n >= BuildersPerSite) continue;
            var (dx, dy) = DoorOf(wk.Rank);
            int dist = Math.Abs(dx - w.X) + Math.Abs(dy - w.Y);
            // A site with nobody at it first, then the nearest.
            bool better = best < 0 || (n == 0 && bestN > 0) || ((n == 0) == (bestN == 0) && dist < bestD);
            if (better) { best = wk.Rank; bestN = n; bestD = dist; }
        }
        if (best < 0) return false;
        double k = 0.55 * (Is(w, "able") ? 1.6 : Is(w, "idle") ? 0.4 : 1) / (1 + bestD / 40.0);
        if (bestN == 0) k *= 1.5;   // an empty site is noticed
        if (Chance() >= k) return false;
        WalkToSite(w, best);
        return true;
    }

    // Test hook for --works: open sites on the next empty lots, up to the cap, so builders can be watched.
    public void TestWorks()
    {
        for (int k = WorksList.Count; k < MaxSites && Built.Count < Lots.Count; k++)
        {
            string t = Content.TypeAt(Built.Count);
            Built.Add(t);
            StartWorks(Built.Count - 1, t, null);
        }
        StaticDirty = true;
    }

    // Walk to a spot along the site's front (also how a builder paces between jobs on it).
    void WalkToSite(Walker w, int rank)
    {
        var (x0, x1, y) = SiteFront(rank);
        GoTo(w, RandInt(x0, x1), y, Goal.Build, rank);
    }

    // A builder at a site carries on while it is open and they can work; they pace to another spot along it.
    bool KeepBuilding(Walker w, bool dark)
    {
        if (w.Goal != Goal.Build || w.Dest < 0 || w.Dest >= Lots.Count) return false;
        if (dark || Stormy || w.Food < WeakAt || !WorksList.Any(k => k.Rank == w.Dest) || LotFlooded(w.Dest)) return false;
        if (Chance() < 0.5) { w.Until = TickNo + Secs(1.5, 4); return true; }   // stays at the same spot a while longer
        WalkToSite(w, w.Dest);
        return true;
    }
}
