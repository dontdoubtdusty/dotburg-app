namespace Dotburg;

// The leader's own contacts (0.69). The Messenger is the leader's account: besides the public contacts (the
// chronicle, the notables, the sisters and their group chats) it lists the people the leader is close to (strong
// friendships, Friends) and the people who hate them or whom they hate (deep grudges, Blocked), plus the leader
// before them while they live in town. These chats are keyed by the leader's id ("pal:<leader>:<other>"), so a new
// leader signs in to a list of their own and the old leader's private chats go. Friends talk in their own font,
// by their traits, about what is happening to them and the town. Talk uses chatRng, so it never changes the town;
// the one exception is on purpose: how the old leader left (a coup, a lost vote, a hand-over) becomes a real bond.
partial class Sim
{
    const double PalFriendAt = 0.25, PalFriendKeep = 0.12;   // a friendship this strong makes a contact; it stays until it fades below Keep
    const double PalFoeAt = 0.35, PalFoeKeep = 0.2;          // the same for a grudge either way (a Blocked contact)
    const int PalFriendsMax = 5, PalFoesMax = 4;
    public int PalLeader;          // the leader the personal contacts belong to (0 before the first check); saved
    public int PalFriendsMade, PalFoesMade, PalLeaders;   // soak counters: contacts added, leaders signed in
    public double PalBestFriend, PalBestGrudge;           // soak: strongest friendship and grudge touching the leader, sampled
    public int PrevLeader;         // the dot-man who led before them, 0 if none in town; saved
    public string FallHow = "";    // how the last leader left office: coup, election, stepdown, turn; set where it happens

    // The other person in a personal chat, from its key.
    public static int PalId(Convo c) => c.Key.Split(':') is { Length: 3 } p && int.TryParse(p[2], out var id) ? id : 0;

    // Once every few seconds: notice a new leader, keep the chronicler's pen in a hand, and bring the list up to date.
    void StepPals()
    {
        if (Quiet) return;
        EnsureChronicler();
        var lw = LeaderWalker;
        int lid = lw?.Id ?? 0;
        if (lid != PalLeader) NewLeader(PalLeader, lid);
        else FallHow = "";   // the same leader kept the chair (a speaker's turn came back to them): forget how
        if (lw == null) return;

        // Who qualifies now: friendship or grudge in either direction, with a lower bar to stay than to join.
        var want = new List<(Walker w, string role, double str)>();
        foreach (var w in Walkers)
        {
            if (w.Gone || w == lw || w.Kind != "" || w.Kid != 0) continue;
            double friend = Math.Max(BondOf(lw, w.Id, "friend")?.Str ?? 0, BondOf(w, lw.Id, "friend")?.Str ?? 0);
            double grudge = Math.Max(BondOf(lw, w.Id, "grudge")?.Str ?? 0, BondOf(w, lw.Id, "grudge")?.Str ?? 0);
            var have = Convos.FirstOrDefault(c => c.Key == PalKey(lid, w.Id) && !c.Gone);
            bool listed = have != null;
            PalBestFriend = Math.Max(PalBestFriend, friend); PalBestGrudge = Math.Max(PalBestGrudge, grudge);
            if (grudge >= (listed ? PalFoeKeep : PalFoeAt)) want.Add((w, "foe", grudge));
            else if (friend >= (listed && have!.Role == "friend" ? PalFriendKeep : PalFriendAt)) want.Add((w, "friend", friend));
            else if (w.Id == PrevLeader) want.Add((w, grudge >= 0.25 ? "foe" : "friend", 0.01));
        }
        var keep = want.Where(x => x.role == "friend").OrderByDescending(x => x.str).Take(PalFriendsMax)
            .Concat(want.Where(x => x.role == "foe").OrderByDescending(x => x.str).Take(PalFoesMax)).ToList();

        // Contacts who left town, died or were taken go Offline; those who are no longer close are deleted.
        foreach (var c in Convos.Where(c => c.Kind == "pal" && !c.Gone).ToList())
        {
            var o = ById(PalId(c));
            if (o == null || o.Goal is Goal.Leave or Goal.Taken) { c.Gone = true; continue; }
            if (!keep.Any(x => x.w == o)) Convos.Remove(c);
        }
        TrimOffline();

        // New contacts say hello; a friend turned enemy says so once, then is blocked.
        foreach (var (w, role, _) in keep)
        {
            var c = Convos.FirstOrDefault(x => x.Key == PalKey(lid, w.Id));
            if (c != null && c.Role == role) continue;
            bool fresh = c == null;
            c ??= ConvoFor(PalKey(lid, w.Id), "pal", CitizenName(w), role);
            c.Role = role;
            c.Gone = false;
            if (role == "foe") PalFoesMade++; else PalFriendsMade++;
            PalHello(c, w, lw, fresh);
        }
    }

