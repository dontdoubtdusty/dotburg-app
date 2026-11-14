namespace Dotburg;

// Composing a frame of pixels: the cached scenery, then everything that moves.
partial class Sim
{
    public int[] Out = new int[W * H];                 // the finished frame, 0xRRGGBB per pixel
    readonly int[] baseImg = new int[W * H];           // the lit scenery, rebuilt only when baseKey changes
    string baseKey = "";
    readonly byte[] litNow = new byte[MaxLots];

    // Does the building on lot `rank` have its lights on at day phase p?
    bool LightsOn(int rank, double p)
    {
        string t = Built[rank];
        if (Content.AllNight.Contains(t)) return true;
        double h = Hash(rank, 3) / 97.0;
        double q = (p + 0.5) % 1; // 0 at noon, 0.5 at midnight
        double bed = Content.Early.Contains(t) ? 0.3 + h * 0.06 : 0.36 + h * 0.17;
        double wake = t == "bakery" ? 0.6 : 0.67 + h * 0.08;
        return !(q >= bed && q < wake);
    }

    byte[] lampNow = Array.Empty<byte>();

    // Is street lamp n lit at day phase p? On from dusk; most go out around midnight, the ones by an
    // all-night building or the seat burn till morning.
    bool LampOn(int n, double p)
    {
        if (LampLate[n]) return true;
        double q = (p + 0.5) % 1; // 0 at noon, 0.5 at midnight
        double outAt = 0.46 + Hash(n + 11, 5) / 97.0 * 0.1;
        return !(q >= outAt && q < 0.75);
    }

    // The scenery as lit and weathered now: snow, puddles, night, grey sky, fog, lights.
    void ComposeBase(double nq, double warm, double gq, double fq, double wq, double cq)
    {
        int snowCol = 0xeef2f6;
        int pudCol = Mix(Sc.Road[0], 0x46658a, 0.65);
        int fogCol = Tint(0xd2d6da, nq, 0);
        double warmE = warm * (1 - gq);
        for (int i = 0; i < baseImg.Length; i++)
        {
            int c = Ground[i];
            byte k = Kind[i];
            if (cq > 0 && k > 0) c = Mix(c, snowCol, cq * (k == 1 ? 0.75 : k == 3 ? 0.55 : 0.35));
            if (wq > 0 && k == 4) c = Mix(c, pudCol, wq * 0.6);
            c = Tint(c, nq, warmE);
            if (gq > 0) c = Gloomy(c, gq);
            if (fq > 0) c = Mix(c, fogCol, fq * fogW[i]);
            byte f = Flags[i];
            if (f == 1)
            {
                int r = WinRank[i];
                if (r >= 0 && litNow[r] != 0) c = Mix(c, 0xffd770, nq);
            }
            else if (f == 2 && lampNow[LampNo[i]] != 0) c = Mix(c, 0xffe8a0, nq * 0.95);
            else if (f == 3 && lampNow[LampNo[i]] != 0) c = Mix(c, 0xffcf80, nq * 0.28);
            baseImg[i] = c;
        }
    }

