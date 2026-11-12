namespace Dotburg;

// A town's whole palette, and the colour index of its native dot-men.
record Scheme(int[] Grass, int[] Road, int[][] Roofs, int Wall, int Dot);

// One season: how it tints the grass, the tree colours, and its crier line.
record Season(string Name, int Grass, double Amt, int[] Tree, string Say);

// Everything the town is made of: sprites, palettes, names. No state lives here.
static class Content
{
    // What gets built, in order: a few of each kind early, then mostly houses.
    public static readonly string[] Open = { "house", "house", "well", "house", "shop", "house", "park", "house", "bakery", "house", "inn", "house", "church", "house", "mill" };
    public static readonly string[] Loop = { "house", "house", "park", "house", "shop", "house", "well", "house", "bakery", "house" };
    public static string TypeAt(int k) => k < Open.Length ? Open[k] : Loop[(k - Open.Length) % Loop.Length];

    // 7 wide, 6 tall, the bottom row touching the road below. '.' shows the grass.
    // R and r are the scheme's roof colours, W its walls.
    public static readonly Dictionary<string, string[]> Sprites = new()
    {
        ["house"] = new[] { ".......", ".rrrrr.", ".RRRRR.", ".RRRRR.", ".WwWwW.", ".WWdWW." },
        ["house2"] = new[] { "..rrr..", ".RRRRR.", ".WwWwW.", ".WWWWW.", ".WwWwW.", ".WWdWW." },
        ["row"] = new[] { "rrrrrrr", "RRRRRRR", "WwWwWwW", "WWWWWWW", "WwWwWwW", "WdWWWdW" },
        ["shop"] = new[] { ".......", "ttttttt", "tTTTTTt", "AaAaAaA", "WwWdWwW", "WWWWWWW" },
        ["market"] = new[] { "ttttttt", "AaAaAaA", "aAaAaAa", "WwWWWwW", "WwWdWwW", "WWWWWWW" },
        ["bakery"] = new[] { "....k..", ".ooooo.", "ooOOOoo", ".ooooo.", ".WwWwW.", ".WWdWW." },
        ["inn"] = new[] { ".bbbbb.", "bBBBBBb", "bBBBBBb", "WwWcWwW", "WwWdWwW", "WWWWWWW" },
        ["hotel"] = new[] { "bbbbbbb", "BBBBBBB", "WwWwWwW", "WwWcWwW", "WwWwWwW", "WWWdWWW" },
        ["church"] = new[] { "...c...", "..sss..", "..sws..", ".sssss.", ".swsws.", ".sssds." },
        ["cathedral"] = new[] { ".c...c.", ".s.c.s.", ".sssss.", "sswswss", "sssssss", "swsdsws" },
        ["well"] = new[] { ".......", ".......", "..sss..", "..sus..", "..sss..", "......." },
        ["fountain"] = new[] { ".......", "...U...", "..sUs..", ".suuus.", ".sssss.", "......." },
        ["park"] = new[] { "..GG...", ".GGGG.G", "..PPPgG", ".PPPP..", "g.PP.gG", ".g...g." },
        ["hall"] = new[] { "...c...", "..ttt..", ".tTTTt.", "tTTTTTt", "sWsWsWs", "sWsdsWs" },
        ["school"] = new[] { ".kc....", ".k.....", "RRRRRRR", "WwWWWwW", "WwWdWwW", "WWWWWWW" },
        ["tower"] = new[] { "...c...", "..sss..", "..sCs..", "..sss..", "..sws..", "..sds.." },
        ["mill0"] = new[] { "x.....x", ".x...x.", "..xmx..", "..mmm..", ".mwmwm.", ".mmdmm." },
        ["mill1"] = new[] { "...x...", "...x...", "xxxmxxx", "..mmm..", ".mwmwm.", ".mmdmm." },
        ["ruin"] = new[] { ".......", ".q..q..", "qQqqQq.", ".qQqqQq", "QqqQqqQ", ".q.Qq.q" },
    };

    // Scaffolding over a lot while something is built or upgraded.
    public static readonly string[] Scaffold = { ".j.j.j.", "JJJJJJJ", ".j.j.j.", "JJJJJJJ", ".j...j.", ".j...j." };

    // Buildings whose lights stay on all night, and ones that close early.
    public static readonly HashSet<string> AllNight = new() { "inn", "hotel", "tower", "hall", "cathedral" };
    public static readonly HashSet<string> Early = new() { "shop", "market", "school", "mill", "bakery", "church", "well", "fountain", "park" };
    // Buildings you stand beside rather than walk into.
    public static readonly HashSet<string> Outdoor = new() { "park", "well", "fountain" };
    // Where night owls go.
    public static readonly HashSet<string> Social = new() { "inn", "hotel", "park", "fountain", "market" };
    // Dot-men each kind of home holds.
    public static readonly Dictionary<string, int> HomeCap = new() { ["house"] = 3, ["house2"] = 4, ["row"] = 6 };
    public static bool IsHome(string t) => HomeCap.ContainsKey(t);

    // How the legend names a building.
    static readonly Dictionary<string, string> Labels = new() { ["house2"] = "two-storey house", ["row"] = "row houses", ["hall"] = "town hall", ["tower"] = "clock tower" };
    public static string Label(string t) => Labels.TryGetValue(t, out var l) ? l : t;

