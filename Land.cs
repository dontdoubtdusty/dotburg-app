namespace Dotburg;

// One piece of land: a tile in the road grid that nobody builds on. K indexes LandKinds.
public record LandTile(int Tx, int Ty, int K);

// Land (0.82): each new town has 3 to 5 tiles of grove, pond, rocks or marsh. A feature tile stays a tile: the roads
// round it are unchanged and growth skips it. Each kind helps and hurts through systems that already exist
// (fires, deaths, illness, works, mood). Old saves have none.
partial class Sim
{
    public const int Grove = 0, PondK = 1, Rocks = 2, Marsh = 3;
    public static readonly string[] LandKinds = { "grove", "pond", "rocks", "marsh" };
    static readonly string[] LandTitles = { "Grove", "Pond", "Rocks", "Marsh" };

    // How likely each kind is per scheme (meadow, harbour, autumn, lavender, snow, pine, desert, cherry).
    static readonly double[][] LandLean =
    {
        new[] { 1.2, 0.6, 1.4, 1.0, 0.6, 2.2, 0.3, 1.3 },   // grove
        new[] { 1.2, 1.4, 1.0, 1.2, 0.8, 1.0, 0.5, 1.2 },   // pond
        new[] { 0.8, 1.0, 0.8, 0.8, 2.0, 1.2, 2.2, 0.6 },   // rocks
        new[] { 0.6, 1.6, 0.8, 1.0, 0.3, 0.8, 0.1, 0.6 },   // marsh
    };
    const double LandJoin = 0.4;   // chance a new feature joins one already placed (same kind, next door)

    public List<LandTile> Land = new();
    public readonly Dictionary<string, int> LandTally = new();   // soak only: how often each land effect happened
    void Tally(string what) => LandTally[what] = LandTally.GetValueOrDefault(what) + 1;
    int[] landAt = Array.Empty<int>();   // per tile: kind + 1, or 0

    // Pick this town's land: 3 to 5 tiles anywhere but the centre, leaning by scheme; some join into bigger areas.
    public void NewLand()
    {
        int cols = (W - 2) / TileW, rows = (H - 2) / TileH;
        Land = new();
        if (cols < 3 || rows < 3) return;
        int n = RandInt(3, 5);
        var taken = new HashSet<(int, int)> { (cols / 2, rows / 2) };   // the first lot (the seat) is never land
        int tries = 0;
        while (Land.Count < n && tries++ < 200)
        {
            int tx, ty, k;
            if (Land.Count > 0 && Chance() < LandJoin)
            {
                var from = Pick(Land);
                var (dx, dy) = Pick(new[] { (1, 0), (-1, 0), (0, 1), (0, -1) });
                (tx, ty, k) = (from.Tx + dx, from.Ty + dy, from.K);
            }
            else
            {
                (tx, ty) = (RandInt(0, cols - 1), RandInt(0, rows - 1));
                k = Weighted(new[] { 0, 1, 2, 3 }, i => LandLean[i][Scheme]);
            }
            if (tx < 0 || ty < 0 || tx >= cols || ty >= rows || !taken.Add((tx, ty))) continue;
            Land.Add(new LandTile(tx, ty, k));
        }
    }

    // Index the land by tile; called from Build before the lots are laid.
    void IndexLand()
    {
        landAt = new int[Math.Max(1, Cols * RowsT)];
        foreach (var l in Land)
            if (l.Tx >= 0 && l.Ty >= 0 && l.Tx < Cols && l.Ty < RowsT) landAt[l.Ty * Cols + l.Tx] = l.K + 1;
    }

    // The kind of land on tile (tx, ty), or -1.
    int LandKindAt(int tx, int ty) => tx < 0 || ty < 0 || tx >= Cols || ty >= RowsT || landAt.Length < Cols * RowsT ? -1 : landAt[ty * Cols + tx] - 1;
    bool IsLand(int tx, int ty) => LandKindAt(tx, ty) >= 0;
    public int LandCount(int k) => Land.Count(l => l.K == k);

