using System.Text;
using System.Text.RegularExpressions;

namespace Dotburg;

// How someone types in the Messenger, 2006 style: their "hand". Fixed per person (a hash of who they are), leaning
// on their traits but often not matching them. Most hands are mild (one quirk), some clearly stylised, a few extreme.
// The chronicle file is never styled (it reads chronicle.txt, not Post).
class Hand
{
    public bool Lower, Upper, Alt;          // all lowercase / SHOUTING / aLtErNaTiNg
    public bool NoStops, Dots, Bang;        // drop full stops / trail off... / !!! and ?!
    public bool TextSpeak, NoApos, Leet;    // u r gr8 / dont / l33t
    public double Typos;                    // chance per long word of a slip
    public string Open = "", Close = "";   // "omg " ... " xx"
    public string Frame = "";               // "~*~" or "xXx" round the line
    public bool Bursts;                     // splits a line into short messages
    public bool Plain => !(Lower || Upper || Alt || NoStops || Dots || Bang || TextSpeak || NoApos || Leet || Bursts) && Typos == 0 && Open == "" && Close == "" && Frame == "";
}

partial class Sim
{
    // The emoticon codes the Messenger draws (order = the sheet in resources/emoticons.png). Styling never touches them.
    public static readonly string[] EmoCodes =
    {
        ":)", ":D", ";)", ":-O", ":P", "(H)", ":@", ":S", ":$", ":(",
        ":'(", ":|", "(A)", "8o|", "8-|", "+o(", "<:o)", "|-)", "*-)", ":-#",
        ":-*", "^o)", "8-)", "(L)", "(U)", "(M)", "(@)", "(&)", "(sn)", "(bah)",
        "(S)", "(*)", "(#)", "(R)", "({)", "(})", "(K)", "(F)", "(W)", "(O)",
        // Picked from the owner's MSN sheet (resources/add-emoticons.ps1).
        "(zz)", "(jail)", "(puke)", "(hen)", "(cow)", "(jester)", "(cop)", "(bouquet)", "(clover)", "(horse)",
        "(peace)", "(Y)", "(!)", "(6)", "(E)", "(crown)", "(hammer)", "(trophy)", "(apple)", "(knife)",
        "(meal)", "(medal)", "(bear)", "(gran)", "(case)", "(food)",
    };
    static readonly HashSet<string> EmoSet = new(EmoCodes);

    // Notables have no citizen traits; their job leans their hand instead.
    static readonly Dictionary<string, string[]> RoleLean = new()
    {
        ["captain"] = new[] { "brave", "hotheaded" }, ["physician"] = new[] { "careful", "calm" }, ["warden"] = new[] { "able", "calm" },
        ["ringleader"] = new[] { "cynical", "spiteful" }, ["steward"] = new[] { "thrifty", "honest" },
        ["baker"] = new[] { "cheerful", "generous" }, ["grocer"] = new[] { "curious", "reckless" },
    };

    // Words that make a line grave: quirks fall away when someone writes about death, war or disaster.
    static readonly Regex Grave = new(@"\b(dead|dies|died|death|killed|murder\w*|buried|war|plague|fire|burn\w*|famine|starv\w*|attack\w*|assassin\w*|coup|revolt|lost)\b", RegexOptions.IgnoreCase);

    // 2006 text speak, applied word by word (lower-case keys).
    static readonly Dictionary<string, string> Speak = new()
    {
        ["you"] = "u", ["your"] = "ur", ["you're"] = "ur", ["are"] = "r", ["because"] = "cuz", ["what"] = "wat", ["great"] = "gr8",
        ["later"] = "l8r", ["tonight"] = "2nite", ["please"] = "plz", ["thanks"] = "thx", ["okay"] = "k", ["really"] = "rly",
        ["see"] = "c", ["for"] = "4", ["before"] = "b4", ["tomorrow"] = "2moro", ["today"] = "2day", ["people"] = "ppl", ["know"] = "no",
    };

