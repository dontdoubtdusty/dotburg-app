namespace Dotburg;

enum Wx { Clear, Cloudy, Rain, Storm, Fog, Snow }

// A puff of chimney smoke, a rain streak or snowflake, and a flock of birds.
class Puff { public int X, Y, T, Life; public bool Dark; }
class Drop { public int X, Y, X0, T, Life; }
class Flock { public int X, Y, N; }

// Stage 2: weather, chimney smoke, the cat, birds, festivals and bunting.
// Everything here is cosmetic or mood only; none of it is saved (a session starts clear).
partial class Sim
{
    const double WxFadeMs = 12_000;       // how long the sky takes to change over
    const double FestMs = 25_000;         // how long a festival lasts
    static readonly int[] Bunting = { 0xd9432f, 0xf2c14e, 0x2f62c9, 0xf5f5f5 };
    static readonly int[] Sheaves = { 0xe0b040, 0x8a5a2a, 0xf2d68a, 0xc08a30 };   // harvest bunting
    static readonly int[] CatColors = { 0xd8873a, 0x2e2e2e, 0x9a9aa2, 0xf0ece4 };

    // How much each scheme leans toward rain, fog and snow (index = scheme).
    static readonly (double rain, double fog, double snow)[] WxBias =
    {
        (1, 1, 0), (1.3, 2, 0), (1.2, 1.3, 0), (1, 1, 0), (0.2, 1, 1.6), (0.8, 1.2, 0.6), (0.25, 0.1, 0), (1, 0.8, 0),
    };
    // Season weather odds: rain, fog, snow multiplier, extra snow.
    static readonly (double rain, double fog, double snowMul, double snowAdd)[] SeasonWx =
    {
        (1.3, 1, 0.3, 0), (0.7, 0.5, 0.1, 0), (1.3, 1.5, 0.6, 0), (0.6, 1.2, 1.5, 0.8),
    };
    static readonly double[] WxGloom = { 0, 0.55, 0.85, 1, 0.45, 0.6 };
    static readonly string[][] WxSay =
    {
        new[] { "The sky clears.", "The clouds break up." },
        new[] { "Clouds roll in.", "The sky turns grey." },
        new[] { "Rain is falling.", "Rain patters on the roofs." },
        new[] { "Thunder rolls over the hills.", "A storm breaks." },
        new[] { "Fog creeps in.", "A thick fog settles." },
        new[] { "Snow is falling.", "Flakes drift down." },
    };
    // Where smoke leaves each kind of building, relative to the sprite's top-left.
    static readonly Dictionary<string, (int x, int y)> Chimney = new()
    {
        ["house"] = (5, 0), ["house2"] = (5, 0), ["row"] = (1, 0), ["bakery"] = (4, -1), ["inn"] = (6, 0), ["hotel"] = (6, 0), ["school"] = (1, -1),
    };
    // The pond pixels in the park sprite, where ducks swim.
    static readonly List<(int x, int y)> Pond = BuildPond();
    static List<(int, int)> BuildPond()
    {
        var l = new List<(int, int)>();
        var rows = Content.Sprites["park"];
        for (int y = 0; y < rows.Length; y++) for (int x = 0; x < rows[y].Length; x++) if (rows[y][x] == 'P') l.Add((x, y));
        return l;
    }

    // ---------- state ----------
    public Wx Weather = Wx.Clear;
    int flash;                       // ticks of lightning flash left
    double wxClock, wxUntil = 0.15 * DayMs;
    double gloom, fogAmt, rainAmt, snowAmt, wet, cover;
    float[] fogW = Array.Empty<float>();   // fog thickness per pixel, thicker toward the edges
    long festUntil;
    int festLot;                     // the lot the festival gathers round: the square, or a newly finished building
    int[] festColors = Bunting;
    readonly List<Puff> puffs = new();
    readonly List<Drop> drops = new();
    Flock? flock;
    long flockDue;
    Walker? cat;
    int catDir = 1;