    static string PalKey(int leader, int other) => $"pal:{leader}:{other}";

    // A new leader signs in: the old leader's private chats go, the leader before them is remembered as a contact,
    // and how they left becomes a bond between the two (a coup leaves hate, a hand-over friendship).
    void NewLeader(int oldId, int newId)
    {
        bool first = PalLeader == 0 && oldId == 0 && Convos.All(c => c.Kind != "pal");
        PalLeader = newId;
        PalLeaders++;
        Convos.RemoveAll(c => c.Kind == "pal");
        var old = oldId > 0 ? ById(oldId) : null;
        var now = newId > 0 ? ById(newId) : null;
        PrevLeader = old != null && old.Goal is not (Goal.Leave or Goal.Taken) ? old.Id : 0;
        string how = FallHow;
        FallHow = "";
        if (first || old == null || now == null || PrevLeader == 0) return;
        string t = CitizenName(now);
        switch (how)
        {
            case "coup": AddBond(old, now, "grudge", 0.6 * GrudgeGain(old), $"{t} took the town from them by force"); break;
            case "election":
                if (Chance() < 0.5) AddBond(old, now, "grudge", 0.35 * GrudgeGain(old), $"{t} beat them at the election");
                else { AddBond(old, now, "friend", 0.4, $"{t} shook their hand after the election"); AddBond(now, old, "friend", 0.4, $"{CitizenName(old)} shook their hand after the election"); }
                break;
            case "stepdown":
            case "turn":
                AddBond(old, now, "friend", 0.4, $"{t} took the chain of office from their hands");
                AddBond(now, old, "friend", 0.4, $"{CitizenName(old)} handed them the chain of office");
                break;
        }
    }

    // A new contact's first line, or a friend's last before the block.
    void PalHello(Convo c, Walker w, Walker lw, bool fresh)
    {
        string kind = c.Role == "foe" ? "grudge" : "friend";
        var mine = BondOf(w, lw.Id, kind);              // what they feel about the leader
        var theirs = mine == null ? BondOf(lw, w.Id, kind) : null;   // or what the leader feels about them
        string why = mine != null ? WhyAs(mine.Why, w, lw, true) : theirs != null ? WhyAs(theirs.Why, lw, w, false) : "";
        string line;
        if (w.Id == PrevLeader && Age - (Leader?.Since ?? 0) < DayMs)
            line = c.Role == "foe" ? CPick(new[] { "Enjoy the chair while it lasts :@", "You'll get yours, {first}. (6)", "Don't get comfortable." }) : CPick(new[] { "The chain is yours now. Look after them (L)", "Good luck, {first}. You'll need it ;)", "Hah, your turn now. Call me if you need me :)" });
        else if (c.Role == "foe")
            line = mine != null ? $"{Cap(why)}. I haven't forgotten :@" : theirs != null ? $"Still angry that {why}? Grow up :P" : CPick(new[] { "Don't write to me again :@", "We're done, {first}." });
        else if (Age - (Leader?.Since ?? 0) < DayMs / 2)
            line = CPick(new[] { "You're {title} now?! (crown) Don't forget us little people :D", "Congratulations, {first}! (*) Drinks on you.", "Look at you! I always knew :)" });
        else
            line = why != "" ? $"Remember when {why}? :)" : CPick(new[] { "Hi {first} :)", "Thought I'd say hello (F)" });
        PalSay(c, w, line);
        if (c.Role == "friend" && chatRng.NextDouble() < 0.7) PalReply(c, "hello");
    }

    // How a bond began, told by the contact to the leader. A bond's origin is written about its holder ("Odo shoved
    // them at the inn"); speakerHolds says whether the contact holds it (then "them" is "me" and the leader "you")
    // or the leader does (then "them" is "you" and the contact "I").
    string WhyAs(string why, Walker holder, Walker other, bool speakerHolds)
    {
        string s = " " + why + " ";
        if (speakerHolds) s = s.Replace(" " + CitizenName(other) + " ", " you ").Replace(" them ", " me ").Replace(" their ", " my ");
        else s = s.Replace(" " + CitizenName(other) + " ", " I ").Replace(" them ", " you ").Replace(" their ", " your ");
        s = s.Trim();
        return s.StartsWith("they ") ? "we " + s[5..] : s;
    }

    // A friend says something, in their own font, as a toast too.
    void PalSay(Convo c, Walker w, string text)
    {
        text = text.Replace("{first}", Leader?.Name ?? "").Replace("{title}", MeName).Replace("{us}", Name);
        text = Post(c, c.Title, text, false, TickNo);
        Pop($"{c.Title} says:", text, c.Key, false);
    }

