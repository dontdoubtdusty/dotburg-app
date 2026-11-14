namespace Dotburg;

// A body part. Size is how often a blow lands there; Vital means losing it kills; Bleed scales bleeding from it;
// Lost is what a destroyed non-vital part leaves behind ("" = it cannot be lost); Bad is what a bad wound leaves when it heals.
record Part(string Key, string Name, double Size, bool Vital, double Bleed, string Lost, string Bad);

// A kind of wound. Bleed is blood lost per day at full severity (untended); Infect the chance it festers if tended badly;
// Heal how fast it closes per day once tended; How is the verb for a death line ("stabbed through the heart").
record Hurt(string Key, string Noun, double Bleed, double Infect, double Heal, string How);

// One thing wrong with a body: a wound on a part, or a condition of the whole body (weak heart, growth, poison, frailty...).
class Cond
{
    public string Kind = "";     // a Hurt key for wounds; else a condition key (heart, growth, frail, back, eyes, ears, poison, stroke, scar, lost)
    public string Part = "";     // body part key, or "" for the whole body
    public double Sev;           // 0..1 (for a wound: how much of the part is torn; 1 destroys it)
    public double Peak;          // the worst it got, for the scar it leaves
    public double TendAt = -1;   // Age when someone tends it; -1 once tended (or for conditions that need none)
    public double Infect = -1;   // infection severity 0..1 once it festers, else -1
    public double Imm;           // the body's fight against that infection or poison, 0..1
    public double Since;         // Age it began
    public string Why = "";      // how it came about: "an attempt on their life", "the fire at the bakery", "a fall"
}

// The health of someone who matters: every leader, and former leaders while they live in town.
// One year of a person's life passes each town day (a town's politics already runs fast: a war about a month apart).
class Body
{
    public int Id;               // the dot-man's id
    public string Name = "";
    public string Was = "";      // "former Mayor Ada the Honest" once out of office, "" while leading
    public double Born;          // Age (ms) at which they were born
    public double Blood = 1;     // 1 full, 0 dead
    public int Attacks;          // heart attacks survived
    public List<Cond> Conds = new();
}

partial class Sim
{
    public const double YearMs = 4 * DayMs; // everyone ages a year every 4 town days (0.67; it was one a day)
    const double AgeRate = DayMs / YearMs;  // age-driven rates are per year: this turns them into per day
    const int HealthEvery = 30;            // ticks between health steps (about 3 s)
    public static double OldK = 0.011;     // how fast age brings on the deadly complaints (Gompertz curve base at 30)

    public static readonly Part[] Parts =
    {
        //  key       name           size  vital  bleed  lost                    bad
        new("head",   "head",        5,    true,  1.0,  "",                     "headaches"),
        new("neck",   "neck",        2,    true,  2.0,  "",                     "scar"),
        new("heart",  "heart",       1.5,  true,  3.0,  "",                     "scar"),
        new("lungl",  "left lung",   4,    false, 1.4,  "lost a lung",          "short of breath"),
        new("lungr",  "right lung",  4,    false, 1.4,  "lost a lung",          "short of breath"),
        new("liver",  "liver",       3,    true,  1.6,  "",                     "scar"),
        new("stomach","stomach",     3,    false, 1.2,  "lost part of the stomach", "scar"),
        new("kidney", "kidney",      2,    false, 1.3,  "lost a kidney",        "scar"),
        new("torso",  "ribs",        10,   false, 0.8,  "",                     "scar"),
        new("arml",   "left arm",    6,    false, 0.7,  "lost the left arm",    "stiff arm"),
        new("armr",   "right arm",   6,    false, 0.7,  "lost the right arm",   "stiff arm"),
        new("handl",  "left hand",   2.5,  false, 0.5,  "lost the left hand",   "scarred hand"),
        new("handr",  "right hand",  2.5,  false, 0.5,  "lost the right hand",  "scarred hand"),
        new("legl",   "left leg",    7,    false, 0.8,  "lost the left leg",    "limp"),
        new("legr",   "right leg",   7,    false, 0.8,  "lost the right leg",   "limp"),
        new("eyel",   "left eye",    0.8,  false, 0.3,  "lost the left eye",    "scar"),
        new("eyer",   "right eye",   0.8,  false, 0.3,  "lost the right eye",   "scar"),
        new("jaw",    "jaw",         1.5,  false, 0.6,  "lost the jaw",         "crooked jaw"),
    };

