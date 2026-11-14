namespace Dotburg;

// A building on fire. Size 0..1 is how fierce the fire is; Burn 0..1 is how much of the building is gone.
class Fire
{
    public int Rank;
    public double Size;
    public double Burn;
    public int Deaths;
}

// What is left of a burnt building, and what it was, to be rebuilt once EvClock passes At.
class Ruin
{
    public int Rank;
    public string Was = "";
    public double At;
}

// Fires, after RimWorld: a fire has a size that grows on fuel and shrinks under rain and water; it eats the
// building it stands on and may jump to neighbouring lots by how flammable they are. Dot-men run to beat it out.
// A building that is eaten through becomes a ruin and is rebuilt through scaffolding after about a day.
partial class Sim
{
    public readonly List<Fire> Fires = new();
    public readonly List<Ruin> Ruins = new();
    public int FireDeaths;                // lives lost to fire, all time (for tests)
    double fireClock;                    // ms since the last fire step
    double dryDays;                      // days since it last rained: dry spells make fires likelier
    const double FireStepMs = 1000;      // fires are simulated once a second
    const double RuinCap = 0.3;          // at most this share of the lots may lie in ruins
    const int MaxFires = 6;

    // How easily a building catches and spreads fire (0 never), and how long it lasts (1 = about 30 s at full size).
    static readonly Dictionary<string, (double Flam, double Hp)> Fuel = new()
    {
        ["house"] = (0.9, 1), ["house2"] = (0.85, 1.2), ["row"] = (0.85, 1.5),
        ["shop"] = (0.8, 1), ["market"] = (0.7, 1.2), ["bakery"] = (1.0, 1), ["mill"] = (1.0, 1),
        ["inn"] = (0.85, 1.2), ["hotel"] = (0.8, 1.6), ["school"] = (0.5, 1.5), ["hall"] = (0.4, 2),
        ["church"] = (0.35, 2), ["cathedral"] = (0.25, 3), ["tower"] = (0.3, 3),
        ["park"] = (0.5, 1),                  // a grass fire: it burns out, the park stays
    };
    static (double Flam, double Hp) FuelOf(string t) => Fuel.TryGetValue(t, out var f) ? f : (0, 1);

    public Fire? FireAt(int rank) => Fires.FirstOrDefault(f => f.Rank == rank);
    bool Burning(int rank) => Fires.Any(f => f.Rank == rank);

    // Lots one block away (sharing a road) from rank.
    IEnumerable<int> Neighbours(int rank)
    {
        int v = Math.Min(Built.Count, Lots.Count);
        var l = Lots[rank];
        for (int r = 0; r < v; r++)
            if (r != rank && Math.Abs(Lots[r].tx - l.tx) + Math.Abs(Lots[r].ty - l.ty) == 1) yield return r;
    }

    // Can a fire take hold on this lot right now?
    bool CanBurn(int rank)
    {
        if (rank < 0 || rank >= Math.Min(Built.Count, Lots.Count)) return false;
        if (FuelOf(Built[rank]).Flam <= 0 || Burning(rank) || WorksList.Any(w => w.Rank == rank)) return false;
        return Fires.Count < MaxFires;
    }

    // A random lot that can burn, weighted by flammability (kitchens and ovens a little more); -1 if none.
    int FlammableRank(Func<string, double>? extra = null)
    {
        var pool = new List<int>();
        int v = Math.Min(Built.Count, Lots.Count);
        for (int r = 0; r < v; r++) if (CanBurn(r)) pool.Add(r);
        if (pool.Count == 0) return -1;
        return Weighted(pool, r => FuelOf(Built[r]).Flam * (extra?.Invoke(Built[r]) ?? 1));
    }