    public bool Festival => TickNo < festUntil;
    // Where the festival is, for talk: "the square", or "the new library" when it celebrates a new building.
    public string FestPlace => festLot > 0 && festLot < Built.Count ? $"the new {Content.Label(Built[festLot])}" : "the square";
    bool Rally;                      // the current festival is a compulsory rally
    bool Stormy => rainAmt > 0.5 || snowAmt > 0.5;
    public string WxWord => Weather == Wx.Clear ? (Daylight() > 0.3 ? "sunny" : "clear") : Weather == Wx.Storm ? "thunder" : Weather.ToString().ToLower();

    // Reset everything weather-like and animated (a new layout or a new town).
    void ResetLife()
    {
        fogW = new float[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                double e = Math.Max(Math.Abs(x - W / 2.0) / (W / 2.0), Math.Abs(y - H / 2.0) / (H / 2.0));
                fogW[y * W + x] = (float)(0.12 + 0.4 * e * e);
            }
        drops.Clear();
        puffs.Clear();
        cat = null;
        flock = null;
    }

    // ---------- weather ----------
    // The next sky: each kind leans toward its neighbours, so rain follows cloud.
    Wx NextWeather()
    {
        var s = SeasonWx[SeasonNo];
        var w = WxBias[Scheme];
        double rain = w.rain * s.rain, fog = w.fog * s.fog;
        double snow = w.snow * s.snowMul + (Scheme == 6 ? 0 : s.snowAdd);
        double storm = SeasonNo == 1 ? 0.35 : 0.15;
        (Wx, double)[] t = Weather switch
        {
            Wx.Clear => new[] { (Wx.Clear, 0.35), (Wx.Cloudy, 0.5), (Wx.Fog, 0.15 * fog) },
            Wx.Cloudy => new[] { (Wx.Cloudy, 0.15), (Wx.Clear, 0.35), (Wx.Rain, 0.4 * rain), (Wx.Snow, 0.4 * snow), (Wx.Fog, 0.1 * fog) },
            Wx.Rain => new[] { (Wx.Rain, 0.25), (Wx.Storm, storm), (Wx.Cloudy, 0.6), (Wx.Clear, 0.15) },
            Wx.Storm => new[] { (Wx.Rain, 0.6), (Wx.Storm, 0.2), (Wx.Cloudy, 0.2) },
            Wx.Snow => new[] { (Wx.Snow, 0.4), (Wx.Cloudy, 0.5), (Wx.Clear, 0.1) },
            _ => new[] { (Wx.Clear, 0.6), (Wx.Cloudy, 0.4) },
        };
        double r = Chance() * t.Sum(x => x.Item2);
        foreach (var (k, wt) in t) { r -= wt; if (r < 0) return k; }
        return Wx.Clear;
    }

    // Move a value toward a target at one whole unit per ms.
    static double Ease(double v, double to, double dt, double ms) => v < to ? Math.Min(to, v + dt / ms) : Math.Max(to, v - dt / ms);

    // Advance the weather by dt ms: change the sky when due, fade the amounts.
    void UpdateWeather(double dt)
    {
        wxClock += dt;
        if (wxClock >= wxUntil)
        {
            var n = NextWeather();
            if (n != Weather) { Weather = n; Say(Pick(WxSay[(int)n])); }
            wxUntil = wxClock + DayMs * (n == Wx.Clear ? 0.2 + Chance() * 0.3 : 0.1 + Chance() * 0.2);
        }
        gloom = Ease(gloom, WxGloom[(int)Weather], dt, WxFadeMs);
        fogAmt = Ease(fogAmt, Weather == Wx.Fog ? 1 : 0, dt, WxFadeMs);
        bool raining = Weather == Wx.Rain || Weather == Wx.Storm;
        rainAmt = Ease(rainAmt, Weather == Wx.Storm ? 1.6 : Weather == Wx.Rain ? 1 : 0, dt, WxFadeMs * 0.6);
        snowAmt = Ease(snowAmt, Weather == Wx.Snow ? 1 : 0, dt, WxFadeMs * 0.6);
        wet = raining ? Ease(wet, 1, dt, Weather == Wx.Storm ? 15_000 : 30_000) : Ease(wet, 0, dt, 150_000);
        if (flash > 0) flash--;
        if (Weather == Wx.Storm && rainAmt > 1 && Chance() < dt / 9_000) { flash = 2; Strike(); }
        double melt = SeasonNo == 3 ? 400_000 : Scheme == 4 ? 300_000 : 150_000; // winter snow lies longer
        cover = Weather == Wx.Snow ? Ease(cover, 1, dt, 60_000) : Ease(cover, 0, dt, melt);
    }