    public static readonly Hurt[] Hurts =
    {
        //  key      noun                    bleed infect heal  how
        new("stab",  "stab wound",           6,    0.30,  0.30, "stabbed"),
        new("cut",   "cut",                  4,    0.25,  0.40, "cut"),
        new("bolt",  "crossbow wound",       5,    0.35,  0.25, "shot"),
        new("crush", "blow",                 0.6,  0.10,  0.35, "struck"),
        new("burn",  "burns",                0.5,  0.45,  0.20, "burned"),
        new("break", "broken bone",          0.4,  0.05,  0.15, "broken"),
        new("surgery","surgery wound",       2,    0.25,  0.35, "cut open"),
    };

    static readonly string[] GrowthParts = { "lungl", "lungr", "stomach", "liver", "kidney" };

    public List<Body> Bodies = new();

    static Part PartOf(string key) => Parts.FirstOrDefault(p => p.Key == key) ?? Parts[8];
    static Hurt? HurtOf(string key) => Hurts.FirstOrDefault(h => h.Key == key);
    public Body? BodyOf(Walker? w) => w == null ? null : Bodies.FirstOrDefault(b => b.Id == w.Id);
    public Body? LeaderBody => BodyOf(LeaderWalker);
    public double YearsOf(Body b) => Math.Floor((Age - b.Born) / YearMs);
    static bool Has(Body b, string kind) => b.Conds.Any(c => c.Kind == kind);

    // How old a new leader is, by the kind of rule (elders are old; a crown's heir can be young).
    double StartAge(string gov, bool heir) => gov switch
    {
        "elders" => 58 + 18 * Chance(),
        "founders" => 45 + 25 * Chance(),
        "shrine" => 40 + 30 * Chance(),
        "crown" => heir ? 18 + 27 * Chance() : 30 + 25 * Chance(),
        "commune" => 25 + 30 * Chance(),
        "temple" => 45 + 25 * Chance(),
        "guild" => 40 + 25 * Chance(),
        _ => 35 + 30 * Chance(),
    };

    // Give a new leader a body (or reuse theirs if they led before); returns it.
    Body EnsureBody(Walker w, string name, bool heir)
    {
        var b = BodyOf(w);
        if (b != null) { b.Was = ""; return b; }
        b = new Body { Id = w.Id, Name = name, Born = Age - StartAge(GovKey, heir) * YearMs };
        // Older people bring a few complaints with them.
        double y = YearsOf(b);
        if (y > 40 && Chance() < (y - 40) / 60) b.Conds.Add(new Cond { Kind = "back", Since = Age });
        if (y > 55 && Chance() < (y - 55) / 40) b.Conds.Add(new Cond { Kind = Chance() < 0.5 ? "eyes" : "ears", Since = Age });
        Bodies.Add(b);
        return b;
    }

    // A leader leaves office but stays in town: they keep their body and their old title.
    void Retire(Walker w)
    {
        var b = BodyOf(w);
        if (b != null && Leader != null && b.Was == "") b.Was = "former " + LeaderTitle;
    }

    // A person's resistance to infection and poison: their own luck, worn down by age, frailty and blood loss.
    double Toughness(Walker w, Body b) => (0.6 + 0.8 * Trait01(w.Id, 300)) * Wear(b);

    // How much age, frailty, a weak heart and blood loss wear down the body's defences (1 = not at all).
    public double Wear(Body b)
    {
        double y = YearsOf(b);
        double t = (1 - Math.Max(0, y - 50) / 80) * (Has(b, "frail") ? 0.75 : 1) * (Has(b, "heart") ? 0.9 : 1);
        return Math.Max(0.25, t * (0.5 + 0.5 * b.Blood));
    }

