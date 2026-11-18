using System.Text.RegularExpressions;

namespace Dotburg;

// Hearsay (0.87): what the leader has been told, as opposed to what is true. The mood and sister tips read the
// leader's reports instead of the sim's numbers: each standing reason reaches the seat through a teller (the leader's
// own eyes, the steward, the physician, a friend, the chronicler, talk in the market, letters or travellers), late,
// and under a feared leader often not at all or softened. People's tips show strangers by repute only.
// Nothing here draws from Rng or changes the town: every roll is a hash of the reason, the leader and the day.

// One thing the leader has been told: a standing reason for the mood or for a sister's regard.
sealed class Report
{
    public string Text = "";     // as told: may be stale (the count when it was told) or wrong
    public string Teller = "";   // who told it; "" when the leader sees it with their own eyes
    public double D;             // its weight as told (sign and size); shown only as a word
    public double Heard;         // Age when it reached (or reaches) the leader, or when it was last brought up to date
    public double Ended = -1;    // Age when the leader hears it is over; -1 while it lasts
    public bool Silent;          // nobody tells this leader: never shown
    public bool Wrong;           // told wrong (softened, overstated, misplaced or made up)
    public string Job = "";      // the council job that told it (steward, physician, spymaster), "" for anyone else (0.88)
    public bool Passed;          // the spymaster has passed it on in the Messenger (0.88)
    public bool Arrived(double age) => !Silent && age >= Heard;
}

partial class Sim
{
    public readonly Dictionary<string, Report> MoodHeard = new();   // mood reasons by key (numbers stripped)
    public readonly Dictionary<string, Report> SisHeard = new();    // "sister|key" -> report
    public double EstMood = -1;                                     // the mood as the leader believes it
    readonly Dictionary<string, double> estRel = new();             // each sister's regard as the leader believes it
    public double HeardTarget;                                      // where the leader is told the mood is heading
    string? hearStamp;                                              // town and leader the store belongs to
    double hearLast;
    public static bool Truth;                                       // --truth: the tips show the real numbers
    public static double ForceDread = -1;                           // --dread: every leader this feared (soak testing, 0.87.2)
    public double PlainSince = -1;                                  // Age of the last survived attempt (HearCrisis); not saved
    public int HearMade, HearSilent, HearWrong;                     // soak counters

    const double HourMs = DayMs / 24;

    // How much the leader is feared, 0..1: harsh rule and cruel, proud or vengeful leaders hear less bad news; humble,
    // tolerant and merciful ones hear more.
    public double Dread()
    {
        if (ForceDread >= 0) return ForceDread;
        double f = G.Harsh * 0.75;
        foreach (var k in Leader?.Traits ?? Array.Empty<string>())
            f += k is "cruel" or "proud" or "vengeful" ? 0.15 : k is "humble" or "tolerant" or "merciful" ? -0.1 : 0;
        return Clamp(f, 0, 1);
    }

    // A number 0..1 fixed for this question, this leader and this day: the same every time it is asked.
    double HearRoll(string key, int salt) =>
        Trait01(Names.Seed(key) ^ (LeaderWalker?.Id ?? 0) * 31 ^ (int)(Age / DayMs) * 7919, 600 + salt);

    // A reason's key: its words without numbers, so "3 sick in bed" and "5 sick in bed" are the same reason.
    static string KeyOf(string why) => Regex.Replace(why, @"\d+%?", "#");

    // The words a reason is told in when it is softened: counts become "a few".
    static string Soften(string why) => Regex.Replace(Regex.Replace(why, @"^\d+( of \d+)?", "A few"), @"\s*\(.*\)$", "");

    // One step: bring the leader's reports in line with what is true, as fast as the tellers carry it.
    void StepHearsay()
    {
        var lw = LeaderWalker;
        string stamp = $"{Name}|{Founded}|{lw?.Id ?? 0}";
        if (stamp != hearStamp)
        {
            // A new leader starts with their own blind spots; after a load the leader already knows what they knew.
            bool warm = hearStamp == null;
            MoodHeard.Clear(); SisHeard.Clear(); estRel.Clear();
            PlainSince = -1;
            hearStamp = stamp;
            StepMoodHeard(warm); StepSisHeard(warm);
            EstMood = Clamp(MoodTarget + MoodBias(), 0, 1);
            hearLast = Age;
            SpyWord(true);
            return;
        }
        double dt = Age - hearLast; hearLast = Age;
        StepMoodHeard(false);
        StepSisHeard(false);
        SpyWord(false);
        // The leader's sense of the mood follows the truth, a few hours behind, bent by what nobody told them.
        HeardTarget = Clamp(MoodTarget + MoodBias(), 0, 1);
        EstMood = Ease(EstMood < 0 ? Mood : EstMood, Clamp(Mood + MoodBias(), 0, 1), dt, DayMs / 4);
        foreach (var s in Sisters)
        {
            double t = Clamp(RelOf(s) + SisBias(s), -1, 1);
            estRel[s.Name] = estRel.TryGetValue(s.Name, out var e) ? Ease(e, t, dt, DayMs * 2) : t;
        }
    }