    // A colour under a grey sky: less saturated, a little darker and bluer.
    static int Gloomy(int c, double k)
    {
        int r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255;
        double l = r * 0.3 + g * 0.59 + b * 0.11, t = 0.45 * k, s = 1 - 0.2 * k;
        int Ch(double v, double add) => Clamp(Round((v + (l - v) * t) * s + add), 0, 255);
        return (Ch(r, 0) << 16) | (Ch(g, 0) << 8) | Ch(b, 6 * k);
    }

    // Rain streaks and snowflakes: a pool of particles sized by how hard it falls.
    void StepDrops()
    {
        bool snow = snowAmt > rainAmt;
        int want = Round(snow ? W * H * snowAmt / 170 : W * H * rainAmt / 150);
        while (drops.Count > want) drops.RemoveAt(drops.Count - 1);
        while (drops.Count < want) drops.Add(NewDrop(snow, true));
        for (int i = 0; i < drops.Count; i++)
        {
            var p = drops[i];
            p.T++;
            if (snow)
            {
                if ((p.T + i) % 3 == 0) p.Y++;
                p.X = p.X0 + Round(Math.Sin((p.T + i * 7) / 6.0));
                if (p.T >= p.Life || p.Y >= H) drops[i] = NewDrop(true, false);
            }
            else if (p.T <= p.Life)
            {
                p.Y += 2;
                if (p.T % 2 == 0) p.X--;
            }
            else if (p.T > p.Life + 2 || p.Y >= H) drops[i] = NewDrop(false, false);
        }
    }

    // A fresh particle somewhere on the picture; spread starts it partway through its life.
    Drop NewDrop(bool snow, bool spread)
    {
        int x = RandInt(0, W + 4);
        int life = snow ? RandInt(30, 70) : RandInt(2, 5);
        return new Drop { X = x, X0 = x, Y = RandInt(0, H - 1), T = spread ? RandInt(0, life) : 0, Life = life };
    }

    // ---------- chimney smoke ----------
    // Morning and evening smoke from homes; the bakery bakes all day, the inn smokes at night.
    void StepSmoke()
    {
        double p = Phase;
        bool morning = p > 0.2 && p < 0.4, evening = p > 0.68 && p < 0.86, cold = cover > 0.3;
        int v = Math.Min(Built.Count, Lots.Count);
        if (puffs.Count < 16)
            for (int r = 0; r < v; r++)
            {
                string t = Built[r];
                if (!Chimney.TryGetValue(t, out var at) || WorksList.Any(wk => wk.Rank == r)) continue;
                double chance = 0;
                if (Content.IsHome(t)) chance = morning || evening ? 0.01 : cold ? 0.004 : 0;
                else if (t == "bakery") chance = p > 0.12 && p < 0.7 ? 0.02 : 0;
                else if (t == "inn" || t == "hotel") chance = evening || p > 0.86 || p < 0.1 ? 0.008 : 0;
                else if (t == "school") chance = morning ? 0.004 : 0;
                if (Chance() >= chance) continue;
                var l = Lots[r];
                puffs.Add(new Puff { X = Ox + l.tx * TileW + 2 + at.x, Y = Oy + l.ty * TileH + 2 + at.y, Life = RandInt(20, 34) });
            }
        int windy = rainAmt > 0.3 ? 4 : 7;
        foreach (var f in puffs)
        {
            f.T++;
            if (f.T % 5 == 0) f.Y--;
            if (f.T % windy == 0) f.X++;
        }
        puffs.RemoveAll(f => f.T >= f.Life || f.Y < 0 || f.X >= W);
    }

