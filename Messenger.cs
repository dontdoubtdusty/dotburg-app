namespace Dotburg;

// One line in a conversation: who said it and what, on which town day. Due is the tick it shows up, so a
// reply can be "typed" for a few seconds first. Ours: written by our leader.
class ChatLine { public string From = ""; public string Text = ""; public int Day; public bool Ours; public long Due; public string S = ""; public int H = -1; }   // H: minute of the town day it arrived (-1 for lines from before 0.74)

// A person's chat font and colour, as MSN let everyone pick their own. Face is a Windows font file name without .ttf.
record ChatStyle(string Face, int Col);

// A conversation in the Messenger: a sister town and our leader, a group chat between two sisters that we
// read along with, or a notable dot-man of this town writing to the leader.
class Convo
{
    public string Key = "";        // "sis:Name", "gos:A|B", "npc:role:founded"
    public string Kind = "";       // sister, gossip, npc
    public string Title = "";      // the contact's name
    public string Role = "";       // npc only: captain, physician, warden, ringleader
    public List<ChatLine> Lines = new();
    public int Seen;               // how many lines the reader has seen
    public bool Gone;              // sister absorbed or collapsed, gossip partner gone, or a notable of an old town
    public double Last;            // EvClock of the newest line, to sort by
    public string LastRaw = "";    // the last line as picked, before styling (not saved), so a notable never repeats
}

// A pop-up in the corner of the town picture: the crier, or someone saying something.
class Toast { public string Title = ""; public string Text = ""; public string Key = ""; public long Tick; public bool Urgent; public bool Crier; }

enum Presence { Online, Busy, Away, Offline, Blocked }

// The Messenger: sisters, gossip and notables talk in MSN-style conversations; crier lines become toasts.
// Its random draws use their own generator, so adding talk never changes what happens in the town.
partial class Sim
{
    const int ChatKeep = 40, OfflineKeep = 4, GossipKeep = 4;
    public readonly List<Convo> Convos = new();
    public readonly List<Toast> Toasts = new();   // new toasts, taken by the window each frame
    public long NudgeTick = -1;                   // the tick of the last urgent message (the panel shakes)
    readonly Random chatRng = new(7177);

    T CPick<T>(IList<T> a) => a[chatRng.Next(a.Count)];
    long Typing() => TickNo + (long)((3 + chatRng.NextDouble() * 6) * 1000 / TickMs);

    // Our leader's short name for the chat ("Mayor Ada"), or the town while nobody leads.
    public string MeName => Leader != null ? $"{G.Title} {Leader.Name}" : Name;

    // ---------- posting ----------
    Convo ConvoFor(string key, string kind, string title, string role = "")
    {
        var c = Convos.FirstOrDefault(x => x.Key == key);
        if (c == null) { c = new Convo { Key = key, Kind = kind, Title = title, Role = role }; Convos.Add(c); }
        c.Title = title;
        return c;
    }

    // The minute of the town day (0..1439) at a given tick, for chat timestamps.
    int MinuteAt(long tick) => (int)(((Age + (tick - TickNo) * TickMs) % DayMs + DayMs) % DayMs / DayMs * 1440);

    // A minute of the town day as MSN wrote it on an XP clock: "2:14 PM".
    public static string ClockText(int minute)
    {
        int h = minute / 60, m = minute % 60;
        return $"{(h % 12 == 0 ? 12 : h % 12)}:{m:00} {(h < 12 ? "AM" : "PM")}";
    }

    // Sister lines are signed with her ruler's address at the time, so old lines keep the old ruler.
    // The line is written in the writer's own hand (Hands.cs); a hand that types in bursts posts several short
    // lines a second or two apart. Returns the line as written, for the toast.
    string Post(Convo c, string from, string text, bool ours, long due, Sister? s = null, bool urgent = false)
    {
        var parts = Styled(c, text, ours, s, urgent);
        long gap = (long)(1.4 * 1000 / TickMs);
        for (int i = 0; i < parts.Count; i++)
        {
            long d = due + i * gap;
            c.Lines.Add(new ChatLine { From = from, Text = parts[i], Day = DayNumber, Ours = ours, Due = d, S = StyleKey(c, ours, s), H = MinuteAt(d) });
        }
        int over = c.Lines.Count - ChatKeep;
        if (over > 0) { c.Lines.RemoveRange(0, over); c.Seen = Math.Max(0, c.Seen - over); }
        c.Last = EvClock;
        return string.Join(" ", parts);
    }

    // Queue a toast. A chat line replaces the crier line of the same tick (they are the same news).
    void Pop(string title, string text, string key, bool urgent, bool crier = false)
    {
        if (Quiet) return;
        var last = Toasts.Count > 0 ? Toasts[^1] : null;
        if (last != null && last.Tick == TickNo)
        {
            if (crier && !last.Crier) return;
            if (!crier && last.Crier) Toasts.RemoveAt(Toasts.Count - 1);
        }
        Toasts.Add(new Toast { Title = title, Text = text, Key = key, Tick = TickNo, Urgent = urgent, Crier = crier });
        if (Toasts.Count > 8) Toasts.RemoveAt(0);
        if (urgent) NudgeTick = TickNo;
    }

