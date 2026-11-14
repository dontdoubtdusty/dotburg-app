using System.Text.RegularExpressions;

namespace Dotburg;

// The chronicler (0.69): the chronicle is written by a dot-man of the town, not by the app. Their traits decide how
// it reads: a remark in their voice after a line, what they choose to write down beyond the major events, and how
// they write about the leader (a friend flatters, an enemy sneers, and a friend of the leader quietly leaves out
// the arrests). Each line is signed "~Name (Trait)" so the Messenger can show it in their font and colour, and the
// chronicle changes hands (and looks) when they die, leave or are raised to lead. All choices use chatRng, so the
// chronicler never changes what happens in the town.
partial class Sim
{
    public int ChroniclerId;               // the dot-man holding the pen, 0 for nobody yet; saved
    public string ChroniclerName = "";     // their name, for the hand-over line after they are gone; saved
    long interestSaid;                      // the last tick a line was written only because it interested them

    public Walker? Chronicler => ChroniclerId > 0 ? ById(ChroniclerId) : null;

    // Their voice: a doomsayer always cries doom, anyone else speaks by their spirit trait (every citizen has one).
    public string VoiceOf(Walker w)
    {
        var t = CitizenTraits(w);
        return t.Contains("doomsayer") ? "doomsayer" : t.FirstOrDefault(k => Traits.Get(k).Cat == 2) ?? "calm";
    }

    // The contact line for the Messenger: who writes the chronicle and what they are like.
    public string ChroniclerLine()
    {
        var w = Chronicler;
        if (w == null) return "Nobody holds the pen yet";
        return $"{CitizenName(w)}, {YearsOfDot(w)}: {string.Join(", ", CitizenTraits(w).Select(k => Traits.Get(k).Name.ToLower()))}";
    }

    // Make sure someone holds the pen; a new chronicler opens with a line of their own (and says why the pen passed).
    void EnsureChronicler()
    {
        var cur = Chronicler;
        if (cur != null && !cur.Lead && cur.Goal is not (Goal.Leave or Goal.Taken)) return;
        var pool = Walkers.Where(w => !w.Gone && w.Kind == "" && w.Kid == 0 && !w.Lead && w.Goal is not (Goal.Leave or Goal.Taken)).ToList();
        if (pool.Count == 0) return;
        // Readers and writers: the curious, the honest, the pious and the cynical take to it; the old more than the young.
        double Weight(Walker w)
        {
            var t = CitizenTraits(w);
            double k = 1 + Math.Clamp((YearsOfDot(w) - 20) / 30.0, 0, 1.5);
            foreach (var x in t) k *= x switch { "curious" => 2.5, "honest" => 1.6, "pious" => 1.5, "cynical" => 1.5, "gloomy" => 1.3, "reckless" => 0.6, _ => 1 };
            return k;
        }
        double r = chatRng.NextDouble() * pool.Sum(Weight);
        var pick = pool[^1];
        foreach (var w in pool) { r -= Weight(w); if (r < 0) { pick = w; break; } }

        string before = ChroniclerName;
        string why = before == "" ? "" : cur != null && cur.Lead ? $"{before} lays down the pen to lead {Name}. " : $"{before} writes no more. ";
        ChroniclerId = pick.Id;
        ChroniclerName = CitizenName(pick);
        string first = CitizenFirst(pick);
        string open = CPick(Openings[VoiceOf(pick)]).Replace("{me}", ChroniclerName).Replace("{us}", Name).Replace("{first}", first);
        Write(why + open);
    }

    // Write a line into the chronicle file under the chronicler's name.
    void Write(string text)
    {
        var d = DateTime.Now;
        var w = Chronicler;
        string sign = w == null ? "" : $" | ~{ChroniclerName} ({Traits.Get(VoiceOf(w)).Name})";
        ChronicleQueue.Add($"- {d:yyyy-MM-dd HH:mm} | {Name}, day {DayNumber}, {SeasonNow.Name} | {text}{sign}");
    }

