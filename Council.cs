namespace Dotburg;

// One council seat (0.88): which seat, the dot-man holding it, since when (Age), and how they got it. Saved.
class Seat
{
    public string Role { get; set; } = "";   // steward, captain, physician, spymaster; in a hamlet elder and elder2
    public int Id { get; set; }               // the holder's dot-man id
    public double Since { get; set; }         // Age when they took the seat
    public string How { get; set; } = "";     // appointed, elected, for life
}

// The council (0.88): four real dot-men who tell the leader what goes on, so the hearsay has names. A steward
// (stores, fields, coffers), a captain (guards, war and fires), a physician (sickness) and a spymaster (what people
// say: a gossip under gentle rule, an informer under harsh). A hamlet has two elders sharing the four jobs.
// The leader picks, inside what the rule allows (favourites and kin under a crown, the pious under a temple, the
// eldest under elders); communes and councils elect. Seats change hands by the rule: for life under elders, shrine,
// founders and temple; with every election under commune and council; otherwise each new leader reshuffles.
// How well a councillor tells depends on their traits, their bond with the leader and how long they have sat
// (Hearsay.cs, CouncilFactor). Dropped councillors and passed-over climbers bear the leader a grudge.
// The player never appoints, dismisses or votes. Uses Rng: who sits changes the town (grudges, hearsay).
partial class Sim
{
    public readonly List<Seat> Council = new();
    int councilLeader = -1;          // the leader the seats were last checked under; -1 after a load or reset
    double councilElection = -1;     // LastElection when last checked (an election re-votes the seats)
    string councilGov = "";          // the rule when last checked
    int councilDay;                  // the day dismissals were last weighed
    readonly Dictionary<int, int> metDay = new();   // dot-man id -> the day they last walked to the council; not saved
    public int CouncilFilled, CouncilDropped, CouncilSnubbed, CouncilKept;   // soak counters

    static readonly string[] JobSeats = { "steward", "captain", "physician", "spymaster" };
    static readonly string[] ElderSeats = { "elder", "elder2" };
    string[] SeatRoles => Govs.IsHamlet(GovKey) ? ElderSeats : JobSeats;

    // The seat that answers for a job: the captain fights fires too (the warden is gone), and in a hamlet one elder
    // keeps the stores and the sick, the other the watch and the talk.
    public string SeatFor(string role) => Govs.IsHamlet(GovKey)
        ? role is "steward" or "physician" ? "elder" : role is "captain" or "warden" or "spymaster" ? "elder2" : role
        : role == "warden" ? "captain" : role;
    public static bool IsCouncilJob(string role) => role is "steward" or "captain" or "physician" or "spymaster" or "warden" or "elder" or "elder2";
    public Seat? SeatOf(string role) { string s = SeatFor(role); return Council.FirstOrDefault(x => x.Role == s); }
    public Walker? HolderOf(string role) => SeatOf(role) is { } s ? ById(s.Id) : null;
    public Seat? SeatHeldBy(Walker w) => Council.FirstOrDefault(s => s.Id == w.Id);

    // How seats change hands under this rule.
    string Turnover => GovKey switch
    {
        "elders" or "shrine" or "founders" or "temple" => "life",
        "commune" or "council" => "vote",
        _ => "reshuffle",
    };

    // A seat in words: "steward", "the elders".
    public static string SeatWord(string seat) => seat is "elder" or "elder2" ? "elder" : seat;

    // A councillor's title and first name, as the Messenger and the hearsay name them.
    string SeatTitle(Seat s, Walker w)
    {
        string n = CitizenFirst(w);
        return s.Role switch
        {
            "steward" => $"Steward {n}",
            "captain" => $"Captain {n}",
            "physician" => GovKey == "temple" ? $"Sister {n}" : $"Doctor {n}",
            "spymaster" => G.Harsh >= 0.6 ? $"Informer {n}" : $"{n} the gossip",
            _ => $"Elder {n}",
        };
    }

