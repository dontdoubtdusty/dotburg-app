namespace Dotburg;

// One dot-man's feeling about another: a grudge (one way) or a friendship, how strong (0..1), and how it began.
class Bond
{
    public int Who { get; set; }               // the other dot-man's id
    public string Kind { get; set; } = "";     // "grudge" or "friend"
    public double Str { get; set; }            // 0..1
    public string Why { get; set; } = "";      // how it began, as a clause: "Odo shoved them at the inn"
    public int Day { get; set; }               // the town day it began
}

// Bonds and grudges (0.68): every dot-man has two traits (the leaders' eight, from a hash of their id), and people
// who share a room now and then do something to each other that their traits decide: the cruel mock and shove,
// the proud quarrel, the cunning cheat at dice, the gentle make friends. What happens is remembered as a bond with
// its origin, so a slight between dotlets can still matter when one of them rules the town. Bonds grow when the
// two meet again, fade slowly, and at most three are kept each. A deep grudge against the leader is a motive
// (Politics.cs). Cheap like Lives.cs: one dot-man in LivesEvery per step, partners found only in their room.
partial class Sim
{
    const int MaxBonds = 3;
    const double MeetsPerDay = 2.5;          // chances a day (awake, indoors, in company) to do something notable: about one a day in practice
    const double WedBond = 0.4;              // a living spouse's friendship never fades below this (0.78)
    const double GrudgeAt = 0.6;            // a grudge this strong is said aloud once, and can drive a dot-man to the knife
    readonly List<Walker> roomMates = new(); // reused
    int bondStep;
    const double MotiveK = 0.1;             // per day, times the grudge, that a deep grudge against the leader turns to the knife
    public int AssassinsKnown, MeetChecks, MeetAlone, Meetings, BondsMade;   // soak counters               // attempts by a named dot-man with a grudge (soak)
    long quarrelSaid, grudgeSaid;

    // A citizen's traits, from their id: one from each group (heart, work, spirit), and a wildcard for about one in
    // sixteen; never a clashing pair (a clash moves on to the next trait in the group). Cached by id: nothing saved.
    static readonly Dictionary<int, string[]> traitCache = new();
    public static string[] CitizenTraits(Walker w)
    {
        if (traitCache.TryGetValue(w.Id, out var got)) return got;
        var list = new List<string>();
        for (int cat = 0; cat <= 3; cat++)
        {
            if (cat == 3 && Trait01(w.Id, 514) >= Traits.WildChance) break;
            var g = Traits.InCat(cat);
            int i = (int)(Trait01(w.Id, 511 + cat * 7) * g.Length) % g.Length;
            for (int k = 0; k < g.Length && list.Any(x => Traits.Clashes(x, g[i].Key)); k++) i = (i + 1) % g.Length;
            list.Add(g[i].Key);
        }
        return traitCache[w.Id] = list.ToArray();
    }
    // The traits that count: a sitting leader's are the government's (the same two since 0.68, unless from an older save).
    string[] TraitsOf(Walker w) => w.Lead && Leader != null && Leader.Traits.Length > 0 ? Leader.Traits : CitizenTraits(w);
    // Any of their traits (heart, work, spirit, wildcard). Before 0.84 only the first two were read, so spirit traits
    // and wildcards (calm, brave, firebug, doomsayer...) never acted between townsfolk.
    bool Is(Walker w, string trait) => TraitsOf(w).Contains(trait);

    public Bond? BondOf(Walker w, int other, string kind) => w.Bonds.FirstOrDefault(b => b.Who == other && b.Kind == kind);
    Walker? ById(int id) { foreach (var w in Walkers) if (w.Id == id && !w.Gone) return w; return null; }
    string NameOfId(int id) { var w = ById(id); return w == null ? "someone long gone" : w.Lead ? LeaderTitle : CitizenName(w); }