    // A line about to be chronicled, as the chronicler would put it: maybe left out, maybe with a remark after it.
    // Returns null when they leave it out.
    string? Pen(string text)
    {
        var w = Chronicler;
        if (w == null) return text;
        var lw = LeaderWalker;
        bool aboutLeader = Leader != null && lw != null && lw != w && text.Contains(Leader.Name);
        double love = 0, hate = 0;
        if (lw != null && lw != w)
        {
            love = BondOf(w, lw.Id, "friend")?.Str ?? 0;
            hate = BondOf(w, lw.Id, "grudge")?.Str ?? 0;
        }
        string kind = KindOf(text);
        // A friend of the leader would rather not write down the leader's arrests and crackdowns.
        if (love >= 0.3 && Regex.IsMatch(text, @"taken away|arrest|crackdown|rounded up", RegexOptions.IgnoreCase) && chatRng.NextDouble() < 0.6) return null;

        string? remark = null;
        string lead = Leader?.Name ?? "";
        if (aboutLeader && hate >= 0.3 && chatRng.NextDouble() < 0.75)
            remark = CPick(kind == "death" ? new[] { "Few will mourn.", "I shall not pretend to grieve." } : Snipes).Replace("{lead}", lead);
        else if (aboutLeader && love >= 0.3 && chatRng.NextDouble() < 0.75)
            remark = CPick(kind == "death" ? new[] { "I have lost a dear friend.", "The town has lost its best, and so have I." } : Praise).Replace("{lead}", lead);
        else
        {
            // Someone they love or hate is named in the line.
            foreach (var b in w.Bonds)
            {
                var o = ById(b.Who);
                if (o == null || o.Lead || !text.Contains(CitizenName(o))) continue;
                string on = CitizenFirst(o);
                remark = b.Kind == "friend"
                    ? (kind == "death" ? $"I will miss {on}." : $"Good old {on}.")
                    : (kind == "death" ? (CitizenTraits(w).Contains("spiteful") ? "Good riddance." : "I shed no tears.") : $"Trust {on} to be in the middle of it.");
                break;
            }
            if (remark == null && chatRng.NextDouble() < 0.55 && Remarks.TryGetValue(VoiceOf(w), out var byKind) && byKind.TryGetValue(kind, out var pool))
                remark = CPick(pool).Replace("{lead}", lead == "" ? "our leader" : lead);
        }
        return remark == null ? text : $"{text} {remark.Replace("{us}", Name)}";
    }

    // What sort of news a line is, for the remark: death, power, disaster, triumph or other.
    static string KindOf(string t)
    {
        if (Regex.IsMatch(t, @"\b(dies|died|buried|perishes|starved|lives? (is|are) lost|life (is|was) lost)\b", RegexOptions.IgnoreCase)) return "death";
        if (Regex.IsMatch(t, @"\b(overthr\w*|revolution|coup|assassin\w*|election|elected|steps down|takes power|crowned|throne|chooses|speaker|army takes|deposes|council|party state)\b", RegexOptions.IgnoreCase)) return "power";
        if (Regex.IsMatch(t, @"\b(fire|burns|plague|famine|fails|defeat|outbreak|raid\w*|saboteur|collapses|war|taken away)\b", RegexOptions.IgnoreCase)) return "disaster";
        if (Regex.IsMatch(t, @"\b(victory|bumper|is now an?|first|trade agreement|harvest is in|founded|chartered|sends aid|wins|festival)\b", RegexOptions.IgnoreCase)) return "triumph";
        return "other";
    }