    // ---------- skill ----------
    // How good a dot-man is at a seat's job, about -1..1, from their traits: able, honest and careful help every seat,
    // idle, reckless, greedy and light fingers hurt; each job has its own (thrifty steward, brave captain, merciful
    // physician, charming spymaster). An elder is judged on both of their jobs.
    public double Skill(Walker w, string seat)
    {
        if (seat == "elder") return (Skill(w, "steward") + Skill(w, "physician")) / 2;
        if (seat == "elder2") return (Skill(w, "captain") + Skill(w, "spymaster")) / 2;
        double s = 0;
        foreach (var k in CitizenTraits(w))
        {
            s += k switch { "able" => 0.4, "honest" => 0.25, "careful" => 0.25, "idle" => -0.4, "reckless" => -0.25, "greedy" => -0.2, "lightfinger" => -0.3, _ => 0 };
            s += (seat, k) switch
            {
                ("steward", "thrifty") => 0.3, ("steward", "cunning") => -0.1,
                ("captain", "brave") => 0.3, ("captain", "calm") => 0.15, ("captain", "hotheaded") => -0.1, ("captain", "craven") => -0.3,
                ("physician", "merciful") => 0.2, ("physician", "calm") => 0.2, ("physician", "curious") => 0.1,
                ("spymaster", "curious") => 0.2, ("spymaster", "charmer") => 0.3, ("spymaster", "cynical") => 0.1, ("spymaster", "cunning") => 0.3,
                _ => 0,
            };
        }
        return Clamp(s, -1, 1);
    }

    // How much a councillor bends what the leader hears (Hearsay.cs): a multiplier on the odds that bad news goes
    // missing or arrives wrong, 0.5 (able, close to the leader, long in the seat) to 2 (idle, bitter, new).
    public double CouncilFactor(string role)
    {
        var s = SeatOf(role);
        var w = s == null ? null : ById(s.Id);
        var lw = LeaderWalker;
        if (w == null || lw == null) return 1;
        double bond = Math.Max(BondOf(w, lw.Id, "friend")?.Str ?? 0, BondOf(lw, w.Id, "friend")?.Str ?? 0) - (BondOf(w, lw.Id, "grudge")?.Str ?? 0);
        double days = (Age - s!.Since) / DayMs;
        double fresh = days < 4 ? 1 + 0.4 * (1 - days / 4) : 1;   // the first days in the seat they still learn the job
        return Clamp(Math.Pow(2, -(Skill(w, s.Role) * 0.7 + bond * 0.5)) * fresh, 0.5, 2);
    }

    // ---------- the step ----------
    void StepCouncil()
    {
        if (GovKey == "none") { if (Council.Count > 0) ClearCouncil(); return; }   // settlers have no council
        var lw = LeaderWalker;
        if (lw == null) return;
        // A rule that crosses between hamlet and village remakes the council.
        if (councilGov != "" && Govs.IsHamlet(councilGov) != Govs.IsHamlet(GovKey))
        {
            if (Council.Count > 0) Say($"The old council of {Name} stands down under the new rule.");
            ClearCouncil();
        }
        // Seats of the dead, the departed, the taken and of a councillor raised to lead fall open.
        foreach (var s in Council.ToList())
        {
            var w = ById(s.Id);
            if (w == null || w.Goal is Goal.Leave or Goal.Taken || w.Lead || w.Kind != "") Vacate(s);
        }
        if (councilLeader >= 0 && councilLeader != lw.Id && Turnover == "reshuffle") Reshuffle(lw);
        else if (Turnover == "vote" && councilElection >= 0 && LastElection != councilElection && Council.Count > 0) Revote(lw);
        councilLeader = lw.Id; councilElection = LastElection; councilGov = GovKey;
        if (DayNumber != councilDay) { councilDay = DayNumber; MaybeDismiss(lw); }
        foreach (var role in SeatRoles) if (!Council.Any(s => s.Role == role)) FillSeat(role, lw, true);
        SyncCouncilChats();
    }

    // The whole council stands down (a new rule, a new town); their chats go Offline.
    void ClearCouncil()
    {
        Council.Clear();
        councilLeader = -1; councilElection = -1; councilGov = GovKey;
        SyncCouncilChats();
    }

    void Vacate(Seat s) => Council.Remove(s);