    // Strengthen (or start) a bond from w toward other. Returns the bond; a new one replaces the weakest if full.
    Bond AddBond(Walker w, Walker other, string kind, double by, string why)
    {
        var b = BondOf(w, other.Id, kind);
        if (b == null)
        {
            if (w.Bonds.Count >= MaxBonds)
            {
                var weakest = w.Bonds.Where(x => !(x.Kind == "friend" && x.Who == w.Spouse)).OrderBy(x => x.Str).First();   // a spouse is never pushed out
                if (weakest.Str > by) return weakest;   // nothing new is as strong as what they already carry
                w.Bonds.Remove(weakest);
            }
            b = new Bond { Who = other.Id, Kind = kind, Why = why, Day = DayNumber };
            BondsMade++;
            w.Bonds.Add(b);
        }
        double before = b.Str;
        b.Str = Math.Min(1, b.Str + by);
        // A grudge grown deep is said aloud once (the crier), with where it began.
        if (kind == "grudge" && before < GrudgeAt && b.Str >= GrudgeAt && (other.Lead || TickNo - grudgeSaid > DayMs / TickMs / 3))
        {
            grudgeSaid = TickNo;
            Say($"{CitizenName(w)} will never forgive {NameOfId(other.Id)}: {WhyFor(w, b)}, on day {b.Day}.", other.Lead);
        }
        // A grudge this deep across two families may become a feud between them (Feuds.cs, 0.85).
        if (kind == "grudge" && before < FeudAt && b.Str >= FeudAt && !other.Lead) MaybeFeud(w, other, WhyFor(w, b));
        return b;
    }

    // How hard a slight lands on this dot-man, and how fast it fades (0.68 traits).
    double GrudgeGain(Walker w) => Is(w, "vengeful") ? 2 : Is(w, "spiteful") ? 1.5 : Is(w, "calm") || Is(w, "cheerful") ? 0.6 : 1;
    double GrudgeFade(Walker w) => Is(w, "vengeful") ? 0 : Is(w, "spiteful") ? 0.5 : Is(w, "calm") || Is(w, "cheerful") ? 2 : 1;

    // The origin clause told about its holder: "Odo mocked them at the inn" becomes "Odo mocked Edda at the inn".
    string WhyFor(Walker holder, Bond b)
    {
        string n = CitizenName(holder);
        return b.Why.Replace(" them ", $" {n} ").Replace(" their ", $" {n}'s ");
    }

    // Once a food step, with the same slicing as Lives: a dot-man in company may do something to someone, and the
    // rare wildcards do their own mischief.
    void StepBonds(double dt)
    {
        bondStep++;
        double step = dt * LivesEvery;
        foreach (var w in Walkers)
        {
            if (w.Gone || w.Kind != "" || (w.Id + bondStep) % LivesEvery != 3) continue;
            // Feelings fade: friendships in days (slower for the loyal), grudges slowly, deep ones hardly at all.
            double fade = GrudgeFade(w);
            for (int i = w.Bonds.Count - 1; i >= 0; i--)
            {
                var b = w.Bonds[i];
                b.Str -= (b.Kind == "friend" ? (Is(w, "loyal") ? 0.01 : 0.03) : 0.012 * (1 - b.Str) * fade) * step;
                // A living spouse stays a friend (0.78): the bond settles at Wed and fades no further.
                if (b.Kind == "friend" && b.Who == w.Spouse && b.Str < WedBond && ById(w.Spouse) != null) b.Str = WedBond;
                if (b.Str <= 0.02) w.Bonds.RemoveAt(i);
            }
            if (w.Kid == 0 && !w.Lead && w.Goal is not (Goal.Leave or Goal.Taken)) Wildcard(w, step);
            if (w.Gone || w.Mode == Mode.Walk || w.At < 0 || w.Sleep || w.Sick || w.Hurt) continue;
            MeetChecks++;
            if (Chance() >= MeetsPerDay * step) continue;
            roomMates.Clear();
            foreach (var x in Walkers) if (x != w && !x.Gone && x.Kind == "" && x.At == w.At && x.Mode != Mode.Walk && !x.Sleep) roomMates.Add(x);
            if (roomMates.Count == 0) { MeetAlone++; continue; }
            Meetings++;
            Meet(w, roomMates[Rng.Next(roomMates.Count)]);
        }
    }