    // Placeholders: {us} this town, {them} the sister, {other} the other sister in a gossip, {me} our leader,
    // {gov} the sister's rule, {tier} our tier, {a} an extra word given by the caller.
    string Fill(string t, Sister? s, string other = "", string a = "") => t
        .Replace("{us}", Name).Replace("{them}", s?.Name ?? "").Replace("{other}", other).Replace("{me}", MeName)
        .Replace("{gov}", s != null ? Govs.Get(s.Gov).Lower : "").Replace("{tier}", TierNames[Tier - 1].ToLower()).Replace("{a}", a).Replace("{fest}", FestPlace);

    Convo SisterConvo(Sister s) => ConvoFor("sis:" + s.Name, "sister", HandleOf(s));
    Sister? SisterOfConvo(Convo c) => c.Key.StartsWith("sis:") ? SisterNamed(c.Key[4..]) : null;

    // A sister writes about an event, and our leader answers (gently or harshly, by our rule) a few seconds later.
    // The sister's line is looked up by the event key, or by its first part ("plea.yes" falls back to "plea").
    // Events the phrase file covers go through Voices (Voices.cs) instead; why tags the moment ("fire", "war").
    public void Tell(Sister? s, string ev, bool urgent = false, string extra = "", string why = "")
    {
        if (s == null || Quiet) return;
        if (VoiceSister(s, ev, urgent, extra, why)) return;
        if (!TheySay.TryGetValue(ev, out var theirs) && !TheySay.TryGetValue(ev.Split('.')[0], out theirs)) return;
        var c = SisterConvo(s);
        c.Gone = false;
        string a = Post(c, HandleOf(s), Fill(CPick(theirs), s, "", extra), false, TickNo, s, urgent);
        Pop($"{HandleOf(s)} says:", a, c.Key, urgent);
        if (WeSay.TryGetValue(ev, out var ours) || WeSay.TryGetValue(ev.Split('.')[0], out ours))
        {
            var pool = G.Harsh >= 0.6 ? ours.harsh : ours.kind;
            if (pool.Length > 0) Post(c, MeName, Fill(CPick(pool), s, "", extra), true, Typing(), null, urgent);
        }
    }

    // Two sisters talk in a group chat we can read; the first speaks, the second answers.
    public void Gossip(Sister a, Sister b, string ev)
    {
        if (Quiet || !GossipSay.TryGetValue(ev, out var pair)) return;
        var names = new[] { a.Name, b.Name }.OrderBy(n => n, StringComparer.Ordinal).ToArray();
        var c = ConvoFor($"gos:{names[0]}|{names[1]}", "gossip", $"{names[0]}, {names[1]}");
        c.Gone = false;
        int k = chatRng.Next(pair.a.Length);
        string la = Fill(pair.a[k], a, b.Name), lb = Fill(pair.b[Math.Min(k, pair.b.Length - 1)], b, a.Name);
        la = Post(c, HandleOf(a), la, false, TickNo, a);
        Post(c, HandleOf(b), lb, false, Typing(), b);
        Pop($"{HandleOf(a)} says:", la, c.Key, false);
        // Only the newest few group chats are kept.
        var old = Convos.Where(x => x.Kind == "gossip").OrderByDescending(x => x.Last).Skip(GossipKeep).ToList();
        foreach (var o in old) Convos.Remove(o);
    }

    // A notable of this town writes to the leader. {a} carries the detail (a building, an illness line, a name).
    // Events in the phrase file go through Voices (Voices.cs) first.
    public void Npc(string role, string ev, bool urgent = false, string a = "")
    {
        if (Quiet || VoiceNpc(role, ev, urgent, a) || (!NpcSay.TryGetValue($"{role}.{ev}", out var lines) && !RoutineSay.TryGetValue($"{role}.{ev}", out lines))) return;
        var c = ConvoFor($"npc:{role}:{Founded}", "npc", NpcTitle(role), role);
        // Never the same line twice running: pick again (a few tries) if it matches the last thing they said
        // (compared before styling, so typos and closers don't hide a repeat).
        string t = Fill(CPick(lines), null, "", a);
        for (int k = 0; k < 4 && t == c.LastRaw && lines.Length > 1; k++) t = Fill(CPick(lines), null, "", a);
        c.LastRaw = t;
        t = Post(c, c.Title, t, false, TickNo, null, urgent);
        Pop($"{c.Title} says:", t, c.Key, urgent);
    }