    // Who wrote a line and with which traits: our leader, a friend or enemy, a notable (by job) or a sister's ruler.
    (int seed, string[] traits) WriterOf(Convo c, bool ours, Sister? s)
    {
        if (ours) return LeaderWalker is { } lw ? (lw.Id * 31 + 7, Leader?.Traits ?? CitizenTraits(lw)) : (Founded * 13 + 5, Array.Empty<string>());
        if (c.Kind == "pal") { int id = PalId(c); return (id * 31 + 7, ById(id) is { } pw ? CitizenTraits(pw) : Array.Empty<string>()); }
        if (c.Kind == "npc") return (Array.IndexOf(NpcRoles, c.Role) * 977 + Founded * 131 + 3, RoleLean.TryGetValue(c.Role, out var r) ? r : Array.Empty<string>());
        if (s != null) return (StableHash(s.Name + "|" + (s.Lord?.Name ?? "")) , s.Lord?.Traits ?? Array.Empty<string>());
        return (0, Array.Empty<string>());
    }

    static int StableHash(string t) { unchecked { int h = 17; foreach (char ch in t) h = h * 31 + ch; return h & 0x7fffffff; } }

    // Rolls a hand from a seed: how strongly stylised (plain, mild, clear, extreme), then quirks weighted by traits.
    public static Hand HandFor(int seed, string[] traits)
    {
        var r = new Random(seed ^ 0x2006);
        var h = new Hand();
        bool Has(params string[] k) => traits.Any(k.Contains);
        double W(params string[] lean) => Has(lean) ? 4 : 1;

        double plain = Has("pious", "honest", "careful", "able", "thrifty", "humble") ? 0.2 : 0.07;
        double roll = r.NextDouble();
        int quirks = roll < plain ? 0 : roll < 0.6 ? 1 : roll < 0.88 ? 2 + r.Next(2) : 2;
        bool extreme = roll >= 0.88;

        if (extreme)
        {
            // One loud trademark: shouting, alternating caps, a sparkle frame or l33t.
            var pick = Weighted(r, ("upper", W("hotheaded", "proud", "doomsayer", "brave")), ("alt", W("spiteful", "lightfinger", "jealous", "cunning")),
                                   ("frame", W("cheerful", "charmer", "proud", "generous")), ("leet", W("cunning", "curious", "reckless") * 0.5));
            if (pick == "upper") h.Upper = true;
            else if (pick == "alt") h.Alt = true;
            else if (pick == "leet") { h.Leet = true; h.Lower = true; }
            else h.Frame = r.Next(2) == 0 ? "~*~" : "xXx";
        }
        var pool = new List<(string, double)>
        {
            ("lower", W("gloomy", "cynical", "calm", "idle")), ("nostops", W("idle", "reckless", "cheerful")), ("dots", W("gloomy", "craven", "doomsayer", "calm")),
            ("bang", W("hotheaded", "cheerful", "brave", "proud", "ambitious")), ("speak", W("idle", "reckless", "cheerful", "charmer")),
            ("noapos", W("idle", "reckless", "cynical")), ("typos", W("reckless", "idle", "hotheaded")),
            ("close", W("cheerful", "charmer", "generous", "loyal", "merciful", "cynical", "cunning")), ("open", W("cheerful", "curious")),
            ("bursts", W("curious", "hotheaded", "cheerful", "reckless")),
        };
        for (int i = 0; i < quirks && pool.Count > 0; i++)
        {
            string q = Weighted(r, pool.ToArray());
            pool.RemoveAll(p => p.Item1 == q);
            switch (q)
            {
                case "lower": if (!h.Upper && !h.Alt) h.Lower = true; break;
                case "nostops": h.NoStops = true; pool.RemoveAll(p => p.Item1 == "dots"); break;
                case "dots": h.Dots = true; pool.RemoveAll(p => p.Item1 == "nostops"); break;
                case "bang": h.Bang = true; break;
                case "speak": h.TextSpeak = true; break;
                case "noapos": h.NoApos = true; break;
                case "typos": h.Typos = 0.03 + r.NextDouble() * 0.05; if (quirks == 1) quirks = 2; break;   // slips alone barely show, so they come with something else
                case "close":
                    h.Close = Has("cynical", "cunning") ? CPickR(r, " lol", " lol", " haha") : Has("charmer") ? " <3" : CPickR(r, " xx", " x", " xXx", " lol", " haha", " <3");
                    break;
                case "open": h.Open = CPickR(r, "omg ", "hey ", "lol ", "ok so "); break;
                case "bursts": h.Bursts = true; break;
            }
        }
        return h;
    }