    // A new leader under an appointing rule keeps the councillors they like and replaces the rest.
    void Reshuffle(Walker lw)
    {
        var kept = new List<string>();
        int dropped = 0;
        foreach (var s in Council.ToList())
        {
            var w = ById(s.Id);
            if (w == null) { Vacate(s); continue; }
            double friend = Math.Max(BondOf(w, lw.Id, "friend")?.Str ?? 0, BondOf(lw, w.Id, "friend")?.Str ?? 0);
            double grudge = Math.Max(BondOf(w, lw.Id, "grudge")?.Str ?? 0, BondOf(lw, w.Id, "grudge")?.Str ?? 0);
            double keep = 0.35 + 0.5 * friend + (w.Last != "" && w.Last == lw.Last ? 0.3 : 0)
                + (Is(lw, "loyal") ? 0.25 : 0) - (Is(lw, "cruel") || Is(lw, "jealous") || Is(lw, "spiteful") || Is(lw, "vengeful") ? 0.15 : 0)
                - (GovKey is "junta" or "party" ? 0.15 : 0);
            if (grudge >= 0.3) keep = 0;
            if (Chance() < keep) { kept.Add(CitizenFirst(w)); CouncilKept++; continue; }
            Drop(s, w, lw, false);
            dropped++;
        }
        if (dropped == 0 && kept.Count == 0) return;
        Say(kept.Count == 0 ? $"{LeaderTitle} names a new council."
            : dropped == 0 ? $"{LeaderTitle} keeps the old council."
            : $"{LeaderTitle} keeps {JoinAnd(kept)} and names new councillors.");
    }

    // Commune and council towns vote their seats at every election. A sitting councillor is usually kept (more so with
    // many friends in town; a fresh appointee of an older rule stands like anyone else); the rest lose to a new face.
    void Revote(Walker lw)
    {
        var lost = new List<string>();
        var losers = new HashSet<int>();
        foreach (var s in Council.ToList())
        {
            var w = ById(s.Id);
            if (w == null) { Vacate(s); continue; }
            int friends = Walkers.Count(x => !x.Gone && BondOf(x, w.Id, "friend") != null);
            if (Chance() < Math.Min(0.9, 0.6 + 0.08 * friends)) { s.How = "elected"; continue; }
            Vacate(s);
            lost.Add(CitizenFirst(w)); losers.Add(w.Id);
            if (!Quiet) Post(SeatConvo(s, w), SeatTitle(s, w), Fill(CPick(NpcSay["council.outvoted"]), null), false, TickNo);
        }
        var won = new List<string>();
        foreach (var role in SeatRoles)
            if (!Council.Any(s => s.Role == role) && FillSeat(role, lw, false, 0, losers) is { } got && ById(got.Id) is { } nw) won.Add(CitizenFirst(nw));
        Say(lost.Count == 0 ? $"The council of {Name} is voted back in."
            : $"{Name} votes {JoinAnd(won)} onto the council; {JoinAnd(lost)} {(lost.Count == 1 ? "loses a seat" : "lose their seats")}.");
    }

    // Once a day the leader may put out a councillor they have come to hate (or who hates them). A loyal leader
    // puts up with more; a cruel, jealous, spiteful or vengeful one with less, and harsh rule with less still.
    // Elected seats are the town's, so they stay.
    void MaybeDismiss(Walker lw)
    {
        if (Turnover == "vote") return;
        double at = Is(lw, "loyal") ? 0.7 : Is(lw, "cruel") || Is(lw, "jealous") || Is(lw, "spiteful") || Is(lw, "vengeful") ? 0.3 : 0.5;
        if (GovKey is "junta" or "party") at -= 0.1;
        foreach (var s in Council.ToList())
        {
            var w = ById(s.Id);
            if (w == null) continue;
            double grudge = Math.Max(BondOf(w, lw.Id, "grudge")?.Str ?? 0, BondOf(lw, w.Id, "grudge")?.Str ?? 0);
            if (grudge < at) continue;
            Drop(s, w, lw, true);
            Say($"{LeaderTitle} puts {CitizenName(w)} out of the council.", true);
            return;   // one a day at most
        }
    }

    // A councillor loses their seat and bears the leader a lasting grudge; their last word goes to the chat.
    void Drop(Seat s, Walker w, Walker lw, bool dismissed)
    {
        Vacate(s);
        CouncilDropped++;
        AddBond(w, lw, "grudge", dismissed ? 0.5 : 0.4, $"{Leader?.Name ?? "the leader"} put them out of the council");
        if (!Quiet) Post(SeatConvo(s, w), SeatTitle(s, w), Fill(CPick(NpcSay[dismissed ? "council.dismissed" : "council.dropped"]), null), false, TickNo);
    }