    // A new settlement shows up on the list with a hello.
    void SignIn(Sister s)
    {
        if (Quiet) return;
        Tell(s, "hello");
        var t = Toasts.LastOrDefault();
        if (t != null && t.Key == "sis:" + s.Name) { t.Title = HandleOf(s); t.Text = "has just signed in."; }
    }

    // A sister that is gone says goodbye and goes Offline; only the newest few offline chats are kept.
    void SignOff(Sister s, string ev, string other = "")
    {
        if (Quiet) return;
        var c = SisterConvo(s);
        string a = Post(c, HandleOf(s), Fill(CPick(TheySay[ev]), s, other), false, TickNo, s);
        Pop($"{HandleOf(s)} says:", a, c.Key, false);
        if (WeSay.TryGetValue(ev, out var ours)) Post(c, MeName, Fill(CPick(G.Harsh >= 0.6 ? ours.harsh : ours.kind), s), true, Typing());
        c.Gone = true;
        foreach (var g in Convos.Where(x => x.Kind == "gossip" && x.Key.Contains(s.Name))) g.Gone = true;
        TrimOffline();
    }

    void TrimOffline()
    {
        var old = Convos.Where(x => x.Gone && x.Kind != "gossip").OrderByDescending(x => x.Last).Skip(OfflineKeep).ToList();
        foreach (var o in old) Convos.Remove(o);
    }

    // On a charter the notables stay with the old town.
    void RetireNotables()
    {
        foreach (var c in Convos.Where(x => x.Kind == "npc")) c.Gone = true;
        TrimOffline();
    }

    // Talk that is not tied to an event, rolled with the region: sisters chatting, the ringleader grumbling.
    void Chatter2()
    {
        if (Quiet) return;
        if (Sisters.Count > 0 && chatRng.NextDouble() < PerDay(0.25 * Math.Min(4, Sisters.Count)))
        {
            var s = CPick(Sisters);
            double r = RelPeek(s);
            Tell(s, r >= 0.25 ? "small.friend" : r > -0.45 ? "small.cool" : "small.hostile");
        }
        if (G.Harsh >= 0.5 && Mood < 0.45 && chatRng.NextDouble() < PerDay(0.4)) Npc("ringleader", "grumble");
        PalChatter();
    }

    // A notable's display name, from a hash of the role and the town (in the town's own naming lean), so it stays put.
    public string NpcTitle(string role)
    {
        int i = Array.IndexOf(NpcRoles, role);
        string n = GivenFrom(Names.Seed($"{role}.{Name}.{Founded}"), TownLean);
        return role switch
        {
            "captain" => $"Captain {n}",
            "physician" => GovKey == "temple" ? $"Sister {n}" : $"Doctor {n}",
            "warden" => $"Warden {n}",
            "steward" => $"Steward {n}",
            "baker" => $"Baker {n}",
            "grocer" => $"Grocer {n}",
            _ => $"xX_Free{Name}_Xx",
        };
    }
    public static readonly string[] NpcRoles = { "captain", "physician", "warden", "ringleader", "steward", "baker", "grocer" };

    // ---------- status and personal messages ----------
    public Presence PresenceOf(Convo c)
    {
        if (c.Gone) return Presence.Offline;
        if (c.Kind == "gossip") return Presence.Online;
        if (c.Kind == "pal") return PalPresence(c);
        if (c.Kind == "npc")
            return c.Role switch
            {
                "captain" => WarOn != null ? Presence.Busy : Presence.Away,
                "physician" => Ill != null ? Presence.Busy : Presence.Away,
                "warden" => Fires.Count > 0 ? Presence.Busy : Presence.Away,
                "steward" => Panic > 0.3 || WeakCount > 0 ? Presence.Busy : Presence.Away,
                "baker" or "grocer" => Daylight() < 0.3 ? Presence.Away : Panic > 0.5 || breadOut || shelvesBare ? Presence.Busy : Presence.Online,
                _ => G.Harsh >= 0.5 ? Presence.Online : Presence.Offline,
            };
        return SisterPresence(SisterOfConvo(c));
    }

    // A sister's status: Busy at war, Away in a blight, Blocked when hostile, else Online.
    public Presence SisterPresence(Sister? s)
    {
        if (s == null) return Presence.Offline;
        if (WarOn?.Foe == s.Name || (SisterWarOn != null && (SisterWarOn.A == s.Name || SisterWarOn.B == s.Name))) return Presence.Busy;
        if (BlightOn?.Towns.Contains(s.Name) == true) return Presence.Away;
        if (RelPeek(s) <= -0.45) return Presence.Blocked;
        return Presence.Online;
    }

    public Presence MePresence => WarOn != null || Fires.Count > 0 || Ill != null ? Presence.Busy : Daylight() < 0.25 ? Presence.Away : Presence.Online;

