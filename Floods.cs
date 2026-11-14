namespace Dotburg;

// Floods (0.85, from the mod's Pass 3): long heavy rain fills the low roads from the bottom of the town up, and the
// ponds and marshes spill over onto the roads round them (Land, 0.82). A town with wet land floods sooner and, with a
// marsh, drains slower; a long spring rain can do it there too. Standing water slows a fire on its lot. At its
// height a flood may wash away a building or two on the low street or by the water (ruins, rebuilt as after a fire),
// someone asleep inside may drown, and for a few days after, the water-borne sicknesses come easier. The level is
// weather, not saved; the ruins are.
partial class Sim
{
    const double FloodRiseMs = 80_000;     // a storm on soaked ground fills the low roads this fast (ms per whole flood)
    const double FloodDrainMs = 200_000;   // and they drain this slowly
    double flood;                          // 0..1: how high the water stands
    double floodSaid = -1e9;               // EvClock the crier last told of a flood
    public double FloodAt = -1e9;          // EvClock of the last flood that did damage (water-borne sickness after)
    float[] floodLevel = Array.Empty<float>();   // per pixel: the flood level at which water reaches it
    int floodVer = -1;
    public int Floods, FloodDamages, Drowned;    // soak counters

    bool WetLand => LandCount(PondK) + LandCount(Marsh) > 0;
    public bool Flooded => flood >= 0.5;

    // Raise or drain the water with the weather; called every tick from StepLife.
    void StepFlood(double dt)
    {
        bool storm = Weather == Wx.Storm && wet > 0.9;
        bool springRain = Weather == Wx.Rain && wet > 0.9 && SeasonNo == 0 && WetLand;   // swollen ponds in spring
        double was = flood;
        if (storm || springRain) flood = Ease(flood, 1, dt, FloodRiseMs * (WetLand ? 0.7 : 1) * (storm ? 1 : 3));
        else flood = Ease(flood, 0, dt, FloodDrainMs * (LandCount(Marsh) > 0 ? 1.5 : 1));
        if (was < 0.5 && flood >= 0.5 && EvClock - floodSaid > 2 * DayMs)
        {
            floodSaid = EvClock;
            Floods++;
            Say(LandCount(PondK) > 0 ? Pick(new[] { "The pond spills over; water runs down the low streets.", "The pond breaks its banks and the low roads flood." })
                : LandCount(Marsh) > 0 ? "The marsh creeps up over the roads." : Pick(new[] { "The low roads flood.", "Water runs ankle-deep in the lower streets." }));
        }
    }

    // Where the water reaches, per pixel: the bottom third of the town rises with the level, and the roads within a
    // few pixels of a pond or marsh go under first. Rebuilt when the static picture changes.
    void IndexFlood()
    {
        if (floodVer == StaticVer && floodLevel.Length == W * H) return;
        floodVer = StaticVer;
        floodLevel = new float[W * H];
        int bottom = Oy + RowsT * TileH + 2;
        double span = Math.Max(1, RowsT * TileH * 0.35);
        var wetTiles = Land.Where(l => l.K is PondK or Marsh).Select(l => (x0: Ox + l.Tx * TileW + 2, y0: Oy + l.Ty * TileH + 2)).ToList();
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                double low = (bottom - y) / span;
                foreach (var (x0, y0) in wetTiles)
                {
                    int dx = x < x0 ? x0 - x : x > x0 + TileW - 3 ? x - (x0 + TileW - 3) : 0;
                    int dy = y < y0 ? y0 - y : y > y0 + TileH - 3 ? y - (y0 + TileH - 3) : 0;
                    low = Math.Min(low, Math.Max(dx, dy) / 6.0);
                }
                floodLevel[y * W + x] = (float)Math.Max(0.05, low);
            }
    }

    // Water over a road pixel at this level? (Town picture coordinates.)
    bool UnderWater(int px, int py, double level)
    {
        IndexFlood();
        return px >= 0 && py >= 0 && px < W && py < H && floodLevel[py * W + px] <= level;
    }

    // Is the street at this lot's door under water now?
    bool LotFlooded(int rank)
    {
        if (flood < 0.3 || rank < 0 || rank >= Lots.Count) return false;
        var (dx, dy) = DoorOf(rank);
        return UnderWater(Ox + dx, Oy + dy, flood);
    }

    // Rolled with the events: at the height of a flood, the water may take a building or two at the low end of town.
    void MaybeFloodDamage()
    {
        if (flood > 0.9 && Calm && Odds(0.8)) FloodDamage();
    }

    public void FloodDamage()
    {
        int v = Math.Min(Built.Count, Lots.Count);
        var low = Enumerable.Range(1, Math.Max(0, v - 1))
            .Where(r => Built[r] != "ruin" && !Content.Outdoor.Contains(Built[r]) && !WorksList.Any(w => w.Rank == r) && !Burning(r))
            .Where(r => { var (dx, dy) = DoorOf(r); return UnderWater(Ox + dx, Oy + dy, 0.8); }).ToList();
        if (low.Count == 0 || Ruins.Count >= Math.Floor(v * RuinCap)) return;
        int n = Math.Min(low.Count, Chance() < 0.3 ? 2 : 1);
        var gone = new List<string>();
        int drowned = 0;
        double speed = (GovKey == "guild" ? 0.5 : 1) * (Gold < 8 ? 1.6 : Gold > 60 ? 0.7 : 1);
        for (int i = 0; i < n; i++)
        {
            int r = low[Rng.Next(low.Count)];
            low.Remove(r);
            string t = Built[r];
            // Those asleep or abed inside may not get out in time (people of note are always carried out).
            foreach (var w in Walkers.Where(w => !w.Gone && w.Kind == "" && w.Mode == Mode.Inside && w.At == r && (w.Sleep || w.Sick) && BodyOf(w) == null).ToList())
                if (Chance() < 0.15 && Walkers.Count(x => !x.Gone && x.Kind == "") > MinPop + 2) { w.Gone = true; Mourn(w, "drowned in the flood"); drowned++; }
            Built[r] = "ruin";
            LoseStore(r, t);
            Ruins.Add(new Ruin { Rank = r, Was = t, At = EvClock + DayMs * (1 + Chance()) * speed });
            gone.Add(Content.Label(t));
        }
        FloodDamages++;
        Drowned += drowned;
        FloodAt = EvClock;
        lastDisaster = EvClock;
        Pay(-3);
        Nudge(-0.06 - 0.03 * drowned, "the flood");
        StaticDirty = true;
        Rehome();
        string what = gone.Count == 1 ? $"the {gone[0]}" : $"the {gone[0]} and the {gone[1]}";
        string lost = drowned == 0 ? "" : drowned == 1 ? "; one dot-man drowns" : $"; {drowned} dot-men drown";
        Say($"The flood washes away {what} on the low street{lost}.");
        Npc("warden", "down", false, gone[0]);
        if (Gold < 8) AskAid(false, "flood");
        else if (GovKey is "commune" or "council" && Chance() < 0.5) { Nudge(0.02, "a collection to rebuild"); Say("Neighbours raise a collection to rebuild after the flood."); }
    }

    // For screenshot tests: the water at its height.
    public void ForceFlood() { ForceWeather(Wx.Storm); flood = 1; }
}