    // Somebody who matters is bedridden: badly hurt, bleeding, poisoned or just struck down.
    static bool Bedridden(Body b) => b.Blood < 0.7 || b.Conds.Any(c =>
        (HurtOf(c.Kind) != null && (c.TendAt >= 0 || c.Sev >= 0.45 || c.Infect >= 0)) || c.Kind == "poison" || (c.Kind == "growth" && c.Sev > 0.6));

    // ---------- causes of harm ----------

    // A wound lands on a part. Returns the wound.
    Cond Wound(Body b, string kind, string part, double sev, string why, double slow = 1)
    {
        // Small vital parts give way to less: a blow that would bruise an arm stops a heart.
        double frag = part switch { "heart" => 2, "neck" => 1.6, "head" => 1.2, "eyel" or "eyer" => 2, _ => 1 };
        var c = new Cond { Kind = kind, Part = part, Sev = Math.Min(1, sev * frag), Since = Age, Why = why };
        c.Peak = c.Sev;
        c.TendAt = Age + DayMs * (0.02 + 0.1 * Chance()) * slow * (Care() > 0.3 ? 0.6 : 1);
        b.Blood -= c.Sev * 0.15 * PartOf(part).Bleed;     // the first rush of blood
        b.Conds.Add(c);
        return c;
    }

    string RandomPart(double headMul = 1) => Weighted(Parts, p => p.Size * (p.Key is "head" or "neck" or "jaw" ? headMul : 1)).Key;

    // An assassin strikes (named when we know who). Returns the line for the crier, and whether the leader died on the spot.
    (string line, bool dead) Attempt(Walker lw, string? by = null)
    {
        var b = LeaderBody ?? EnsureBody(lw, Leader?.Name ?? "", false);
        string t = LeaderTitle;
        string weapon = Weighted(new[] { "knife", "bolt", "club", "poison" }, k => k switch
        {
            "knife" => 3,
            "bolt" => G.Harsh >= 0.8 ? 3 : 1,
            "club" => 1.5,
            _ => GovKey is "crown" or "temple" or "guild" ? 3 : 1,
        });
        const string why = "an attempt on their life";
        var hits = new List<Cond>();
        switch (weapon)
        {
            case "knife":
                int n = Chance() < 0.25 ? 1 : Chance() < 0.5 ? 2 : 3;
                for (int i = 0; i < n; i++) hits.Add(Wound(b, "stab", RandomPart(), 0.4 + 0.7 * Chance(), why, 2.2));
                break;
            case "bolt":
                hits.Add(Wound(b, "bolt", RandomPart(2), 0.55 + 0.7 * Chance(), why, 2.2));
                break;
            case "club":
                int m = Chance() < 0.5 ? 1 : 2;
                for (int i = 0; i < m; i++) hits.Add(Wound(b, Chance() < 0.3 ? "break" : "crush", RandomPart(4), 0.45 + 0.75 * Chance(), why, 2.2));
                break;
            default:
                b.Conds.Add(new Cond { Kind = "poison", Sev = 0.15 + 0.25 * Chance(), Since = Age, Why = why });
                break;
        }
        var fatal = hits.FirstOrDefault(h => h.Sev >= 1 && PartOf(h.Part).Vital);
        if (fatal != null)
            return ($"{t} is assassinated{(by != null ? " by " + by : "")}: {HowLine(fatal)}.", true);
        // Blood lost before anyone can tend the wounds: too much, and the leader dies where they fell.
        double lost = hits.Sum(h => h.Sev * HurtOf(h.Kind)!.Bleed * PartOf(h.Part).Bleed * (h.TendAt - Age) / DayMs);
        if (b.Blood - lost <= 0)
            return ($"{t} is assassinated{(by != null ? " by " + by : "")}: {HowList(hits)}, and bleeds to death before help comes.", true);
        lw.Until = TickNo;
        return weapon == "poison"
            ? ($"{by ?? "An assassin"} poisons {t}. The physician is sent for.", false)
            : ($"{by ?? "An assassin"} strikes at {t}: {HowList(hits)}. {t} is carried to bed.", false);
    }