    // How far what the leader believes is from the truth: each reason as told minus as it is.
    double MoodBias()
    {
        var truth = MoodParts().Where(p => Math.Abs(p.D) >= 0.005).GroupBy(p => KeyOf(p.Why)).ToDictionary(g => g.Key, g => g.Sum(p => p.D));
        double b = 0;
        foreach (var (k, r) in MoodHeard) b += (r.Arrived(Age) ? r.D : 0) - truth.GetValueOrDefault(k);
        foreach (var (k, d) in truth) if (!MoodHeard.ContainsKey(k)) b -= d;
        return b;
    }
    double SisBias(Sister s)
    {
        var truth = RelParts(s).Where(p => Math.Abs(p.D) >= 0.005).ToDictionary(p => $"{s.Name}|{SisKey(p.Why)}", p => p.D);
        double b = 0;
        foreach (var (k, r) in SisHeard) if (k.StartsWith(s.Name + "|")) b += (r.Arrived(Age) ? r.D : 0) - truth.GetValueOrDefault(k);
        foreach (var (k, d) in truth) if (!SisHeard.ContainsKey(k)) b -= d;
        return b;
    }

    // Who carries a mood reason to the leader, how late, and how honestly.
    void StepMoodHeard(bool warm)
    {
        var seen = new HashSet<string>();
        foreach (var (why, d) in MoodParts())
        {
            if (Math.Abs(d) < 0.005) continue;
            string k = KeyOf(why);
            if (!seen.Add(k)) continue;
            if (MoodHeard.TryGetValue(k, out var r) && r.Ended < 0)
            {
                // The teller brings it up to date now and then (the eyes at once); a softened one stays soft.
                if (!r.Silent && Age >= r.Heard && !(r.Wrong && k.StartsWith("The leader is")) && (r.Teller == "" || Age - r.Heard > 6 * HourMs) && r.Text != (r.Wrong ? Soften(why) : why))
                {
                    r.Text = r.Wrong ? Soften(why) : why;
                    r.D = !r.Wrong ? d : d < 0 ? d * 0.35 : d * 1.6;   // a softened or swollen report stays so
                    r.Heard = Age;
                }
                continue;
            }
            MoodHeard[k] = NewMoodReport(k, why, d, warm);
        }
        // Flatterers: a feared leader sometimes hears praise nobody is singing.
        double fear = Dread();
        const string praise = "praise";
        seen.Add(praise);
        // The flatterer is a real dot-man (0.89.1): a friend of the leader, else a councillor; nobody, no praise.
        if (fear >= 0.45 && HearRoll(praise, 9) < fear * 0.6 && !MoodHeard.ContainsKey(praise) && Flatterer() is { } fl)
            MoodHeard[praise] = Made(new Report { Text = "They sing your praises", Teller = $"{CitizenFirst(fl)}, at the door", D = 0.05, Heard = Age, Wrong = true });
        else if (fear < 0.45) seen.Remove(praise);

        // Reasons that are over: the leader hears so a little later, and until then still believes them.
        foreach (var (k, r) in MoodHeard)
            if (!seen.Contains(k) && r.Ended < 0) r.Ended = r.Silent || r.Teller == "" || warm ? Age : Age + (r.Heard > Age ? 0 : Delay(r, k, 5) * 0.5);
        foreach (var k in MoodHeard.Where(kv => kv.Value.Ended >= 0 && Age >= kv.Value.Ended).Select(kv => kv.Key).ToList()) MoodHeard.Remove(k);
    }

