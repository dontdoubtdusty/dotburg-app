namespace Dotburg;

// Citizen health (0.67): every dot-man has an age and can die of an ordinary cause, the way sister rulers do
// (Rulers.cs): a handful of causes, each a rate per day, one roll for whether and which. No bodies or wounds;
// leaders and former leaders keep their full bodies (Health.cs). Everyone ages a year every 4 town days.
// Cheap by design (lessons from RimWorld and Songs of Syx, see ../claude/game-perf-lessons.md): each dot-man is
// checked on one food step in LivesEvery, offset by id; habits and names come from a hash of the id, so the only
// saved number is the age; the town-wide numbers are worked out once per step; no LINQ in the per-dot loop.
// Deaths are not announced one by one: the dead are gathered and named in one line every 4 days.
partial class Sim
{
    const int LivesEvery = 8;                  // food steps (about a second each) between one dot-man's checks
    const double CitOldK = 0.004;              // death from age, per year at 30, rising e-fold every 10 years (as sister rulers)
    const int GrownAt = 16;                    // a dotlet grows up at this age
    const int WeekDays = 4;                    // the burial summary comes every this many days

    readonly List<(string who, string how)> burials = new();
    readonly List<(string how, double rate, bool sudden)> causes = new();   // reused each check
    int livesStep;
    public int OrdinaryDead;                   // died of age or an accident since the app started (not saved)

    // A dot-man's names, given once when they arrive or are born (Christen in Names.cs) and saved.
    public static string CitizenFirst(Walker w) { if (w.First == "") LegacyName(w); return w.First; }
    public static string CitizenName(Walker w) => $"{CitizenFirst(w)} {w.Last}";
    public int YearsOfDot(Walker w) => double.IsNaN(w.Born) ? 0 : (int)Math.Floor((Age - w.Born) / YearMs);

    // Someone without an age gets one: a newborn is 0; a settler, newcomer or dot-man from an older save 18 to 58.
    void GiveAge(Walker w) => w.Born = w.Kid != 0 ? Age : Age - (18 + 40 * Trait01(w.Id, 401)) * YearMs;

    // A dotlet growing up turns 16 (childhood takes a day on screen; the years are for the age line).
    void GrowUp(Walker w) { w.Kid = 0; w.Born = Age - GrownAt * YearMs; }