    // ---------- the cat and the birds ----------
    // The town cat: wanders the roads, sits a while, hides from rain and snow.
    void StepCat()
    {
        if (cat == null)
        {
            if (Built.Count < 6 || Lots.Count == 0) return;
            cat = NewWalker(0, 0);
            (cat.X, cat.Y) = DoorOf(0);
            cat.Mode = Mode.Wait;
            cat.Until = TickNo + Secs(2, 6);
            cat.Period = 3;
        }
        if (Stormy) return;
        if (cat.Mode == Mode.Wait)
        {
            if (TickNo >= cat.Until) StrollSomewhere(cat);
            return;
        }
        if (TickNo % cat.Period != 0) return;
        int x = cat.X;
        if (WalkStep(cat))
        {
            cat.Mode = Mode.Wait;
            cat.Until = TickNo + Secs(3, 20);
        }
        else if (cat.X != x) catDir = cat.X > x ? 1 : -1;
    }

    // Birds now and then cross the sky by day in fair weather.
    void StepFlock(double d)
    {
        if (flock == null && TickNo >= flockDue)
        {
            if (d > 0.35 && (Weather == Wx.Clear || Weather == Wx.Cloudy))
                flock = new Flock { X = -4, Y = RandInt(3, Math.Max(3, H - 8)), N = Chance() < 0.5 ? 3 : 5 };
            flockDue = TickNo + Secs(60, 180);
        }
        if (flock == null || TickNo % 2 != 0) return;
        flock.X++;
        if (TickNo % 24 == 0) flock.Y = Clamp(flock.Y + RandInt(-1, 1), 2, H - 6);
        if (flock.X - flock.N > W + 6) flock = null;
    }

    // ---------- festivals ----------
    // A spot on the road just above or below the festival lot, where festivals gather.
    (int x, int y) FestSpot()
    {
        var l = Lots[festLot < Lots.Count ? festLot : 0];
        return (RandInt(l.tx * TileW + 1, (l.tx + 1) * TileW + 1), (Chance() < 0.5 ? l.ty : l.ty + 1) * TileH);
    }

    void JoinFestival(Walker w)
    {
        var (x, y) = FestSpot();
        GoTo(w, x, y, Goal.Fest, -1);
    }

    // A festival on the square (or round lot `at`, for a new building): bunting goes up and most of the awake dot-men gather.
    public void StartFestival(string? line = null, int[]? colors = null, int at = 0)
    {
        if (Quiet || Lots.Count == 0) return;
        var g = G;
        bool rally = g.Harsh >= 0.8 && colors == null; // harsh rule turns a festival into a rally
        // Sickness or hunger calls a festival off (a rally is ordered, so it goes ahead).
        if (!rally && FestCalledOff() is { } off)
        {
            if (EvClock - festOffSaid > DayMs / 2) { festOffSaid = EvClock; Say(off, false); }
            return;
        }
        // Rain or snow on the square puts a festival off until it clears (a rally is ordered, so it goes ahead).
        if (!rally && Stormy)
        {
            if (!festWait)
            {
                festWait = true;
                festWaitLine = line;
                festWaitColors = colors;
                festWaitLot = at;
                festWaitUntil = EvClock + DayMs * 2;
                Say($"{(snowAmt > 0.5 ? "Snow" : "Rain")} on the square: the festival is put off until the sky clears.");
            }
            return;
        }
        bool fresh = TickNo >= festUntil;
        if (fresh) FestsHeld++;
        festUntil = Math.Max(festUntil, TickNo + Round(FestMs * Math.Max(0.4, g.Fest) / TickMs));
        Rally = rally;
        festLot = at < Lots.Count ? at : 0;
        festColors = colors ?? (rally ? new[] { g.Banner, 0x1e1e1e, g.Banner, 0x1e1e1e } : Govs.IsHamlet(GovKey) ? Bunting : new[] { g.Banner, Bunting[1], Bunting[2], Bunting[3] });
        Nudge(rally ? 0.02 : 0.08 * g.Fest, rally ? "a rally" : "a festival");
        if (fresh) Say(line ?? Pick(Govs.FestSay[GovKey]));
        foreach (var w in Walkers)
        {
            if (w.Sleep || w.Gone || w.Sick || w.Mode == Mode.Walk || (!rally && Chance() > 0.8)) continue;
            if (w.Mode == Mode.Inside && w.At >= 0 && w.At < Lots.Count) (w.X, w.Y) = DoorOf(w.At);
            JoinFestival(w);
        }
    }

