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
    public string[] Traits = Array.Empty<string>();
}

// A leader's character. Each number is a nudge on the town while they rule:
// Mood is added to the level mood settles at; Kill, Revolt, Arrest, Flee and Scandal multiply those odds;
// Care slows illness in the sick; Gold is added to each treasury check; Quarantine seals sick houses.
// Harsh is how much harsh rule favours this trait when a leader is chosen (negative: gentle rule does).
record Trait(string Key, string Name, string Blurb, double Mood, double Kill, double Revolt, double Arrest, double Flee,
             double Scandal, double Care, double Gold, bool Quarantine, double Harsh);

static class Traits
{
    public static readonly Trait[] All =
    {
        //  key         name        blurb                                  mood   kill revolt arrest flee scandal care   gold  quar   harsh
        new("able",     "Able",     "runs a tidy town",                    0.03,  1,   1,     1,     1,   0.6,    0.1,   0.3,  false, 0),
        new("idle",     "Idle",     "lets things slide",                  -0.03,  1,   1.1,   0.7,   1,   1.3,   -0.15, -0.3,  false, 0.2),
        new("cruel",    "Cruel",    "rules by fear",                      -0.04,  1.6, 1.3,   1.8,   1.3, 1,      0,     0,    true,  1.5),
        new("merciful", "Merciful", "forgives easily and tends the sick",  0.04,  0.6, 0.8,   0.3,   0.8, 1,      0.2,   0,    false, -1),
        new("honest",   "Honest",   "keeps clean ledgers",                 0.02,  0.8, 1,     1,     1,   0.3,    0,     0,    false, -0.3),
        new("cunning",  "Cunning",  "always has an angle",                 0,     0.7, 1,     1,     1,   2,      0,     0.2,  false, 0.3),
        new("proud",    "Proud",    "wants to be remembered",              0.01,  1.2, 1.2,   1,     1,   1,      0,     0,    false, 0.5),
        new("tolerant", "Tolerant", "welcomes everyone",                   0.02,  1,   0.9,   0.6,   0.5, 1,      0.05,  0,    false, -0.8),
    };

    // Pairs a leader cannot have together.
    static readonly (string, string)[] Clash = { ("able", "idle"), ("cruel", "merciful"), ("honest", "cunning") };

    public static Trait Get(string key) => All.First(t => t.Key == key);
    public static bool Exists(string key) => All.Any(t => t.Key == key);
    public static bool Clashes(string a, string b) => a == b || Clash.Any(p => (p.Item1 == a && p.Item2 == b) || (p.Item1 == b && p.Item2 == a));

    // How likely a trait is under a rule of this harshness.
    public static double Weight(Trait t, double harsh) => Math.Max(0.15, 1 + t.Harsh * (harsh - 0.4) * 2);
}

static class Govs
{
    public static readonly Gov[] All =
    {
        new("elders", "Elders", "council of elders", "Elder", 0.15, 0x8a7a5a, 0, 1, 1, false, 0, 1, 0, 0.6, 0),
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

    // What a successor does, after their name.
    public static readonly Dictionary<string, string> Succeed = new()
    {
        ["elders"] = "becomes the elder", ["commune"] = "speaks for the commune", ["council"] = "is elected chair", ["mayor"] = "is elected mayor",
        ["guild"] = "is elected guildmaster", ["temple"] = "is anointed", ["crown"] = "is crowned", ["junta"] = "takes command", ["party"] = "is named Protector",
    };

    public static readonly Dictionary<string, string[]> FestSay = new()
    {
        ["elders"] = new[] { "Festival on the square.", "Bunting goes up: a job well done.", "Everyone gathers to celebrate." },
        ["commune"] = new[] { "A people's festival on the square.", "The commune throws a feast for all." },
        ["council"] = new[] { "The council declares a holiday.", "Bunting goes up: a job well done." },
        ["mayor"] = new[] { "The mayor opens a street party.", "Everyone gathers to celebrate." },
        ["guild"] = new[] { "A trade fair on the square.", "Merchants put on a fair." },
        ["temple"] = new[] { "A holy day: the square fills.", "A procession to the square." },
        ["crown"] = new[] { "The Sovereign throws a feast.", "A royal feast on the square." },
        ["junta"] = new[] { "A parade on the square.", "Troops march round the square." },
        ["party"] = new[] { "A rally on the square. Attendance is required.", "The Party holds a rally." },
    };

    public static readonly string[] First =
    {
        "Ada", "Bram", "Cora", "Dell", "Edda", "Finn", "Greta", "Hob", "Ivo", "Juno", "Kit", "Lark", "Mabel", "Ned", "Odo", "Pim",
        "Quill", "Rosa", "Sim", "Tilda", "Ulf", "Vera", "Wynn", "Yara", "Zeb",
    };
    static readonly string[] Roman = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
    public static string RomanOf(int n) => n < Roman.Length ? Roman[n] : n.ToString();
}