    // Does the lot share a road with land of kind k?
    bool LandBy(int rank, int k)
    {
        if (rank < 0 || rank >= Lots.Count) return false;
        var (tx, ty) = Lots[rank];
        return LandKindAt(tx - 1, ty) == k || LandKindAt(tx + 1, ty) == k || LandKindAt(tx, ty - 1) == k || LandKindAt(tx, ty + 1) == k;
    }

    // Built lots on the other sides of the groves next to rank (a fire in a dry summer runs through the trees).
    IEnumerable<int> AcrossGrove(int rank)
    {
        int v = Math.Min(Built.Count, Lots.Count);
        var (tx, ty) = Lots[rank];
        var groves = Land.Where(l => l.K == Grove && Math.Abs(l.Tx - tx) + Math.Abs(l.Ty - ty) == 1).ToList();
        if (groves.Count == 0) yield break;
        for (int r = 0; r < v; r++)
        {
            if (r == rank) continue;
            var (ax, ay) = Lots[r];
            if (Math.Abs(ax - tx) + Math.Abs(ay - ty) == 1) continue;   // a plain neighbour already
            if (groves.Any(g => Math.Abs(g.Tx - ax) + Math.Abs(g.Ty - ay) == 1)) yield return r;
        }
    }

    // Fire season for the trees: summer, or two dry days running.
    bool GroveDry => SeasonNo == 1 || dryDays >= 2;

    // Stone from the rocks speeds every building's works (10% less time per rocks tile, down to 70%).
    double StoneK => Math.Max(0.7, 1 - 0.1 * LandCount(Rocks));

    // ---------- painting ----------