    // Once a food step: each dot-man's turn comes every LivesEvery steps.
    void StepLives(double dt)
    {
        livesStep++;
        double step = dt * LivesEvery;
        int alive = 0;
        foreach (var w in Walkers) if (!w.Gone && w.Kind == "") alive++;
        // The town's side of the odds, once for everyone.
        bool winter = SeasonNo == 3, autumn = SeasonNo == 2, snow = cover > 0.4 || Weather == Wx.Snow;
        bool pond = Built.Contains("park"), cart = false;
        foreach (var v in Visitors) if (v.Kind == "cart" && !v.Gone) cart = true;
        int works = WorksList.Count;
        double care = Care();
        foreach (var w in Walkers)
        {
            if (w.Gone || w.Kind != "") continue;
            if (double.IsNaN(w.Born)) GiveAge(w);
            if ((w.Id + livesStep) % LivesEvery != 0 || w.Kid != 0 || w.Lead || w.Fading != "") continue;
            if (BodyOf(w) != null) continue;   // leaders and former leaders have bodies
            int y = YearsOfDot(w);
            w.Period = (Trait01(w.Id, 1) < 0.5 ? 2 : 3) + (y >= 70 ? 1 : 0);   // the old walk a little slower
            if (alive <= MinPop) continue;

            causes.Clear();
            // Age: one rate, shared between the ways old age comes.
            double old = CitOldK * AgeRate * Math.Exp((y - 30) / 10.0) * (1 - 0.5 * care);
            double sleep = y >= 60 ? 0.35 : 0.05, heart = 0.25, stroke = y >= 50 ? 0.2 : 0.05, growth = 0.15, chill = winter ? 0.2 : 0.1;
            double sum = sleep + heart + stroke + growth + chill;
            causes.Add(("in their sleep", old * sleep / sum, false));
            causes.Add(("of a weak heart", old * heart / sum, false));
            causes.Add(("of a stroke", old * stroke / sum, false));
            causes.Add(("of a growth", old * growth / sum, false));
            causes.Add((winter ? "of a winter chill" : "of a fever", old * chill / sum, false));
            // Accidents: rare, tied to what the town is doing.
            if (autumn) causes.Add(("after a fall from a hayrick", 1 / 1500.0, true));
            if (w.Good >= 0) causes.Add(("under a cart's wheels", 1 / 2500.0, true));
            if (snow && y >= 55) causes.Add(("after a slip on the ice", 1 / 400.0, true));
            if (winter && pond && y < 40) causes.Add(("through the ice on the pond", 1 / 3000.0, true));
            if (works > 0) causes.Add(("after a fall from the scaffolding", works / 6000.0, true));
            if (cart && !winter) causes.Add(("kicked by a horse", 1 / 4000.0, true));
            if (winter && (w.Food < WeakAt || y >= 75)) causes.Add(("of the cold", 1 / 200.0, false));

            // The reckless have twice the accidents, the careful half (0.68).
            double luck = Is(w, "reckless") ? 2 : Is(w, "careful") ? 0.5 : 1;
            for (int i = 0; i < causes.Count; i++) if (causes[i].sudden) causes[i] = (causes[i].how, causes[i].rate * luck, true);
            double total = 0;
            foreach (var c in causes) total += c.rate;
            if (Chance() >= 1 - Math.Exp(-total * step)) continue;
            double pick = Chance() * total;
            foreach (var c in causes)
            {
                pick -= c.rate;
                if (pick > 0) continue;
                if (c.sudden) PassAway(w, c.how);
                else { w.Fading = c.how; if (w.Mode == Mode.Inside && w.At == HomeOf(w)) PassAway(w, c.how); }
                alive--;
                break;
            }
        }
    }

    // A dot-man whose time has come walks home first (Decide sends them); this is the end, at home or where they fell.
    void PassAway(Walker w, string how)
    {
        if (w.Gone) return;
        if (w.InLine >= 0) LeaveLine(w);
        w.Gone = true;
        OrdinaryDead++;
        Nudge(-0.01, "a death in town");
        Mourn(w, how);
    }

    // Everyone who dies goes on the list for the burial summary (hunger, illness and fire call this too).
    void Mourn(Walker w, string how)
    {
        string who = w.Kid != 0 ? "a dotlet" : $"{CitizenName(w)}, {YearsOfDot(w)},";
        burials.Add((who, how));
        if (burials.Count > 60) burials.RemoveAt(0);
    }

    // Every few days at daybreak: one line naming the dead, up to three by name and the rest by cause.
    void BurialSummary()
    {
        if (Quiet) return;
        if (burials.Count == 0)
        {
            if (Walkers.Count(x => !x.Gone && x.Kind == "") > 10 && chatRng.NextDouble() < 0.5) Npc("physician", "noweek");
            return;
        }
        var parts = new List<string>();
        var named = burials.Where(b => b.who != "a dotlet").Take(3).ToList();
        foreach (var b in named) parts.Add($"{b.who} {b.how}");
        var rest = burials.Except(named).GroupBy(b => b.how).Select(g => g.Count() == 1 ? $"{(g.First().who == "a dotlet" ? "a dotlet" : "one more")} {g.Key}" : $"{g.Count()} {g.Key}");
        parts.AddRange(rest);
        string list = parts.Count == 1 ? parts[0] : string.Join("; ", parts.Take(parts.Count - 1)) + "; and " + parts[^1];
        int n = burials.Count;
        string line = $"{(n == 1 ? "One" : n.ToString())} buried in {Name} these {WeekDays} days: {list}.";
        Say(line, false);
        Npc("physician", "week", false, n == 1 ? "one funeral" : $"{n} funerals");
        burials.Clear();
    }
}
