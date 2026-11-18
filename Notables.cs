namespace Dotburg;

// Notables off the council (0.89.1): the baker, the grocer and the ringleader are real dot-men, never a name made up
// from a hash. A shop's keeper is the grown dot-man living nearest to it (kept until they leave, die, lead or join
// the council); the ringleader is whoever bears the leader the deepest grudge, and stays so while it lasts. With
// nobody in a job, that job is silent, as is a council job with an empty seat. Choosing never touches Rng, so who
// speaks never changes the town. Saved (`SaveData.Keepers`).
partial class Sim
{
    public readonly Dictionary<string, int> Keepers = new();   // baker, grocer, ringleader -> dot-man id
    const double RingleaderAt = 0.3, RingleaderUntil = 0.15;   // grudge strength to start stirring, and to stop

    // The real dot-man behind a notable's job, or null when nobody holds it.
    public Walker? NotableOf(string role) =>
        IsCouncilJob(role) ? HolderOf(role) : Keepers.TryGetValue(role, out var id) && ById(id) is { } w && Free(w) ? w : null;

    // Can hold a job off the council: grown, in town, not leading, not seated, not on their way out.
    bool Free(Walker w) => w.Kid == 0 && !w.Gone && !w.Lead && w.Kind == "" && w.Fading == "" && w.Goal is not (Goal.Leave or Goal.Taken) && SeatHeldBy(w) == null;

    // Keep every job pointing at someone real; a chat whose holder changed goes Offline.
    void StepKeepers()
    {
        KeepShop("baker", t => t == "bakery");
        KeepShop("grocer", IsSeller);
        KeepRingleader();
        SyncKeeperChats();
    }

    // A shop's keeper stays while they can; otherwise the free grown-up living nearest the shop takes over.
    void KeepShop(string role, Func<string, bool> isShop)
    {
        var shops = Enumerable.Range(0, Math.Min(Visible, Lots.Count)).Where(r => isShop(Built[r])).ToList();
        if (shops.Count == 0) { Keepers.Remove(role); return; }
        if (NotableOf(role) != null) return;
        var taken = Keepers.Where(k => k.Key != role).Select(k => k.Value).ToHashSet();
        Walker? best = null; int bestD = int.MaxValue;
        foreach (var w in Walkers)
        {
            if (!Free(w) || taken.Contains(w.Id)) continue;
            int h = HomeOf(w);
            if (h < 0 || h >= Lots.Count) continue;
            int d = shops.Min(r => Math.Abs(Lots[r].tx - Lots[h].tx) + Math.Abs(Lots[r].ty - Lots[h].ty));
            if (d < bestD || (d == bestD && best != null && w.Id < best.Id)) { best = w; bestD = d; }
        }
        if (best != null) Keepers[role] = best.Id; else Keepers.Remove(role);
    }

    // The ringleader: the free dot-man with the deepest grudge against the leader, kept while it lasts.
    void KeepRingleader()
    {
        var lw = LeaderWalker;
        if (lw == null) { Keepers.Remove("ringleader"); return; }
        if (NotableOf("ringleader") is { } cur && (BondOf(cur, lw.Id, "grudge")?.Str ?? 0) >= RingleaderUntil) return;
        var best = Walkers.Where(Free).Select(w => (w, g: BondOf(w, lw.Id, "grudge")?.Str ?? 0))
            .Where(x => x.g >= RingleaderAt).OrderByDescending(x => x.g).ThenBy(x => x.w.Id).FirstOrDefault();
        if (best.w != null) Keepers["ringleader"] = best.w.Id; else Keepers.Remove("ringleader");
    }

    // Chats of off-council notables: the current holder's stays, everyone else's (and old hashed ones) goes Offline.
    void SyncKeeperChats()
    {
        bool changed = false;
        foreach (var c in Convos)
        {
            if (c.Kind != "npc" || c.Gone || IsCouncilJob(c.Role)) continue;
            if (NotableId(c) is var id and > 0 && NotableOf(c.Role)?.Id == id) continue;
            c.Gone = true; changed = true;
        }
        if (changed) TrimOffline();
    }

    // A flatterer for a feared leader: a friend of theirs, else a councillor; null when there is nobody.
    Walker? Flatterer()
    {
        var lw = LeaderWalker;
        if (lw == null) return null;
        var friend = Walkers.Where(w => !w.Gone && !w.Lead && w.Kid == 0 && BondOf(w, lw.Id, "friend") != null)
            .OrderByDescending(w => BondOf(w, lw.Id, "friend")!.Str).ThenBy(w => w.Id).FirstOrDefault();
        return friend ?? Council.Select(s => ById(s.Id)).FirstOrDefault(w => w != null);
    }

    // Soak summary: who holds each job now, and every notable chat ever opened.
    public string KeepersLine() =>
        string.Join(", ", new[] { "baker", "grocer", "ringleader" }.Select(r => $"{r}: {(NotableOf(r) is { } w ? CitizenName(w) + " #" + w.Id : "nobody")}"))
        + $"\nnotable chats: {string.Join("; ", Convos.Where(c => c.Kind == "npc").Select(c => $"{c.Title} ({c.Role}, {c.Lines.Count} lines{(c.Gone ? ", offline" : "")})"))}";

    public void RestoreKeepers(Dictionary<string, int>? k)
    {
        Keepers.Clear();
        if (k != null) foreach (var (r, id) in k) if (id > 0) Keepers[r] = id;
    }
}
