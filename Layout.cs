namespace Dotburg;

// Roads, lots and the unchanging scenery.
partial class Sim
{
    public int Cols, RowsT, Ox, Oy;
    public List<(int tx, int ty)> Lots = new();
    int[] rankAt = Array.Empty<int>();
    public int[] Ground = new int[W * H];     // scenery colour per pixel
    public byte[] Flags = new byte[W * H];    // 1 window, 2 lamp, 3 lamp glow
    public byte[] Kind = new byte[W * H];     // 1 grass, 2 road, 3 roof, 4 road where puddles form
    public short[] WinRank = new short[W * H];// the building a window pixel belongs to
    public int StaticVer;

    // Lay the roads and lots out. The picture size is fixed, so this is 7 x 7 lots.
    public void Build()
    {
        LayLots();
        ResetLife();
        PlaceAll();
        StaticDirty = true;
        baseKey = "";
    }

    // The grid and the lot order (middle outwards, land tiles left out); used by Build and the charter.
    void LayLots()
    {
        Cols = (W - 2) / TileW;
        RowsT = (H - 2) / TileH;
        if (Cols < 1 || RowsT < 1) { Cols = 0; RowsT = 0; }
        Ox = (W - (Cols * TileW + 2)) / 2;
        Oy = (H - (RowsT * TileH + 2)) / 2;
        IndexLand();
        // Lots fill from the middle outwards; land is never built on.
        var all = new List<(int tx, int ty, double k)>();
        for (int ty = 0; ty < RowsT; ty++)
            for (int tx = 0; tx < Cols; tx++)
            {
                if (IsLand(tx, ty)) continue;
                double dx = tx - (Cols - 1) / 2.0;
                double dy = ty - (RowsT - 1) / 2.0;
                all.Add((tx, ty, dx * dx + dy * dy * 1.3 + Hash(tx, ty) * 0.001));
            }
        Lots = all.OrderBy(l => l.k).Take(MaxLots).Select(l => (l.tx, l.ty)).ToList();
        rankAt = new int[Math.Max(1, Cols * RowsT)];
        Array.Fill(rankAt, 1 << 20);
        for (int i = 0; i < Lots.Count; i++) rankAt[Lots[i].ty * Cols + Lots[i].tx] = i;
    }

    // The door of a building, on the road below it.
    public (int x, int y) DoorOf(int rank) => (Lots[rank].tx * TileW + 5, (Lots[rank].ty + 1) * TileH);
    // The west gate, where newcomers walk in.
    public (int x, int y) Entrance() => (0, RowsT / 2 * TileH);