    // Fire reaches someone who matters: burns instead of a sudden death.
    void BurnBody(Walker w, Body b, Fire f)
    {
        int n = Chance() < 0.5 ? 1 : 2;
        string why = $"the fire at the {Content.Label(Built[f.Rank] == "ruin" ? "house" : Built[f.Rank])}";
        for (int i = 0; i < n; i++) Wound(b, "burn", RandomPart(), 0.3 + 0.6 * f.Size * Chance() + 0.2 * Chance(), why);
        w.Sleep = false;
        w.Until = TickNo;
        var c = b.Conds.Last();
        if (!Quiet) Say($"{NameOf(b)} is pulled from the fire with {HurtOf("burn")!.Noun} to the {PartOf(c.Part).Name}.");
    }

    // ---------- the step ----------

    void StepHealth()
    {
        // The sitting leader always has a body (an older save, or a leader appointed some other way).
        if (LeaderWalker is { } lw0 && Leader != null && BodyOf(lw0) == null) EnsureBody(lw0, Leader.Name, false);
        if (Bodies.Count == 0) return;
        double dt = HealthEvery * TickMs / DayMs;    // days
        double care = Care();
        double q = Clamp(0.3 + care, 0, 1);         // how well wounds are tended
        foreach (var b in Bodies.ToList())
        {
            var w = Walkers.FirstOrDefault(x => x.Id == b.Id && !x.Gone);
            if (w == null) { Bodies.Remove(b); continue; }  // moved away, exiled or soldiering elsewhere
            if (b.Was == "" && !w.Lead) b.Was = "former leader " + b.Name; // out of office by a path that did not retire them
            double y = YearsOf(b);
            string? death = null;
            Cond? killer = null;

            // Wounds: bleed until tended, may fester, close over time and leave their mark.
            double bleed = 0;
            foreach (var c in b.Conds.ToList())
            {
                var h = HurtOf(c.Kind);
                if (h == null) continue;
                var p = PartOf(c.Part);
                if (c.TendAt >= 0 && Age >= c.TendAt)
                {
                    c.TendAt = -1;
                    if (Chance() < h.Infect * (1 - q) * (c.Sev + 0.3)) { c.Infect = 0.05; c.Imm = 0; }
                }
                bleed += c.Sev * h.Bleed * p.Bleed * (c.TendAt >= 0 ? 1 : 0.03 * (1 - care));
                if (c.Infect >= 0)
                {
                    c.Infect += 0.45 * (1 - care) * dt;
                    c.Imm += 0.5 * Toughness(w, b) * dt;
                    if (c.Infect >= 1) { death = $"an infected {h.Noun} {On(c.Kind)} the {p.Name}"; killer = c; break; }
                    if (c.Imm >= 1) c.Infect = -1;
                    continue; // a festering wound does not close
                }
                c.Sev -= h.Heal * (c.TendAt >= 0 ? 0.3 : 1) * (Has(b, "frail") ? 0.6 : 1) * dt;
                if (c.Sev <= 0)
                {
                    b.Conds.Remove(c);
                    if (c.Peak >= 1 && p.Lost != "") b.Conds.Add(new Cond { Kind = "lost", Part = c.Part, Since = Age, Why = c.Why });
                    else if (c.Peak >= 0.6) b.Conds.Add(new Cond { Kind = "scar", Part = c.Part, Since = Age, Why = c.Why });
                }
            }
            if (death == null)
            {
                b.Blood = Math.Min(1, b.Blood + (0.6 - bleed) * dt); // the body makes blood back while it bleeds less than that
                if (b.Blood <= 0)
                {
                    killer = b.Conds.Where(c => HurtOf(c.Kind) != null).OrderByDescending(c => c.Sev * HurtOf(c.Kind)!.Bleed * PartOf(c.Part).Bleed).FirstOrDefault();
                    death = killer != null ? $"blood loss from {AOrAn(HurtOf(killer.Kind)!.Noun)} {On(killer.Kind)} the {PartOf(killer.Part).Name}" : "blood loss";
                }
            }

            // Poison: a race, like illness, that care slows.
            var poison = b.Conds.FirstOrDefault(c => c.Kind == "poison");
            if (death == null && poison != null)
            {
                poison.Sev += 0.9 * (1 - care) * dt;
                poison.Imm += 0.55 * Toughness(w, b) * dt;
                if (poison.Sev >= 1) { death = "poison"; killer = poison; }
                else if (poison.Imm >= 1) b.Conds.Remove(poison);
            }

            // Age: the deadly complaints come on faster the older they are.
            if (death == null)
            {
                double old = OldK * AgeRate * Math.Exp((y - 30) / 10);
                if (!Has(b, "heart") && Chance() < PerDays(0.4 * old, dt)) b.Conds.Add(new Cond { Kind = "heart", Since = Age });
                if (!Has(b, "growth") && Chance() < PerDays(0.35 * old, dt))
                    b.Conds.Add(new Cond { Kind = "growth", Part = GrowthParts[Rng.Next(GrowthParts.Length)], Sev = 0.02, Since = Age });
                if (!Has(b, "frail") && y >= 65 && Chance() < PerDays(0.08 * AgeRate, dt)) b.Conds.Add(new Cond { Kind = "frail", Since = Age });
                if (!Has(b, "back") && y >= 40 && Chance() < PerDays(0.01 * AgeRate, dt)) b.Conds.Add(new Cond { Kind = "back", Since = Age });
                if (!Has(b, "eyes") && y >= 55 && Chance() < PerDays(0.008 * AgeRate, dt)) b.Conds.Add(new Cond { Kind = "eyes", Since = Age });
                if (!Has(b, "ears") && y >= 60 && Chance() < PerDays(0.008 * AgeRate, dt)) b.Conds.Add(new Cond { Kind = "ears", Since = Age });

                // A stroke: often fatal, else it leaves a weak side.
                if (Chance() < PerDays(0.25 * old, dt))
                {
                    if (Chance() < 0.5 - 0.3 * care) death = "a stroke";
                    else if (!Has(b, "stroke")) { b.Conds.Add(new Cond { Kind = "stroke", Since = Age }); HealthNews(b, "stroke"); }
                }
                // A weak heart gives out now and then; each attack survived makes the next likelier.
                if (death == null && Has(b, "heart") && Chance() < PerDays(0.12 * AgeRate * (1 + Math.Max(0, y - 60) / 30) * (1 + b.Attacks), dt))
                {
                    if (Chance() < 0.5 - 0.3 * care) death = "a heart attack";
                    else { b.Attacks++; b.Blood = Math.Min(b.Blood, 0.6); HealthNews(b, "heart"); }
                }
                // A growth swells until it kills, unless it is found in time and cut out.
                var gr = b.Conds.FirstOrDefault(c => c.Kind == "growth");
                if (death == null && gr != null)
                {
                    double before = gr.Sev;
                    gr.Sev += (0.08 + 0.06 * Trait01(w.Id, 301)) * AgeRate * (1 - 0.4 * care) * dt;
                    if (before < 0.35 && gr.Sev >= 0.35)
                    {
                        if (care >= 0.2 && Chance() < 0.3 + 0.6 * care)
                        {
                            b.Conds.Remove(gr);
                            Wound(b, "surgery", gr.Part, 0.45, $"surgery for a growth in the {PartOf(gr.Part).Name}");
                            HealthNews(b, "surgery", PartOf(gr.Part).Name);
                        }
                        else HealthNews(b, "growth", PartOf(gr.Part).Name);
                    }
                    else if (gr.Sev >= 1) { death = $"a growth in the {PartOf(gr.Part).Name}"; killer = gr; }
                }
                // Falls: rare, worse for the frail and the lame.
                if (death == null && Chance() < PerDays(0.012 * AgeRate * (Has(b, "frail") ? 3 : 1) * (Limps(b) ? 1.5 : 1), dt))
                {
                    var c = Wound(b, Chance() < 0.4 + (Has(b, "frail") ? 0.3 : 0) ? "break" : "crush", Pick(new[] { "legl", "legr", "arml", "armr", "head", "torso" }), 0.15 + 0.6 * Chance() * (Has(b, "frail") ? 1.4 : 1), "a fall");
                    if (c.Sev >= 1 && PartOf(c.Part).Vital) { death = $"a fall: {HowLine(c)}"; killer = c; }
                    else HealthNews(b, "fall", $"{AOrAn(HurtOf(c.Kind)!.Noun)} {On(c.Kind)} the {PartOf(c.Part).Name}");
                }
            }

            w.Period = (Trait01(w.Id, 1) < 0.5 ? 2 : 3) + (Limps(b) ? 1 : 0);
            w.Hurt = death == null && Bedridden(b);
            if (death != null) BodyDies(w, b, death, killer);
        }
    }