    // Beyond the major events, each voice writes down what catches their eye (at most one such line every half day).
    static readonly Dictionary<string, Regex> Interests = new()
    {
        ["gloomy"] = new(@"buried in|dies of", RegexOptions.IgnoreCase),
        ["doomsayer"] = new(@"buried in|crying that|comet|lightning", RegexOptions.IgnoreCase),
        ["pious"] = new(@"buried in|wedding|church|cathedral|baby|welcomes a dotlet", RegexOptions.IgnoreCase),
        ["cheerful"] = new(@"festival|wedding|baby|welcomes a dotlet|dancing|snowman", RegexOptions.IgnoreCase),
        ["curious"] = new(@"caravan|envoy|spy|refugee|fair|letter|stranger", RegexOptions.IgnoreCase),
        ["hotheaded"] = new(@"fists fly|never forgive|trade words", RegexOptions.IgnoreCase),
        ["cynical"] = new(@"scandal|taken away|arrest|bribe|promises", RegexOptions.IgnoreCase),
        ["brave"] = new(@"soldier|front|march|puts out", RegexOptions.IgnoreCase),
        ["craven"] = new(@"raid|fire at|smoke", RegexOptions.IgnoreCase),
        ["calm"] = new(@"harvest|sown|reaped", RegexOptions.IgnoreCase),
    };

    // A crier line that is not major may still interest the chronicler.
    void MaybeChronicle(string text)
    {
        var w = Chronicler;
        if (w == null || TickNo - interestSaid < DayMs / TickMs / 2) return;
        if (!Interests.TryGetValue(VoiceOf(w), out var re) || !re.IsMatch(text)) return;
        interestSaid = TickNo;
        Note(text);
    }

    static readonly string[] Praise =
    {
        "Wisely done, as ever, {lead}.", "{lead} never lets us down.", "Long may {lead} lead us.", "{lead} saw it coming, of course.",
    };
    static readonly string[] Snipes =
    {
        "As if {lead} cared.", "{lead} will take the credit, no doubt.", "Ask {lead} where the money went.", "{lead} was nowhere to be seen.",
    };

    // A new chronicler's first line, by voice.
    static readonly Dictionary<string, string[]> Openings = new()
    {
        ["gloomy"] = new[] { "I, {me}, take up the pen. Someone must write down how it all goes wrong.", "{me} keeps the chronicle now. There will be much to mourn." },
        ["cheerful"] = new[] { "I, {me}, take up the pen, and what a town to write about!", "{me} keeps the chronicle now. Only good things, I hope :)" },
        ["pious"] = new[] { "I, {me}, take up the pen, under heaven's eye.", "{me} keeps the chronicle now, God willing, for many years." },
        ["cynical"] = new[] { "I, {me}, take up the pen. Somebody has to write down what really happened.", "{me} keeps the chronicle now. Don't expect flattery." },
        ["curious"] = new[] { "I, {me}, take up the pen. There is so much to find out.", "{me} keeps the chronicle now, and means to ask a lot of questions." },
        ["hotheaded"] = new[] { "I, {me}, take up the pen, and I will not mince words.", "{me} keeps the chronicle now. Fools beware." },
        ["calm"] = new[] { "I, {me}, take up the pen. Plainly and in order.", "{me} keeps the chronicle now. Facts, and little else." },
        ["brave"] = new[] { "I, {me}, take up the pen. I will write it as it was, whatever comes.", "{me} keeps the chronicle now, without fear or favour." },
        ["craven"] = new[] { "I, {me}, take up the pen, if nobody minds.", "{me} keeps the chronicle now. Quietly." },
        ["doomsayer"] = new[] { "I, {me}, take up the pen, so the end of {us} is written down.", "{me} keeps the chronicle now. The last chapter, surely." },
    };

