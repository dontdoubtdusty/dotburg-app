namespace Dotburg;

// How a town may be ruled, from gentle to harsh. Harsh drives assassination odds, emigration and tone;
// Mood is the level the town's mood settles at under this rule; Lean is how often towns pick it.
record Gov(string Key, string Name, string Lower, string Title, double Harsh, int Banner, int Term, double CostMul,
           double Welcome, bool Curfew, int Guards, double Fest, int HomeBonus, double Mood, double Lean);

// The town's leader: a name, a regnal number (monarchs only), when they took office, and two traits.
class Leader
{
    public string Name = "";
    public int Gen = 1;
    public double Since;
    public double Born;            // sister rulers only (EvClock ms); our leader's age lives in their Body
    public string[] Traits = Array.Empty<string>();
}

// A dot-man's character (leaders and, since 0.68, every citizen). Each number is a nudge on the town while they rule:
// Mood is added to the level mood settles at; Kill, Revolt, Arrest, Flee and Scandal multiply those odds;
// Care slows illness in the sick; Gold is added to each treasury check; Quarantine seals sick houses.
// Harsh is how much harsh rule favours this trait when a leader is chosen (negative: gentle rule does).
// Cat is the group: 0 heart (how they treat others), 1 work (how they get things done), 2 spirit (temper and outlook),
// 3 wildcard (rare, for drama). Everyone has one of each of the first three, and a few have a wildcard too.
record Trait(string Key, string Name, string Blurb, double Mood, double Kill, double Revolt, double Arrest, double Flee,
             double Scandal, double Care, double Gold, bool Quarantine, double Harsh, int Cat);

