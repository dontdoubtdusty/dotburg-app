namespace Dotburg;

// Voices (0.79): the dialogue generator. Every message is 1 to 3 hand-written parts (open, news, feel, aside,
// close) from resources/voices.txt, picked by who speaks, to whom, and the moment. Voices decides what is said;
// Hands (Hands.cs) decides how it is typed. Talk stays cosmetic: every draw comes from the caller's chatRng.
// Design and what was rejected: claude/voices-vision.md. Events not in the phrase file fall back to the old
// tables in Messenger.cs.

// One phrase from the file. Need: tags that must hold (an "a/b" tag needs either); Lean: trait tags that only
// raise the score; Not: tags that rule it out. Line: where it is in the file, for the checker.
record VoicePart(int Id, string Ev, string Slot, string[] Need, string[] Lean, string[] Not, string Text, int Line);

// The phrase file, read once. Problems: what the loader could not read (shown by --voices).
sealed class VoiceBook
{
    public readonly List<VoicePart> Parts = new();
    public readonly List<string> Problems = new();
    public readonly List<(string Header, int Line, int Count)> Headers = new();

    // True when the file has a news part for this event, so Voices can speak it.
    public bool Speaks(string ev) => Parts.Any(p => p.Ev == ev && p.Slot == "news");

    // Reads the file: "[event slot | tag, tag]" headers, one part per line below, # comments.
    public static VoiceBook Load(string path)
    {
        var b = new VoiceBook();
        if (!File.Exists(path)) { b.Problems.Add($"no phrase file at {path}"); return b; }
        string ev = "", slot = "";
        string[] need = Array.Empty<string>(), lean = Array.Empty<string>(), not = Array.Empty<string>();
        bool inHeader = false;
        int n = 0;
        foreach (var raw in File.ReadLines(path))
        {
            n++;
            string line = raw.Trim();
            if (line == "" || line.StartsWith('#')) continue;
            if (line.StartsWith('['))
            {
                inHeader = false;
                if (!line.EndsWith(']')) { b.Problems.Add($"line {n}: header without a closing ]"); continue; }
                var halves = line[1..^1].Split('|');
                var head = halves[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (head.Length != 2) { b.Problems.Add($"line {n}: header needs 'event slot', got '{halves[0].Trim()}'"); continue; }
                (ev, slot) = (head[0], head[1]);
                var tags = halves.Length > 1 ? halves[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : Array.Empty<string>();
                not = tags.Where(t => t.StartsWith('!')).Select(t => t[1..]).ToArray();
                lean = tags.Where(t => !t.StartsWith('!') && Voices.IsLean(t)).ToArray();
                need = tags.Where(t => !t.StartsWith('!') && !Voices.IsLean(t)).ToArray();
                b.Headers.Add((line, n, 0));
                inHeader = true;
                continue;
            }
            if (!inHeader) { b.Problems.Add($"line {n}: a phrase before any header"); continue; }
            // A header may name several events ("npc.steward.harvest,npc.steward.bumper"): one part for each.
            foreach (var e in ev.Split(',', StringSplitOptions.RemoveEmptyEntries))
                b.Parts.Add(new VoicePart(b.Parts.Count, e, slot, need, lean, not, line, n));
            var h = b.Headers[^1];
            b.Headers[^1] = (h.Header, h.Line, h.Count + 1);
        }
        return b;
    }
}

// The picking rules, kept apart from the town so --voices runs the very same code as the app.
static class Voices
{
    public static readonly string[] Slots = { "open", "news", "feel", "aside", "close" };

    // Tags a part can need (anything else must be t:<trait> or g:<rule>). Some are not set by the code yet;
    // they are listed so the file can use them as the moving over goes on (voices-vision.md, "Moving over").
    public static readonly string[] Known =
    {
        "sister", "leader", "captain", "physician", "warden", "ringleader", "steward", "baker", "grocer", "friend", "enemy", "oldleader", "gossip",
        "harsh", "kind",
        "warm", "cool", "hostile", "wary", "fearful",
        "ill", "wounded", "grieving", "new", "old", "shaken",
        "morning", "day", "evening", "night", "spring", "summer", "autumn", "winter",
        "clear", "cloudy", "rain", "storm", "fog", "snow", "festival", "hungry", "fire", "war", "flood",
        "grave", "light",
        "newleader", "mine", "yours",
    };

    // Notable events the code raises (role -> events), from the Npc calls.
    public static readonly Dictionary<string, string[]> NpcEvents = new()
    {
        ["captain"] = new[] { "warwe", "warthey", "loss", "hold", "raid", "won", "lost", "gone" },
        ["physician"] = new[] { "start", "leader", "over", "wound", "heart", "stroke", "growth", "surgery", "fall", "week", "noweek" },
        ["warden"] = new[] { "fire", "great", "out", "down", "death" },
        ["steward"] = new[] { "harvest", "bumper", "fails", "rotting", "seed", "low", "queue", "ration", "hungry", "death", "sold", "caravan", "report" },
        ["ringleader"] = new[] { "grumble", "arrest", "won" },
        ["baker"] = new[] { "out", "back", "noflour", "rush", "small", "winter" },
        ["grocer"] = new[] { "bare", "restock", "line", "hoard", "greens", "nogreens", "small" },
    };
    public static readonly string[] PalTroubles = { "sick", "hungry", "leaderill", "war", "fire", "plague", "queue", "festival" };

    // The events the code speaks through Voices so far; the checker flags any other event in the file.
    public static readonly string[] Events = new[] { "sister.aid", "sister.aid.re", "pal.hello", "pal.hello.re", "pal.small", "pal.small.re", "pal.trouble.re", "pal.trouble.festival.re", "pal.trouble.leaderill.re" }
        .Concat(PalTroubles.Select(t => "pal.trouble." + t))
        .Concat(NpcEvents.SelectMany(kv => kv.Value.Select(e => $"npc.{kv.Key}.{e}"))).ToArray();

    // Events whose news is grave: fewer parts, no light ones.
    public static readonly HashSet<string> Grave = new()
    {
        "npc.captain.warthey", "npc.captain.loss", "npc.captain.raid", "npc.captain.lost", "npc.physician.leader", "npc.physician.growth",
        "npc.physician.stroke", "npc.physician.week", "npc.warden.great", "npc.warden.down", "npc.warden.death", "npc.steward.fails",
        "npc.steward.hungry", "npc.steward.death", "npc.steward.seed", "npc.ringleader.arrest",
        "pal.trouble.war", "pal.trouble.fire", "pal.trouble.plague",
    };

    // Only trait tags lean: people do not always talk the way they act. A rule tag must hold, since a line in
    // another rule's words ("The Party thanks you" from a guild) reads plainly wrong.
    public static bool IsLean(string t) => t.StartsWith("t:");

    // Whether a tag is one the code knows (for the checker).
    public static bool IsKnown(string t) =>
        t.StartsWith("t:") ? Traits.Exists(t[2..]) : t.StartsWith("g:") ? Govs.Exists(t[2..]) : t.Split('/').All(Known.Contains);

    // How well a part fits: 0 = ruled out. Each needed tag adds 2, each fitting trait adds 2, a part written for
    // this very event (not "*") adds 1. A trait part from someone without any of its traits scores 1, the lowest,
    // so it only comes up when little else fits.
    static int Score(VoicePart p, ISet<string> ctx)
    {
        if (p.Not.Any(ctx.Contains)) return 0;
        if (!p.Need.All(t => t.Split('/').Any(ctx.Contains))) return 0;
        int fit = p.Lean.Count(ctx.Contains);
        if (p.Lean.Length > 0 && fit == 0) return 1;
        return 1 + 2 * p.Need.Length + 2 * fit + (p.Ev == "*" ? 0 : 1);
    }

    // Picks a part for a slot: the best few that fit, weighted by score, skipping what this speaker said lately.
    // Only news may repeat when nothing fresh fits (the event must be told); an extra is then left out, so a
    // conversation never says the same thing twice. Null when nothing fits.
    public static VoicePart? Pick(VoiceBook b, string ev, string slot, ISet<string> ctx, Random rng, List<int> recent)
    {
        var all = b.Parts.Where(p => p.Slot == slot && (p.Ev == ev || (p.Ev == "*" && slot != "news")))
            .Select(p => (p, s: Score(p, ctx))).Where(x => x.s > 0).ToList();
        var fresh = all.Where(x => !recent.Contains(x.p.Id) && !recent.Any(r => b.Parts[r].Text == x.p.Text)).ToList();
        if (fresh.Count > 0 || slot != "news") all = fresh;
        if (all.Count == 0) return null;
        int top = all.Max(x => x.s);
        var best = all.Where(x => x.s * 2 >= top).OrderByDescending(x => x.s).ThenBy(_ => rng.Next()).Take(4).ToList();
        int r = rng.Next(best.Sum(x => x.s));
        foreach (var x in best)
        {
            if ((r -= x.s) < 0)
            {
                recent.Add(x.p.Id);
                if (recent.Count > 8) recent.RemoveAt(0);
                return x.p;
            }
        }
        return best[^1].p;
    }

    // How likely each extra slot is to join a message (halved when the news is grave: fewer words, no jokes).
    static readonly Dictionary<string, double> ExtraOdds = new() { ["open"] = 0.45, ["feel"] = 0.45, ["aside"] = 0.2, ["close"] = 0.35 };

    // One message: up to 3 parts in slot order. With news, the event's news part is the core; without it
    // (a second message in a row), one or two of feel and aside, never a lone sign-off. Null when nothing fits.
    public static string? Message(VoiceBook b, string ev, ISet<string> ctx, Random rng, List<int> recent, bool withNews)
    {
        bool grave = ctx.Contains("grave");
        var got = new Dictionary<string, string>();
        if (withNews)
        {
            var news = Pick(b, ev, "news", ctx, rng, recent);
            if (news == null) return null;
            got["news"] = news.Text;
        }
        var extras = (withNews ? new[] { "open", "feel", "aside", "close" } : new[] { "feel", "aside" }).OrderBy(_ => rng.Next()).ToList();
        foreach (var slot in extras)
        {
            if (got.Count >= 3) break;
            double odds = ExtraOdds[slot] * (grave ? 0.5 : 1) * (withNews ? 1 : 1.6);
            if (rng.NextDouble() >= odds) continue;
            if (Pick(b, ev, slot, ctx, rng, recent) is { } p) got[slot] = p.Text;
        }
        if (got.Count == 0)
            foreach (var slot in new[] { "feel", "aside" })
                if (Pick(b, ev, slot, ctx, rng, recent) is { } p) { got[slot] = p.Text; break; }
        if (got.Count == 0) return null;
        return string.Join(" ", Slots.Where(got.ContainsKey).Select(s => got[s]));
    }

    // The reply event for an event: its own ".re", else its parent's ("pal.trouble.war" falls back to
    // "pal.trouble.re"), so a family of events can share replies. Null when there is none (notables get no reply).
    public static string? ReplyKey(VoiceBook b, string ev)
    {
        for (string k = ev; ; k = k[..k.LastIndexOf('.')])
        {
            if (b.Speaks(k + ".re")) return k + ".re";
            if (!k.Contains('.')) return null;
        }
    }

    // A whole exchange for an event: who writes each message (ours = the leader) and its text, before names are
    // filled in. Shapes: one message; message and reply (the common case); message, reply and a follow-up;
    // two messages in a row, then a reply. The reply is the event's ".re" parts in the leader's context.
    public static List<(bool Ours, string Text)> Conversation(VoiceBook b, string ev, ISet<string> them, ISet<string> us,
        Random rng, List<int> recentThem, List<int> recentUs)
    {
        var turns = new List<(bool, string)>();
        bool grave = them.Contains("grave");
        double r = rng.NextDouble();
        int shape = grave ? (r < 0.2 ? 0 : r < 0.85 ? 1 : r < 0.9 ? 2 : 3) : (r < 0.12 ? 0 : r < 0.72 ? 1 : r < 0.88 ? 2 : 3);
        var first = Message(b, ev, them, rng, recentThem, true);
        if (first == null) return turns;
        turns.Add((false, first));
        if (shape == 3 && Message(b, ev, them, rng, recentThem, false) is { } second) turns.Add((false, second));
        if (shape == 0) return turns;
        var reKey = ReplyKey(b, ev);
        if (reKey == null) return turns;
        var reply = Message(b, reKey, us, rng, recentUs, true);
        if (reply == null) return turns;
        turns.Add((true, reply));
        if (shape == 2 && Pick(b, ev, "aside", them, rng, recentThem) is { } more) turns.Add((false, more.Text));
        return turns;
    }
}

partial class Sim
{
    static VoiceBook? book;
    // The phrase file, read on first use from resources/voices.txt next to the exe.
    public static VoiceBook Book => book ??= VoiceBook.Load(Path.Combine(AppContext.BaseDirectory, "resources", "voices.txt"));

    // The last parts each speaker used (not saved): a reload may repeat a line once, which is fine.
    readonly Dictionary<string, List<int>> voiceRecent = new();
    List<int> RecentOf(string who) => voiceRecent.TryGetValue(who, out var l) ? l : voiceRecent[who] = new();

    // The moment, shared by everyone in a conversation: time of day, season, weather, festival, hunger, and why
    // the event happened (a caller's tag such as "fire" or "war").
    void MomentTags(HashSet<string> t, string why)
    {
        int m = MinuteAt(TickNo);
        t.Add(m < 300 ? "night" : m < 660 ? "morning" : m < 1020 ? "day" : m < 1260 ? "evening" : "night");
        t.Add(SeasonNow.Name.ToLowerInvariant());
        t.Add(Weather.ToString().ToLowerInvariant());
        if (Festival) t.Add("festival");
        if (Famine || WeakCount > 0) t.Add("hungry");
        if (WarOn != null) t.Add("war");
        foreach (var w in why.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) t.Add(w);
    }

    // A bond word from a sister's regard (read without fixing it, so talk never changes her).
    string BondTag(Sister s) { double r = RelPeek(s); return r >= 0.25 ? "warm" : r > -0.45 ? "cool" : "hostile"; }

    // A sister's ruler speaking: role, traits, her rule, her bond with us, new or old in office.
    HashSet<string> SisterTags(Sister s, string why, bool grave)
    {
        var t = new HashSet<string> { "sister", BondTag(s), "g:" + s.Gov, Govs.Get(s.Gov).Harsh >= 0.6 ? "harsh" : "kind", grave ? "grave" : "light" };
        foreach (var k in RulerOf(s).Traits) t.Add("t:" + k);
        if (EvClock - RulerOf(s).Since < DayMs) t.Add("new");
        if (RulerYears(s) >= 60) t.Add("old");
        MomentTags(t, why);
        return t;
    }

    // Our leader answering: traits, our rule, the bond with whoever wrote. (Their health and silences: step 2.)
    HashSet<string> LeaderTags(string bond, string why, bool grave)
    {
        var t = new HashSet<string> { "leader", bond, "g:" + GovKey, G.Harsh >= 0.6 ? "harsh" : "kind", grave ? "grave" : "light" };
        foreach (var k in Leader?.Traits ?? Array.Empty<string>()) t.Add("t:" + k);
        MomentTags(t, why);
        return t;
    }

    // A sister's event spoken through Voices: her message(s), our leader's reply a few seconds later, maybe a
    // follow-up. False when the phrase file has nothing for it (Tell then uses the old tables).
    bool VoiceSister(Sister s, string ev, bool urgent, string extra, string why)
    {
        string key = "sister." + ev;
        if (!Book.Speaks(key)) return false;
        bool grave = urgent || why.Contains("war") || why.Contains("hungry");
        var turns = Voices.Conversation(Book, key, SisterTags(s, why, grave), LeaderTags(BondTag(s), why, grave), chatRng,
            RecentOf("sis:" + s.Name + "|" + RulerOf(s).Name), RecentOf("leader"));
        if (turns.Count == 0) return false;
        var c = SisterConvo(s);
        c.Gone = false;
        PostTurns(c, HandleOf(s), turns, t => Fill(t, s, "", extra), s, urgent);
        return true;
    }

    // Posts an exchange: each message a few seconds of "typing" after the one before; the first is also a toast.
    void PostTurns(Convo c, string them, List<(bool Ours, string Text)> turns, Func<string, string> fill, Sister? s, bool urgent)
    {
        long due = TickNo;
        for (int i = 0; i < turns.Count; i++)
        {
            var (ours, text) = turns[i];
            if (i > 0) due += Typing() - TickNo;
            string a = Post(c, ours ? MeName : them, fill(text), ours, due, ours ? null : s, urgent);
            if (i == 0) Pop($"{them} says:", a, c.Key, urgent);
        }
    }

    // A notable writes to the leader through Voices: by job, under our rule, afraid of a harsh one (all but the
    // ringleader). No reply yet: the leader's side comes last (voices-vision.md). False when the file has nothing.
    bool VoiceNpc(string role, string ev, bool urgent, string a)
    {
        string key = $"npc.{role}.{ev}";
        if (!Book.Speaks(key)) return false;
        bool grave = urgent || Voices.Grave.Contains(key);
        var t = new HashSet<string> { role, "g:" + GovKey, G.Harsh >= 0.6 ? "harsh" : "kind", grave ? "grave" : "light" };
        if (G.Harsh >= 0.6 && role != "ringleader") t.Add("fearful");
        // Every notable is a real dot-man (0.89.1) and speaks with their own traits.
        if (NotableConvo(role) is not { } c || NotableOf(role) is not { } holder) return false;
        foreach (var k in CitizenTraits(holder)) t.Add("t:" + k);
        MomentTags(t, "");
        var turns = Voices.Conversation(Book, key, t, new HashSet<string>(), chatRng, RecentOf($"npc:{role}"), RecentOf("leader"));
        if (turns.Count == 0) return false;
        PostTurns(c, c.Title, turns, x => Fill(x, null, "", a), null, urgent);
        return true;
    }

    // A friend or enemy of the leader writes, and the leader may answer (friends only; an enemy is Blocked).
    // why: how the bond began, told from the speaker's side; mine: the speaker holds the feeling (else the leader).
    bool VoicePal(Convo c, Walker w, string ev, string a = "", string why = "", bool mine = false)
    {
        if (!Book.Speaks(ev)) return false;
        bool grave = Voices.Grave.Contains(ev);
        bool foe = c.Role == "foe";
        string bond = foe ? "hostile" : "warm";
        var t = new HashSet<string> { foe ? "enemy" : "friend", bond, "g:" + GovKey, G.Harsh >= 0.6 ? "harsh" : "kind", grave ? "grave" : "light" };
        foreach (var k in CitizenTraits(w)) t.Add("t:" + k);
        if (w.Id == PrevLeader) t.Add("oldleader");
        if (Leader != null && Age - Leader.Since < DayMs) t.Add("newleader");
        if (w.Sick) t.Add("ill");
        if (w.Hurt) t.Add("wounded");
        if (YearsOfDot(w) >= 60) t.Add("old");
        if (why != "") t.Add(mine ? "mine" : "yours");
        MomentTags(t, "");
        var us = LeaderTags(bond, "", grave);
        if (t.Contains("newleader")) us.Add("newleader");
        if (t.Contains("oldleader")) us.Add("oldleader");
        var turns = Voices.Conversation(Book, ev, t, us, chatRng, RecentOf(c.Key), RecentOf("leader"));
        if (turns.Count == 0) return false;
        string first = Leader?.Name ?? "";
        PostTurns(c, c.Title, turns, x => Fill(x.Replace("{first}", first).Replace("{title}", MeName)
            .Replace("{Why}", why == "" ? "" : Cap(why)).Replace("{why}", why), null, "", a), null, false);
        return true;
    }

    // Test (--voices): checks the phrase file and writes sample conversations for a spread of speakers, rules,
    // bonds and moments into voice-samples.txt next to the exe.
    public static void WriteVoiceSamples(string path)
    {
        var b = Book;
        var sb = new System.Text.StringBuilder();
        var issues = new List<string>(b.Problems);
        foreach (var p in b.Parts)
        {
            foreach (var t in p.Need.Concat(p.Lean).Concat(p.Not).Where(t => !Voices.IsKnown(t)).Distinct())
                issues.Add($"line {p.Line}: unknown tag '{t}'");
            if (!Voices.Slots.Contains(p.Slot)) issues.Add($"line {p.Line}: unknown slot '{p.Slot}'");
        }
        foreach (var h in b.Headers.Where(h => h.Count == 0)) issues.Add($"line {h.Line}: {h.Header} has no phrases");
        foreach (var ev in b.Parts.Select(p => p.Ev).Distinct().Where(e => e != "*"))
        {
            if (!Voices.Events.Contains(ev)) issues.Add($"event '{ev}': no code speaks it yet");
            if (!b.Speaks(ev)) issues.Add($"event '{ev}': has no news part, so it is never said");
        }
        foreach (var ev in Voices.Events.Where(e => !b.Speaks(e))) issues.Add($"event '{ev}': the code speaks it, the file has no news for it");
        foreach (var slot in Voices.Slots.Where(s => !b.Parts.Any(p => p.Slot == s))) issues.Add($"slot '{slot}': no part uses it");

        sb.AppendLine($"Voices check: {b.Parts.Count} parts under {b.Headers.Count} headers, {issues.Count} issue(s).");
        foreach (var i in issues) sb.AppendLine("  ! " + i);
        sb.AppendLine();
        foreach (var ev in b.Parts.Select(p => p.Ev).Distinct().OrderBy(e => e))
            sb.AppendLine($"  {ev}: " + string.Join(", ", Voices.Slots.Select(s => $"{s} {b.Parts.Count(p => p.Ev == ev && p.Slot == s)}")));
        sb.AppendLine();

        var rng = new Random(2026);
        string[] govs = { "commune", "council", "mayor", "guild", "temple", "crown", "junta", "party" };
        string[] whys = { "", "", "fire", "hungry", "war" };
        string[] times = { "morning", "day", "evening", "night" };
        string[] skies = { "clear", "cloudy", "rain", "snow", "fog" };
        string[] seasons = { "spring", "summer", "autumn", "winter" };
        var keys = Traits.All.Select(t => t.Key).ToArray();
        var recent = new Dictionary<string, List<int>>();
        List<int> Rec(string k) => recent.TryGetValue(k, out var l) ? l : recent[k] = new();
        string Harsh(string g) => Govs.Get(g).Harsh >= 0.6 ? "harsh" : "kind";
        foreach (var ev in Voices.Events.Where(e => !e.EndsWith(".re")))
        {
            int n = ev == "sister.aid" ? 12 : 3;
            for (int i = 0; i < n; i++)
            {
                string theirGov = govs[rng.Next(govs.Length)], ourGov = govs[rng.Next(govs.Length)];
                string why = ev == "sister.aid" ? whys[rng.Next(whys.Length)] : "";
                bool grave = why is "war" or "hungry" || Voices.Grave.Contains(ev);
                var moment = new[] { times[rng.Next(4)], seasons[rng.Next(4)], skies[rng.Next(skies.Length)] }.Concat(why == "" ? Array.Empty<string>() : new[] { why }).ToList();
                var lt = keys.OrderBy(_ => rng.Next()).Take(3).ToArray();
                HashSet<string> them;
                string who, bond = "warm";
                if (ev.StartsWith("npc."))
                {
                    string role = ev.Split('.')[1];
                    them = new HashSet<string>(moment) { role, "g:" + ourGov, Harsh(ourGov), grave ? "grave" : "light" };
                    if (Harsh(ourGov) == "harsh" && role != "ringleader") them.Add("fearful");
                    foreach (var k in RoleLean.GetValueOrDefault(role) ?? Array.Empty<string>()) them.Add("t:" + k);
                    who = $"{role} under our {ourGov}";
                }
                else if (ev.StartsWith("pal."))
                {
                    bool foe = ev == "pal.hello" && rng.Next(3) == 0;
                    bond = foe ? "hostile" : "warm";
                    var tt = keys.OrderBy(_ => rng.Next()).Take(3).ToArray();
                    them = new HashSet<string>(moment) { foe ? "enemy" : "friend", bond, "g:" + ourGov, Harsh(ourGov), grave ? "grave" : "light" };
                    foreach (var k in tt) them.Add("t:" + k);
                    if (ev == "pal.hello" && rng.Next(4) == 0) them.Add("oldleader");
                    if (ev == "pal.hello" && rng.Next(3) == 0) them.Add("newleader");
                    if (ev == "pal.hello" && rng.Next(2) == 0) them.Add(rng.Next(2) == 0 ? "mine" : "yours");
                    if (ev == "pal.trouble.sick") them.Add("ill");
                    who = $"{(foe ? "enemy" : "friend")} ({string.Join(", ", tt)}) [{string.Join(", ", them.Where(x => x is "oldleader" or "newleader" or "mine" or "yours"))}]";
                }
                else
                {
                    var tt = keys.OrderBy(_ => rng.Next()).Take(2).ToArray();
                    them = new HashSet<string>(moment) { "sister", bond, "g:" + theirGov, Harsh(theirGov), grave ? "grave" : "light" };
                    foreach (var k in tt) them.Add("t:" + k);
                    who = $"{theirGov} ({string.Join(", ", tt)})";
                }
                var us = new HashSet<string>(moment) { "leader", bond, "g:" + ourGov, Harsh(ourGov), grave ? "grave" : "light" };
                foreach (var k in lt) us.Add("t:" + k);
                foreach (var k in new[] { "newleader", "oldleader" }) if (them.Contains(k)) us.Add(k);
                sb.AppendLine($"#{i + 1} {ev}: {who}; to our {ourGov} ({string.Join(", ", lt)}); {string.Join(", ", moment)}");
                foreach (var (ours, text) in Voices.Conversation(b, ev, them, us, rng, Rec(ev.Split('.')[0] + ev.Split('.')[1]), Rec("leader")))
                    sb.AppendLine($"    {(ours ? "us  " : "them")}: " + text.Replace("{us}", "Dotburg").Replace("{them}", "Oakford").Replace("{me}", "Mayor Ada")
                        .Replace("{gov}", Govs.Get(theirGov).Lower).Replace("{tier}", "village").Replace("{fest}", "the new library").Replace("{a}", SampleA(ev)).Replace("{other}", "Elmwick")
                        .Replace("{first}", "Ada").Replace("{title}", "Mayor Ada").Replace("{Why}", "You shared your bread with me at the bakery").Replace("{why}", "you shared your bread with me at the bakery"));
                sb.AppendLine();
            }
        }
        File.WriteAllText(path, sb.ToString());
    }

    // What {a} carries for each event in the samples, the same kind of thing the code passes.
    static string SampleA(string ev) => ev switch
    {
        "npc.physician.start" => "Marsh fever in the low houses. The church takes in the sick.",
        "npc.physician.over" => "The marsh fever has passed: 6 fell ill and a life was lost.",
        "npc.physician.leader" => "Mayor Ada",
        "npc.physician.growth" or "npc.physician.surgery" => "belly",
        "npc.physician.fall" => "a broken wrist",
        "npc.physician.week" => "3 funerals",
        "npc.physician.wound" => "Mayor Ada",
        "npc.steward.report" => "9 days of food in store; the ovens baked 40 loaves and the counters sold 52 meals.",
        "npc.steward.death" => "A dotlet",
        "npc.steward.low" => "2",
        "npc.steward.hungry" => "7",
        "npc.steward.caravan" => "Oakford",
        "npc.warden.death" => "Bram Holt",
        "npc.ringleader.won" => "junta",
        "npc.grocer.line" => "5",
        "npc.grocer.hoard" => "4",
        _ when ev.StartsWith("npc.warden") => "bakery",
        _ when ev.StartsWith("npc.captain") => "Oakford",
        _ when ev.StartsWith("npc.steward") => "120",
        _ when ev.StartsWith("pal.") => "Oakford",
        _ => "Ada",
    };
}