    // Blend colour c into the frame pixel at (x, y) by a, if it is on the picture.
    void Blend(int x, int y, int c, double a)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return;
        int i = y * W + x;
        Out[i] = Mix(Out[i], c, a);
    }

    // Draw a dot-man: a body pixel with a shadow below and an optional cap pixel above.
    void Dot(int px, int py, int c, int cap)
    {
        int x = Ox + px, y = Oy + py;
        if (x < 0 || x >= W || y < 0 || y + 1 >= H) return;
        int s = (y + 1) * W + x;
        Out[s] = Mix(Out[s], 0x000000, 0.28);
        Out[y * W + x] = c;
        if (cap >= 0 && y > 0) Out[(y - 1) * W + x] = cap;
    }

    // Food you can see: goods in the shop and bakery windows (dark when bare), sacks stacked by the mill and at the
    // field gate as the stores rise and fall, and under a rationing rule a guard at every counter while people panic.
    void DrawFoodSigns(int v, double d, double nq, double warm)
    {
        int bread = GoodColors[(int)Good.Bread], greens = GoodColors[(int)Good.Greens], grain = GoodColors[(int)Good.Grain];
        int bare = 0x3a3632;
        bool day = d > 0.25;
        var works = new HashSet<int>(WorksList.Select(w => w.Rank));
        int pileN = 0;
        for (int r = 0; r < v; r++)
        {
            if (works.Contains(r) || Burning(r)) continue;
            string t = Built[r];
            int bx = Ox + Lots[r].tx * TileW + 2, by = Oy + Lots[r].ty * TileH + 2;
            void Px(int sx, int sy, int c) => Blend(bx + sx, by + sy, Tint(c, nq, warm), 1);
            if (day && litNow[r] == 0)
            {
                if (IsSeller(t))
                {
                    // Left window bread (or porridge grain before the mill), right window greens.
                    double loaf = Stock[r, (int)Good.Bread] + Stock[r, (int)Good.Grain];
                    Px(1, 4, loaf >= 1 ? (Stock[r, (int)Good.Bread] >= 1 ? bread : grain) : bare);
                    Px(5, 4, Stock[r, (int)Good.Greens] >= 1 ? greens : bare);
                    if (t == "market")
                    {
                        Px(1, 3, loaf >= 10 ? bread : bare);
                        Px(5, 3, Stock[r, (int)Good.Greens] >= 10 ? greens : bare);
                    }
                }
                else if (t == "bakery")
                {
                    Px(2, 4, Stock[r, (int)Good.Bread] >= 1 ? bread : bare);
                    Px(4, 4, Stock[r, (int)Good.Bread] >= 12 ? bread : bare);
                }
            }
            // The mill's granary spills over into sacks either side of the door.
            if (t == "mill" && pileN++ == 0)
            {
                int n = (int)Math.Min(6, Math.Ceiling(Stock[r, (int)Good.Grain] / 60));
                (int, int)[] spots = { (0, 5), (6, 5), (0, 4), (6, 4), (0, 3), (6, 3) };
                for (int k = 0; k < n; k++) Px(spots[k].Item1, spots[k].Item2, k % 2 == 0 ? grain : Mix(grain, 0x8a6a20, 0.3));
            }
            // A guard at the counter door while food is rationed.
            if (day && Panic > 0.5 && FoodRule.hoard == 0 && G.Guards > 0 && SellsFood(r))
            {
                var (dx, dy) = DoorOf(r);
                Dot(dx + 1, dy, Tint(0x2a2e36, nq * 0.5, 0), Tint(G.Banner, nq * 0.7, 0));
            }
        }
        // The barn at the fields: stooks and sacks by the west gate, a green crate when there are greens.
        var (ex, ey) = Entrance();
        int gx = Ox + ex, gy = Oy + ey;
        int sacks = (int)Math.Min(6, Math.Ceiling(Barn[(int)Good.Grain] / 80));
        (int, int)[] gate = { (-1, -1), (-2, -1), (-1, 1), (-2, 1), (-2, -2), (-1, 2) };
        for (int k = 0; k < sacks; k++) Blend(gx + gate[k].Item1, gy + gate[k].Item2, Tint(k % 2 == 0 ? grain : Mix(grain, 0x8a6a20, 0.3), nq, warm), 1);
        if (Barn[(int)Good.Greens] >= 3) Blend(gx - 3, gy - 1, Tint(greens, nq, warm), 1);
    }

    // Compose a frame of pixels into Out.
    public void Frame()
    {
        double d = Daylight();
        double n = 1 - Clamp((d - 0.15) / 0.6, 0, 1);
        double nq = Math.Round(n * 24, MidpointRounding.AwayFromZero) / 24;
        double warm = Clamp(1 - Math.Abs(d - 0.5) * 3, 0, 1) * 0.8;
        double p = Phase;
        int v = Math.Min(Built.Count, Lots.Count);
        var litKey = new char[v];
        for (int r = 0; r < v; r++)
        {
            litNow[r] = (byte)(nq > 0 && LightsOn(r, p) ? 1 : 0);
            litKey[r] = litNow[r] == 1 ? '1' : '0';
        }
        if (lampNow.Length != LampLate.Count) lampNow = new byte[LampLate.Count];
        var lampKey = new char[LampLate.Count];
        for (int k = 0; k < lampNow.Length; k++)
        {
            lampNow[k] = (byte)(nq > 0 && LampOn(k, p) ? 1 : 0);
            lampKey[k] = lampNow[k] == 1 ? '1' : '0';
        }
        double gq = Math.Round(gloom * 10) / 10, fq = Math.Round(fogAmt * 8) / 8;
        double wq = Math.Round(wet * 6) / 6, cq = Math.Round(cover * 8) / 8;
        string key = $"{StaticVer}|{nq}|{warm}|{gq}|{fq}|{wq}|{cq}|{new string(litKey)}|{new string(lampKey)}";
        if (key != baseKey)
        {
            baseKey = key;
            ComposeBase(nq, warm, gq, fq, wq, cq);
        }
        Array.Copy(baseImg, Out, baseImg.Length);
        int lx(int r) => Ox + Lots[r].tx * TileW;
        int ly(int r) => Oy + Lots[r].ty * TileH;

        // Ducks on the park ponds by day; fireflies over them on fair summer nights.
        for (int r = 0, parks = 0; r < v && parks < 6; r++)
        {
            if (Built[r] != "park" || WorksList.Any(w => w.Rank == r) || Pond.Count == 0) continue;
            parks++;
            if (d > 0.3 && snowAmt < 0.3 && SeasonNo != 3)
            {
                int first = -1;
                for (int k = 0; k < 2; k++)
                {
                    int slot = (int)((TickNo + r * 29 + k * 53) / 50);
                    int idx = Hash(r * 7 + k, slot) % Pond.Count;
                    if (idx == first) idx = (idx + 1) % Pond.Count;
                    first = idx;
                    Blend(lx(r) + 2 + Pond[idx].x, ly(r) + 2 + Pond[idx].y, Tint(0xf2eedc, nq, 0), 1);
                }
            }
            if (nq >= 0.5 && SeasonNo == 1 && rainAmt < 0.2 && snowAmt < 0.2 && cover < 0.3)
                for (int k = 0; k < 3; k++)
                {
                    if ((TickNo + k * 5) % 12 >= 8) continue;
                    int slot = (int)((TickNo + k * 7) / 12);
                    Blend(lx(r) + 2 + Hash(r * 11 + k, slot) % 7, ly(r) + 2 + Hash(r * 13 + k, slot + 1) % 6, 0xe8f27a, 0.85);
                }
        }

        DrawLand(d, nq);   // ducks, heron, fireflies, a burning grove (Land.cs)

        // Chimney smoke, fading as it rises and spreading in its second half.
        int smokeCol = Tint(0xf2f2f6, nq * 0.8, 0);
        foreach (var f in puffs)
        {
            double a = 0.3 + 0.6 * (1 - (double)f.T / f.Life);
            int pc = f.Dark ? Tint(0x4a4644, nq * 0.6, 0) : smokeCol;
            Blend(f.X, f.Y, pc, f.Dark ? Math.Min(0.9, a + 0.15) : a);
            if (f.T > f.Life / 2) Blend(f.X + 1, f.Y, pc, a * 0.6);
        }

        // Fires: a warm glow round the lot (strong at night), then flames licking up through the roof.
        foreach (var fr in Fires)
        {
            if (fr.Rank >= Lots.Count) continue;
            int bx = lx(fr.Rank) + 2, by = ly(fr.Rank) + 2;
            double glow = (0.08 + 0.2 * fr.Size) * (0.5 + nq);
            for (int y = by - 2; y < by + TileH; y++)
                for (int x = bx - 2; x < bx + TileW; x++) Blend(x, y, 0xff8a30, glow);
            int slot = (int)(TickNo >> 1);
            int flames = 4 + (int)(fr.Size * 16);
            for (int k = 0; k < flames; k++)
            {
                int fx = bx + (int)(Trait01(fr.Rank * 31 + k, slot) * (TileW - 2));
                double h = Trait01(fr.Rank * 17 + k, slot + 9);
                int fy = by + 5 - (int)(h * (2 + fr.Size * 6));
                int col = h > 0.66 ? 0xffe060 : h > 0.33 ? 0xff9020 : 0xe04010;
                Blend(fx, fy, col, 0.92);
                if (h > 0.5 && fr.Size > 0.5) Blend(fx, fy - 1, 0xffd040, 0.7);
            }
        }

        // Bunting round the festival lot during a festival, the colours stepping along.
        if (Festival && v > 0)
        {
            int fl = festLot < Lots.Count ? festLot : 0;
            int x0 = lx(fl) + 1, x1 = lx(fl) + TileW + 1, y0 = ly(fl) + 1, y1 = ly(fl) + TileH + 1;
            int shift = (int)(TickNo >> 3), k = 0;
            void Flag(int x, int y)
            {
                if (k % 3 != 2) Blend(x, y, Tint(festColors[(k + shift) % 4], nq * 0.6, 0), 1);
                k++;
            }
            for (int x = x0; x <= x1; x++) Flag(x, y0);
            for (int y = y0 + 1; y <= y1; y++) Flag(x1, y);
            for (int x = x1 - 1; x >= x0; x--) Flag(x, y1);
            for (int y = y1 - 1; y > y0; y--) Flag(x0, y);
        }

        // A builder in a hi-vis vest pacing in front of each scaffold.
        foreach (var wk in WorksList)
        {
            if (wk.Rank >= Lots.Count) continue;
            int s = (int)(((TickNo >> 2) + wk.Rank * 3) % 12);
            int bx = Ox + Lots[wk.Rank].tx * TileW + 2 + (s < 6 ? s : 12 - s);
            int by = Oy + Lots[wk.Rank].ty * TileH + TileH - 1 + 2 - 2;
            Blend(bx, by, Tint(0xf08a24, nq, 0), 1);
            if (((TickNo >> 1) % 4) == 0) Blend(bx, by - 1, 0xfff4c0, 0.5); // a hammer glint
        }

        DrawFoodSigns(v, d, nq, warm);

        // Dot-men, each with a shadow; at a festival they dance a little.
        for (int j = 0; j < Walkers.Count; j++)
        {
            var w = Walkers[j];
            if (w.Mode == Mode.Inside || w.Gone) continue;
            int dx = Festival && !Rally && w.Goal == Goal.Fest && w.Mode == Mode.Wait ? (int)(((TickNo >> 2) + j) & 1) : 0;
            int c = Content.DotColors[w.C];
            if (w.Sick) c = Mix(c, SickTint(w), 0.5);
            else if (w.Kid != 0) c = Mix(c, 0xffffff, 0.4);
            if (w.Food < WeakAt) c = Mix(c, 0xc8c0b0, 0.45); // the starving look washed out
            Dot(w.X + dx, w.Y, Tint(c, nq, warm), w.Lead ? Tint(0xf2d050, nq * 0.5, 0) : -1);
            // A carrier has a sack beside them in the colour of what is in it (grain gold, flour cream, bread brown, greens green).
            if (w.Good >= 0 && w.Load > 0) Blend(Ox + w.X + dx + 1, Oy + w.Y, Tint(GoodColors[w.Good], nq, warm), 1);
            // A hoarder walks home with an armful of bread.
            else if (w.Bag > 0 && w.Mode == Mode.Walk) Blend(Ox + w.X + dx + 1, Oy + w.Y, Tint(GoodColors[(int)Good.Bread], nq, warm), 1);
            // Someone beating a fire carries a bucket: a blue pixel that splashes.
            if (w.Goal == Goal.Fight && w.Mode != Mode.Inside) Blend(Ox + w.X + dx + 1, Oy + w.Y - 1, (TickNo >> 1) % 2 == 0 ? 0x78b8ff : 0xc8e4ff, 1);
        }
        // Guards: dark with a cap in the rule's banner colour.
        int capCol = Tint(G.Banner, nq * 0.7, 0);
        foreach (var g in guards) Dot(g.X, g.Y, Tint(0x2a2e36, nq * 0.5, 0), capCol);
        // Visitors: envoys, teams and refugees in their town's colour, a spy in dark with a red cap;
        // a cart is a horse and a wagon.
        foreach (var vi in Visitors)
        {
            if (vi.Mode == Mode.Inside || vi.Gone) continue;
            if (vi.Kind == "cart")
            {
                int dir = vi.Lane == 1 ? 1 : -1;
                Blend(Ox + vi.X, Oy + vi.Y + 1, 0x000000, 0.2);
                Blend(Ox + vi.X - dir, Oy + vi.Y + 1, 0x000000, 0.2);
                Blend(Ox + vi.X, Oy + vi.Y, Tint(0x6a4a2a, nq, warm), 1);       // horse
                Blend(Ox + vi.X - dir, Oy + vi.Y, Tint(vi.Good >= 0 ? GoodColors[vi.Good] : 0xc8a46a, nq, warm), 1); // wagon
                if (vi.Good >= 0) Blend(Ox + vi.X - dir, Oy + vi.Y - 1, Tint(GoodColors[vi.Good], nq, warm), 1); // piled with grain sacks
                continue;
            }
            int vc = vi.Kind is "envoy" or "team" or "refugee" ? Content.DotColors[vi.C]
                : VisitorColors.TryGetValue(vi.Kind, out var col) ? col : 0x888888;
            Dot(vi.X, vi.Y, Tint(vc, nq, warm), VisitorHead.TryGetValue(vi.Kind, out var hd) ? Tint(hd, nq, 0) : -1);
        }

        // The cat: a body and a head pixel facing where it walks; hidden in bad weather.
        if (cat != null && !Stormy)
        {
            int cc = Tint(CatColors[Founded % CatColors.Length], nq, warm);
            int x = Ox + cat.X, y = Oy + cat.Y;
            Blend(x, y + 1, 0x000000, 0.2);
            Blend(x, y, cc, 1);
            Blend(x + catDir, y, Mix(cc, 0xffffff, 0.15), 1);
        }

        // Rain streaks with a splash, or drifting snowflakes.
        if (drops.Count > 0)
        {
            bool snow = snowAmt > rainAmt;
            int col = Tint(snow ? 0xf6f8fa : 0xc8d8f0, nq * 0.6, 0);
            foreach (var q in drops)
            {
                if (snow) Blend(q.X, q.Y, col, 0.85);
                else if (q.T <= q.Life)
                {
                    Blend(q.X, q.Y, col, 0.6);
                    Blend(q.X + 1, q.Y - 1, col, 0.3);
                }
                else
                {
                    Blend(q.X, q.Y, col, 0.35);
                    if (q.T == q.Life + 1) { Blend(q.X - 1, q.Y, col, 0.2); Blend(q.X + 1, q.Y, col, 0.2); }
                }
            }
        }

        // Northern lights over cold towns on clear winter nights.
        if (nq >= 0.6 && SeasonNo == 3 && (Scheme == 4 || Scheme == 5) && Weather == Wx.Clear)
        {
            int rows = Math.Min(10, H >> 2);
            double t = TickNo >> 2;
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < W; x++)
                {
                    double sv = Math.Sin(x / 6.0 + t / 9 + y * 0.35) + Math.Sin(x / 11.0 - t / 13);
                    if (sv > 0.9) Blend(x, y, sv > 1.5 ? 0x9a6ad8 : 0x4fe0a0, (sv - 0.9) * 0.35 * (1 - (double)y / rows));
                }
        }

        // A lightning flash whitens everything for a tick or two.
        if (flash > 0) for (int i = 0; i < Out.Length; i++) Out[i] = Mix(Out[i], 0xf4f6ff, 0.45);

        // Birds in a V, with faint shadows far below them.
        if (flock != null)
        {
            int flap = (int)((TickNo >> 2) & 1);
            var pts = new List<(int x, int y)> { (flock.X, flock.Y) };
            for (int k = 1; k <= (flock.N - 1) / 2; k++)
            {
                int dy = k - (flap == 1 && k == 1 ? 1 : 0);
                pts.Add((flock.X - k, flock.Y - dy));
                pts.Add((flock.X - k, flock.Y + dy));
            }
            foreach (var (x, y) in pts) Blend(x + 3, y + 4, 0x000000, 0.15);
            int bc = Tint(0x2a2a34, nq, 0);
            foreach (var (x, y) in pts) Blend(x, y, bc, 1);
        }
    }
}