static class Traits
{
    public static readonly Trait[] All =
    {
        //  key            name             blurb                                       mood   kill revolt arrest flee scandal care   gold  quar   harsh cat
        new("cruel",       "Cruel",         "rules by fear",                           -0.04,  1.6, 1.3,   1.8,   1.3, 1,      0,     0,    true,  1.5,  0),
        new("merciful",    "Merciful",      "forgives easily and tends the sick",       0.04,  0.6, 0.8,   0.3,   0.8, 1,      0.2,   0,    false, -1,   0),
        new("proud",       "Proud",         "wants to be remembered",                   0.01,  1.2, 1.2,   1,     1,   1,      0,     0,    false, 0.5,  0),
        new("tolerant",    "Tolerant",      "welcomes everyone",                        0.02,  1,   0.9,   0.6,   0.5, 1,      0.05,  0,    false, -0.8, 0),
        new("humble",      "Humble",        "asks nothing for themself",                0.02,  0.8, 0.9,   1,     1,   0.7,    0,     0,    false, -0.5, 0),
        new("jealous",     "Jealous",       "cannot bear another's good fortune",       -0.02,  1.1, 1.1,   1.4,   1.1, 1,      0,     0,    false, 0.6,  0),
        new("loyal",       "Loyal",         "stands by their friends",                  0.01,  0.9, 0.9,   1,     1,   1,      0,     0,    false, 0,    0),
        new("spiteful",    "Spiteful",      "never lets a slight go",                  -0.03,  1.3, 1.2,   1.5,   1.1, 1,      0,     0,    false, 0.8,  0),
        new("generous",    "Generous",      "gives more than they keep",                0.03,  0.8, 0.9,   0.8,   0.9, 1,      0.05, -0.2,  false, -0.6, 0),

        new("able",        "Able",          "runs a tidy town",                         0.03,  1,   1,     1,     1,   0.6,    0.1,   0.3,  false, 0,    1),
        new("idle",        "Idle",          "lets things slide",                       -0.03,  1,   1.1,   0.7,   1,   1.3,   -0.15, -0.3,  false, 0.2,  1),
        new("honest",      "Honest",        "keeps clean ledgers",                      0.02,  0.8, 1,     1,     1,   0.3,    0,     0,    false, -0.3, 1),
        new("cunning",     "Cunning",       "always has an angle",                      0,     0.7, 1,     1,     1,   2,      0,     0.2,  false, 0.3,  1),
        new("greedy",      "Greedy",        "always wants more",                       -0.03,  1.2, 1.2,   1,     1.1, 1.6,   -0.05,  0.4,  false, 0.5,  1),
        new("thrifty",     "Thrifty",       "counts every crumb",                       0,     1,   1,     1,     1,   0.8,    0,     0.2,  false, 0,    1),
        new("reckless",    "Reckless",      "acts first, thinks later",                -0.01,  1.2, 1.1,   1,     1,   1.2,   -0.05, -0.1,  false, 0.3,  1),
        new("careful",     "Careful",       "measures twice",                           0.01,  0.8, 1,     1,     1,   0.8,    0.05,  0.1,  false, -0.1, 1),
        new("ambitious",   "Ambitious",     "climbs over anyone in the way",           -0.01,  1.2, 1.1,   1.2,   1,   1.3,    0,     0.2,  false, 0.6,  1),

        new("brave",       "Brave",         "stands firm when it matters",              0.01,  0.9, 1,     1,     0.9, 1,      0,     0,    false, 0.1,  2),
        new("craven",      "Craven",        "hides when trouble comes",                -0.02,  1.1, 1.1,   1.2,   1.1, 1,      0,     0,    false, 0.2,  2),
        new("pious",       "Pious",         "lives by the old prayers",                 0.01,  1,   0.9,   1,     1,   0.8,    0.1,   0,    false, 0.2,  2),
        new("cynical",     "Cynical",       "believes nothing they are told",           0,     1,   1,     1,     1,   1.2,    0,     0,    false, 0,    2),
        new("cheerful",    "Cheerful",      "always finds the bright side",             0.03,  0.9, 0.9,   1,     0.9, 1,      0,     0,    false, -0.4, 2),
        new("gloomy",      "Gloomy",        "expects the worst",                       -0.02,  1,   1,     1,     1.1, 1,      0,     0,    false, 0.1,  2),
        new("hotheaded",   "Hot-headed",    "quick to anger",                          -0.02,  1.2, 1.2,   1.3,   1,   1,      0,     0,    false, 0.6,  2),
        new("calm",        "Calm",          "slow to anger",                            0.01,  0.9, 0.9,   0.9,   1,   1,      0,     0,    false, -0.3, 2),
        new("curious",     "Curious",       "wonders what lies past the hills",         0,     1,   1,     1,     1.1, 1,      0.05,  0.1,  false, -0.2, 2),

        new("firebug",     "Firebug",       "loves to watch things burn",              -0.03,  1.2, 1.2,   1.2,   1.2, 1.2,    0,     0,    false, 0.4,  3),
        new("vengeful",    "Vengeful",      "remembers every wrong, forever",          -0.03,  1.4, 1.2,   1.6,   1.1, 1,      0,     0,    false, 0.8,  3),
        new("doomsayer",   "Doomsayer",     "sees the end of the world in every cloud", -0.04, 1,   1.1,   1,     1.4, 1,      0,     0,    false, 0.3,  3),
        new("lightfinger", "Light-fingered", "takes what is not theirs",               -0.02,  1.1, 1.1,   1,     1,   2.5,    0,     0.3,  false, 0.2,  3),
        new("charmer",     "Charmer",       "wins anyone over in a moment",             0.04,  0.6, 0.8,   1,     0.9, 1.4,    0,     0.1,  false, 0,    3),
    };

    public const double WildChance = 0.06;   // share of dot-men (and leaders) with a wildcard

    // Pairs one dot-man cannot have together (the groups already keep opposites like cruel and merciful apart).
    static readonly (string, string)[] Clash = { ("able", "idle"), ("cruel", "merciful"), ("honest", "cunning"),
        ("greedy", "generous"), ("humble", "ambitious"), ("honest", "lightfinger"), ("merciful", "vengeful"), ("calm", "firebug") };