    // A new mood reason reaches the leader: seen with their own eyes, or told by someone, late, maybe softened or not at all.
    Report NewMoodReport(string k, string why, double d, bool warm)
    {
        var r = new Report { Text = why, D = d, Heard = Age };
        string topic = MoodTopic(k);
        if (topic == "eyes") return Made(r);
        bool atLeader = k.StartsWith("The leader is");
        // An empty seat tells nothing (0.89.1): the news comes as gossip instead.
        var (teller, honest, job) = topic switch
        {
            "steward" when HolderOf("steward") != null => (NpcTitle("steward"), 1.0, "steward"),
            "physician" when HolderOf("physician") != null => (NpcTitle("physician"), 1.0, "physician"),
            _ => Gossip(),
        };
        if (job != topic) topic = "gossip";
        r.Teller = teller;
        r.Job = job;
        double fear = Dread() * (1 - honest);
        if (topic is "steward" or "physician") fear = Dread();
        double miss = 0.08 + 0.45 * fear, wrong = 0.04 + 0.22 * fear;
        if (atLeader) miss = Math.Min(0.9, miss * 1.6);
        if (honest >= 1 && topic == "gossip") { miss = 0.1; wrong = 0; }   // a friend tells it straight, if they noticed
        // A councillor's skill, bond with the leader and time in the seat bend it either way (Council.cs, 0.88).
        if (job != "") { double f = CouncilFactor(job); miss = Math.Min(0.9, miss * f); wrong = Math.Min(0.9, wrong * f); }
        if (d < 0)
        {
            if (HearRoll(k, 1) < miss) r.Silent = true;
            else if (HearRoll(k, 2) < wrong)
            {
                r.Wrong = true;
                if (atLeader) { r.Text = "They grumble about the times"; r.D = d * 0.5; }
                else { r.Text = Soften(why); r.D = d * 0.35; }
            }
        }
        else if (HearRoll(k, 2) < wrong * 0.5) { r.Wrong = true; r.D = d * 1.6; }   // good news grows in the telling
        r.Heard = warm ? Age - Delay(r, k, 3) : Age + Delay(r, k, 3);   // already known after a load or a pre-grown start: heard a while back (0.87.2)
        return Made(r);
    }

    // A survived attempt on the leader (0.87.2): whatever was kept from them, softened or still on the way comes out at
    // once, and flattery stops, so the believed mood snaps to the truth. News after this is bent again as usual.
    void HearCrisis()
    {
        var truth = MoodParts().Where(p => Math.Abs(p.D) >= 0.005).GroupBy(p => KeyOf(p.Why)).ToDictionary(g => g.Key, g => (why: g.First().Why, d: g.Sum(p => p.D)));
        foreach (var (k, r) in MoodHeard.ToList())
        {
            if (r.Ended >= 0) continue;
            if (!truth.TryGetValue(k, out var t)) { MoodHeard.Remove(k); continue; }   // made up: the praise of a well-wisher
            if (!r.Silent && !r.Wrong && r.Heard <= Age) continue;
            (r.Silent, r.Wrong, r.Text, r.D, r.Teller) = (false, false, t.why, t.d, "");
            r.Heard = Math.Min(r.Heard, Age);
        }
        foreach (var (k, t) in truth) if (!MoodHeard.ContainsKey(k)) MoodHeard[k] = new Report { Text = t.why, D = t.d, Heard = Age };
        PlainSince = Age;
        EstMood = Clamp(Mood + MoodBias(), 0, 1);
    }

    // Count a report for the soak and hand it back; told bad news is also counted by how feared the leader is
    // (under a third, middling, two thirds or more), since that is what the error rates were set for.
    Report Made(Report r)
    {
        HearMade++;
        if (r.Silent) HearSilent++;
        if (r.Wrong) HearWrong++;
        if (r.Teller != "" && r.D < 0)
        {
            double f = Dread();
            int band = f < 0.33 ? 0 : f < 0.66 ? 1 : 2;
            HearBand[band, 0]++;
            if (r.Silent) HearBand[band, 1]++;
            if (r.Wrong) HearBand[band, 2]++;
        }
        return r;
    }
    readonly int[,] HearBand = new int[3, 3];   // [dread band, told / silent / wrong]

    // Who tells a mood reason: the leader's own eyes for what anyone can see, the steward for stores, crowding and
    // coffers, the physician for illness, and gossip for feelings about the leader, feuds and walks.
    static string MoodTopic(string k)
    {
        if (k.Contains("hungry") || k.Contains("starving") || k.StartsWith("Fear of running out") || k.StartsWith("Crowded") || k.StartsWith("The coffers")) return "steward";
        if (k.Contains("sick in bed") || k.StartsWith("Sick houses")) return "physician";
        if (k.StartsWith("The leader is") || k.StartsWith("A feud") || k.StartsWith("Walks in the grove")) return "gossip";
        return "eyes";
    }