    // A dot-man walks slower with a limp, a lost leg, a bad back or a stroke.
    static bool Limps(Body b) => b.Conds.Any(c => c.Kind is "back" or "stroke" || (c.Kind is "lost" or "scar" && c.Part is "legl" or "legr"));

    static double PerDays(double rate, double days) => rate <= 0 ? 0 : 1 - Math.Exp(-rate * days);

    // ---------- death ----------

    string NameOf(Body b) => b.Was != "" ? Cap(b.Was) : Leader != null && Walkers.Any(x => x.Id == b.Id && x.Lead) ? LeaderTitle : b.Name; // the leader may already be marked gone

    // Someone who matters dies of a named cause. The leader is succeeded; a former leader is mourned.
    void BodyDies(Walker w, Body b, string cause, Cond? killer)
    {
        int years = (int)YearsOf(b);
        string also = Contributing(b, killer, cause);
        string after = killer != null && killer.Why != "" && Age - killer.Since > DayMs * 0.2
            ? $", {DaysWord((Age - killer.Since) / DayMs)} after {killer.Why}" : "";
        string full = $"{cause}{after}{also}";
        Bodies.Remove(b);
        if (w.Lead)
        {
            bool murdered = killer?.Why == "an attempt on their life";
            Death(w, $"{LeaderTitle}, {years}, dies of {full}.", murdered);
        }
        else
        {
            w.Gone = true;
            Nudge(-0.02, "a former leader died");
            Say($"{NameOf(b)}, {years}, dies of {full}.", false); // former leaders: the crier tells, the chronicle keeps to the sitting one
        }
        Npc("physician", "leader", false, NameOf(b));
    }