    // Why the town will not dance now, as the crier says it, or null. Only what the town knows: an outbreak the
    // crier has announced that is the grey plague or has 3 or more sick (a couple of coughs do not stop a dance),
    // a plague on the town, or hunger as the steward reports it (a famine, two weak, or the
    // 30% hungry at which the steward speaks up).
    double festOffSaid = -1e9;
    public int FestsHeld, FestsOffIll, FestsOffHunger;   // soak counters
    string? FestCalledOff()
    {
        string? why = FestOffWhy();
        if (why != null && why.StartsWith("No dancing while")) FestsOffHunger++;
        else if (why != null) FestsOffIll++;
        return why;
    }
    string? FestOffWhy()
    {
        if (Ill is { Told: true } && (Diseases[Ill.D].Key == "plague" || Walkers.Count(w => !w.Gone && w.Sick) >= 3)) return $"{Cap(Diseases[Ill.D].Name)} in {Name}: no dancing on the square until it passes.";
        if (BlightOn?.Kind == "plague" && BlightOn.Towns.Contains(Name)) return $"Sickness in {Name}: the square stays empty, no dancing.";
        int pop = Walkers.Count(w => !w.Gone && w.Kind == "");
        if (Famine || WeakCount >= 2 || HungryCount > 0.3 * pop) return "No dancing while the bread runs short: the festival is called off.";
        return null;
    }

    // A festival put off by bad weather, run when the sky clears (if that is within two days), and a running
    // one broken up when rain or snow sets in. Rallies are compulsory and carry on.
    bool festWait;
    string? festWaitLine;
    int[]? festWaitColors;
    int festWaitLot;
    double festWaitUntil;
    void StepFestWeather()
    {
        if (festWait)
        {
            if (EvClock >= festWaitUntil) festWait = false;
            else if (!Stormy)
            {
                festWait = false;
                StartFestival(festWaitLine ?? "The festival goes ahead at last.", festWaitColors, festWaitLot);
            }
        }
        if (Festival && !Rally && Stormy)
        {
            festUntil = TickNo;
            Nudge(-0.02, "a festival rained off");
            Say($"{(snowAmt > 0.5 ? "Snow" : "Rain")} breaks up the festival; the bunting drips over an empty square.");
        }
    }

    // The weather as saved: sky, clock and the eased amounts (gloom, fog, rain, snow, wet, cover).
    public double[] WxAmounts
    {
        get => new[] { wxClock, wxUntil, gloom, fogAmt, rainAmt, snowAmt, wet, cover };
        set
        {
            if (value.Length < 8) return;
            wxClock = value[0]; wxUntil = value[1];
            gloom = Clamp(value[2], 0, 1); fogAmt = Clamp(value[3], 0, 1); rainAmt = Clamp(value[4], 0, 1.6);
            snowAmt = Clamp(value[5], 0, 1); wet = Clamp(value[6], 0, 1); cover = Clamp(value[7], 0, 1);
        }
    }

    // For screenshot tests: jump straight to a sky with every amount fully faded in.
    public void ForceWeather(Wx w)
    {
        Weather = w;
        wxUntil = wxClock + 10 * DayMs;
        gloom = WxGloom[(int)w];
        fogAmt = w == Wx.Fog ? 1 : 0;
        rainAmt = w == Wx.Storm ? 1.6 : w == Wx.Rain ? 1 : 0;
        snowAmt = w == Wx.Snow ? 1 : 0;
        wet = rainAmt > 0 ? 1 : 0;
        cover = w == Wx.Snow ? 1 : 0;
    }

    // One step of everything that lives around the town.
    void StepLife(double d, double dtMs)
    {
        UpdateWeather(dtMs);
        StepFlood(dtMs);
        StepFestWeather();
        StepCat();
        StepFlock(d);
        StepSmoke();
        StepDrops();
    }
}