    // The rare traits: a firebug sets a fire now and then, a doomsayer frightens the square.
    void Wildcard(Walker w, double step)
    {
        string n = CitizenName(w);
        if (Is(w, "firebug") && Fires.Count == 0 && !Stormy && Chance() < step / 20)
        {
            int r = FlammableRank();
            if (r < 0) return;
            string what = Content.Label(Built[r]);
            if (!StartFire(r, 0.2, $"Fire at the {what}!")) return;
            if (Chance() < 0.4) { Arrest(w, true); Say($"{n} was seen running from the {what}. Guards take them away."); }
            else Say($"Some say {n} was near the {what} when it caught.", false);
            return;
        }
        if (Is(w, "doomsayer") && Chance() < step * 0.6 * (Comet ? 3 : 1))   // a comet sets them off (Sky.cs)
        {
            Hear(0.03);
            if (TickNo - doomSaid > 1.5 * DayMs / TickMs)
            {
                doomSaid = TickNo;
                Say($"{n} stands in the square crying that {(Comet && Chance() < 0.6 ? "the comet will burn the world" : Pick(Dooms))}.", false);
            }
        }
    }
    long doomSaid;
    static readonly string[] Dooms = { "the harvest will fail", "the plague is coming", "the wells are poisoned", "the end is near",
        "the sky will fall on the faithless", "the granary is empty and the council lies" };

    // Two dot-men in one room: what the first does to the second, by their traits and what lies between them.
    void Meet(Walker a, Walker b)
    {
        string place = a.At >= 0 && a.At < Visible ? (a.At == HomeOf(a) ? $"{CitizenFirst(a)}'s house" : a.At == HomeOf(b) ? $"{CitizenFirst(b)}'s house" : $"the {Content.Label(Built[a.At])}") : "the square";
        bool young = a.Kid != 0 || b.Kid != 0;
        string an = CitizenName(a), bn = CitizenName(b), when = young ? " when they were dotlets" : "";
        // Old enemies meeting again: the calm may cool it, otherwise words, and the hot-headed come to blows.
        var old = BondOf(b, a.Id, "grudge") ?? BondOf(a, b.Id, "grudge");
        if (old != null && Chance() < 0.5)
        {
            if ((Is(a, "calm") || Is(a, "humble")) && Chance() < 0.4) { old.Str = Math.Max(0.03, old.Str - 0.15); return; }
            bool hot = Is(a, "hotheaded") || Is(b, "hotheaded");
            old.Str = Math.Min(1, old.Str + (hot ? 0.1 : 0.05));
            if (TickNo - quarrelSaid > DayMs / TickMs && old.Str >= 0.3)
            {
                quarrelSaid = TickNo;
                Say(hot && old.Str >= 0.5
                    ? $"Fists fly between {an} and {bn} at {place}; it goes back to day {old.Day}."
                    : Pick(new[] {
                        $"{an} and {bn} trade words at {place}; it goes back to day {old.Day}.",
                        $"{an} will not sit at the same table as {bn}.",
                        $"{an} and {bn} glare at each other across {place}.",
                        $"Old trouble at {place}: {an} and {bn} at it again.",
                        $"{an} turns their back on {bn} at {place}." }), false);
            }
            return;
        }
        double gb = GrudgeGain(b), ga = GrudgeGain(a);
        if (Is(a, "cruel") && Chance() < 0.6)
        {
            string verb = Pick(young ? new[] { "pushed", "mocked", "tripped", "stole a toy from" } : new[] { "mocked", "shoved", "humiliated", "spat at" });
            AddBond(b, a, "grudge", (young ? 0.45 : 0.25) * gb, $"{an} {verb} them at {place}{when}");
            return;
        }
        if (Is(a, "lightfinger") && Chance() < 0.4)
        {
            AddBond(b, a, "grudge", 0.3 * gb, young ? $"{an} stole their marbles at {place}{when}" : $"{an} picked their pocket at {place}");
            return;
        }
        if (Is(a, "hotheaded") && Chance() < 0.35)
        {
            AddBond(b, a, "grudge", 0.2 * gb, $"{an} flew at them over nothing at {place}{when}");
            if (Is(b, "hotheaded")) AddBond(a, b, "grudge", 0.2 * ga, $"{bn} shouted them down at {place}{when}");
            return;
        }
        if (Is(a, "jealous") && Chance() < 0.3)
        {
            AddBond(a, b, "grudge", 0.15 * ga, young ? $"{bn} was always the favourite{when}" : $"{bn} got the praise they wanted at {place}");
            return;
        }
        if (Is(a, "proud") && Is(b, "proud") && Chance() < 0.5)
        {
            AddBond(a, b, "grudge", 0.15 * ga, $"they quarrelled over who came first at {place}{when}");
            AddBond(b, a, "grudge", 0.15 * gb, $"they quarrelled over who came first at {place}{when}");
            return;
        }
        if ((Is(a, "cunning") || Is(a, "greedy")) && Chance() < 0.3)
        {
            AddBond(b, a, "grudge", 0.2 * gb, $"{an} cheated them at dice at {place}{when}");
            return;
        }
        // Families at feud (0.85): a sour word about the other family, and no friendship unless a charmer is at work.
        bool charm = Is(a, "charmer");
        if (Feuding(a, b))
        {
            if (Chance() < 0.5) AddBond(b, a, "grudge", 0.12 * gb, $"{an} cursed their family at {place}{when}");
            if (!charm) return;
        }
        // Friendship: never with the cruel; the gentle make friends readily, and a charmer wins anyone over.
        if (Is(a, "cruel") || Is(b, "cruel")) return;
        bool warm = Is(a, "merciful") || Is(a, "tolerant") || Is(a, "honest") || Is(a, "generous") || Is(a, "cheerful")
                    || Is(a, "humble") || Is(a, "loyal") || Is(a, "calm");
        if (Chance() < (charm ? 0.9 : warm ? 0.6 : 0.3))
        {
            string why = Is(a, "generous") ? $"{an} shared their bread with them at {place}{when}" : $"they shared a table at {place}{when}";
            AddBond(a, b, "friend", 0.2, $"they shared a table at {place}{when}");
            AddBond(b, a, "friend", charm ? 0.4 : 0.2, why);
        }
    }