    // One complaint that helped them along, if any ("weakened by a weak heart").
    string Contributing(Body b, Cond? killer, string cause)
    {
        var others = b.Conds.Where(c => c != killer && c.Kind is "heart" or "frail" or "growth" or "stroke" or "poison" || (HurtOf(c.Kind) != null && c != killer && c.Sev >= 0.3)).ToList();
        var o = others.FirstOrDefault(c => !cause.Contains(CondName(c)) && !(c.Kind == "heart" && cause.Contains("heart")) && !(c.Kind == "stroke" && cause.Contains("stroke")));
        return o == null ? "" : $", weakened by {CondName(o)}";
    }

    static string DaysWord(double d) => d < 0.5 ? "hours" : d < 1.5 ? "a day" : $"{Math.Round(d)} days";

    // A body-holder in this town dies of something the town already tells (illness, hunger): the cause gets a body.
    public string? BodyCause(Walker w, string cause)
    {
        var b = BodyOf(w);
        if (b == null) return null;
        int years = (int)YearsOf(b);
        string line = $"{NameOf(b)}, {years}, dies of {cause}{Contributing(b, null, cause)}.";
        Bodies.Remove(b);
        return line;
    }

    // ---------- words ----------

    // "a blow to the head", "a stab wound in the liver".
    static string On(string kind) => kind is "crush" or "burn" ? "to" : "in";