    static T CPickR<T>(Random r, params T[] a) => a[r.Next(a.Length)];

    static string Weighted(Random r, params (string k, double w)[] opts)
    {
        double t = r.NextDouble() * opts.Sum(o => o.w);
        foreach (var o in opts) { t -= o.w; if (t <= 0) return o.k; }
        return opts[^1].k;
    }

    // Writes a line in a hand. Grave news drops the playful quirks (frames, openers, closers, l33t, alternating caps,
    // shouting) and halves the typos; lowercase, trailing dots and missing stops stay. Returns the pieces to post
    // (more than one for a hand that types in bursts).
    static List<string> WriteIn(Hand h, string text, bool grave, Random rng)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        bool upper = h.Upper && !grave, alt = h.Alt && !grave, leet = h.Leet && !grave;
        bool excited = !grave && text.Contains('!');
        // Where the sentences and clauses end, read before stops are dropped, so bursts still break there.
        var ends = words.Select(w => !EmoSet.Contains(w) && (w.EndsWith('.') || w.EndsWith('!') || w.EndsWith('?') || w.EndsWith(',') || w.EndsWith(':') || w.EndsWith(';'))).ToList();
        for (int i = 0; i < words.Count; i++)
        {
            string w = words[i];
            if (EmoSet.Contains(w)) continue;
            // Split off leading and trailing punctuation so the word itself can change.
            int a = 0, b = w.Length;
            while (a < b && !char.IsLetterOrDigit(w[a]) && w[a] != '\'') a++;
            while (b > a && !char.IsLetterOrDigit(w[b - 1])) b--;
            string pre = w[..a], core = w[a..b], post = w[b..];
            if (h.TextSpeak && Speak.TryGetValue(core.ToLowerInvariant(), out var sp) && rng.NextDouble() < 0.8) core = sp;
            if (h.NoApos) core = core.Replace("'", "");
            if (core.Length >= 4 && rng.NextDouble() < h.Typos * (grave ? 0.5 : 1)) core = Slip(core, rng);
            if (leet) core = core.Replace('e', '3').Replace('o', '0');
            if (post.Contains('.') && !post.Contains("..."))
            {
                if (h.Dots) post = post.Replace(".", "...");
                else if (h.NoStops) post = post.Replace(".", "");
            }
            if (h.Bang && !grave) post = post.Replace("!", new string('!', 2 + rng.Next(3))).Replace("?", rng.Next(2) == 0 ? "??" : "?!");
            words[i] = pre + core + post;
        }
        // A trailing-dots hand trails off even where there was no full stop.
        if (h.Dots && words.Count > 0 && !EmoSet.Contains(words[^1]) && char.IsLetter(words[^1][^1])) words[^1] += "...";

        // Case changes, skipping emoticon codes.
        int k = 0;
        for (int i = 0; i < words.Count; i++)
        {
            if (EmoSet.Contains(words[i])) continue;
            if (upper) words[i] = words[i].ToUpperInvariant();
            else if (alt)
            {
                var sb = new StringBuilder();
                foreach (char ch in words[i]) sb.Append(char.IsLetter(ch) ? (k++ % 2 == 0 ? char.ToLowerInvariant(ch) : char.ToUpperInvariant(ch)) : ch);
                words[i] = sb.ToString();
            }
            else if (h.Lower || leet) words[i] = words[i].ToLowerInvariant();
        }