    // The leader answers a friend a few seconds later, warmly or curtly by their own voice.
    void PalReply(Convo c, string topic)
    {
        var lw = LeaderWalker;
        if (lw == null) return;
        string v = VoiceOf(lw);
        string[] pool = topic switch
        {
            "trouble" => v is "cheerful" or "calm" or "brave" ? new[] { "We'll get through it (Y)", "Hang in there :)" } : v is "gloomy" or "doomsayer" ? new[] { "I know :(", "It's bad." } : new[] { "Doing what I can :S", "Thanks. Stay safe (F)" },
            "hello" => v is "cheerful" ? new[] { ":D Hi!!", "(L)" } : v is "cynical" or "hotheaded" ? new[] { "What do you want? ;)", "Hm. Hello." } : new[] { "Hi :)", "Good to hear from you (F)" },
            _ => v is "cheerful" ? new[] { "Haha :D", "Same here!", "(L)" } : v is "gloomy" ? new[] { "Mm.", "If you say so :|" } : v is "cynical" ? new[] { "Sure ;)", "Right." } : new[] { ":)", "Ha, yes.", "Speak soon (F)" },
        };
        Post(c, MeName, CPick(pool), true, Typing());
    }

    // Once a check (four times a day): each friend may write, about the trouble of the moment or their own life.
    void PalChatter()
    {
        if (Quiet) return;
        var lw = LeaderWalker;
        if (lw == null) return;
        foreach (var c in Convos.Where(c => c.Kind == "pal" && !c.Gone && c.Role == "friend").ToList())
        {
            var w = ById(PalId(c));
            if (w == null || w.Sleep || chatRng.NextDouble() >= PerDay(0.6)) continue;
            string v = VoiceOf(w);
            string? trouble = PalTrouble(w, lw);
            PalSay(c, w, trouble ?? CPick(PalSmall.TryGetValue(v, out var s) ? s : PalSmall["calm"]));
            if (chatRng.NextDouble() < 0.6) PalReply(c, trouble != null ? "trouble" : "small");
        }
    }

    // What a friend would write about first: their own trouble, the leader's, then the town's.
    string? PalTrouble(Walker w, Walker lw)
    {
        if (w.Sick) return CPick(new[] { "In bed with a fever +o( Bring soup?", "Sick as a dog. Don't visit (puke)" });
        if (w.Food < HungryAt) return CPick(new[] { "Haven't eaten since yesterday :(", "Any bread at your end? (meal)" });
        if (lw.Sick || lw.Hurt) return CPick(new[] { "Heard you're poorly. Get well soon (F)", "Rest, {first}. The town can wait :(" });
        if (WarOn != null) return CPick(new[] { $"Any news from the front? :S", $"Is it true about {WarOn.Foe}? :-O" });
        if (Fires.Count > 0) return CPick(new[] { "Can you see the smoke from yours? :-O", "Fire! Are you all right? :S" });
        if (Ill != null) return CPick(new[] { "Is the sickness bad at your end? +o(", "Washing my hands every hour now :S" });
        if (Famine || Panic > 0.5) return CPick(new[] { "Queues everywhere. Is there going to be enough? :S", "Everyone's hoarding. Should I? :$" });
        if (Festival) return CPick(new[] { "Coming to the square? (jester)", "Save me a dance! :D" });
        return null;
    }

    // Small talk by voice.
    static readonly Dictionary<string, string[]> PalSmall = new()
    {
        ["cheerful"] = new[] { "Lovely day! :D", "Guess what, my hens laid twins (hen)", "Life is good (L)", "Saw a rainbow over the mill (R)" },
        ["gloomy"] = new[] { "Rain again, probably :(", "My back hurts. Everything hurts.", "Is it always this grey? :|" },
        ["pious"] = new[] { "Prayed for you today (A)", "See you at service? (A)", "Blessings on your house." },
        ["cynical"] = new[] { "Heard the latest rumour? Don't believe it ;)", "Taxes again? :|", "Nobody tells us anything 8-|" },
        ["curious"] = new[] { "Did you know the mill wheel has 24 paddles? I counted :D", "Who was that stranger at the inn? :-O", "What's past the hills, do you think?" },
        ["hotheaded"] = new[] { "My neighbour's goat ate my cabbages AGAIN :@", "Don't get me started on the baker :@", "I nearly hit someone today. Nearly." },
        ["calm"] = new[] { "Quiet day. Nice.", "Sitting by the pond (F)", "All well here :)" },
        ["brave"] = new[] { "Need a hand with anything? (Y)", "Going for a swim in the pond. Cold! :D" },
        ["craven"] = new[] { "Did you hear that noise last night? :S", "I'm staying in today :$" },
        ["doomsayer"] = new[] { "The crows are gathering. A sign (6)", "Mark my words, the end is near.", "I dreamt of fire again :S" },
    };