    public string MeMessage()
    {
        if (WarOn != null) return $"At war with {WarOn.Foe} :@";
        if (Fires.Count > 0) return "FIRE. Buckets, everyone!";
        if (Ill != null) return "Wash your hands +o(";
        if (Famine) return "Tighten your belts :(";
        if (Festival) return $"Party at {FestPlace}! (jester)";
        if (Leader == null) return "Looking for a leader";
        if (Mood < 0.3) return "Things will get better. Probably :S";
        // Half of all leaders write their own motto (by their voice), the rest the rule's: the account is theirs.
        if (LeaderWalker is { } lw && Trait01(lw.Id, 901) < 0.5 && TraitMottos.TryGetValue(VoiceOf(lw), out var own)) return own;
        return Mottos[GovKey];
    }

    public string MessageOf(Convo c)
    {
        if (c.Kind == "gossip") return c.Gone ? "(this chat has ended)" : "(group chat, you are reading along)";
        if (c.Kind == "pal") return PalMessage(c);
        if (c.Kind == "npc")
            return c.Role switch
            {
                "captain" => WarOn != null ? $"At the front against {WarOn.Foe}" : "Polishing my boots 8-)",
                "physician" => Ill != null ? "Visiting the sick. Do not disturb" : "An apple a day (A)",
                "warden" => Fires.Count > 0 ? "AT A FIRE" : "Check your chimneys",
                "steward" => WeakCount > 0 ? "People are starving :'(" : Panic > 0.3 ? "Queues everywhere :S" : $"{Math.Max(0, Math.Round(FoodDays))} days of food in store (meal)",
                "baker" => Daylight() < 0.3 ? "Asleep. Up at four (zz)" : breadOut ? "Sold out :S" : "Fresh bread every morning (food)",
                "grocer" => Daylight() < 0.3 ? "Closed. Open at dawn" : shelvesBare ? "Nothing on the shelves :(" : "Bread, greens, gossip :)",
                _ => G.Harsh >= 0.5 ? "you know what this is about :-#" : "retired (H)",
            };
        return SisterMessage(SisterOfConvo(c));
    }

    // A sister's personal message: her war or blight, else her rule's motto and how she feels about us.
    public string SisterMessage(Sister? s)
    {
        if (s == null) return "gone, but not forgotten";
        if (WarOn?.Foe == s.Name) return $"At war with {Name} :@";
        if (SisterWarOn != null && (SisterWarOn.A == s.Name || SisterWarOn.B == s.Name)) return $"At war with {(SisterWarOn.A == s.Name ? SisterWarOn.B : SisterWarOn.A)}";
        if (BlightOn?.Towns.Contains(s.Name) == true) return BlightOn.Kind == "plague" ? "Plague here. Stay away +o(" : "Hungry :(";
        if (s.Rel == null) return Mottos[s.Gov];   // not looked at yet: Attitude would fix the relation
        string word = Attitude(s).word;
        string tail = word switch { "allied" => $" (L) {Name}", "friendly" => " :)", "hostile" => $" :@ {Name}", "wary" or "scornful" => " 8-|", _ => "" };
        return Mottos[s.Gov] + tail;
    }

    static readonly Dictionary<string, string> Mottos = new()
    {
        ["none"] = "Room for more :)", ["elders"] = "Slow and steady (gran)", ["headman"] = "My word is law", ["founders"] = "Family first (L)", ["shrine"] = "Light a candle (A)",
        ["commune"] = "All for all (L)", ["council"] = "Minutes of the last meeting attached",
        ["mayor"] = "Vote for me ;)", ["guild"] = "Everything has a price", ["temple"] = "Bless you (A)", ["crown"] = "Long live me (crown)",
        ["junta"] = "Order. (cop)", ["party"] = "The Party thanks you",
    };

    // ---------- fonts and colours ----------
    // Who picks what: our leader by their first trait, a sister town by its rule, a notable by their job.
    // The key is stored on each line, so a line keeps its look after the leader changes.
    // A friend picks by their own traits, as a leader does, so someone keeps their font if they come to lead.
    string StyleKey(Convo c, bool ours, Sister? s) =>
        ours ? TraitStyle(Leader?.Traits)
        : c.Kind == "pal" ? (ById(PalId(c)) is { } pw ? TraitStyle(CitizenTraits(pw)) : "")
        : c.Kind == "npc" ? "n:" + c.Role
        : s != null ? "g:" + s.Gov : "";

    // The style of the first trait that has one (heart, then work, then spirit; every spirit trait has one).
    public static string TraitStyle(string[]? traits) => traits?.FirstOrDefault(k => ChatStyles.ContainsKey("t:" + k)) is { } t ? "t:" + t : "";