    static string AOrAn(string w) => (System.Text.RegularExpressions.Regex.IsMatch(w, "^[aeiou]") ? "an " : "a ") + w;

    // "stabbed through the heart", "shot in the left leg".
    static string HowLine(Cond c)
    {
        var h = HurtOf(c.Kind);
        var p = PartOf(c.Part);
        if (h == null) return CondName(c);
        string through = c.Sev >= 1 && h.Key is "stab" or "bolt" ? "through" : "in";
        return $"{h.How} {through} the {p.Name}";
    }

    static string HowList(List<Cond> hits)
    {
        var parts = hits.GroupBy(h => h.Kind).Select(g =>
        {
            var names = g.Select(h => PartOf(h.Part).Name).Distinct().ToList();
            string where = names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and the " + names[^1];
            return $"{HurtOf(g.Key)!.How} in the {where}";
        });
        return string.Join(", ", parts);
    }

    public static string CondName(Cond c) => c.Kind switch
    {
        "heart" => "a weak heart",
        "growth" => $"a growth in the {PartOf(c.Part).Name}",
        "frail" => "frailty",
        "back" => "a bad back",
        "eyes" => "cataracts",
        "ears" => "poor hearing",
        "poison" => "poison",
        "stroke" => "a weak side from a stroke",
        "lost" => PartOf(c.Part).Lost,
        "scar" => PartOf(c.Part).Bad == "scar" ? $"a scar on the {PartOf(c.Part).Name}" : PartOf(c.Part).Bad,
        _ => HurtOf(c.Kind) is { } h ? $"{AOrAn(h.Noun)} {On(c.Kind)} the {PartOf(c.Part).Name}" : c.Kind,
    };

    // News of a health event: the crier, and the physician's note to the leader.
    void HealthNews(Body b, string ev, string a = "")
    {
        if (Quiet || b.Was != "") return; // only the sitting leader's health is news
        string who = NameOf(b);
        string line = ev switch
        {
            "heart" => $"{who} survives a heart attack.",
            "stroke" => $"{who} survives a stroke, but one side is weak.",
            "growth" => $"The physician finds a growth in the {a} of {who}.",
            "surgery" => $"A growth is cut from the {a} of {who}.",
            "fall" => $"{who} takes a fall: {a}.",
            _ => "",
        };
        if (line == "") return;
        Say(line);   // complaints go to the crier and the physician only; the chronicle keeps the death itself (0.66)
        if (LeaderBody == b) Npc("physician", ev, false, a);
    }

    // ---------- shown in the panels ----------

    // One word for the leader's state.
    public string HealthWord(Body b)
    {
        if (b.Blood < 0.5 || b.Conds.Any(c => c.Infect >= 0 || c.Kind == "poison" || (c.Kind == "growth" && c.Sev >= 0.35))) return "gravely ill";
        if (b.Conds.Any(c => HurtOf(c.Kind) != null)) return "wounded";
        if (Has(b, "heart") || Has(b, "frail") || Has(b, "stroke")) return "ailing";
        return "in good health";
    }

    // One line for a hover tip: "67, ailing: a weak heart, a bad back".
    public string HealthSummary(Body b)
    {
        var shown = b.Conds.Where(c => !(c.Kind == "growth" && c.Sev < 0.35)).Select(CondName).Distinct().ToList();
        return $"{YearsOf(b)}, {HealthWord(b)}{(shown.Count > 0 ? ": " + string.Join(", ", shown) : "")}";
    }