    // Gossip reaches the leader through the spymaster (0.88: a gossip under gentle rule, honest by half; an informer
    // under harsh rule, who tells what pleases), else their closest friend (honest), else the chronicler (honest by
    // half), else market talk (as honest as the leader is unfeared).
    (string teller, double honest, string job) Gossip()
    {
        var lw = LeaderWalker;
        if (HolderOf("spymaster") != null) return (NpcTitle("spymaster"), G.Harsh >= 0.6 ? 0.3 : 0.5, "spymaster");
        var friend = lw?.Bonds.Where(b => b.Kind == "friend" && ById(b.Who) is { Gone: false }).OrderByDescending(b => b.Str).FirstOrDefault();
        if (friend != null) return ($"your friend {CitizenFirst(ById(friend.Who)!)}", 1.0, "");
        if (Chronicler is { } c && c != lw) return ($"{CitizenFirst(c)}, who keeps the chronicle", 0.5, "");
        return ("talk in the market", 0.0, "");
    }

    // How late a teller's word arrives, in ms: the steward and physician within hours, the spymaster and friends
    // within half a day, the chronicler within a day, market talk within a day and a half.
    double Delay(Report r, string k, int salt)
    {
        string teller = r.Teller;
        var (lo, hi) = r.Job == "steward" ? (2, 8) : r.Job == "physician" ? (1, 6) : r.Job == "spymaster" ? (2, 10)
            : teller.Contains("chronicle") ? (6, 24) : teller.StartsWith("your friend") ? (2, 12)
            : teller == "talk in the market" ? (12, 36) : teller == "" ? (0, 0) : (1, 6);
        return (lo + (hi - lo) * HearRoll(k, salt)) * HourMs;
    }

    // The spymaster passes on what they hear in the Messenger as it reaches the leader, at most one a quarter day;
    // anything else they carried shows only in the tips.
    // A reason that drops out and comes back (walks in the grove stop in a storm) is not news again for 5 days (0.90.1).
    double spyTold = -DayMs;
    readonly Dictionary<string, double> spySaid = new();
    void SpyWord(bool warm)
    {
        foreach (var (k, r) in MoodHeard)
        {
            if (r.Job != "spymaster" || r.Passed || !r.Arrived(Age)) continue;
            r.Passed = true;
            if (warm || Age - spyTold < DayMs / 4) continue;
            if (spySaid.TryGetValue(k, out var said) && Age - said < 5 * DayMs) continue;
            spyTold = Age;
            spySaid[k] = Age;
            Npc("spymaster", G.Harsh >= 0.6 ? "inform" : "word", false, SpyTale(r.Text));
        }
    }

    // A mood reason as street talk (0.90.1): the tips' short labels read as gossip, not as a list entry.
    static string SpyTale(string why)
    {
        if (why == "Walks in the grove") return "folk are out walking in the grove, and happier for it";
        if (why == "They grumble about the times") return "people grumble about the times";
        if (why.StartsWith("The leader is ")) return $"they call you {why["The leader is ".Length..]}";
        if (why.StartsWith("A feud between ")) return $"there's bad blood between {why["A feud between ".Length..]}";
        return why.Length > 1 ? char.ToLowerInvariant(why[0]) + why[1..] : why;
    }

    // A sister's reason as a key: her ruler's trait or the rule gap, without amounts.
    static string SisKey(string why) => why;

    // News of the sisters: what letters, envoys and travellers say, slower and thinner from a cold sister.
    void StepSisHeard(bool warm)
    {
        var seen = new HashSet<string>();
        foreach (var s in Sisters)
        {
            double cold = Math.Max(0, -RelOf(s));
            foreach (var (why, d) in RelParts(s))
            {
                if (Math.Abs(d) < 0.005 || why == "Neighbourly goodwill") continue;
                string k = $"{s.Name}|{SisKey(why)}";
                seen.Add(k);
                if (SisHeard.ContainsKey(k)) continue;
                var r = new Report { Text = why, D = d, Heard = Age };
                if (why.StartsWith("Their ruler is"))
                {
                    r.Teller = "travellers' talk";
                    if (HearRoll(k, 1) < 0.25 + 0.3 * cold) r.Silent = true;
                    else if (HearRoll(k, 2) < 0.15)
                    {
                        // The wrong trait: another of the same kind, said with the same feeling.
                        string tk = why["Their ruler is ".Length..];
                        var t = Traits.All.FirstOrDefault(x => x.Name.Equals(tk, StringComparison.OrdinalIgnoreCase));
                        var alike = t == null ? Array.Empty<Trait>() : Traits.InCat(t.Cat).Where(x => x.Key != t.Key).ToArray();
                        if (alike.Length > 0) { r.Wrong = true; r.Text = $"Their ruler is {alike[(int)(HearRoll(k, 4) * alike.Length)].Name.ToLowerInvariant()}"; }
                    }
                    r.Heard = Age + (warm ? -1 : 1) * (12 + 36 * HearRoll(k, 3)) * HourMs;
                }
                else if (why != "We live under the same rule")
                {
                    r.Teller = "their letters";
                    if (HearRoll(k, 1) < 0.1 + 0.4 * cold) r.Silent = true;
                    r.Heard = Age + (warm ? -1 : 1) * (12 + 36 * HearRoll(k, 3)) * HourMs;   // backdated when warm, so times differ (0.87.2)
                }
                SisHeard[k] = Made(r);
            }
        }
        foreach (var (k, r) in SisHeard) if (!seen.Contains(k) && r.Ended < 0) r.Ended = r.Silent || warm ? Age : Age + 12 * HourMs;
        foreach (var k in SisHeard.Where(kv => kv.Value.Ended >= 0 && Age >= kv.Value.Ended).Select(kv => kv.Key).ToList()) SisHeard.Remove(k);
    }