    // Paint the land over the static picture, before the lamps go in. Called from PaintStatic.
    void PaintLand(int[] grass, Season se)
    {
        if (Land.Count == 0) return;
        bool winter = se.Name == "winter", autumn = se.Name == "autumn";
        bool pine = Scheme == 5;
        int[] crown = winter && !pine ? new[] { 0x6a5a48, 0x584a3c } : pine ? new[] { 0x2f5a3a, 0x3a6a44 } : se.Tree;
        int rockBase = Mix(0x8a847c, Sc.Road[0], 0.25);
        int reed = winter ? 0xb0aa90 : autumn ? 0x9a8a4a : 0x4a7a34;
        int reed2 = winter ? 0xc4bea4 : autumn ? 0xb09a58 : 0x5e8c3e;

        // The kind under a town pixel: a tile's inside, or the road between two tiles of the same kind ('-' for none).
        int KindAtPx(int x, int y)
        {
            int X = x - Ox, Y = y - Oy;
            if (X < 0 || Y < 0) return -1;
            int tx = X / TileW, ty = Y / TileH, lx = X % TileW, ly = Y % TileH;
            if (tx >= Cols || ty >= RowsT) return -1;
            if (lx >= 2 && ly >= 2) return LandKindAt(tx, ty);
            if (lx < 2 && ly >= 2) { int a = LandKindAt(tx - 1, ty), b = LandKindAt(tx, ty); return a >= 0 && a == b ? a + 10 : -1; }
            if (ly < 2 && lx >= 2) { int a = LandKindAt(tx, ty - 1), b = LandKindAt(tx, ty); return a >= 0 && a == b ? a + 10 : -1; }
            return -1;   // crossings stay road
        }
        char Blobby(int x, int y, int k) => KindAtPx(x, y) == k ? 'x' : '\0';

        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int k = KindAtPx(x, y);
                if (k < 0) continue;
                int i = y * W + x;
                int h = Hash(x, y);
                int c; byte kd = 0;
                if (k >= 10)
                {
                    // The road between two tiles of one kind: a boardwalk over water, a forest path, a stony track.
                    k -= 10;
                    int X = x - Ox, Y = y - Oy;
                    bool across = X % TileW < 2;   // a strip between left and right tiles: planks run across it
                    if (k is PondK or Marsh) c = (across ? y : x) % 2 == 0 ? 0x8a6a46 : 0x765a3a;
                    else if (k == Grove) c = h < 20 ? Mix(Sc.Road[0], crown[h & 1], 0.5) : Mix(Sc.Road[0], 0x4a3a28, 0.35);
                    else c = h < 30 ? Mix(rockBase, 0xffffff, 0.15) : Sc.Road[h & 1];
                    Ground[i] = c; Kind[i] = 2; Flags[i] = 0; WinRank[i] = -1;
                    continue;
                }
                int sx = (x - Ox) % TileW - 2, sy = (y - Oy) % TileH - 2;   // 0..6, 0..5 inside the tile
                switch (k)
                {
                    case PondK:
                    {
                        // A round pond; the side facing another pond tile runs straight on under the boardwalk.
                        int tx = (x - Ox) / TileW, ty = (y - Oy) / TileH;
                        double dx = (sx - 3) / 3.6, dy = (sy - 2.5) / 3.1;
                        if ((sx < 3 && LandKindAt(tx - 1, ty) == PondK) || (sx > 3 && LandKindAt(tx + 1, ty) == PondK)) dx = 0;
                        if ((sy < 3 && LandKindAt(tx, ty - 1) == PondK) || (sy > 2 && LandKindAt(tx, ty + 1) == PondK)) dy = 0;
                        double r = dx * dx + dy * dy;
                        if (r >= 0.8) { c = !winter && h < 40 ? (h < 20 ? reed : reed2) : grass[h < 30 ? 1 : 0]; kd = 1; break; }   // the bank
                        if (winter) { c = h < 8 ? 0x8aa4b8 : r > 0.4 ? 0xc8d8e2 : 0xb4c8d6; kd = 0; break; }
                        c = r > 0.4 ? 0x4f86b0 : 0x3d6a94;
                        if (Hash(x + 3, y * 2) < 7) c = 0x9cc8e4;   // a glint
                        else if (r < 0.5 && Hash(x + 21, y + 8) < 5) c = 0x5a9a4a;   // a lily pad
                        c = Mix(c, grass[0], 0.08);
                        break;
                    }
                    case Marsh:
                    {
                        if (LowNoise(x, y) < 0.45) { c = winter ? (h < 20 ? 0x98aab0 : 0xb0c0c4) : h < 12 ? 0x5a7a70 : 0x48685e; kd = 0; }
                        else
                        {
                            c = h < 45 ? reed : reed2;
                            if (!winter && Hash(x + 9, y + 4) < 9) c = 0x5a3a24;   // a bulrush head
                            kd = 1;
                        }
                        break;
                    }
                    case Grove:
                        // Shaded ground under the trees; the trees themselves are drawn below.
                        c = Mix(grass[h < 30 ? 1 : 0], 0x203018, winter ? 0.15 : 0.35); kd = 1;
                        break;
                    default:   // rocks
                    {
                        var b = Blob(x, y, 4, 'x', 3, 2.6, (bx, by) => Blobby(bx, by, Rocks));
                        if (b == null)
                        {
                            // Grass between the boulders, in shadow right under one.
                            bool under = Blob(x, y - 1, 4, 'x', 3, 2.6, (bx, by) => Blobby(bx, by, Rocks)) != null;
                            c = under ? Mix(grass[0], 0x000000, 0.35) : grass[h < 30 ? 1 : 0]; kd = (byte)(under ? 0 : 1);
                            break;
                        }
                        var (dx, dy, id) = b.Value;
                        int stone = Mix(rockBase, id % 3 == 0 ? 0xa09080 : 0x707880, 0.25);
                        c = dy < -1.0 ? Mix(stone, 0xffffff, 0.3) : dy > 0.9 ? Mix(stone, 0x000000, 0.35) : dx < -0.8 ? Mix(stone, 0xffffff, 0.12) : stone;
                        if (dy < -1.0) kd = 3;
                        else if (!winter && Hash(x + 30, y + 3) < 4) c = grass[1];   // moss
                        break;
                    }
                }
                Ground[i] = c; Kind[i] = kd; Flags[i] = 0; WinRank[i] = -1;
            }