    public static readonly Dictionary<string, ChatStyle> ChatStyles = new()
    {
        ["t:able"] = new("verdana", 0x1f4fa0), ["t:idle"] = new("comic", 0x8a4ab8), ["t:cruel"] = new("arialbd", 0x8b0000),
        ["t:merciful"] = new("georgiai", 0x2a7a5a), ["t:honest"] = new("tahoma", 0x000080), ["t:cunning"] = new("cour", 0x3a4458),
        ["t:proud"] = new("palab", 0x7a3a9a), ["t:tolerant"] = new("trebuc", 0x1a7f80),
        // Spirit traits and the doomsayer (0.69): everyone has a spirit trait, so the chronicler and every friend has a look.
        ["t:pious"] = new("palai", 0x5a2a6a), ["t:cynical"] = new("cour", 0x404040), ["t:cheerful"] = new("comic", 0xc06010),
        ["t:gloomy"] = new("georgiai", 0x3a3a5a), ["t:hotheaded"] = new("impact", 0xb02020), ["t:calm"] = new("georgia", 0x2a5a7a),
        ["t:curious"] = new("trebuc", 0x1a7a5a), ["t:brave"] = new("arialbd", 0x2a4a8a), ["t:craven"] = new("verdana", 0x7a7a5a),
        ["t:doomsayer"] = new("courbd", 0x6a0000),
        ["g:elders"] = new("times", 0x6a4a2a), ["g:commune"] = new("comic", 0x2f8a3a), ["g:council"] = new("verdana", 0x2050a0),
        ["g:mayor"] = new("trebuc", 0x1f7f7a), ["g:guild"] = new("georgia", 0x8a5a10), ["g:temple"] = new("pala", 0x6a2a6a),
        ["g:crown"] = new("georgiai", 0x6a1a9a), ["g:junta"] = new("arialbd", 0x4a5a2a), ["g:party"] = new("impact", 0xb02020),
        ["n:captain"] = new("arialbd", 0x3a5a2a), ["n:physician"] = new("times", 0x103a80), ["n:warden"] = new("verdanab", 0xc04000), ["n:steward"] = new("georgia", 0x6a4a10),
        ["n:baker"] = new("comic", 0xa0522d), ["n:grocer"] = new("trebuc", 0x2a6a2a),
        ["n:ringleader"] = new("courbd", 0xb00000),
    };
    public static IEnumerable<string> ChatFaces => ChatStyles.Values.Select(v => v.Face).Distinct();