        // Bursts: break at sentence ends, else at a comma, else in two near the middle.
        var parts = new List<string>();
        if (h.Bursts && words.Count >= 4)
        {
            var cur = new List<string>();
            for (int i = 0; i < words.Count; i++)
            {
                cur.Add(words[i]);
                if (ends[i] && cur.Count >= 2) { parts.Add(string.Join(' ', cur).TrimEnd(',', ';')); cur.Clear(); }
            }
            // An emoticon left alone joins the piece before it.
            if (cur.Count > 0 && cur.All(EmoSet.Contains) && parts.Count > 0) parts[^1] += " " + string.Join(' ', cur);
            else if (cur.Count > 0) parts.Add(string.Join(' ', cur));
            if (parts.Count == 1 && words.Count >= 9)
            {
                int cut = words.Count / 2;
                while (cut < words.Count - 1 && EmoSet.Contains(words[cut])) cut++;
                parts = new() { string.Join(' ', words.Take(cut)), string.Join(' ', words.Skip(cut)) };
            }
        }
        if (parts.Count == 0) parts.Add(string.Join(' ', words));

        // Openers, closers and frames only when not grave; a closer sometimes skipped so it does not wear thin.
        if (!grave)
        {
            if (h.Open != "" && rng.NextDouble() < (excited ? 0.8 : 0.5)) parts[0] = Cased(h, h.Open) + parts[0];
            if (h.Close != "" && rng.NextDouble() < 0.7) parts[^1] += h.Close;
            if (h.Frame != "") { parts[0] = h.Frame + " " + parts[0]; parts[^1] += " " + h.Frame; }
        }
        return parts;
    }

    static string Cased(Hand h, string s) => h.Upper ? s.ToUpperInvariant() : s;

    // A typing slip: two letters swapped, one doubled or one dropped.
    static string Slip(string w, Random rng)
    {
        int i = 1 + rng.Next(w.Length - 2);
        return rng.Next(3) switch
        {
            0 => w[..i] + w[i + 1] + w[i] + w[(i + 2)..],
            1 => w[..i] + w[i] + w[i..],
            _ => w[..i] + w[(i + 1)..],
        };
    }

    // The hand a line is written in, and whether its news is grave (urgent, or about death, war or disaster).
    List<string> Styled(Convo c, string text, bool ours, Sister? s, bool urgent)
    {
        var (seed, traits) = WriterOf(c, ours, s);
        var h = HandFor(seed, traits);
        return h.Plain ? new() { text } : WriteIn(h, text, urgent || Grave.IsMatch(text), chatRng);
    }
}

partial class Sim
{
    // Test (--hands): 24 sample people with random traits, each writing the same few lines, into a text file.
    public static void WriteHandSamples(string path)
    {
        var r = new Random(1);
        string[] lines =
        {
            "My neighbour's goat ate my cabbages AGAIN :@",
            "Did you know the mill wheel has 24 paddles? I counted :D",
            "Queues everywhere. Is there going to be enough? :S",
            "Fire at the bakery! Everyone out, buckets to the square.",
        };
        var keys = Traits.All.Select(t => t.Key).ToArray();
        var sb = new System.Text.StringBuilder();
        for (int p = 0; p < 24; p++)
        {
            var traits = keys.OrderBy(_ => r.Next()).Take(3).ToArray();
            var h = HandFor(r.Next(), traits);
            var q = new List<string>();
            if (h.Lower) q.Add("lower"); if (h.Upper) q.Add("UPPER"); if (h.Alt) q.Add("aLt"); if (h.NoStops) q.Add("no stops"); if (h.Dots) q.Add("dots...");
            if (h.Bang) q.Add("!!!"); if (h.TextSpeak) q.Add("text speak"); if (h.NoApos) q.Add("no apostrophes"); if (h.Leet) q.Add("l33t");
            if (h.Typos > 0) q.Add($"typos {h.Typos:0.00}"); if (h.Open != "") q.Add($"opens '{h.Open.Trim()}'"); if (h.Close != "") q.Add($"closes '{h.Close.Trim()}'");
            if (h.Frame != "") q.Add($"frame {h.Frame}"); if (h.Bursts) q.Add("bursts");
            sb.AppendLine($"#{p + 1} ({string.Join(", ", traits)}): {(q.Count == 0 ? "plain" : string.Join(", ", q))}");
            foreach (var l in lines)
            {
                var parts = h.Plain ? new() { l } : WriteIn(h, l, Grave.IsMatch(l), r);
                sb.AppendLine("    " + string.Join("  /  ", parts));
            }
        }
        File.WriteAllText(path, sb.ToString());
    }
}