    public static Trait Get(string key) => All.First(t => t.Key == key);
    public static bool Exists(string key) => All.Any(t => t.Key == key);
    public static bool Clashes(string a, string b) => a == b || Clash.Any(p => (p.Item1 == a && p.Item2 == b) || (p.Item1 == b && p.Item2 == a));
    public static Trait[] InCat(int cat) => All.Where(t => t.Cat == cat).ToArray();

    // How likely a trait is under a rule of this harshness.
    public static double Weight(Trait t, double harsh) => Math.Max(0.15, 1 + t.Harsh * (harsh - 0.4) * 2);
}

static class Govs
{
    public static readonly Gov[] All =
    {
        // Before any rule (0.71): fewer than Sim.RuleAt grown dot-men, no leader, no politics.
        new("none", "Settlers", "settlers", "Settler", 0, 0x9a8a6a, 0, 1, 1, false, 0, 1, 0, 0.6, 0),
        new("elders", "Elders", "council of elders", "Elder", 0.15, 0x8a7a5a, 0, 1, 1, false, 0, 1, 0, 0.6, 0),
        new("headman", "Headman", "headman", "Headman", 0.3, 0x7a4a32, 0, 1, 0.9, false, 0, 0.9, 0, 0.55, 0),
        new("founders", "Founding family", "founding family", "Founder", 0.2, 0x5a6a9a, 0, 1, 1, false, 0, 1, 1, 0.6, 0),
        new("shrine", "Shrine keepers", "shrine keepers", "Keeper", 0.1, 0xd8c890, 0, 1, 1.1, false, 0, 1.2, 0, 0.62, 0),
        new("commune", "Commune", "commune", "Speaker", 0, 0x4f9a5a, 2, 1.1, 1.3, false, 0, 1.5, 1, 0.7, 1),
        new("council", "Council", "council", "Chair", 0.1, 0x3f6fb0, 3, 1, 1.5, false, 0, 1.2, 0, 0.65, 1.2),
        new("mayor", "Mayoralty", "mayor", "Mayor", 0.25, 0x2f8f8a, 4, 0.9, 1.2, false, 0, 1, 0, 0.6, 1.2),
        new("guild", "Merchant guild", "merchant guild", "Guildmaster", 0.4, 0xc8902a, 5, 0.8, 1, false, 0, 0.8, 0, 0.5, 0.9),
        new("temple", "Theocracy", "theocracy", "High priest", 0.5, 0xf0e6c0, 0, 1, 0.8, true, 0, 1.3, 0, 0.55, 0.8),
        new("crown", "Monarchy", "monarchy", "Sovereign", 0.5, 0x8a3aa8, 0, 1, 0.9, false, 0, 1.2, 0, 0.55, 1),
        new("junta", "Junta", "junta", "General", 0.8, 0x5a6a3a, 0, 0.75, 0.5, true, 2, 0.5, 0, 0.3, 0.6),
        new("party", "Party state", "party state", "Protector", 1, 0xb02a2a, 0, 0.7, 0.3, true, 3, 0.4, 0, 0.25, 0.5),
    };
    public static Gov Get(string key) => All.First(g => g.Key == key);

    // The rules a new hamlet can start under. A village outgrows them (MaybeChooseGov); sisters and later changes never pick them.
    public static readonly string[] Hamlet = { "elders", "headman", "founders", "shrine" };
    public static bool IsHamlet(string key) => Hamlet.Contains(key) || key == "none";   // settlers count as a hamlet for every rule check

    // Old habits carry over: what each hamlet rule makes likelier when the village chooses how it will be ruled.
    public static readonly Dictionary<string, Dictionary<string, double>> Grows = new()
    {
        ["elders"] = new() { ["council"] = 1.5, ["temple"] = 1.2 },
        ["headman"] = new() { ["crown"] = 1.6, ["junta"] = 1.4, ["mayor"] = 1.2 },
        ["founders"] = new() { ["crown"] = 1.5, ["guild"] = 1.4 },
        ["shrine"] = new() { ["temple"] = 2, ["commune"] = 1.3 },
    };
    public static bool Exists(string key) => All.Any(g => g.Key == key);