    // Set a lot alight. Returns false if it could not catch (wet stone, a ruin, too many fires, too many ruins).
    bool StartFire(int rank, double size, string? line)
    {
        if (!CanBurn(rank)) return false;
        int v = Math.Min(Built.Count, Lots.Count);
        if (Ruins.Count >= Math.Floor(v * RuinCap)) return false;
        string t = Built[rank];
        Fires.Add(new Fire { Rank = rank, Size = size });
        Say(line ?? $"Fire! The {Content.Label(t)} is burning.");
        Npc("warden", "fire", false, Content.Label(t));
        if (Fires.Count == 3) { Say($"A great fire grips {Name}!"); Npc("warden", "great", true); }
        // Anyone awake inside runs out of the door.
        foreach (var w in Walkers)
            if (w.Mode == Mode.Inside && w.At == rank && !w.Sleep && !w.Sick && !w.Gone)
            {
                (w.X, w.Y) = DoorOf(rank);
                w.Mode = Mode.Wait;
                w.Until = TickNo + Secs(0.3, 1.5);
            }
        return true;
    }

    // ---------- where fires come from ----------
    // Rolled with the other events: a kitchen or oven fire, likelier in a dry spell and in summer, never in rain.
    void FireDice()
    {
        if (wet < 0.1 && rainAmt < 0.05) dryDays += CheckDays; else if (wet > 0.5) dryDays = 0;
        if (rainAmt > 0.2 || wet > 0.5) return;
        double rate = 0.02 * (SeasonNo == 1 ? 1.8 : SeasonNo == 3 ? 1.3 : 1) * (1 + Math.Min(2, dryDays) * 0.5)
                    * (cover > 0.3 ? 0.3 : 1) * Clamp(Built.Count / 20.0, 0.4, 2.5);
        if (!Odds(rate)) return;
        int r = FlammableRank(t => t is "bakery" or "inn" or "hotel" or "mill" ? 2 : 1);
        if (r >= 0) StartFire(r, 0.12, null);
    }

    // A flash that hits the town: taller buildings draw it. Usually the rain puts it out within seconds.
    void Strike()
    {
        if (Chance() > 0.06) return;
        int r = FlammableRank(t => t is "tower" or "church" or "cathedral" ? 4 : 1);
        if (r < 0) return;
        StartFire(r, 0.15, $"Lightning strikes the {Content.Label(Built[r])}!");
    }

    // A saboteur or raider sets a lot alight (bigger than an accident).
    void Arson(string line)
    {
        int r = FlammableRank();
        if (r >= 0) StartFire(r, 0.3, line);
    }

    // ---------- the fire step ----------
    void StepFires(double dtMs)
    {
        if (Fires.Count == 0) { fireClock = 0; return; }
        // Dark smoke from every blaze, every few ticks.
        if (TickNo % 3 == 0 && puffs.Count < 40)
            foreach (var fr in Fires)
                if (fr.Rank < Lots.Count && Chance() < 0.25 + 0.6 * fr.Size)
                    puffs.Add(new Puff { X = Ox + Lots[fr.Rank].tx * TileW + 2 + RandInt(0, 5), Y = Oy + Lots[fr.Rank].ty * TileH + 2, Life = RandInt(18, 30), Dark = true });
        fireClock += dtMs;
        if (fireClock < FireStepMs) return;
        fireClock -= FireStepMs;
        double dry = 1 + Math.Min(2, dryDays) * 0.5;
        double wetK = 1 - Math.Min(1, rainAmt);
        foreach (var f in Fires.ToList())
        {
            if (f.Rank >= Built.Count || f.Rank >= Lots.Count) { Fires.Remove(f); continue; }
            string t = Built[f.Rank];
            var (flam, hp) = FuelOf(t);
            // Grow on fuel; rain, snow, water close by and the bucket line cut it down.
            f.Size += 0.2 * flam * f.Size * (1 - f.Size);
            f.Size -= Math.Min(1.6, rainAmt) * 0.10 + (cover > 0.3 ? 0.03 : 0);
            int fighters = Walkers.Count(w => !w.Gone && w.Goal == Goal.Fight && w.Dest == f.Rank && w.Mode == Mode.Wait);
            f.Size -= 0.01 * fighters;
            if (Tier >= 3) f.Size -= 0.02;                                                  // a fire brigade turns out
            if (Neighbours(f.Rank).Any(n => Built[n] is "well" or "fountain")) f.Size -= 0.015; // a bucket chain from the well
            f.Size = Clamp(f.Size, 0, 1);
            f.Burn += f.Size / (30 * hp);
            // Jump to the lots next door: fiercer fires, flammable neighbours and dry weather all raise it.
            if (f.Size > 0.4)
                foreach (int n in Neighbours(f.Rank))
                {
                    if (!CanBurn(n)) continue;
                    double p = 0.012 * f.Size * FuelOf(Built[n]).Flam * dry * wetK * (cover > 0.3 ? 0.5 : 1);
                    if (Chance() < p) StartFire(n, 0.1, $"The fire spreads to the {Content.Label(Built[n])}.");
                }
            // Sleepers and the bedridden inside may wake in time; if not, a fierce fire kills.
            foreach (var w in Walkers.Where(w => !w.Gone && w.Mode == Mode.Inside && w.At == f.Rank && (w.Sleep || w.Sick || w.Hurt)).ToList())
            {
                if (Chance() < 0.22) { w.Sleep = false; w.Sick = false; w.Dis = -1; (w.X, w.Y) = DoorOf(f.Rank); w.Mode = Mode.Wait; w.Until = TickNo; continue; }
                if (f.Size > 0.5 && Chance() < 0.03)
                {
                    // Someone who matters is burned and carried out rather than dying on the spot; the burns may still kill.
                    if (BodyOf(w) is { } b) { BurnBody(w, b, f); w.Sick = false; (w.X, w.Y) = DoorOf(f.Rank); w.Mode = Mode.Wait; w.Until = TickNo; }
                    else DieInFire(w, f);
                }
            }
            SendFighters(f);
            // The end: put out, burnt out, or burnt down.
            if (f.Size <= 0.02)
            {
                Fires.Remove(f);
                Say(rainAmt > 0.3 ? $"The rain puts out the fire at the {Content.Label(t)}." : $"The bucket line puts out the fire at the {Content.Label(t)}.");
                Npc("warden", "out", false, Content.Label(t));
                Nudge(0.01, "a fire put out");
            }
            else if (f.Burn >= 1)
            {
                Fires.Remove(f);
                if (Content.Outdoor.Contains(t)) Say($"The fire in the {Content.Label(t)} burns itself out.");
                else BurnDown(f, t);
            }
        }
        if (Fires.Count == 0) Rehome();
    }