    // A contact's status: blocked if it's a grudge, away asleep, busy if sick, else online.
    Presence PalPresence(Convo c)
    {
        var w = ById(PalId(c));
        if (w == null || w.Goal is Goal.Leave or Goal.Taken) return Presence.Offline;
        if (c.Role == "foe") return Presence.Blocked;
        if (w.Sick || w.Hurt || w.Fading != "") return Presence.Busy;
        if (w.Sleep) return Presence.Away;
        return Presence.Online;
    }

    // A contact's personal message: their state first, else a line by their voice.
    string PalMessage(Convo c)
    {
        var w = ById(PalId(c));
        if (w == null) return "(no longer in town)";
        if (w.Goal == Goal.Taken) return "(jail)";
        if (c.Role == "foe") return "Leave me alone :@";
        if (w.Fading != "") return "Not long now (F)";
        if (w.Sick) return "Sick in bed +o(";
        if (w.Food < WeakAt) return "So hungry :'(";
        return TraitMottos.TryGetValue(VoiceOf(w), out var m) ? m : "";
    }

    // Who this contact is, for the tooltip: age, traits, the leader before, and how the bond began.
    public string PalInfo(Convo c)
    {
        var w = ById(PalId(c));
        if (w == null) return "";
        var lw = LeaderWalker;
        string traits = string.Join(", ", CitizenTraits(w).Select(k => Traits.Get(k).Name.ToLower()));
        string was = w.Id == PrevLeader && BodyOf(w) is { } body && body.Was != "" ? $"{body.Was}; " : "";
        var b = lw == null ? null : c.Role == "foe" ? BondOf(w, lw.Id, "grudge") ?? BondOf(lw, w.Id, "grudge") : BondOf(w, lw.Id, "friend") ?? BondOf(lw, w.Id, "friend");
        string bond = b == null ? "" : $"\n{(c.Role == "foe" ? "Bad blood" : "Friends")} since day {b.Day}: {b.Why}.";
        return $"{was}{YearsOfDot(w)}, {traits}{bond}";
    }

    // --pals: two friends and an enemy for the leader, a chat with each, and chronicle lines in two hands (a friend
    // of the leader, then an enemy). Returns the first friend's chat key to open.
    public string TestPals()
    {
        var lw = LeaderWalker;
        var o = Walkers.Where(w => !w.Gone && w.Kind == "" && w.Kid == 0 && w != lw).Take(3).ToList();
        if (lw == null || o.Count < 3) return "bot";
        string ln = CitizenName(lw);
        AddBond(o[0], lw, "friend", 0.5, "they shared a table at the inn");
        AddBond(lw, o[0], "friend", 0.5, "they shared a table at the inn");
        AddBond(o[1], lw, "friend", 0.4, $"{ln} shared their bread with them at the bakery");
        AddBond(o[2], lw, "grudge", 0.7, $"{ln} spat at them at the market");
        StepPals();
        foreach (var c in Convos.Where(c => c.Kind == "pal" && c.Role == "friend").ToList())
        {
            var w = ById(PalId(c))!;
            PalSay(c, w, CPick(PalSmall[VoiceOf(w)]));
            PalReply(c, "small");
        }
        foreach (var (w, lines) in new[] { (o[0], new[] { "The harvest is in: 1200 sacks.", $"{LeaderTitle} opens the new market." }),
                                            (o[2], new[] { "Fire at the bakery!", $"{LeaderTitle} raises the bread tax." }) })
        {
            ChroniclerId = w.Id;
            ChroniclerName = CitizenName(w);
            Write(CPick(Openings[VoiceOf(w)]).Replace("{me}", ChroniclerName).Replace("{us}", Name));
            foreach (var l in lines) Note(l);
        }
        foreach (var c in Convos) foreach (var l in c.Lines) l.Due = 0;
        return Convos.FirstOrDefault(c => c.Kind == "pal")?.Key ?? "bot";
    }

    // Personal messages by voice, for friends and for a leader whose motto is their own.
    static readonly Dictionary<string, string> TraitMottos = new()
    {
        ["cheerful"] = "Every day is a good day :D", ["gloomy"] = "It'll rain. It always does :(", ["pious"] = "Faith and bread (A)",
        ["cynical"] = "Believe half of what you see 8-|", ["curious"] = "What's over the next hill?", ["hotheaded"] = "DON'T. :@",
        ["calm"] = "Breathe (F)", ["brave"] = "Fear nothing (Y)", ["craven"] = "Please don't knock :$", ["doomsayer"] = "The end is nigh (6)",
    };
}