    // Paint the unchanging picture: grass, roads, buildings, lamps, in the town's scheme and season.
    void PaintStatic()
    {
        var s = Sc;
        int townW = Cols * TileW + 2;
        int townH = RowsT * TileH + 2;
        Array.Clear(Flags);
        Array.Fill(WinRank, (short)-1);
        StaticVer++;
        var scaffolded = new HashSet<int>(WorksList.Select(w => w.Rank));
        var se = SeasonNow;
        var grass = s.Grass.Select(g => Mix(g, se.Grass, se.Amt)).ToArray();
        int Tree(int x, int y) => se.Tree[Hash(x + 7, y + 2) < 30 ? 1 : 0];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                int X = x - Ox, Y = y - Oy;
                int c = grass[Hash(x, y) < 30 ? 1 : 0];
                if (se.Name == "autumn" && Hash(x + 11, y + 17) < 3) c = se.Tree[Hash(x, y + 5) & 1]; // fallen leaves
                byte k = 1;
                if (X >= 0 && Y >= 0 && X < townW && Y < townH)
                {
                    int lx = X % TileW, ly = Y % TileH;
                    bool road = Y >= RowsT * TileH || ly < 2 || X >= Cols * TileW || lx < 2;
                    if (road)
                    {
                        // Dirt with the odd stone in a hamlet; cobbles from Village up.
                        bool odd = Tier >= 2 ? ((x >> 1) + (y >> 1)) % 2 == 0 : (x * 7 + y * 13) % 11 == 0;
                        c = s.Road[odd ? 1 : 0];
                        k = (byte)(Hash(x >> 1, (y >> 1) + 40) < 11 ? 4 : 2);
                    }
                    else
                    {
                        int rank = rankAt[Y / TileH * Cols + X / TileW];
                        if (rank < Built.Count)
                        {
                            string type = Built[rank];
                            var rows = scaffolded.Contains(rank) ? Content.Scaffold : type == "mill" ? Content.Sprites["mill" + MillFrame] : Content.Sprites[type];
                            char ch = rows[ly - 2][lx - 2];
                            if (ch == 'R' || ch == 'r') c = s.Roofs[rank % 3][ch == 'R' ? 0 : 1];
                            else if (ch == 'W') c = s.Wall;
                            else if (ch != '.') c = Content.Pal[ch];
                            if (ch == 'w') { Flags[i] = 1; WinRank[i] = (short)rank; }
                            // Roofs take snow; so does the grass of a lot, but not walls or water.
                            k = (byte)(ch is 'R' or 'r' or 't' or 'T' or 'b' or 'B' or 'o' ? 3 : ch is '.' or 'G' or 'g' ? 1 : 0);
                        }
                        else if (Hash(x + 5, y + 9) < 7) c = Tree(x, y); // a bush on an empty lot
                    }
                }
                else if (Hash(x + 3, y + 1) < 5) c = Tree(x, y); // a tree in the countryside
                Ground[i] = c;
                Kind[i] = k;
            }
        // Paint a pixel of lot rank at sprite position (sx, sy) as plain scenery.
        void Put(int rank, int sx, int sy, int c)
        {
            int x = Ox + Lots[rank].tx * TileW + 2 + sx, y = Oy + Lots[rank].ty * TileH + 2 + sy;
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            int i = y * W + x;
            Ground[i] = c; Kind[i] = 0; Flags[i] = 0; WinRank[i] = -1;
        }
        // Banners on the seat of government (or the leader's home); harsh rule hangs them everywhere.
        int v = Math.Min(Built.Count, Lots.Count);
        var gv = G;
        var lw = LeaderWalker;
        int seat = SeatRank() >= 0 ? SeatRank() : lw != null ? HomeOf(lw) : -1;
        for (int r = 0; r < v; r++)
        {
            if (scaffolded.Contains(r) || Content.Outdoor.Contains(Built[r]) || Built[r] == "ruin") continue;
            if (r != seat && !(gv.Harsh >= 0.8 && r % 3 == 1)) continue;
            Put(r, 0, 0, 0x4a4038);
            Put(r, 0, 1, gv.Banner);
            Put(r, 0, 2, GovKey == "party" ? 0x1e1e1e : gv.Banner);
        }
        // Statues in the parks: remembered leaders, or the Protector in every park.
        int statues = GovKey == "party" ? MaxLots : Honoured;
        for (int r = 0, n = 0; r < v && n < statues; r++)
        {
            if (Built[r] != "park" || scaffolded.Contains(r)) continue;
            Put(r, 5, 3, GovKey == "party" ? 0x6a5a48 : 0xd8d4c8);
            Put(r, 5, 4, 0x7a7a74);
            n++;
        }
        PaintLand(grass, se);   // groves, ponds, rocks and marsh (Land.cs)
        // Street lamps grow with the town: none in a hamlet, the seat's corners in a village, the middle and the
        // public buildings in a town, most crossings in a city (a few always stay dark). Lamps by an all-night
        // building (inn, hall, tower...) burn till dawn; the rest go out around midnight (LampsOn).
        Array.Fill(LampNo, (short)-1);
        LampLate.Clear();
        void Lamp(int x, int y, bool late)
        {
            if (x < 0 || y < 0 || x >= W || y >= H || Flags[y * W + x] == 2) return;
            short no = (short)LampLate.Count;
            LampLate.Add(late);
            Flags[y * W + x] = 2; LampNo[y * W + x] = no;
            foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < W && ny < H && Flags[ny * W + nx] == 0) { Flags[ny * W + nx] = 3; LampNo[ny * W + nx] = no; }
            }
        }
        // The building on tile (tx, ty), or null when off the grid, empty or under scaffolding.
        string? At(int tx, int ty)
        {
            if (tx < 0 || ty < 0 || tx >= Cols || ty >= RowsT) return null;
            int r = rankAt[ty * Cols + tx];
            return r < v && !scaffolded.Contains(r) ? Built[r] : null;
        }
        bool Public(string? t) => t != null && t is not ("house" or "house2" or "row" or "ruin") && !Content.Outdoor.Contains(t);
        int seatR = SeatRank() >= 0 ? SeatRank() : 0;
        (int tx, int ty) seatT = v > 0 ? Lots[seatR] : (0, 0);
        if (Tier >= 2)
            for (int j = 0; j <= RowsT; j++)
                for (int i = 0; i <= Cols; i++)
                {
                    var near = new[] { At(i - 1, j - 1), At(i, j - 1), At(i - 1, j), At(i, j) };
                    if (near.All(t => t == null)) continue;
                    bool bySeat = (i == seatT.tx || i == seatT.tx + 1) && (j == seatT.ty || j == seatT.ty + 1);
                    bool late = near.Any(t => t != null && Content.AllNight.Contains(t));
                    bool pub = near.Any(Public);
                    bool great = near.Any(t => t is "market" or "church" or "cathedral" or "school");
                    double ci = i - Cols / 2.0, cj = j - RowsT / 2.0;
                    bool middle = Math.Abs(ci) <= 1.5 && Math.Abs(cj) <= 1.5;
                    int h = Hash(i + 31, j + 17); // 0..96, fixed per crossing
                    bool on = Tier switch
                    {
                        2 => bySeat || (late && near.Any(t => t == "inn")),
                        3 => bySeat || late || (middle && h < 40) || (great && h < 30),
                        _ => bySeat || late || (middle && h < 75) || (pub && h < 55) || h < 25,
                    };
                    if (on) Lamp(Ox + i * TileW + 1, Oy + j * TileH + 1, late || bySeat);
                    // A city also lights the road in front of its public buildings mid-block.
                    if (Tier >= 4 && i < Cols && (Public(At(i, j - 1)) || Public(At(i, j))))
                        Lamp(Ox + i * TileW + 5, Oy + j * TileH + 1, late);
                }
    }

    public short[] LampNo = new short[W * H];   // the lamp a lamp or glow pixel belongs to
    readonly List<bool> LampLate = new();       // per lamp: burns all night
}