    // ---------- what they say ----------
    static readonly Dictionary<string, string[]> TheySay = new()
    {
        ["hello"] = new[] { "Hello? Is this thing on? :$", "Hi neighbours! {them} here, just founded :)" },
        ["charter"] = new[] { "(*) Good luck, little {us}! We were a hamlet once too ;)", "Proud of you, {us}. Write often (L)" },
        ["aid"] = new[] { "Wagons are on the way. Chin up :)", "Grain and timber, on us. Don't mention it (F)" },
        ["thanks"] = new[] { "Your aid arrived. We won't forget this (L)", "Thank you, {us}. We were in a bad way :(" },
        ["gifts"] = new[] { "Our envoys are bringing you gifts (*)", "A little something for your hall. Hope you like it :D" },
        ["trade"] = new[] { "Sending envoys over. Let's talk trade (case)", "Our people want your goods. The envoys will explain 8-)" },
        ["turned"] = new[] { "Your guards turned our envoys back at the gate?! :@", "Really? Sent home at the gate? :(" },
        ["note"] = new[] { "A formal note is on its way. Read it carefully :|", "Our envoys carry a complaint. Several, actually 8-|" },
        ["wedding"] = new[] { "One of ours and one of yours, at the altar? (bouquet)", "Wedding bells between our towns? Say yes (R)" },
        ["plea"] = new[] { "Our granaries are empty. Can you spare anything? :(", "We're down to the last sack of flour :'(" },
        ["tribute"] = new[] { "Pay tribute, or else :@", "Our soldiers are bored. Tribute keeps them that way 8-)" },
        ["rule"] = new[] { "(H) New management here. A {gov} now.", "Big news: {them} is a {gov} now :-O" },
        ["revolution"] = new[] { "(H) The old order is gone! {them} is free :D", "We rose up. A {gov} rules {them} now (*)" },
        ["refugees"] = new[] { "Our people are fleeing. Please take them in :'(", "Families on the road to you. Be kind :(" },
        ["rigged"] = new[] { "Our team came home bruised. Some match :@", "That referee was one of yours :@" },
        ["fairwon"] = new[] { "Well played :| Rematch next year.", "You got lucky ;)" },
        ["fairlost"] = new[] { "Ha! We won (trophy)", "Good game, {us} :P" },
        ["spycaught"] = new[] { "Spy? What spy? :$", "That man was a tourist :S", "Release our man from your (jail) at once :@" },
        ["spypapers"] = new[] { "Lovely ledgers you keep ;)", "Lost something? ;)" },
        ["spyfire"] = new[] { ":-# Nothing to do with us.", "Shame about the fire ;)" },
        ["caravan"] = new[] { "Our wagons are heading your way :)", "Caravan's left. Buy everything (horse)" },
        ["warwe"] = new[] { ":-O You're marching on us?!", "So it's war, then :@" },
        ["warthey"] = new[] { ":@ Enough. Our soldiers march on {us}.", "We are coming for you, {us} :@" },
        ["raid"] = new[] { ":@ Our raiders send their regards.", "Smell the smoke? (6)" },
        ["warwon"] = new[] { ":'( Enough. We yield.", "Take your victory. We'll remember :@", "(peace) Enough blood. We sue for peace." },
        ["warlost"] = new[] { "(trophy) Better luck next war.", "Victory to {them}! Pay up :P" },
        ["plague"] = new[] { ":'( Plague in our streets. Keep your gates shut.", "Sickness here. Don't visit +o(" },
        ["famine"] = new[] { "Our bread is gone :( Anything to spare?", "Hungry here. Very hungry :'(", "No (food) left. The dotlets ask for bread :'(" },
        ["tierup"] = new[] { "(*) Look at you, a {tier} now :D", "Congrats on becoming a {tier}! (medal)" },
        ["absorbed"] = new[] { "(bah) {other} has taken us in. Signing off." },
        ["collapse"] = new[] { "It's over here. Look after our people :'( Signing off.", "The last of us are leaving. Goodbye, {us} :(" },
        ["mourn"] = new[] { ":'( We buried {a} today.", "Black flags over {them}. {a} is gone :(", "(bah) {a} is dead. I rule {them} now." },
        ["mourn.knife"] = new[] { ":-# {a} was murdered. Nobody saw anything, of course.", "{a} is dead. A (knife) in the dark :@" },
        ["mourn.battle"] = new[] { ":'( {a} fell at the front. I lead {them} now." },
        ["mourn.age"] = new[] { "{a} passed in their sleep. A long life (F)", ":( {a} is gone. Old age. I lead {them} now." },
        ["small"] = new[] { "Hey :)" },
        ["small.friend"] = new[] { "Hey :)", "Lovely weather, isn't it? (#)", "Miss you guys (L)", "Anything new over there? :D", "(F) Thinking of you.", "Our baker says hi ;)", "Our hens are laying like mad (hen)", "Fresh apples here (apple) come and get them" },
        ["small.cool"] = new[] { "Hello. :|", "We see you, {us}. 8-|", "Busy. Talk later |-)", "Nice town. Shame if something happened to it ;)" },
        ["small.hostile"] = new[] { "We're watching you :@", "(bah)", "Still here? :P", "Your banner is ugly :@" },
    };