    // Each scheme's leanings (index = scheme): a harbour of merchants, a desert of kings.
    public static readonly Dictionary<string, double>[] Lean =
    {
        new() { ["council"] = 1.5, ["mayor"] = 1.5 },                                   // meadow
        new() { ["guild"] = 2, ["council"] = 1.3 },                                     // harbour
        new() { ["crown"] = 1.8, ["temple"] = 1.4 },                                    // autumn
        new() { ["commune"] = 2, ["temple"] = 1.5 },                                    // lavender
        new() { ["crown"] = 1.5, ["junta"] = 1.8 },                                     // snow
        new() { ["commune"] = 1.5, ["council"] = 1.4 },                                 // pine
        new() { ["crown"] = 1.6, ["temple"] = 1.6, ["party"] = 1.5 },                   // desert
        new() { ["commune"] = 1.6, ["mayor"] = 1.4 },                                   // cherry
    };

    // One plain line on who holds power under each rule, for the government tooltip.
    public static readonly Dictionary<string, string> Blurb = new()
    {
        ["none"] = "Too few to need a rule yet: neighbours just get along.",
        ["elders"] = "The oldest heads decide together, slowly.",
        ["headman"] = "One strong hand settles disputes.",
        ["founders"] = "The family that built the first house runs it.",
        ["shrine"] = "The keepers of the shrine guide the hamlet.",
        ["commune"] = "Everyone shares; a speaker takes a turn and steps down.",
        ["council"] = "An elected council, with a chair to speak for it.",
        ["mayor"] = "A mayor, elected, with an eye on business.",
        ["guild"] = "The merchants run the town like a shop.",
        ["temple"] = "The priests rule in the name of the old prayers.",
        ["crown"] = "A sovereign rules for life; the crown passes to an heir.",
        ["junta"] = "The army rules. Order first, questions never.",
        ["party"] = "The Party is the town. The Protector is the Party.",
    };

    // What a successor does, after their name.
    public static readonly Dictionary<string, string> Succeed = new()
    {
        ["none"] = "is looked to", ["elders"] = "becomes the elder", ["headman"] = "is chosen headman", ["founders"] = "heads the family now", ["shrine"] = "takes up the keeper's lamp",
        ["commune"] = "speaks for the commune", ["council"] = "is elected chair", ["mayor"] = "is elected mayor",
        ["guild"] = "is elected guildmaster", ["temple"] = "is anointed", ["crown"] = "is crowned", ["junta"] = "takes command", ["party"] = "is named Protector",
    };

    public static readonly Dictionary<string, string[]> FestSay = new()
    {
        ["none"] = new[] { "The settlers share a meal outdoors.", "Everyone gathers to celebrate." },
        ["elders"] = new[] { "Festival on the square.", "Bunting goes up: a job well done.", "Everyone gathers to celebrate." },
        ["headman"] = new[] { "The headman calls a feast.", "Everyone gathers to celebrate." },
        ["founders"] = new[] { "The founding family lays a long table on the square.", "Everyone gathers to celebrate." },
        ["shrine"] = new[] { "Lanterns at the shrine: a feast day.", "A small procession to the square." },
        ["commune"] = new[] { "A people's festival on the square.", "The commune throws a feast for all." },
        ["council"] = new[] { "The council declares a holiday.", "Bunting goes up: a job well done." },
        ["mayor"] = new[] { "The mayor opens a street party.", "Everyone gathers to celebrate." },
        ["guild"] = new[] { "A trade fair on the square.", "Merchants put on a fair." },
        ["temple"] = new[] { "A holy day: the square fills.", "A procession to the square." },
        ["crown"] = new[] { "The Sovereign throws a feast.", "A royal feast on the square." },
        ["junta"] = new[] { "A parade on the square.", "Troops march round the square." },
        ["party"] = new[] { "A rally on the square. Attendance is required.", "The Party holds a rally." },
    };

    static readonly string[] Roman = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
    public static string RomanOf(int n) => n < Roman.Length ? Roman[n] : n.ToString();
}