    // Someone is taken away: their friends will remember who did it (the loyal more than most).
    void TakenNews(Walker taken)
    {
        var lw = LeaderWalker;
        if (lw == null) return;
        string t = LeaderTitle;
        foreach (var w in Walkers)
            if (!w.Gone && w != taken && w.Kind == "" && !w.Lead && BondOf(w, taken.Id, "friend") is { Str: >= 0.15 })
                AddBond(w, lw, "grudge", (Is(w, "loyal") ? 0.6 : 0.4) * GrudgeGain(w), $"{t} had their friend {CitizenName(taken)} taken away");
    }

    // The strongest grudge anyone in town holds against the sitting leader, if any.
    (Walker? who, Bond? bond) MotiveAgainstLeader()
    {
        var lw = LeaderWalker;
        if (lw == null) return (null, null);
        Walker? best = null; Bond? bb = null;
        foreach (var w in Walkers)
        {
            if (w.Gone || w.Lead || w.Kind != "" || w.Kid != 0 || w.Sick || w.Hurt || w.Goal is Goal.Leave or Goal.Taken) continue;
            foreach (var b in w.Bonds) if (b.Kind == "grudge" && b.Who == lw.Id && (bb == null || b.Str > bb.Str)) { best = w; bb = b; }
        }
        return (best, bb);
    }

    // Lines for a building's hover tip: who is in or at it, with traits and the strongest feeling they carry.
    public IEnumerable<string> PeopleAt(int rank)
    {
        int n = 0;
        foreach (var w in Walkers)
        {
            if (w.Gone || w.Kind != "" || w.At != rank || w.Mode == Mode.Walk) continue;
            if (++n > 4) yield break;
            var tr = TraitsOf(w);
            string who = w.Lead ? LeaderTitle : w.Kid != 0 ? $"{CitizenName(w)} (a dotlet)" : $"{CitizenName(w)}, {YearsOfDot(w)}";
            var top = w.Bonds.OrderByDescending(b => b.Str).FirstOrDefault();
            string feel = top == null ? "" : top.Kind == "grudge" ? $"; hates {NameOfId(top.Who)} ({top.Why})" : $"; friends with {NameOfId(top.Who)}";
            if (SpouseOf(w) is { } sp && (top == null || top.Kind != "grudge")) feel = $"; married to {NameOfId(sp.Id)}";
            yield return $"{who}: {string.Join(", ", tr.Where(Traits.Exists).Select(k => Traits.Get(k).Name))}{feel}.";
        }
    }
}