    // Our leader's answer to each event: gentle rule, then harsh rule (0.6 and up).
    static readonly Dictionary<string, (string[] kind, string[] harsh)> WeSay = new()
    {
        ["hello"] = (new[] { "Welcome to the region! :D", "Hi! Come visit (F)" }, new[] { "Noted. Behave." }),
        ["charter"] = (new[] { "We'll make you proud (L)" }, new[] { "We will surpass you." }),
        ["aid"] = (new[] { "Thank you, friends (L)", "You're the best :'(" }, new[] { "Acceptable." }),
        ["thanks"] = (new[] { "Anytime :)" }, new[] { "You owe us." }),
        ["gifts"] = (new[] { ":D Thank you!", "You shouldn't have (L)" }, new[] { "We accept your tribute." }),
        ["trade"] = (new[] { "Deal :)", "Send them to the market ;)" }, new[] { "Our terms. Not yours." }),
        ["turned"] = (new[] { "Sorry, rules are rules :S" }, new[] { "Nobody enters without leave.", "Next time, ask." }),
        ["note"] = (new[] { "Received. We'll think on it :S" }, new[] { "Noted. Filed. Ignored." }),
        ["wedding"] = (new[] { "Yes! (L) (K) (bouquet)", "I'll bring the cake :D" }, new[] { "Approved. Paperwork follows." }),
        ["plea.yes"] = (new[] { "Grain is on its way :)" }, new[] { "Grain sent. Remember who sent it." }),
        ["plea.no"] = (new[] { "Our cupboards are bare too :(" }, new[] { "No." }),
        ["tribute.refused"] = (new[] { "No. We won't be bullied :@" }, new[] { "Come and take it." }),
        ["tribute.paid"] = (new[] { "Fine. Here :(" }, new[] { "Paid. This time." }),
        ["rule"] = (new[] { "Congratulations, I think :S" }, new[] { "We'll see how long that lasts." }),
        ["revolution"] = (new[] { "Good luck to you (*)" }, new[] { "Rabble." }),
        ["refugees.in"] = (new[] { "Gates are open. Send them :)" }, new[] { "They may enter. They will work." }),
        ["refugees.out"] = (new[] { "We're full, I'm so sorry :'(" }, new[] { "Turned back. Feed your own." }),
        ["rigged"] = (new[] { "The scoreboard says otherwise (H)" }, new[] { "The scoreboard says otherwise (H)" }),
        ["fairwon"] = (new[] { "Good game! :D" }, new[] { "As expected." }),
        ["fairlost"] = (new[] { "Well played (*)" }, new[] { "Rigged." }),
        ["spycaught"] = (new[] { "Sure he was :|" }, new[] { "He's in our cells. Want him back? 8-)" }),
        ["spypapers"] = (new[] { ":@ Give them back." }, new[] { "We will find him." }),
        ["spyfire"] = (new[] { ":@ We know it was you." }, new[] { "You will pay for this." }),
        ["caravan"] = (new[] { "Ooh, shopping :D", "The market's ready :)" }, new[] { "Taxed at the gate." }),
        ["warwe"] = (new[] { "We had no choice :(" }, new[] { "Yes. Surrender now." }),
        ["warthey"] = (new[] { "We don't want this :'(" }, new[] { "Come, then." }),
        ["raid"] = (new[] { "Cowards :@" }, new[] { "We'll repay that tenfold." }),
        ["warwon"] = (new[] { "Let's never do this again :|" }, new[] { "(H) Bow." }),
        ["warlost"] = (new[] { "You win. Enough :'(" }, new[] { "This isn't over :@" }),
        ["plague"] = (new[] { "Stay safe (F)", "Thinking of you :(" }, new[] { "Gates shut. Good luck." }),
        ["famine"] = (new[] { "We'll see what we can spare :(" }, new[] { "Ration harder." }),
        ["tierup"] = (new[] { "Thank you! :D", "(*) We're growing up!" }, new[] { "Naturally." }),
        ["absorbed"] = (new[] { "Goodbye, old friend :'(" }, new[] { "Goodbye." }),
        ["collapse"] = (new[] { "Goodbye, old friend :'(", "We'll look after them (L)" }, new[] { "Goodbye." }),
        ["mourn"] = (new[] { "Our condolences (F)", "So sorry :( Welcome to the job." }, new[] { "Noted. Who signs the treaties now?", "Condolences. Formally." }),
        ["mourn.knife"] = (new[] { ":-O That's awful. Stay safe." }, new[] { "Messy. Keep your house in order." }),
        ["small.friend"] = (new[] { ":) Hi!", "All well here!", "(L)", "Same old, same old ;)" }, new[] { "Hm.", "Yes." }),
        ["small.cool"] = (new[] { "Hello :)", "Hi..." }, new[] { "We see you too." }),
        ["small.hostile"] = (new[] { ":S", "Okay then." }, new[] { "Likewise :@" }),
    };

    // Group chats: what the first sister says, and the second's answer at the same index.
    static readonly Dictionary<string, (string[] a, string[] b)> GossipSay = new()
    {
        ["pact"] = (new[] { "Trade deal? Same terms as last spring :)", "Our salt for your timber?" }, new[] { "Deal :D", "Done (*)" }),
        ["quarrel"] = (new[] { "That river is ours and you know it :@", "Your goats are on our side of the fence AGAIN :@" }, new[] { "Come and take it 8-|", "(bah)" }),
        ["war"] = (new[] { ":@ That's it. Our soldiers march at dawn.", "You asked for this, {other}." }, new[] { "We'll be waiting.", "Bring it :@" }),
        ["warend"] = (new[] { "(H) Told you.", "Say it. Say we won." }, new[] { ":'( Enough.", "...you won :(" }),
        ["absorb"] = (new[] { "Welcome to the family, {other} :)" }, new[] { "Fine :(" }),
    };