    // Remarks after a line, by voice and by what kind of news it is.
    static readonly Dictionary<string, Dictionary<string, string[]>> Remarks = new()
    {
        ["gloomy"] = new()
        {
            ["death"] = new[] { "And so it goes.", "Another stone in the yard.", "We are all on our way there." },
            ["power"] = new[] { "Nothing will change.", "It will end badly, as these things do." },
            ["disaster"] = new[] { "I said it would come to this.", "Worse is coming." },
            ["triumph"] = new[] { "It will not last.", "Enjoy it while you can." },
            ["other"] = new[] { "Hm.", "So it is written." },
        },
        ["cheerful"] = new()
        {
            ["death"] = new[] { "A good life, well lived.", "We'll raise a cup to them tonight." },
            ["power"] = new[] { "Fresh faces, fresh hopes!", "Here's to new beginnings." },
            ["disaster"] = new[] { "We'll mend. We always do.", "Chins up, everyone." },
            ["triumph"] = new[] { "What a day!", "Bells and bunting!", "Best news in ages." },
            ["other"] = new[] { "Life goes on, and it's lovely.", "Onwards!" },
        },
        ["pious"] = new()
        {
            ["death"] = new[] { "May they rest in the light.", "Heaven keep them." },
            ["power"] = new[] { "May heaven guide {lead}.", "We pray for wisdom in high places." },
            ["disaster"] = new[] { "A trial sent to test us.", "We have strayed, and we are punished." },
            ["triumph"] = new[] { "Heaven be thanked.", "Blessings upon us all." },
            ["other"] = new[] { "As heaven wills.", "Amen." },
        },
        ["cynical"] = new()
        {
            ["death"] = new[] { "The gravedigger, at least, is doing well.", "No doubt someone profits." },
            ["power"] = new[] { "Same cart, new driver.", "The new one will be no better.", "Watch your purses." },
            ["disaster"] = new[] { "Nobody will be blamed, of course.", "Predictable." },
            ["triumph"] = new[] { "Someone will find a way to ruin it.", "Who paid for the bunting?" },
            ["other"] = new[] { "Believe it if you like.", "So they tell us." },
        },
        ["curious"] = new()
        {
            ["death"] = new[] { "I wonder what they saw at the end.", "I must ask the physician what happened." },
            ["power"] = new[] { "I wonder what {lead} will do first.", "How did it come to this? I mean to find out." },
            ["disaster"] = new[] { "How did it start? Nobody can say.", "I went to look. Strange business." },
            ["triumph"] = new[] { "How did we manage that?", "I must find out who to thank." },
            ["other"] = new[] { "Interesting.", "I wonder." },
        },
        ["hotheaded"] = new()
        {
            ["death"] = new[] { "Someone should answer for this!", "It makes my blood boil." },
            ["power"] = new[] { "About time!", "Fools, the lot of them." },
            ["disaster"] = new[] { "Somebody's head should roll!", "Unforgivable!" },
            ["triumph"] = new[] { "Ha! Take that!", "That showed them!" },
            ["other"] = new[] { "Bah.", "Enough said!" },
        },
        ["calm"] = new()
        {
            ["death"] = new[] { "We go on.", "Noted, with sorrow." },
            ["power"] = new[] { "We shall see.", "The town goes on." },
            ["disaster"] = new[] { "We will manage.", "Steady, now." },
            ["triumph"] = new[] { "Well done, all.", "A good day." },
            ["other"] = new[] { "Noted.", "So it was." },
        },
        ["brave"] = new()
        {
            ["death"] = new[] { "They will be remembered.", "Honour to them." },
            ["power"] = new[] { "Let {lead} prove themself.", "We will stand by the town, whoever leads." },
            ["disaster"] = new[] { "We stood firm.", "We do not run." },
            ["triumph"] = new[] { "Hard won and well deserved.", "Proud to see it." },
            ["other"] = new[] { "Onward.", "So be it." },
        },
        ["craven"] = new()
        {
            ["death"] = new[] { "I kept my door shut.", "It could have been me." },
            ["power"] = new[] { "I will keep my head down.", "Best not to say anything." },
            ["disaster"] = new[] { "I hid in the cellar until it was over.", "I did not go out." },
            ["triumph"] = new[] { "Thank goodness.", "Phew." },
            ["other"] = new[] { "Best not to ask.", "I saw nothing." },
        },
        ["doomsayer"] = new()
        {
            ["death"] = new[] { "The first of many.", "The end draws nearer." },
            ["power"] = new[] { "The last ruler of {us}, mark my words.", "It changes nothing. The end is coming." },
            ["disaster"] = new[] { "I warned them. Nobody listened.", "The signs were all there." },
            ["triumph"] = new[] { "The calm before the storm.", "A last feast before the fall." },
            ["other"] = new[] { "The sky grows darker.", "It is written." },
        },
    };
}