    public static readonly Dictionary<char, int> Pal = new()
    {
        ['w'] = 0x6b8aa8, ['d'] = 0x6b4a32,
        ['t'] = 0xb89a6b, ['T'] = 0xd0b585, ['A'] = 0xc44a4a, ['a'] = 0xf0e8d8,
        ['o'] = 0xd98a3a, ['O'] = 0xe6a45a, ['k'] = 0x5a4a44,
        ['b'] = 0x4f5f8f, ['B'] = 0x6f80b0, ['c'] = 0xe0c050,
        ['s'] = 0xa8a8a8, ['u'] = 0x4f8fc0, ['U'] = 0xa8d8f0, ['C'] = 0xf5f0e0, ['x'] = 0xd8c8a0, ['m'] = 0xb8a890,
        ['G'] = 0x3f7a3f, ['g'] = 0x5a9a4a, ['P'] = 0x4f8fc0, ['j'] = 0x7a5636, ['J'] = 0xb8925e, ['q'] = 0x3a3230, ['Q'] = 0x8a8078,
    };

    public static readonly int[] DotColors = { 0xd9432f, 0x2f62c9, 0xf2c14e, 0x8e44ad, 0xf5f5f5, 0x2b2b2b, 0xe67e22, 0x16a085 };
    public static readonly string[] DotNames = { "red", "blue", "yellow", "purple", "white", "black", "orange", "teal" };

    public static readonly Scheme[] Schemes =
    {
        new(new[] { 0x7fae5a, 0x78a655 }, new[] { 0xcdbd9b, 0xc4b390 }, new[] { new[] { 0xc4503f, 0x9b3b2f }, new[] { 0x6a89b0, 0x4f6b8f }, new[] { 0x6aa07a, 0x4d7a5a } }, 0xe8dcc0, 0), // meadow, red
        new(new[] { 0x6f9e86, 0x67957e }, new[] { 0xb8b5ad, 0xaba79e }, new[] { new[] { 0x3f6f8f, 0x2f5570 }, new[] { 0x5aa0a0, 0x407c7c }, new[] { 0xd8d4c8, 0xb0aca0 } }, 0xe2e6e8, 1), // harbour, blue
        new(new[] { 0xa8a04e, 0x9e9646 }, new[] { 0x9a7a52, 0x8f704a }, new[] { new[] { 0xc8642a, 0xa04c1e }, new[] { 0x8a4a2a, 0x6a361e }, new[] { 0x9a3a3a, 0x7a2a2a } }, 0xeadbb8, 2), // autumn, yellow
        new(new[] { 0x8aa66a, 0x829e62 }, new[] { 0xd0c3c8, 0xc6b8be }, new[] { new[] { 0x8a5aa8, 0x6c4488 }, new[] { 0xb07ab8, 0x8f5e98 }, new[] { 0x5a5a8a, 0x46466c } }, 0xf0e6ee, 3), // lavender, purple
        new(new[] { 0xe6ecee, 0xd8e0e4 }, new[] { 0xa8a098, 0x9c948c }, new[] { new[] { 0x8a2f2f, 0x6a2222 }, new[] { 0x2f5f45, 0x224834 }, new[] { 0x4a5560, 0x384048 } }, 0xc9b48f, 5), // snow, black
        new(new[] { 0x4f7a4a, 0x487244 }, new[] { 0x7d6e58, 0x736450 }, new[] { new[] { 0x7a4a2a, 0x5c381f }, new[] { 0xa8452f, 0x843424 }, new[] { 0x6a7078, 0x52575e } }, 0xd8c8a8, 4), // pine, white
        new(new[] { 0xd9c48e, 0xd0ba84 }, new[] { 0xb08f62, 0xa5855a }, new[] { new[] { 0xc0663a, 0x9c4f2c }, new[] { 0x2f8f8a, 0x22706c }, new[] { 0xd8a040, 0xb08030 } }, 0xf2e6c8, 7), // desert, teal
        new(new[] { 0x86b46a, 0x7eac62 }, new[] { 0xd8c8b0, 0xcebda4 }, new[] { new[] { 0xe08aa0, 0xc06a80 }, new[] { 0xc04a5a, 0x9c3646 }, new[] { 0x8a6aa0, 0x6c5080 } }, 0xf6ece6, 6), // cherry, orange
    };

    public static readonly Season[] Seasons =
    {
        new("spring", 0x8ccf6a, 0.15, new[] { 0x4f8a3f, 0xf0b4c8 }, "Spring comes to"),
        new("summer", 0xa8b84a, 0.12, new[] { 0x3f7a3f, 0x4a8a3a }, "Summer warms"),
        new("autumn", 0xb89a4a, 0.30, new[] { 0xc0702a, 0xa8502a }, "Autumn turns the leaves in"),
        new("winter", 0x9aa49c, 0.35, new[] { 0x6a5a48, 0x5a4a3c }, "Winter settles on"),
    };

    static readonly string[] Pre = { "Ash", "Mill", "Oak", "Brook", "Stone", "Wren", "Elder", "Fen", "Hollow", "Thorn", "Bram", "Larch", "Cold", "Marsh", "Pebble", "Kettle", "Apple", "Heron" };
    static readonly string[] Suf = { "bury", "ford", "field", "wick", "ton", "stead", "dale", "mere", "by", "haven", "well", "combe" };

    // A town name that is not already in use.
    public static string TownName(Random rng, ICollection<string> used, int founded)
    {
        for (int i = 0; i < 50; i++)
        {
            var n = Pre[rng.Next(Pre.Length)] + Suf[rng.Next(Suf.Length)];
            if (!used.Contains(n)) return n;
        }
        return "Dotburg " + founded;
    }
}