    // Notables, by role and event.
    static readonly Dictionary<string, string[]> NpcSay = new()
    {
        ["captain.warwe"] = new[] { ":@ We march on {a} at dawn.", "Boots on. We're off to {a}." },
        ["captain.warthey"] = new[] { ":-O {a} has declared war! Mustering now.", "War! {a} is coming. Everyone to the gate!" },
        ["captain.loss"] = new[] { ":'( We lost one today.", "Bad news. A soldier won't be coming home :(" },
        ["captain.hold"] = new[] { "The front holds 8-)", "Skirmish on the road. No losses (H)", "Muddy. Cold. Holding :|" },
        ["captain.raid"] = new[] { ":@ Raiders slipped past us. Check the town!" },
        ["captain.won"] = new[] { "(trophy) Victory! We're coming home.", "We won! Put the kettle on :D" },
        ["captain.lost"] = new[] { ":'( We're beaten. Coming home.", "It's over. I'm sorry :(" },
        ["captain.gone"] = new[] { "{a} is gone. Nobody left to fight. Coming home :|" },
        ["physician.start"] = new[] { "(puke) {a}" },
        ["physician.leader"] = new[] { ":'( I couldn't save {a}." },
        ["physician.over"] = new[] { "(*) {a}" },
        ["physician.wound"] = new[] { ":-O I'm on my way. Keep pressing on the wound!", "Stitched and bandaged. Now we watch for fever :|" },
        ["physician.heart"] = new[] { ":S That was your heart. No more stairs, please.", "Rest. I mean it this time :@" },
        ["physician.stroke"] = new[] { ":( One side is weak now. It may come back a little." },
        ["physician.growth"] = new[] { ":( I've found a growth in your {a}. I can't reach it.", "There's a swelling in your {a}. I'm sorry :'(" },
        ["physician.surgery"] = new[] { "(*) The growth is out of your {a}. Keep the wound clean!" },
        ["physician.fall"] = new[] { ":S Mind the steps! That's {a} to mend.", "A fall, and {a}. Rest it :|" },
        ["warden.fire"] = new[] { ":-O Fire at the {a}! Buckets!", "Smoke over the {a}! On my way!" },
        ["warden.great"] = new[] { ":@ It's spreading! Everyone to the buckets!" },
        ["warden.out"] = new[] { "Fire's out at the {a} (Y)", "Got it. The {a} is safe (H)" },
        ["warden.down"] = new[] { ":'( We lost the {a}.", "The {a} is gone. I'm sorry :(" },
        ["warden.death"] = new[] { ":'( {a} didn't make it out." },
        ["steward.harvest"] = new[] { "(hen) The harvest is in: {a} sacks in the barn.", "Harvest home! {a} sacks. Now to get them into the granary :)" },
        ["steward.bumper"] = new[] { "(clover) A bumper year! {a} sacks and counting.", ":D {a} sacks! We'll need a bigger granary." },
        ["steward.fails"] = new[] { ":'( The harvest has failed. {a} sacks, no more. Hold on to what we have.", "(!) Only {a} sacks this year. It will be a hungry winter." },
        ["steward.rotting"] = new[] { "(!) {a} sacks rotting in the fields. We needed more hands :(" },
        ["steward.seed"] = new[] { "(!) We had to eat the seed corn. The fields are half sown :S" },
        ["steward.low"] = new[] { "(!) Our stores will last about {a} days. We should stop selling grain.", ":S {a} days of food left, if nobody hoards." },
        ["steward.queue"] = new[] { ":S Queues at every counter. People are buying for the week.", "(!) The shelves are empty by noon. Everyone is hoarding." },
        ["steward.ration"] = new[] { "(cop) Rationing is in force. One meal a head.", ":| Ration cards are out. Grumbling, but no queues." },
        ["steward.hungry"] = new[] { ":( {a} of our people have nothing to eat today.", "(meal) Empty bowls in {a} houses." },
        ["steward.death"] = new[] { ":'( {a} starved last night.", "(!) {a} died of hunger. We have to do something." },
        ["steward.sold"] = new[] { "(case) Sold {a} sacks of surplus grain. The treasury thanks us.", "{a} sacks sold to the region (Y)" },
        ["steward.caravan"] = new[] { "(horse) Wagons of grain from {a}. Bless them.", "(food) {a} sent grain. We eat tonight :)" },
        ["ringleader.grumble"] = new[] { "They can't keep this up forever :-#", "Meeting tonight. Usual place :-#", "Watch out for the (cop) on the corner", "Psst. Burn this message 8-)", "One day {us} will be free :@" },
        ["ringleader.arrest"] = new[] { ":( They took another one. (jail)", "Another neighbour gone. Remember their name :'(" },
        ["ringleader.won"] = new[] { "(H) WE DID IT. The {a} has fallen!", ":D Free! Free at last!" },
    };

    // ---------- test ----------
    // --chat: a spread of conversations for a screenshot, with every reply already typed. Returns a sister key to open.
    public string TestChat()
    {
        if (Sisters.Count == 0) return "bot";
        var s = Sisters[0];
        foreach (var ev in new[] { "hello", "gifts", "small.friend", "wedding", "fairlost", "tribute.refused", "warthey", "aid" }) Tell(s, ev);
        if (Sisters.Count > 1) { Gossip(Sisters[0], Sisters[1], "quarrel"); Tell(Sisters[1], "plague"); }
        Npc("warden", "fire", false, "bakery");
        Npc("physician", "start", false, "Marsh fever in the low houses. The church takes in the sick.");
        Npc("captain", "warthey", true, s.Name);
        foreach (var c in Convos) foreach (var l in c.Lines) l.Due = 0;
        return "sis:" + s.Name;
    }

    // ---------- saving ----------
    public void RestoreConvo(Convo c)
    {
        foreach (var l in c.Lines) l.Due = 0;
        c.Seen = Clamp(c.Seen, 0, c.Lines.Count);
        Convos.Add(c);
    }
}