    // Nearby dot-men run to the fire; at night the alarm wakes some of the street.
    void SendFighters(Fire f)
    {
        int have = Walkers.Count(w => !w.Gone && w.Goal == Goal.Fight && w.Dest == f.Rank && w.Mode != Mode.Inside);
        int want = Math.Min(6, Math.Max(2, Headcount / 6));
        if (have >= want) return;
        var (dx, dy) = DoorOf(f.Rank);
        foreach (var w in Walkers
            .Where(w => !w.Gone && !w.Sick && w.Kid == 0 && w.Kind == "" && w.Goal != Goal.Leave && w.Goal != Goal.Taken && w.Goal != Goal.Fight
                        && !Is(w, "craven") && !(w.Mode == Mode.Inside && w.Sleep && Chance() > 0.4))   // the craven stay away
            .OrderBy(w => Math.Abs(w.X - dx) + Math.Abs(w.Y - dy) - (Is(w, "brave") ? 30 : 0)).Take(want - have).ToList())   // the brave come from further off
        {
            if (w.Mode == Mode.Inside && w.At >= 0 && w.At < Lots.Count) (w.X, w.Y) = DoorOf(w.At);
            w.Sleep = false;
            GoTo(w, Clamp(dx + RandInt(-5, 5), 0, Cols * TileW + 1), dy, Goal.Fight, f.Rank);
        }
    }

    void DieInFire(Walker w, Fire f)
    {
        w.Gone = true;
        f.Deaths++;
        FireDeaths++;
        Mourn(w, "in the fire");
        Nudge(-0.04, "a death in a fire");
        if (w.Lead)
        {
            string t = LeaderTitle;
            Unseat(w, true);
            Say($"{t} perishes in the fire.");
            Npc("warden", "death", false, t);
        }
        else Npc("warden", "death", false, "Someone");
    }