    // Fill a seat from the townsfolk: the rule decides who may be picked and who is favoured, the leader's bonds
    // count where the leader appoints, and the town's friendships where it votes. Passed-over climbers may resent it.
    // keep: a sitting councillor standing again (re-election). Returns the new seat, or null if nobody can sit.
    Seat? FillSeat(string role, Walker lw, bool announce, int keep = 0, HashSet<int>? not = null)
    {
        bool vote = Turnover == "vote";
        bool eldest = GovKey == "elders" || role is "elder" or "elder2";
        var pool = Walkers.Where(w => !w.Gone && w.Kind == "" && w.Kid == 0 && !w.Lead && w.Goal is not (Goal.Leave or Goal.Taken)
            && !Council.Any(s => s.Id == w.Id) && not?.Contains(w.Id) != true).ToList();
        if (pool.Count == 0) return null;
        var liked = vote ? Walkers.Where(w => !w.Gone).SelectMany(w => w.Bonds.Where(b => b.Kind == "friend").Select(b => b.Who)).GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count()) : null;
        double Weight(Walker w)
        {
            double wt = Math.Pow(2, Skill(w, role) * 1.5);   // the able are likelier, but nobody sees traits clearly
            var tr = CitizenTraits(w);
            if (RuleDraw.TryGetValue(GovKey, out var d)) wt *= 1 + tr.Count(d.traits.Contains);   // the rule's kind of person
            if (GovKey is "shrine" or "temple" && tr.Contains("pious")) wt *= 3;
            if (role == "spymaster") wt *= tr.Any(k => k is "cunning" or "cruel" or "jealous") ? (G.Harsh >= 0.6 ? 3 : 1) : tr.Any(k => k is "charmer" or "curious" or "cheerful") ? (G.Harsh >= 0.6 ? 1 : 2.5) : 1;
            if (eldest && !double.IsNaN(w.Born)) wt *= Math.Pow(Math.Max(1, (Age - w.Born) / YearMs) / 30, GovKey == "elders" ? 4 : 2);   // elders by age
            if (vote) wt *= 1 + liked!.GetValueOrDefault(w.Id) + (w.Id == keep ? 2 : 0);
            else
            {
                double friend = Math.Max(BondOf(w, lw.Id, "friend")?.Str ?? 0, BondOf(lw, w.Id, "friend")?.Str ?? 0);
                double grudge = Math.Max(BondOf(w, lw.Id, "grudge")?.Str ?? 0, BondOf(lw, w.Id, "grudge")?.Str ?? 0);
                wt *= 1 + 3 * friend;
                if (w.Spouse == lw.Id) wt *= 2;
                if (w.Last != "" && w.Last == lw.Last) wt *= GovKey is "crown" or "founders" ? 4 : 1.5;
                if (grudge >= 0.3) wt *= 0.1;
            }
            return wt;
        }
        var weights = pool.ToDictionary(w => w, Weight);
        var chosen = Weighted(pool, w => weights[w]);
        var seat = new Seat { Role = role, Id = chosen.Id, Since = Age, How = vote ? "elected" : Turnover == "life" ? "for life" : "appointed" };
        Council.Add(seat);
        CouncilFilled++;
        // A proud or ambitious dot-man who would have suited better takes it personally (appointed seats only).
        if (!vote && !eldest)
        {
            var snub = pool.Where(w => w != chosen && weights[w] >= weights[chosen] * 0.5 && CitizenTraits(w).Any(k => k is "proud" or "ambitious"))
                .OrderByDescending(w => weights[w]).FirstOrDefault();
            if (snub != null && Chance() < 0.5) { AddBond(snub, lw, "grudge", 0.25, $"{Leader?.Name ?? "the leader"} passed them over for the council"); CouncilSnubbed++; }
        }
        if (announce && !Quiet)
        {
            string job = SeatWord(role);
            Say(role is "elder" or "elder2" ? $"{CitizenName(chosen)} sits with the elders of {Name}."
                : vote ? $"{Name} elects {CitizenName(chosen)} {job} of the council."
                : $"{LeaderTitle} names {CitizenName(chosen)} {job} of the council.");
            Post(SeatConvo(seat, chosen), SeatTitle(seat, chosen), Fill(CPick(NpcSay[$"{(role is "elder" or "elder2" ? "elder" : role)}.seat"]), null), false, Typing());
        }
        return seat;
    }

    static string JoinAnd(List<string> n) => n.Count == 1 ? n[0] : string.Join(", ", n.Take(n.Count - 1)) + " and " + n[^1];

    // ---------- the Messenger ----------
    // A councillor's chat: one per seat and holder, so a new holder starts a new chat and the old one goes Offline.
    Convo SeatConvo(Seat s, Walker w) => ConvoFor($"npc:{s.Role}:{Founded}:{w.Id}", "npc", SeatTitle(s, w), s.Role);

    // The holder of a councillor's chat (from its key), or 0 for the other notables.
    public static int NotableId(Convo c) => c.Kind == "npc" && int.TryParse(c.Key.Split(':').ElementAtOrDefault(3), out var id) ? id : 0;

    // Chats of seated councillors stay online; anyone no longer on the council goes Offline, as do the hashed
    // notables of older saves for jobs the council now holds.
    void SyncCouncilChats()
    {
        bool changed = false;
        foreach (var c in Convos)
        {
            if (c.Kind != "npc" || c.Gone || !IsCouncilJob(c.Role)) continue;
            int id = NotableId(c);
            if (id != 0 && Council.Any(s => s.Id == id && s.Role == c.Role)) continue;
            c.Gone = true; changed = true;
        }
        if (changed) TrimOffline();
    }

    // Where a seat sits in the contact list.
    public static int SeatOrder(string role) => Array.IndexOf(new[] { "elder", "elder2", "steward", "captain", "physician", "spymaster" }, role);

    // A councillor's tooltip in the contact list: their seat, since when and how, and their traits (the leader
    // knows their council well).
    public string CouncilInfo(Convo c)
    {
        int id = NotableId(c);
        var s = Council.FirstOrDefault(x => x.Id == id && x.Role == c.Role);
        var w = ById(id);
        if (s == null || w == null) return "";
        int day = (int)Math.Floor(s.Since / DayMs) + 1;
        string jobs = s.Role == "elder" ? "the stores and the sick" : s.Role == "elder2" ? "the watch and the talk of the town" : SeatWord(s.Role);
        return $"Sits on the council for {jobs} since day {day} ({s.How}). {string.Join(", ", CitizenTraits(w).Select(k => Traits.Get(k).Name))}.";
    }

    // ---------- the meeting ----------
    // Once a day in daylight the councillors and the leader walk to the hall (or the clock tower) to meet.
    bool GoToCouncil(Walker w)
    {
        if (Council.Count == 0 || (!w.Lead && SeatHeldBy(w) == null)) return false;
        if (metDay.TryGetValue(w.Id, out var d) && d == DayNumber) return false;
        int r = SeatRank();
        if (r < 0 && LeaderWalker is { } lw) r = HomeOf(lw);   // no hall yet: they meet at the leader's house
        if (r < 0 || r == w.At || Burning(r)) return false;
        metDay[w.Id] = DayNumber;
        Visit(w, r);
        return true;
    }

    // ---------- save ----------
    public void RestoreCouncil(List<Seat>? seats)
    {
        Council.Clear();
        if (seats != null) Council.AddRange(seats.Where(s => s.Role != "" && s.Id > 0));
        councilLeader = -1; councilElection = -1; councilGov = GovKey;
    }

    // Soak summary: who sits now and how the seats changed hands.
    public string CouncilLine() =>
        $"seats filled {CouncilFilled}, kept by a new leader {CouncilKept}, dropped or put out {CouncilDropped}, passed over {CouncilSnubbed}\n" +
        string.Join("\n", Council.Select(s => ById(s.Id) is { } w
            ? $"{s.Role}: {SeatTitle(s, w)} ({string.Join(", ", CitizenTraits(w))}), {s.How}, {(Age - s.Since) / DayMs:F1} days, skill {Skill(w, s.Role):F2}, tells x{CouncilFactor(s.Role):F2}"
            : $"{s.Role}: gone"));
}