    // A sister's regard as the leader believes it, and its word and colour.
    public double HeardRel(Sister s) => Truth ? RelOf(s) : estRel.TryGetValue(s.Name, out var e) ? e : RelOf(s);
    public (string word, int col) HeardAttitude(Sister s) => Truth ? Attitude(s) : AttitudeAt(s, HeardRel(s));

    // The town's mood as the leader believes it.
    public double HeardMood => Truth || EstMood < 0 ? Mood : EstMood;

    // When a report reached the leader, in words.
    public string HeardWhen(Report r)
    {
        int hours = (int)((Age - r.Heard) / HourMs);
        return hours < 1 ? "just now" : hours < 2 ? "an hour ago" : hours < 24 ? $"{hours} hours ago" : hours < 48 ? "yesterday" : $"{hours / 24} days ago";
    }

    // ---------- people ----------
    // How the leader knows a dot-man: "" for a stranger, else why ("you", "your spouse", "your friend", ...).
    public string KnownAs(Walker w)
    {
        var lw = LeaderWalker;
        if (Truth) return "known";
        if (lw == null) return "known";   // settlers with no leader yet: the view is the settlement's own
        if (w == lw) return "you";
        if (lw.Spouse == w.Id) return "your spouse";
        if (BondOf(lw, w.Id, "friend") != null) return "your friend";
        if (BondOf(lw, w.Id, "grudge") != null) return "your foe";
        if (SeatHeldBy(w) is { } seat) return $"your {SeatWord(seat.Role)}";   // the leader knows their council well (0.88)
        if (w.Last != "" && w.Last == lw.Last) return "your kin";
        if (w.Id == ChroniclerId) return "keeps the chronicle";
        return "";
    }

    // What is said of a stranger: one of their traits by repute (one in five times the wrong one), or nothing.
    string Repute(Walker w)
    {
        if (Trait01(w.Id, 610) >= 0.65) return "";
        var tr = CitizenTraits(w).Where(Traits.Exists).ToArray();
        if (tr.Length == 0) return "";
        var t = Traits.Get(tr[(int)(Trait01(w.Id, 611) * tr.Length)]);
        if (Trait01(w.Id, 612) < 0.2)
        {
            var alike = Traits.InCat(t.Cat).Where(x => x.Key != t.Key).ToArray();
            if (alike.Length > 0) t = alike[(int)(Trait01(w.Id, 613) * alike.Length)];
        }
        return $"said to be {t.Name.ToLowerInvariant()}";
    }

    // Soak summary: how many reports were made, kept silent and told wrong.
    public string HearsayLine() =>
        $"reports {HearMade}, silent {HearSilent} ({100.0 * HearSilent / Math.Max(1, HearMade):F0}%), wrong {HearWrong} ({100.0 * HearWrong / Math.Max(1, HearMade):F0}%); " +
        $"mood {Mood * 100:F0}, believed {HeardMood * 100:F0}; now heard {MoodHeard.Values.Count(r => r.Arrived(Age))}, on the way {MoodHeard.Values.Count(r => !r.Silent && Age < r.Heard)}, silent {MoodHeard.Values.Count(r => r.Silent)}\n" +
        string.Join("\n", new[] { "gentle", "middling", "feared" }.Select((n, b) =>
            $"bad news to a {n} leader: {HearBand[b, 0]}, silent {100.0 * HearBand[b, 1] / Math.Max(1, HearBand[b, 0]):F0}%, wrong {100.0 * HearBand[b, 2] / Math.Max(1, HearBand[b, 0]):F0}%"));
}