        // Trees in each grove, back to front: round crowns lit from the top left, or conifers in pine and snow towns.
        bool conifer = pine || Scheme == 4;
        int[] cone = { 0x2f5a3a, 0x3a6a44 };
        foreach (var l in Land)
        {
            if (l.K != Grove) continue;
            int bx = Ox + l.Tx * TileW + 2, by = Oy + l.Ty * TileH + 2;
            var at = Hash(l.Tx + 3, l.Ty + 5) % 2 == 0
                ? new[] { (1, 0), (5, 0), (3, 1), (1, 3), (5, 3), (3, 3) }
                : new[] { (2, 0), (5, 1), (1, 2), (4, 3), (2, 3), (6, 3) };
            foreach (var (cx, cy) in at)
            {
                int id = Hash(l.Tx * 7 + cx, l.Ty * 5 + cy);
                int leaf = conifer ? cone[id & 1] : crown[id & 1];
                int lit = Mix(leaf, winter ? 0xffffff : 0xfff0c0, winter ? 0.6 : 0.22), dark = Mix(leaf, 0x000000, 0.32);
                void P(int sx, int sy, int c, byte kd)
                {
                    if (sx < 0 || sx > 6 || sy < 0 || sy > 5) return;
                    int i = (by + sy) * W + bx + sx;
                    if (i < 0 || i >= W * H) return;
                    Ground[i] = c; Kind[i] = kd;
                }
                if (conifer)
                {
                    P(cx, cy, lit, 3);
                    P(cx - 1, cy + 1, leaf, 0); P(cx, cy + 1, leaf, 0); P(cx + 1, cy + 1, dark, 0);
                }
                else
                {
                    P(cx - 1, cy, lit, 3); P(cx, cy, lit, 3); P(cx + 1, cy, leaf, 3);
                    P(cx - 1, cy + 1, leaf, 0); P(cx, cy + 1, leaf, 0); P(cx + 1, cy + 1, dark, 0);
                }
                P(cx, cy + 2, 0x5a4030, 0);   // the trunk
            }
        }
    }

    // Smooth noise 0..1 on a 3-pixel grid, so edges wobble rather than fizz.
    static double LowNoise(int x, int y)
    {
        int cx = FloorDiv(x, 3), cy = FloorDiv(y, 3);
        double fx = (x - cx * 3) / 3.0, fy = (y - cy * 3) / 3.0;
        double N(int a, int b) => Hash(a + 50, b + 77) / 96.0;
        double top = N(cx, cy) * (1 - fx) + N(cx + 1, cy) * fx;
        double bot = N(cx, cy + 1) * (1 - fx) + N(cx + 1, cy + 1) * fx;
        return top * (1 - fy) + bot * fy;
    }

    // The nearest round blob (one per s-pixel cell, centre jittered) whose centre sits on land k:
    // the pixel's offset from that centre and the blob's id, or null if no blob covers it.
    static (double dx, double dy, int id)? Blob(int x, int y, int s, char k, int salt, double r, Func<int, int, char> land)
    {
        int cx0 = FloorDiv(x, s), cy0 = FloorDiv(y, s);
        (double, double, int)? best = null;
        double bd = r * r;
        for (int cy = cy0 - 1; cy <= cy0 + 1; cy++)
            for (int cx = cx0 - 1; cx <= cx0 + 1; cx++)
            {
                double px = cx * s + Hash(cx + salt + 40, cy * 3 + salt + 40) / 96.0 * (s - 1);
                double py = cy * s + Hash(cx * 5 + salt + 40, cy + salt + 40) / 96.0 * (s - 1);
                if (land((int)Math.Round(px), (int)Math.Round(py)) != k) continue;
                double dx = x - px, dy = y - py, d = dx * dx + dy * dy * 1.2;
                if (d < bd) { bd = d; best = (dx, dy, Hash(cx + 7, cy + 13)); }
            }
        return best;
    }

    static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

    // ---------- moving life on the land ----------

    // Ducks on the ponds by day, a heron in the marsh, fireflies over groves and marshes on fair summer nights,
    // and the trees catching when a fire burns next to a dry grove. Called from Frame.
    void DrawLand(double d, double nq)
    {
        int idx = 0;
        foreach (var l in Land)
        {
            idx++;
            int bx = Ox + l.Tx * TileW + 2, by = Oy + l.Ty * TileH + 2;
            bool fair = rainAmt < 0.2 && snowAmt < 0.2 && cover < 0.3;
            if (l.K == PondK && d > 0.3 && SeasonNo != 3 && snowAmt < 0.3)
                for (int k = 0; k < 2; k++)
                {
                    int slot = (int)((TickNo + idx * 29 + k * 53) / 50);
                    Blend(bx + 1 + Hash(idx * 7 + k, slot) % 5, by + 1 + Hash(idx * 9 + k, slot + 3) % 4, Tint(0xf2eedc, nq, 0), 1);
                }
            if (l.K == Marsh && d > 0.3 && SeasonNo != 3 && idx % 2 == 1)
            {
                int slot = (int)((TickNo + idx * 41) / 160);
                int hx = bx + 1 + Hash(idx * 3, slot) % 5, hy = by + 1 + Hash(idx * 5, slot) % 3;
                Blend(hx, hy, Tint(0xc8ccd0, nq, 0), 1);       // the heron
                Blend(hx, hy + 1, Tint(0x8a8e92, nq, 0), 1);
            }
            if (l.K is Grove or Marsh && nq >= 0.5 && SeasonNo == 1 && fair)
                for (int k = 0; k < 3; k++)
                {
                    if ((TickNo + k * 5 + idx) % 12 >= 8) continue;
                    int slot = (int)((TickNo + k * 7) / 12);
                    Blend(bx + Hash(idx * 11 + k, slot) % 7, by + Hash(idx * 13 + k, slot + 1) % 6, 0xe8f27a, 0.85);
                }
            // Flames in the trees beside a big fire in a dry spell.
            if (l.K == Grove && GroveDry && Fires.Any(f => f.Size > 0.4 && f.Rank < Lots.Count && Math.Abs(Lots[f.Rank].tx - l.Tx) + Math.Abs(Lots[f.Rank].ty - l.Ty) == 1))
            {
                int slot = (int)(TickNo >> 1);
                for (int k = 0; k < 6; k++)
                {
                    double h = Trait01(idx * 31 + k, slot);
                    Blend(bx + (int)(Trait01(idx * 17 + k, slot + 5) * 7), by + 5 - (int)(h * 5), h > 0.5 ? 0xffb030 : 0xe04010, 0.85);
                }
            }
        }
    }

    // ---------- hover ----------

    // The land tile under a town pixel (an index into Land), or -1.
    public int LandAtPixel(int px, int py)
    {
        int X = px - Ox, Y = py - Oy;
        if (X < 0 || Y < 0 || X >= Cols * TileW || Y >= RowsT * TileH) return -1;
        if (X % TileW < 2 || Y % TileH < 2) return -1;
        int tx = X / TileW, ty = Y / TileH;
        return Land.FindIndex(l => l.Tx == tx && l.Ty == ty);
    }

    public (int x, int y, int w, int h) LandBox(int i) => (Ox + Land[i].Tx * TileW + 2, Oy + Land[i].Ty * TileH + 2, TileW - 2, TileH - 2);

    // Title and lines for a land tile: what anyone in town knows about it.
    public (string title, List<string> lines) LandInfo(int i)
    {
        var l = Land[i];
        var lines = new List<string>();
        bool winter = SeasonNo == 3;
        switch (l.K)
        {
            case Grove:
                lines.Add(winter ? "Bare trees, and a path through them." : "A stand of trees; the town walks here in spring and summer.");
                lines.Add("Fireflies on fair summer nights.");
                lines.Add(GroveDry ? "Dry now: a fire next door could run through it." : "In a dry summer, a fire next door can run through the trees.");
                break;
            case PondK:
                lines.Add(winter ? "Frozen over. The ice is thinner than it looks." : "Ducks by day.");
                lines.Add("Buckets for any fire next door.");
                if (!winter) lines.Add("In winter it freezes, and not always thick enough.");
                break;
            case Rocks:
                lines.Add("Stone for every new building: the works go quicker.");
                lines.Add("The young climb them. Now and then someone falls.");
                break;
            case Marsh:
                lines.Add("Reeds, frogs, a heron.");
                lines.Add("The damp ground slows a fire next door.");
                lines.Add(SeasonNo == 1 ? "Warm weather: the fever comes off the wet ground." : "Marsh fever starts here in the warm months.");
                break;
        }
        int size = Land.Count(o => o.K == l.K && Math.Abs(o.Tx - l.Tx) + Math.Abs(o.Ty - l.Ty) == 1);
        if (size > 0) lines.Add($"Part of a larger {LandKinds[l.K]}.");
        return (LandTitles[l.K], lines);
    }
}