    // Where the people who matter live, for the hover tip on a building.
    public IEnumerable<string> HealthAt(int rank)
    {
        foreach (var b in Bodies)
        {
            var w = Walkers.FirstOrDefault(x => x.Id == b.Id && !x.Gone);
            if (w == null || HomeOf(w) != rank) continue;
            yield return w.Lead ? $"Health of {LeaderTitle}: {HealthSummary(b)}." : $"Home of {b.Was} ({HealthSummary(b)}).";
            if (w.Hurt) yield return w.Lead ? "The leader is in bed, being tended." : "They are in bed, being tended.";
        }
    }

    // Every line of a body's chart, for a tooltip: age, blood, then each complaint.
    public List<string> HealthLines(Body b)
    {
        var l = new List<string> { $"{b.Name}, {YearsOf(b)} years old, {HealthWord(b)}." };
        if (b.Blood < 0.98) l.Add($"Blood: {Math.Round(b.Blood * 100)}%.");
        foreach (var c in b.Conds)
        {
            if (c.Kind == "growth" && c.Sev < 0.35) continue; // not found yet
            string s = Cap(CondName(c));
            if (HurtOf(c.Kind) != null) s += $": {Math.Round(c.Sev * 100)}%, {(c.Infect >= 0 ? "infected" : c.TendAt >= 0 ? "untended" : "bandaged")}";
            if (c.Why != "" && c.Kind is not ("heart" or "frail" or "back" or "eyes" or "ears")) s += $" ({c.Why})";
            l.Add(s + ".");
        }
        if (b.Attacks > 0) l.Add(b.Attacks == 1 ? "Has survived a heart attack." : $"Has survived {b.Attacks} heart attacks.");
        if (l.Count == 1) l.Add("Nothing troubles them.");
        return l;
    }

    // ---------- tests ----------

    // Test only: a fresh leader of a given age, attacked once; run until they die or heal. Returns (died at once, died later, days).
    public (bool now, bool later, string line) TestAttempt()
    {
        var lw = LeaderWalker;
        if (lw == null) return (false, false, "");
        var (line, dead) = Attempt(lw);
        if (dead) return (true, false, line);
        var b = LeaderBody!;
        for (int i = 0; i < 40_000; i++)
        {
            StepHealthOnly();
            if (!Bodies.Contains(b)) { string end = SayLog?.LastOrDefault() ?? ""; return (false, end.Contains("attempt on their life"), line + " / " + end); }
            if (!b.Conds.Any(c => HurtOf(c.Kind) != null || c.Kind == "poison")) break;
        }
        return (false, false, line);
    }

    // Test only: how long someone appointed at a given age lives, from age alone (no politics, no wounds). In days.
    public double TestLifespan(double startYears)
    {
        Bodies.Clear();
        var w = Walkers.First(x => !x.Gone);
        var b = new Body { Id = w.Id, Name = "Test", Born = Age - startYears * YearMs, Was = "former Test" };
        Bodies.Add(b);
        double start = Age;
        for (int i = 0; i < 2_000_000; i++)
        {
            StepHealthOnly();
            if (!Bodies.Contains(b) || w.Gone) { w.Gone = false; return (Age - start) / DayMs; }
        }
        return (Age - start) / DayMs;
    }

    // Test only (--hurt): the leader survives a stabbing and has a weak heart, for a screenshot.
    public void TestHurt()
    {
        var lw = LeaderWalker;
        if (lw == null || Leader == null) return;
        var b = EnsureBody(lw, Leader.Name, false);
        b.Born = Age - 61 * YearMs;
        b.Conds.Add(new Cond { Kind = "heart", Since = Age });
        Wound(b, "stab", "legl", 0.55, "an attempt on their life");
        Wound(b, "stab", "arml", 0.3, "an attempt on their life");
        b.Conds.Add(new Cond { Kind = "scar", Part = "handr", Since = Age, Why = "a fall" });
        lw.Hurt = true;
    }

    // Advance time by one health step with nothing else happening.
    void StepHealthOnly()
    {
        Age += HealthEvery * TickMs;
        TickNo += HealthEvery;
        StepHealth();
    }
}