    // A building is eaten through: it becomes a ruin, the town pays, the rule shapes the aftermath.
    void BurnDown(Fire f, string t)
    {
        Built[f.Rank] = "ruin";
        LoseStore(f.Rank, t);
        double speed = (GovKey == "guild" ? 0.5 : 1) * (Gold < 8 ? 1.6 : Gold > 60 ? 0.7 : 1);
        Ruins.Add(new Ruin { Rank = f.Rank, Was = t, At = EvClock + DayMs * (1 + Chance()) * speed });
        Pay(-4);
        Nudge(-0.03, "a building burnt down");
        if (Gold < 8) AskAid(false, "fire"); // a poor town asks its friends for help rebuilding
        StaticDirty = true;
        string lost = f.Deaths > 0 ? $"; {f.Deaths} {(f.Deaths == 1 ? "life is" : "lives are")} lost" : "";
        Say($"The {Content.Label(t)} burns down{lost}.");
        Npc("warden", "down", false, Content.Label(t));
        Rehome();
        if (G.Harsh >= 0.8 && Chance() < 0.4) { Arrest(); Say("The authorities blame a saboteur for the fire."); }
        else if (GovKey is "commune" or "council" && Chance() < 0.5) { Nudge(0.02, "a collection to rebuild"); Say("Neighbours raise a collection to rebuild."); }
    }

    // Rolled with events: ruins whose time has come are rebuilt through scaffolding.
    void RebuildRuins()
    {
        foreach (var ru in Ruins.ToList())
        {
            if (EvClock < ru.At) continue;
            Ruins.Remove(ru);
            if (ru.Rank >= Built.Count || Built[ru.Rank] != "ruin") continue;
            Built[ru.Rank] = ru.Was;
            StartWorks(ru.Rank, ru.Was, "ruin");
            Rehome();
            StaticDirty = true;
        }
    }

    // After a load: make sure every ruin has a record, so none stays a ruin for ever.
    public void RestoreRuins(IEnumerable<(int rank, string was, double inMs)> saved)
    {
        Ruins.Clear();
        foreach (var (r, was, inMs) in saved)
            if (r >= 0 && r < Built.Count && Built[r] == "ruin" && Content.Sprites.ContainsKey(was) && was != "ruin")
                Ruins.Add(new Ruin { Rank = r, Was = was, At = EvClock + Math.Max(0, inMs) });
        for (int r = 0; r < Built.Count; r++)
            if (Built[r] == "ruin" && !Ruins.Any(x => x.Rank == r)) Ruins.Add(new Ruin { Rank = r, Was = "house", At = EvClock + DayMs });
    }

    // Forget fires and ruins (a new town).
    void ResetFires() { Fires.Clear(); Ruins.Clear(); fireClock = 0; dryDays = 0; }

    // ---------- legend, tooltip and tests ----------
    public string FireLine()
    {
        if (Fires.Count > 0) return Fires.Count == 1 ? $"Fire at the {Content.Label(Built[Fires[0].Rank])}" : $"{Fires.Count} fires burning";
        if (Ruins.Count > 0) return Ruins.Count == 1 ? "1 ruin awaits rebuilding" : $"{Ruins.Count} ruins await rebuilding";
        return "";
    }

    // Test only: light lot rank (or a random one) and run until every fire is out. Returns buildings lost, lives lost, seconds, lots touched.
    public (int lost, int dead, double secs, int touched, int left) TestFire(int rank = -1)
    {
        int before = Ruins.Count;
        int r = rank >= 0 ? rank : FlammableRank();
        if (r < 0 || !StartFire(r, 0.12, null)) return (0, 0, 0, 0, 0);
        var seen = new HashSet<int> { r };
        int deadBefore = FireDeaths;
        double start = Age;
        for (int i = 0; i < 100_000 && Fires.Count > 0; i++)
        {
            Tick(TickMs);
            foreach (var f in Fires) { seen.Add(f.Rank); }
        }
        int lostNow = Ruins.Count - before, deadNow = FireDeaths - deadBefore;
        double took = (Age - start) / 1000;
        for (int i = 0; i < 12_000 && Ruins.Count > before; i++) Tick(TickMs);   // up to three days for the rebuilding
        return (lostNow, deadNow, took, seen.Count, Ruins.Count - before);
    }

    // Test only: set a fire going on the most flammable lot for a screenshot.
    public void ForceFire()
    {
        int r = FlammableRank();
        if (r >= 0) StartFire(r, 0.5, null);
    }
}
