namespace Dotburg;

// What food can be. Grain and greens come in from the fields; the mill makes flour; the bakery makes bread;
// a home keeps a few meals in its pantry. One unit of anything edible is one meal.
enum Good { Grain, Flour, Bread, Greens, Meals }

// One haulage job: carry one good from a store (or the barn, -1) to another store.
record struct Job(int Src, int Dst, Good G, int Hands);

// Food, Songs of Syx style: supply (fields, barn, mill, bakery, shelves, carriers on the roads, rot) is kept apart
// from access (a hungry dot-man has to walk to a counter that has something, or to a pantry). Stock lives in buildings,
// carriers are ordinary dot-men with a sack, and every dot-man has a stomach. Each piece is simple; shortages come
// from the links between them: hungry workers carry, grind and reap less, so a shortage feeds itself.
partial class Sim
{
    // ---------- tuning ----------
    const double MealValue = 0.6;           // how much one meal fills a dot-man (Food runs 0..1)
    const double HungerPerDay = 0.9;        // how fast Food falls for an adult; children at 0.6 of it
    public const double HungryAt = 0.45;    // below this a dot-man goes looking for food
    public const double WeakAt = 0.15;      // below this: slow, no work, mood falls hard
    const double EatAt = 0.6;               // below this a dot-man eats when food is at hand (a counter or the pantry)
    const double StarveDays = 2;            // days with an empty stomach before a dot-man dies of hunger
    public const double NeedPerDay = HungerPerDay / MealValue;   // meals an adult eats a day (1.5)
    // Share of the able adults out in the fields (off the map, not drawn), by tier: a hamlet is mostly farmers.
    static readonly double[] FarmShares = { 0.5, 0.4, 0.33, 0.3 };
    const double GrainPerFarmer = 170;      // a full harvest per field worker at crop quality 1
    const double SeedPerFarmer = 6;         // grain sown per field worker on the first day of spring
    const double ReapPerFarmer = 35;        // grain a field worker cuts and stooks in a day of autumn
    static readonly double[] GreensPerFarmer = { 2.0, 3.0, 2.5, 0.2 };  // greens per field worker a day, by season
    const double MillPerDay = 140;          // flour the mill grinds a day at full strength
    const double BakePerDay = 45;           // bread each bakery bakes a day from flour
    const double QuernPerDay = 30;          // bread a bakery makes a day grinding by hand (no mill)
    const double Load = 10;                 // what one carrier takes in one trip
    // Rot a day: grain, flour, bread, greens, pantry meals. Grain keeps well in the mill's granary, badly in the barn.
    static readonly double[] RotPerDay = { 0.008, 0.02, 0.35, 0.2, 0.1 };
    const double BarnGrainRot = 0.01, BarnGrainRotWet = 0.04;
    const int FoodEvery = 9;                // ticks between food steps (about once a second)
    const int HungerSlices = 4;             // each dot-man's stomach is updated on one step in four, by id
    // Panic: an S-curve of what people see. PanicAt is the signal at which half the town would panic from calm;
    // PanicSelf is how much panic holds itself up (hoarders empty the shelves everyone watches), which makes it
    // stick: once high it only eases when the signal is well above PanicAt.
    const double PanicK = 10, PanicAt = 0.35, PanicSelf = 3;
    const double ShelfNormal = 0.4;              // share of counter room a well-stocked town keeps (they refill at half)
    const double PanicUp = 3, PanicDown = 0.5;   // per day: fear comes fast, calm slowly
    const double RumourFade = 0.15;              // rumour lost a day

    // What each rule does about food: days of grain it holds back beyond the next harvest before selling the surplus,
    // how far its people hoard when they panic (0: rationed or shared out), whether the granary opens to the hungry
    // when the counters are bare (closed: people can starve beside a full mill), and its answer to the queues.
    static readonly Dictionary<string, (double reserve, double hoard, bool open, string answer)> FoodRules = new()
    {
        ["none"] = (6, 1, true, "The settlers share what they have."),
        ["elders"] = (8, 1, true, "The elders open the old granary to the queues."),
        ["headman"] = (5, 1, true, "The headman hands out grain by the measure."),
        ["founders"] = (7, 1.2, true, "The founding family feeds its own first."),
        ["shrine"] = (6, 0.5, true, "The shrine keepers set out bread at the shrine."),
        ["commune"] = (6, 0, true, "The commune shares out what there is, a bowl each."),
        ["council"] = (5, 1, true, "The council asks everyone to buy only what they need."),
        ["mayor"] = (4, 1.2, false, "The mayor promises cheap bread by the weekend."),
        ["guild"] = (2, 1.5, false, "The guild raises the price of bread."),
        ["temple"] = (6, 0.7, true, "The temple hands out alms bread at the door."),
        ["crown"] = (5, 1, false, "The royal granary stays shut; the court still dines."),
        ["junta"] = (5, 0, false, "Soldiers stand at every counter: one loaf a head."),
        ["party"] = (10, 0, false, "Ration cards are issued. One meal a head, no exceptions."),
    };
    (double reserve, double hoard, bool open, string answer) FoodRule => FoodRules.TryGetValue(GovKey, out var r) ? r : (5, 1, true, "");

    // ---------- state ----------
    public double[] Barn = new double[5];                 // at the fields by the west gate: grain and greens
    public double[,] Stock = new double[MaxLots, 5];      // each lot's store, by good
    public double CropQ = 1;                               // how good this year's crop looks (seed, weather, luck)
    public double Standing;                                // grain still in the fields in autumn
    public double Seed;                                    // seed corn set aside at harvest for next spring (not eaten unless desperate)
    public double Panic;                                   // 0..1: how hard people are hoarding
    public double Rumour;                                  // 0..1: bad news in the air (failed harvest, famine); not saved
    public int FoodSeason = -1;                            // the season the fields last saw (sowing, reaping)
    int foodStep;
    double harvestIn;                                      // grain reaped this autumn (for the crier)
    bool queueSaid, lowSaid;
    long lastQueueTick;
    public int HungerDead;                                 // starved since the town was founded (not saved)
    readonly List<Job> jobs = new();

    // ---------- who stores what ----------
    static bool IsSeller(string t) => t is "shop" or "market";
    double RoomFor(int rank, Good g)
    {
        string t = Built[rank];
        double m = t == "market" ? 2 : 1;
        return (t, g) switch
        {
            ("mill", Good.Grain) => 1000,
            ("mill", Good.Flour) => 150,
            ("bakery", Good.Grain) => Has("mill") ? 0 : 60,
            ("bakery", Good.Flour) => 60,
            ("bakery", Good.Bread) => 50,
            ("shop" or "market", Good.Bread) => 30 * m,
            ("shop" or "market", Good.Greens) => 30 * m,
            ("shop" or "market", Good.Grain) => Has("mill") ? 0 : 40 * m,   // porridge oats until the mill makes flour for real bread
            (_, Good.Meals) when Content.IsHome(t) => 3 * Math.Max(1, Residents(rank)),
            _ => 0,
        };
    }
    bool Has(string t) => Built.Contains(t);
    int Residents(int rank)
    {
        int n = 0;
        foreach (var w in Walkers) if (!w.Gone && w.Kind == "" && HomeOf(w) == rank) n++;
        return n;
    }
    int Visible => Math.Min(Built.Count, Lots.Count);

    // Edible stock a counter has (bread, greens, or grain where there is no bakery).
    double OnSale(int r) => Stock[r, (int)Good.Bread] + Stock[r, (int)Good.Greens] + (Built[r] == "bakery" ? 0 : Stock[r, (int)Good.Grain]);
    bool SellsFood(int r) => r < Visible && (IsSeller(Built[r]) || Built[r] == "bakery") && !Burning(r);

    // Everything edible in town and barn, and how many days it would feed everyone.
    public double FoodTotal()
    {
        double t = Barn[(int)Good.Grain] + Barn[(int)Good.Greens];
        for (int r = 0; r < Visible; r++) for (int g = 0; g < 5; g++) t += Stock[r, g];
        foreach (var w in Walkers) if (w.Load > 0) t += w.Load;
        return t;
    }
    public double GrainTotal()
    {
        double t = Barn[(int)Good.Grain];
        for (int r = 0; r < Visible; r++) t += Stock[r, (int)Good.Grain] + Stock[r, (int)Good.Flour];
        return t;
    }
    public double Eaters => Math.Max(1, Walkers.Count(w => !w.Gone && w.Kind == "") * 0.95);
    public double FoodDays => FoodTotal() / (Eaters * NeedPerDay);
    public int HungryCount => Walkers.Count(w => !w.Gone && w.Kind == "" && w.Food < HungryAt);
    public int WeakCount => Walkers.Count(w => !w.Gone && w.Kind == "" && w.Food < WeakAt);

    // Able adults, the hungry counting for less: the labour behind the fields, the mill and the ovens.
    double Hands()
    {
        double h = 0;
        foreach (var w in Walkers)
        {
            if (w.Gone || w.Kind != "" || w.Kid != 0 || w.Sick || w.Hurt || w.Goal is Goal.Leave or Goal.Taken) continue;
            h += w.Food < WeakAt ? 0.2 : w.Food < HungryAt ? 0.7 : 1;
        }
        return h;
    }
    // Share of the able adults that is fed: scales the mill and the ovens.
    double Fed()
    {
        int n = 0;
        double h = 0;
        foreach (var w in Walkers)
        {
            if (w.Gone || w.Kind != "" || w.Kid != 0) continue;
            n++;
            h += w.Food < WeakAt ? 0.2 : w.Food < HungryAt ? 0.7 : 1;
        }
        return n == 0 ? 1 : h / n;
    }
    double Farmers => Math.Max(1, Hands() * FarmShares[Tier - 1]);

    // ---------- start ----------
    // A new town: settlers arrive with seed in the ground and a few days of grain in the barn.
    void InitFood()
    {
        Barn = new double[5];
        Stock = new double[MaxLots, 5];
        Barn[(int)Good.Grain] = 40;
        Barn[(int)Good.Greens] = 6;
        CropQ = 0.85 + 0.3 * Chance();
        Standing = 0;
        Panic = 0;
        FoodSeason = SeasonNo;
        Seed = 0;
        foreach (var w in Walkers) { w.Food = 0.8; w.Starve = 0; }
    }

    // A town from a save made before food existed: about a week of stores spread along the chain, everyone fed.
    public void FreshFood()
    {
        double daily = Eaters * NeedPerDay;
        Barn = new double[5];
        Stock = new double[MaxLots, 5];
        Barn[(int)Good.Grain] = daily * 3;
        Barn[(int)Good.Greens] = daily * 0.5;
        for (int r = 0; r < Built.Count && r < MaxLots; r++)
        {
            string t = Built[r];
            if (t == "mill") Stock[r, (int)Good.Grain] = daily * 3;
            if (t == "bakery") { Stock[r, (int)Good.Flour] = 20; Stock[r, (int)Good.Bread] = 15; }
            if (IsSeller(t)) { Stock[r, (int)Good.Bread] = 10; Stock[r, (int)Good.Greens] = 10; }
        }
        CropQ = 1;
        FoodSeason = SeasonNo;
        Seed = SeasonNo == 3 ? Farmers * SeedPerFarmer : 0;
        Standing = SeasonNo == 2 ? Farmers * GrainPerFarmer * 0.5 : 0;
        foreach (var w in Walkers) { w.Food = 0.8; w.Starve = 0; }
    }

    // Put saved stores back.
    public void RestoreFood(double[] barn, IEnumerable<(int r, double[] g)> stores, double cropQ, double standing, double panic, int season, double seed)
    {
        Barn = new double[5];
        for (int k = 0; k < 5 && k < barn.Length; k++) Barn[k] = Math.Max(0, barn[k]);
        Stock = new double[MaxLots, 5];
        foreach (var (r, g) in stores)
            if (r >= 0 && r < MaxLots) for (int k = 0; k < 5 && k < g.Length; k++) Stock[r, k] = Math.Max(0, g[k]);
        CropQ = Clamp(cropQ, 0, 1.5);
        Standing = Math.Max(0, standing);
        Seed = Math.Max(0, seed);
        Panic = Clamp(panic, 0, 1);
        FoodSeason = season;
    }

    // ---------- the step ----------
    // Once a second: stomachs, fields, mill and ovens, rot, and the list of things that need carrying.
    void StepFood()
    {
        StepQueues();
        if (TickNo % FoodEvery != 0 || Lots.Count == 0) return;
        double dt = FoodEvery * TickMs / DayMs;   // days this step covers
        foodStep++;
        Stomachs(dt);
        Fields(dt);
        Kitchens(dt);
        Rot(dt);
        FeedSeat();
        UpdatePanic(dt);
        PlanJobs();
        StepVoices(dt);
        StepLives(dt);
        StepBonds(dt);
    }

    // Each dot-man gets hungrier; one in HungerSlices is updated per step (hash-offset by id, so the work is spread).
    void Stomachs(double dt)
    {
        double step = dt * HungerSlices;
        foreach (var w in Walkers)
        {
            if (w.Gone || w.Kind != "" || (w.Id + foodStep) % HungerSlices != 0) continue;
            w.Food = Math.Max(0, w.Food - HungerPerDay * (w.Kid != 0 ? 0.6 : 1) * (w.Sleep ? 0.4 : 1) * step); // sleep slows hunger
            if (w.Food <= 0) w.Starve += step; else w.Starve = Math.Max(0, w.Starve - step * 2);
            if (w.Starve >= StarveDays && !w.Lead && Walkers.Count(x => !x.Gone && x.Kind == "") > MinPop) Starved(w);
        }
    }

    void Starved(Walker w)
    {
        w.Gone = true;
        HungerDead++;
        Mourn(w, "of hunger");
        Nudge(-0.03, "a death from hunger");
        var bl = BodyCause(w, "hunger");
        string who = w.Kid != 0 ? "A dotlet" : $"A {Content.DotNames[w.C]} dot-man";
        Say(bl ?? $"{who} dies of hunger in {Name}.", false);   // crier only: one starvation each is too many for the chronicle; famines are chronicled
        Npc("steward", "death", false, w.Kid != 0 ? "A dotlet" : "Another of us");
    }

    // The fields: sowing on the first day of spring, greens through the year, the grain harvest in autumn.
    void Fields(double dt)
    {
        int se = SeasonNo;
        if (se != FoodSeason)
        {
            if (FoodSeason >= 0) SeasonTurns(se);
            FoodSeason = se;
        }
        bool famine = Famine;
        bool snow = Weather == Wx.Snow || cover > 0.4;
        // Greens: a steady trickle while things grow.
        double greens = Farmers * GreensPerFarmer[se] * dt * (famine ? 0.3 : 1) * (snow ? 0.2 : 1);
        Barn[(int)Good.Greens] += greens;
        // The crop suffers in a dry spell or a famine before harvest.
        if (se <= 1)
        {
            if (dryDays >= 2) CropQ *= Math.Pow(0.96, dt);
            if (famine) CropQ *= Math.Pow(0.92, dt);
            if (Weather == Wx.Rain) CropQ = Math.Min(1.3, CropQ * Math.Pow(1.01, dt));
        }
        // Autumn: the standing grain is cut and stooked in the barn, slower in the rain and with hungry hands.
        if (se == 2 && Standing > 0)
        {
            double rate = Farmers * ReapPerFarmer * (Weather is Wx.Rain or Wx.Storm ? 0.3 : 1) * (Daylight() > 0.3 ? 2 : 0);
            double cut = Math.Min(Standing, rate * dt);
            Standing -= cut;
            Barn[(int)Good.Grain] += cut;
            harvestIn += cut;
            if (Standing <= 0.5)
            {
                Standing = 0;
                HarvestDone();
            }
        }
    }

    // Spring sows (seed from the stores: short seed, small harvest), autumn ripens, winter takes what was not cut.
    void SeasonTurns(int se)
    {
        if (se == 0)
        {
            double need = Farmers * SeedPerFarmer;
            double got = Math.Min(Seed, need);
            Seed = 0;   // what is not needed is sown thick; it does not come back
            if (got < need) got += TakeGrain(need - got);
            CropQ = (got / Math.Max(1, need)) * (0.8 + 0.4 * Chance());
            if (got < need * 0.6) { Say($"Too little seed to sow the fields of {Name}."); Npc("steward", "seed", false); Hear(0.15); }
            else Say("Seed goes into the fields.", false);
        }
        else if (se == 2)
        {
            Standing = Farmers * GrainPerFarmer * CropQ;
            harvestIn = 0;
            Say(CropQ < 0.55 ? "The crop is thin this year; the reaping begins." : "The reaping begins in the fields.", false);
        }
        else if (se == 3 && Standing > 0)
        {
            double lost = Standing;
            Standing = 0;
            Say($"Winter comes with {Math.Round(lost)} sacks of grain still in the fields; they rot.");
            Npc("steward", "rotting", false, Math.Round(lost).ToString());
            if (harvestIn > 0) HarvestDone();
        }
    }

    // The crier and the steward tell how the harvest went.
    void HarvestDone()
    {
        // First the seed corn for next spring is put aside.
        Seed = Math.Max(Seed, TakeGrain(Farmers * SeedPerFarmer * 1.2));
        double need = Eaters * NeedPerDay * 16;   // a year of meals
        string sacks = Math.Round(harvestIn).ToString();
        if (harvestIn < need * 0.5 || CropQ < 0.55) { Say($"The harvest fails in {Name}: {sacks} sacks."); Npc("steward", "fails", true, sacks); Hear(0.25); }
        else if (CropQ > 1.1) { Say($"A bumper harvest in {Name}: {sacks} sacks."); Npc("steward", "bumper", false, sacks); StartFestival("Harvest home! Sheaves and dancing on the square."); }
        else { Say($"The harvest is in: {sacks} sacks."); Npc("steward", "harvest", false, sacks); }
        harvestIn = 0;
    }

    // The mill grinds and the bakeries bake, as fast as fed hands allow.
    void Kitchens(double dt)
    {
        double fed = Fed();
        int v = Visible;
        // Bake and grind to demand: bread rots in days, so the ovens stop at about a day of bread in town and the
        // mill at two days of flour. Grain keeps best as grain.
        double daily = Eaters * NeedPerDay, bread = 0, flour = 0;
        for (int r = 0; r < v; r++) { bread += Stock[r, (int)Good.Bread]; flour += Stock[r, (int)Good.Flour]; }
        double bakeRoom = Math.Max(0, daily * 0.9 - bread), millRoom = Math.Max(0, daily * 2 - flour);
        for (int r = 0; r < v; r++)
        {
            if (Burning(r)) continue;
            string t = Built[r];
            if (t == "mill")
            {
                double n = Math.Min(Stock[r, 0], Math.Min(MillPerDay * fed * dt, Math.Min(millRoom, RoomFor(r, Good.Flour) - Stock[r, 1])));
                millRoom -= Math.Max(0, n);
                if (n > 0) { Stock[r, 0] -= n; Stock[r, 1] += n; }
            }
            else if (t == "bakery")
            {
                double room = Math.Min(bakeRoom, RoomFor(r, Good.Bread) - Stock[r, 2]);
                double n = Math.Min(Stock[r, 1], Math.Min(BakePerDay * fed * dt, room));
                if (n > 0) { Stock[r, 1] -= n; Stock[r, 2] += n; room -= n; CountBaked(n); }
                // No flour: grind grain by hand, slowly.
                double q = Math.Min(Stock[r, 0], Math.Min(QuernPerDay * fed * dt, room));
                if (q > 0 && Stock[r, 1] < 1) { Stock[r, 0] -= q; Stock[r, 2] += q; CountBaked(q); }
                bakeRoom = Math.Max(0, bakeRoom - Math.Max(0, n) - (Stock[r, 1] < 1 ? Math.Max(0, q) : 0));
            }
        }
    }

    // Everything rots a little: bread fast, greens in a few days, grain slowly (fast in an open barn in the rain).
    void Rot(double dt)
    {
        bool wetSky = Weather is Wx.Rain or Wx.Storm;
        double cold = SeasonNo == 3 ? 0.5 : 1;   // winter keeps things
        double blight = Famine ? 3 : 1;   // a famine is blight and mould in the stores too
        Barn[(int)Good.Grain] *= 1 - (wetSky ? BarnGrainRotWet : BarnGrainRot) * blight * dt;
        if (Famine && Standing > 0) Standing *= 1 - 0.05 * dt;
        Barn[(int)Good.Greens] *= 1 - RotPerDay[(int)Good.Greens] * cold * dt;
        int v = Visible;
        for (int r = 0; r < v; r++)
            for (int g = 0; g < 5; g++)
                if (Stock[r, g] > 0) Stock[r, g] *= 1 - RotPerDay[g] * (g >= 2 ? cold : 1) * (g <= 1 ? blight : 1) * dt;
        // A carrier held up on the road (sick in bed with the sack) loses it twice as fast.
        foreach (var w in Walkers) if (w.Load > 0) w.Load *= 1 - RotPerDay[Math.Max(0, w.Good)] * 2 * dt;
    }

    // The seat eats first: a leader who is hungry is served from any store.
    void FeedSeat()
    {
        var lw = LeaderWalker;
        if (lw == null || lw.Food >= HungryAt) return;
        if (TakeGrain(1) >= 1 || TakeAny(Good.Bread) || TakeAny(Good.Greens)) lw.Food = Math.Min(1, lw.Food + MealValue);
    }

    // People panic at what they see: bare shelves and bad news count as much as the real stores. Once panic is high
    // it holds itself up, so a small shortfall can tip the town into hoarding that outlasts the shortage.
    void UpdatePanic(double dt)
    {
        double days = FoodDays;
        Rumour = Math.Max(0, Rumour - RumourFade * dt);
        double signal = PanicSignal();
        double target = 1 / (1 + Math.Exp(-PanicK * (PanicAt - signal) - PanicSelf * Panic));
        Panic += (target - Panic) * Math.Min(1, dt * (target > Panic ? PanicUp : PanicDown));
        if (Panic > 0.5 && !queueSaid)
        {
            queueSaid = true;
            Say(FoodRule.hoard > 0 ? $"Queues at every counter in {Name}: people buy for the week." : $"Queues at every counter in {Name}.");
            if (FoodRule.answer != "") Say(FoodRule.answer);
            Npc("steward", FoodRule.hoard > 0 ? "queue" : "ration", true);
        }
        else if (Panic < 0.15) queueSaid = false;
        if (days < 3 && !lowSaid) { lowSaid = true; Npc("steward", "low", false, Math.Max(0, Math.Round(days)).ToString()); }
        else if (days > 6) lowSaid = false;
    }

    // What the town sees, 0 (nothing anywhere, bad news) to 1 (full shelves, a year in store): half the shelves
    // (or the pantries, where there is no counter), half the stores as people hear of them, less rumour and queues.
    public double PanicSignal()
    {
        double days = FoodDays;
        bool turnedAway = TickNo - lastQueueTick < Secs(30, 30);
        return 0.5 * ShelfFill() + 0.5 * Clamp(days / 6, 0, 1) - Rumour - (turnedAway ? 0.1 : 0);
    }

    // How full the food counters are (bread and greens, or porridge grain before the mill), or the pantries in a
    // hamlet with no counter yet.
    public double ShelfFill()
    {
        double have = 0, room = 0;
        int v = Visible;
        for (int r = 0; r < v; r++)
        {
            if (!SellsFood(r)) continue;
            have += OnSale(r);
            room += RoomFor(r, Good.Bread) + RoomFor(r, Good.Greens) + (Built[r] == "bakery" ? 0 : RoomFor(r, Good.Grain));
        }
        if (room <= 0)
            for (int r = 0; r < v; r++)
                if (Content.IsHome(Built[r])) { have += Stock[r, (int)Good.Meals]; room += RoomFor(r, Good.Meals); }
        // Counters are topped up at half full, so a well-run town sits near 0.4 of room: that counts as full.
        return room <= 0 ? 0.5 : Clamp(have / (room * ShelfNormal), 0, 1);
    }

    // Bad news spreads faster than bread: a failed harvest or a famine raises panic before the stores fall.
    void Hear(double bad) => Rumour = Clamp(Rumour + bad, 0, 1);

    // ---------- moving food ----------
    // What needs carrying, most urgent first. Each job takes one carrier (the harvest to the mill takes up to three).
    void PlanJobs()
    {
        jobs.Clear();
        int v = Visible;
        int mill = Built.FindIndex(t => t == "mill");
        if (mill >= v || (mill >= 0 && Burning(mill))) mill = -1;
        var bakeries = new List<int>();
        var sellers = new List<int>();
        for (int r = 0; r < v; r++)
        {
            if (Burning(r)) continue;
            if (Built[r] == "bakery") bakeries.Add(r);
            if (IsSeller(Built[r])) sellers.Add(r);
        }
        // Bread from the ovens to the shop counters.
        foreach (int s in sellers)
            if (Stock[s, (int)Good.Bread] < RoomFor(s, Good.Bread) * 0.5)
            {
                int b = bakeries.Where(x => Stock[x, (int)Good.Bread] >= 3).OrderByDescending(x => Stock[x, (int)Good.Bread]).DefaultIfEmpty(-1).First();
                if (b >= 0) jobs.Add(new Job(b, s, Good.Bread, 1));
            }
        // Greens from the barn to the counters (the bakery sells none).
        if (Barn[(int)Good.Greens] >= 3)
            foreach (int s in sellers) if (Stock[s, (int)Good.Greens] < RoomFor(s, Good.Greens) * 0.5) jobs.Add(new Job(-1, s, Good.Greens, 1));
        // Flour from the mill to the ovens, or grain if there is no mill.
        foreach (int b in bakeries)
        {
            if (mill >= 0 && Stock[b, (int)Good.Flour] < 25 && Stock[mill, (int)Good.Flour] >= 3) jobs.Add(new Job(mill, b, Good.Flour, 1));
            if (mill < 0 && Stock[b, (int)Good.Grain] < 30)
            {
                if (Barn[(int)Good.Grain] >= 3) jobs.Add(new Job(-1, b, Good.Grain, 1));
            }
        }
        // No mill yet: the counters also sell grain for porridge.
        if (mill < 0 && Barn[(int)Good.Grain] >= 3)
            foreach (int s in sellers) if (Stock[s, (int)Good.Grain] < RoomFor(s, Good.Grain) * 0.5) jobs.Add(new Job(-1, s, Good.Grain, 1));
        // No counter at all (a young hamlet): the barn fills the pantries.
        if (sellers.Count == 0 && bakeries.Count == 0)
        {
            Good g = Barn[(int)Good.Greens] >= 3 ? Good.Greens : Good.Grain;
            if (Barn[(int)g] >= 3)
                for (int r = 0; r < v; r++)
                    if (Content.IsHome(Built[r]) && Stock[r, (int)Good.Meals] < RoomFor(r, Good.Meals) * 0.5) jobs.Add(new Job(-1, r, g, 1));
        }
        // The harvest from the barn into the mill's granary (more hands while the barn is full).
        if (mill >= 0 && Barn[(int)Good.Grain] >= 3 && Stock[mill, (int)Good.Grain] < RoomFor(mill, Good.Grain) - Load)
            jobs.Add(new Job(-1, mill, Good.Grain, Barn[(int)Good.Grain] > 60 ? 4 : 1));
    }

    // A dot-man deciding what to do takes a carrying job if one is waiting and not already manned. True if taken.
    bool TakeJob(Walker w)
    {
        if (jobs.Count == 0 || w.Kid != 0 || w.Lead || w.Food < WeakAt || w.Kind != "") return false;
        foreach (var j in jobs)
        {
            int on = 0;
            foreach (var x in Walkers) if (x.Good == (int)j.G && x.Dst == j.Dst && x.Src == j.Src && !x.Gone) on++;
            if (on >= j.Hands) continue;
            w.Good = (int)j.G;
            w.Src = j.Src;
            w.Dst = j.Dst;
            w.Load = 0;
            var (x2, y2) = j.Src < 0 ? Entrance() : DoorOf(j.Src);
            GoTo(w, x2, y2, Goal.Haul, j.Src);
            return true;
        }
        return false;
    }

    // Back on the road with a full sack: to the destination.
    void Deliver(Walker w)
    {
        if (w.Dst < 0 || w.Dst >= Visible) { DropLoad(w); return; }
        var (x2, y2) = DoorOf(w.Dst);
        GoTo(w, x2, y2, Goal.Haul, w.Dst);
    }

    // A carrier reaches the end of a leg: load up at the source, or unload at the destination.
    void ArriveHaul(Walker w)
    {
        w.Mode = Mode.Wait;
        if (w.Load <= 0)
        {
            double got = w.Src < 0 ? TakeBarn((Good)w.Good, Load) : Take(w.Src, (Good)w.Good, Load);
            if (got <= 0) { EndJob(w); w.Until = TickNo + Secs(1, 3); return; }
            w.Load = got;
            w.At = w.Src;
            w.Until = TickNo + (w.Src < 0 ? Secs(3, 6) : Secs(1, 3));   // loading the cart at the fields takes longer
            return;
        }
        DropLoad(w);
        w.At = w.Dst;
        w.Until = TickNo + Secs(1, 2);
    }

    // Put what a carrier holds into the destination (if it still takes that good), else back in the barn.
    void DropLoad(Walker w)
    {
        if (w.Load > 0 && w.Good >= 0)
        {
            var g = (Good)w.Good;
            int d = w.Dst;
            if (d >= 0 && d < Visible && !Burning(d))
            {
                // A home turns grain and greens into pantry meals.
                var into = Content.IsHome(Built[d]) ? Good.Meals : g;
                double room = Math.Max(0, RoomFor(d, into) - Stock[d, (int)into]);
                double put = Math.Min(room, w.Load);
                Stock[d, (int)into] += put;
                if (put > 0) CountTrip();
                w.Load -= put;
            }
            if (w.Load > 0) Barn[g == Good.Flour || g == Good.Bread ? (int)Good.Grain : (int)g] += w.Load;
        }
        EndJob(w);
    }

    static void EndJob(Walker w) { w.Good = -1; w.Load = 0; w.Src = -1; w.Dst = -1; }

    double Take(int r, Good g, double n)
    {
        double t = Math.Min(n, Stock[r, (int)g]);
        Stock[r, (int)g] -= t;
        return t;
    }
    double TakeBarn(Good g, double n)
    {
        double t = Math.Min(n, Barn[(int)g]);
        Barn[(int)g] -= t;
        return t;
    }
    // Grain from anywhere: the barn first, then the mill, then the bakeries and counters.
    double TakeGrain(double n)
    {
        double got = TakeBarn(Good.Grain, n);
        for (int r = 0; r < Visible && got < n; r++) got += Take(r, Good.Grain, n - got);
        return got;
    }
    bool TakeAny(Good g)
    {
        for (int r = 0; r < Visible; r++) if (Stock[r, (int)g] >= 1) { Stock[r, (int)g] -= 1; return true; }
        return false;
    }

    // ---------- eating ----------
    // A hungry dot-man walks to the nearest counter with food, or home if the pantry has some. True if they set off.
    bool GoEat(Walker w, bool night)
    {
        if (w.Food >= EatAt || w.Kind != "" || TickNo < w.NoFoodUntil) return false;
        int home = HomeOf(w);
        bool pantry = home >= 0 && Stock[home, (int)Good.Meals] >= 1;
        if (night)
        {
            if (!pantry || w.At == home) return false;
            var (hx, hy) = DoorOf(home);
            GoTo(w, hx, hy, Goal.Eat, home);
            return true;
        }
        int best = -1, bestD = int.MaxValue;
        int v = Visible;
        for (int r = 0; r < v; r++)
        {
            if (!SellsFood(r) || OnSale(r) < 1) continue;
            var (dx, dy) = DoorOf(r);
            int dd = Math.Abs(dx - w.X) + Math.Abs(dy - w.Y);
            if (dd < bestD) { bestD = dd; best = r; }
        }
        if (pantry && (best < 0 || home == w.At)) best = home;
        // Counters bare: under a gentle rule the granary doors open and people take grain for porridge.
        if (best < 0 && FoodRule.open) { int mill = Built.FindIndex(t => t == "mill"); if (mill >= 0 && mill < v && !Burning(mill) && Stock[mill, (int)Good.Grain] + Stock[mill, (int)Good.Flour] >= 1) best = mill; }
        if (best < 0)
        {
            // Nothing anywhere: they give up for a while (and the empty counters feed the panic).
            if (v > 0 && Enumerable.Range(0, v).Any(SellsFood)) lastQueueTick = TickNo;
            w.NoFoodUntil = TickNo + Secs(25, 45);
            return false;
        }
        var (x2, y2) = DoorOf(best);
        GoTo(w, x2, y2, Goal.Eat, best);
        return true;
    }

    // ---------- queues ----------
    // A counter serves one dot-man at a time; the rest stand in line on the road outside its door. In everyday
    // times the line is rarely more than one or two; in a panic hoarders take longer, and a bare counter keeps its
    // line until patience runs out, so bread lines can be seen.
    const double ServeSecs = 1.2;           // seconds to serve one meal's worth over the counter
    const int MaxQueue = 8;                 // longer than this and newcomers give up at the sight of it
    readonly Dictionary<int, List<Walker>> queues = new();
    readonly Dictionary<int, long> servingUntil = new();

    List<Walker> LineAt(int r) { if (!queues.TryGetValue(r, out var q)) queues[r] = q = new(); return q; }
    public int QueueLength(int r) => queues.TryGetValue(r, out var q) ? q.Count : 0;

    // Stand everyone in a counter's line on the road beside its door, head of the line nearest.
    void PlaceInLine(int r)
    {
        if (!queues.TryGetValue(r, out var q) || r >= Visible) return;
        var (dx, dy) = DoorOf(r);
        for (int i = 0; i < q.Count; i++)
        {
            int x = dx - 1 - i;
            if (x < 0) x = dx + 1 + (i - dx + 1);   // the road ends at the town edge: the line bends to the other side
            q[i].X = x; q[i].Y = dy;
            q[i].Path.Clear();
        }
    }

    // Out of the line (gave up, called away, fell ill); the people behind step up.
    void LeaveLine(Walker w)
    {
        int r = w.InLine;
        w.InLine = -1;
        if (queues.TryGetValue(r, out var q) && q.Remove(w)) PlaceInLine(r);
    }

    // Every tick: the counter that is free serves the head of its line, if it has anything to sell.
    void StepQueues()
    {
        foreach (var (r, q) in queues)
        {
            if (q.Count == 0) continue;
            int before = q.Count;
            q.RemoveAll(x => x.Gone || x.InLine != r);
            if (r >= Visible || !SellsFood(r))
            {
                foreach (var x in q) { x.InLine = -1; x.Until = TickNo; }
                q.Clear();
                continue;
            }
            if (TickNo >= servingUntil.GetValueOrDefault(r) && q.Count > 0 && OnSale(r) >= 1)
            {
                var head = q[0];
                q.RemoveAt(0);
                head.InLine = -1;
                Serve(head, r);
                PlaceInLine(r);
            }
            else if (q.Count != before) PlaceInLine(r);
        }
    }

    // At the counter or the pantry: eat a meal, and in a panic buy extra for the pantry at home. At a busy counter,
    // join the line instead.
    void ArriveEat(Walker w)
    {
        int r = w.Dest;
        w.At = r;
        if (r < 0 || r >= Visible) { w.Mode = Mode.Wait; w.Until = TickNo + Secs(1, 2); return; }
        if (!Content.IsHome(Built[r]) && Built[r] != "mill")
        {
            var q = LineAt(r);
            if (q.Count > 0 || TickNo < servingUntil.GetValueOrDefault(r) || OnSale(r) < 1)
            {
                if (q.Count >= MaxQueue)
                {
                    // The line is round the corner: not today.
                    CountTurned();
                    lastQueueTick = TickNo;
                    w.Mode = Mode.Wait;
                    w.Until = TickNo + Secs(1, 2);
                    w.NoFoodUntil = TickNo + Secs(20, 35);
                    return;
                }
                q.Add(w);
                LineNews(q.Count);
                w.InLine = r;
                w.Mode = Mode.Wait;
                w.Until = TickNo + Secs(18, 35);   // patience; Decide takes them out of the line when it runs out
                if (q.Count >= 3) lastQueueTick = TickNo;
                PlaceInLine(r);
                return;
            }
        }
        Serve(w, r);
    }

    // Eat (or fail to) at a counter, mill or pantry, buy for home, and hold the counter for the time it takes.
    void Serve(Walker w, int r)
    {
        w.At = r;
        bool ate = false;
        int bought = 0;
        if (Content.IsHome(Built[r]))
        {
            if (Stock[r, (int)Good.Meals] >= 1) { Stock[r, (int)Good.Meals] -= 1; ate = true; }
        }
        else
        {
            ate = Built[r] == "mill" ? (Take(r, Good.Grain, 1) >= 1 || Take(r, Good.Flour, 1) >= 1) : BuyMeal(r);
            int home = HomeOf(w);
            if (ate && home >= 0 && home < Visible)
            {
                // Everyday shopping: a meal for the pantry if it holds less than one each. In a panic (where the rule
                // allows it) up to two more: hoarding.
                int extra = Stock[home, (int)Good.Meals] < Residents(home) ? 1 : 0;
                // The greedy grab half as much again, the thrifty and the generous half as much (0.68).
                double hand = Is(w, "greedy") ? 1.5 : Is(w, "thrifty") || Is(w, "generous") ? 0.5 : 1;
                if (FoodRule.hoard > 0 && Panic > 0.2) extra += (int)Math.Round(Panic * FoodRule.hoard * 2 * hand * (0.5 + Chance()));
                for (int i = 0; i < extra && home >= 0 && Stock[home, (int)Good.Meals] < RoomFor(home, Good.Meals) && BuyMeal(r); i++) { Stock[home, (int)Good.Meals] += 1; bought++; }
                if (bought >= 2) w.Bag = bought;   // arms full: they walk it home
                HoardNews(bought);
            }
            if (Built[r] != "mill") servingUntil[r] = TickNo + Secs(ServeSecs * (1 + bought), ServeSecs * (1.5 + bought));
        }
        if (ate)
        {
            w.Food = Math.Min(1, w.Food + MealValue);
            w.Mode = Mode.Inside;
            w.Until = TickNo + Secs(3, 8);
        }
        else
        {
            // Sold out by the time they got here.
            CountTurned();
            lastQueueTick = TickNo;
            w.Mode = Mode.Wait;
            w.Until = TickNo + Secs(1, 3);
            w.NoFoodUntil = TickNo + Secs(8, 15);
        }
    }

    bool BuyMeal(int r)
    {
        foreach (var g in new[] { Good.Bread, Good.Greens, Good.Grain })
        {
            if (g == Good.Grain && Built[r] == "bakery") continue;
            if (Stock[r, (int)g] >= 1) { Stock[r, (int)g] -= 1; CountSold(); return true; }
        }
        return false;
    }

    // Coming home: a hungry dot-man eats from the pantry before bed.
    void SupperAtHome(Walker w, int home)
    {
        if (home >= 0 && home < Visible && w.Food < 0.8 && Stock[home, (int)Good.Meals] >= 1)
        {
            Stock[home, (int)Good.Meals] -= 1;
            w.Food = Math.Min(1, w.Food + MealValue);
        }
    }

    // ---------- region and politics ----------
    // Rolled with the events (four times a day): surplus sold, hungry families leave.
    void FoodEvents()
    {
        if (Lots.Count == 0) return;
        double daily = Eaters * NeedPerDay;
        // Grain above what the town needs until the next harvest, plus the days the rule holds back, is sold to the region.
        double surplus = GrainTotal() - (DaysToHarvest() + FoodRule.reserve) * daily * GrainShare();
        if (surplus > 20 && WarOn == null && OncePer(1))
        {
            double sold = TakeGrain(surplus * 0.4);
            if (sold >= 5)
            {
                Pay(Math.Min(3, Math.Round(sold / 30, 1)));
                Say($"{Name} sells {Math.Round(sold)} sacks of grain to the region.", false);
                if (sold >= 30) Npc("steward", "sold", false, Math.Round(sold).ToString());
            }
        }
        // Starving, with seed corn in the loft: the town eats it, and next year pays for it.
        if (Seed > 0 && FoodDays < 0.3 && WeakCount > 0)
        {
            Barn[(int)Good.Grain] += Seed;
            Seed = 0;
            Say($"Starving, {Name} eats its seed corn.");
            Npc("steward", "seed", true);
        }
        // Hungry families give up and leave for a town with bread.
        int pop = Walkers.Count(w => !w.Gone && w.Kind == "");
        double hungry = pop == 0 ? 0 : (double)HungryCount / pop;
        if (hungry > 0.25 && pop > MinPop && Odds(1.5 * hungry))
        {
            var cand = Walkers.Where(w => !w.Lead && !w.Gone && w.Kind == "" && w.Food < HungryAt && w.Mode != Mode.Walk).ToList();
            if (cand.Count > 0)
            {
                var fed = Sisters.Where(s => BlightOn?.Towns.Contains(s.Name) != true && RelPeek(s) > -0.45).ToList();
                var dest = fed.Count > 0 ? Pick(fed) : null;
                SendAway(Pick(cand), Goal.Leave, dest?.Name ?? "");
                Say(dest != null ? $"A hungry family leaves {Name} for {dest.Name}." : "A hungry family takes to the road.");
            }
        }
        // Many hungry: the steward says so (once until it eases).
        if (hungry > 0.3 && OncePer(2)) Npc("steward", "hungry", false, HungryCount.ToString());
    }

    // Days until the next harvest is mostly in (reaping takes about two days of autumn).
    public double DaysToHarvest()
    {
        if (SeasonNo == 2 && Standing > 0) return 0;
        double dayIn = Age / DayMs % DaysPerSeason;
        int seasons = (2 - SeasonNo + 4) % 4;
        if (seasons == 0) seasons = 4;   // autumn, reaped: a whole year to wait
        return seasons * DaysPerSeason - dayIn + 2;
    }

    // Share of meals that must come from grain until then (greens cover the rest while things grow).
    public double GrainShare() => SeasonNo == 3 ? 0.9 : 0.75;

    // How hard the region is pressed, 0..1: the share of towns (sisters and this one) in a blight or a war.
    public double RegionStress()
    {
        int n = Sisters.Count + 1, hit = Sisters.Count(s => Struck(s) > 0) + (Famine || WarOn != null ? 1 : 0);
        return (double)hit / n;
    }

    // A caravan arrives: with grain if we are short, else to buy our surplus. Returns the crier line's goods.
    // When the region is pressed, towns keep their grain at home: harsh rules and the guild stop selling, the rest
    // bring less of it, and it costs more.
    string CaravanFood(Sister from)
    {
        if (FoodDays >= 4) return "";
        double stress = RegionStress();
        var g = Govs.Get(from.Gov);
        if ((g.Harsh >= 0.5 || from.Gov == "guild") && stress > 0.3)
        {
            Say($"The wagons from {from.Name} carry no grain: the {g.Lower} keeps its harvest at home.");
            Hear(0.05);
            return "";
        }
        double price = 2 * (1 + 2 * stress);
        if (Chance() > 1 - 0.8 * stress || Gold < price) return "";
        double n = (20 + 20 * Chance()) * (1 - 0.5 * stress);
        Barn[(int)Good.Grain] += n;
        Pay(-price);
        Npc("steward", "caravan", false, from.Name);
        return "grain";
    }

    // Aid in a famine comes as wagons of grain.
    void AidGrain() => Barn[(int)Good.Grain] += 40;

    // A burnt-down building loses its store.
    void LoseStore(int rank, string was)
    {
        double lost = 0;
        for (int g = 0; g < 5; g++) { lost += Stock[rank, g]; Stock[rank, g] = 0; }
        if (lost >= 10 && !Content.IsHome(was)) { Say($"{Math.Round(lost)} sacks of food burn with the {Content.Label(was)}."); Hear(Math.Min(0.2, lost / 300)); }
    }

    // A newcomer brings provisions to the barn.
    void Provisions() => Barn[(int)Good.Grain] += 16;   // about ten days of meals

    // How hunger weighs on mood, part by part (amounts are negative): the hungry, the weak, and the panic.
    IEnumerable<(string Why, double D)> HungerMoodParts()
    {
        int pop = Walkers.Count(w => !w.Gone && w.Kind == "");
        if (pop == 0) yield break;
        int hungry = HungryCount, weak = WeakCount;
        if (hungry > 0) yield return ($"{hungry} of {pop} hungry", -0.25 * hungry / pop);
        if (weak > 0) yield return ($"{weak} starving", -0.3 * weak / pop);
        if (Panic > 0.005) yield return ($"Fear of running out (panic {Panic * 100:F0}%)", -0.05 * Panic);
    }

    // ---------- words ----------
    public string FoodLine()
    {
        int weak = WeakCount, hungry = HungryCount;
        if (weak > 0) return $"Hunger: {weak} starving, {hungry - weak} hungry";
        if (hungry > 2) return $"Hunger: {hungry} hungry";
        if (Panic > 0.5) return FoodRule.hoard > 0 ? "Bread queues: people hoard" : "Rationing";
        return "";
    }

    // Goods in town by kind (for the ledger bar): grain (with the barn), flour, bread, greens, pantry meals.
    public double[] GoodsTotals()
    {
        var t = new double[5];
        t[0] = Barn[0]; t[3] = Barn[3];
        for (int r = 0; r < Visible; r++) for (int g = 0; g < 5; g++) t[g] += Stock[r, g];
        return t;
    }
    public static readonly string[] GoodNames = { "grain", "flour", "bread", "greens", "pantry meals" };
    public static readonly int[] GoodColors = { 0xd8b040, 0xf0e6c8, 0xa86a30, 0x5aa040, 0xc87850 };
    public int JobCount => jobs.Count;
    public int Carriers => Walkers.Count(w => !w.Gone && w.Good >= 0);

    // What the ledger's food tooltip shows, gathered in one place so Panels only draws it.
    public record FoodReport(double Days, string Who, double ToHarvest, double Reserve, double Keep,
        double BarnGrain, double BarnGreens, double[] Town, double Seed,
        string Fields, string Crop, double CropQ, int Carriers, int Hungry, int Weak, int Dead, string RuleLine);

    public FoodReport GetFoodReport()
    {
        var t = GoodsTotals();
        int adults = Walkers.Count(w => !w.Gone && w.Kind == "" && w.Kid == 0);
        int kids = Walkers.Count(w => !w.Gone && w.Kind == "" && w.Kid != 0);
        string who = Plural(adults, "dot-man", "dot-men") + (kids > 0 ? " and " + Plural(kids, "dotlet", "dotlets") : "") + $", {NeedPerDay:F1} meals a day each";
        var town = new[] { t[0] - Barn[0], t[1], t[2], t[3] - Barn[3], t[4] };
        string[] sn = { "sown, growing", "growing", "harvest in", "bare for winter" };
        string fields = SeasonNo == 2 && Standing > 0 ? $"{Math.Round(Standing)} sacks still to cut" : sn[SeasonNo];
        string crop = SeasonNo <= 1 ? CropWord : "";
        var r = FoodRule;
        double th = DaysToHarvest();
        string rule = $"Under the {G.Lower}, grain for {r.reserve:0} days past the next harvest is held back and the rest is sold. "
            + (r.hoard > 0 ? "In a panic, people hoard." : "In a panic, food is rationed.")
            + (r.open ? "" : " The granary stays shut to the hungry.");
        return new FoodReport(Math.Max(0, FoodDays), who, th, r.reserve, th + r.reserve, Barn[0], Barn[3], town, Seed,
            fields, crop, CropQ, Carriers, HungryCount, WeakCount, HungerDead, rule);
    }
    static string Plural(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";
    string CropWord => CropQ < 0.55 ? "poor" : CropQ < 0.85 ? "thin" : CropQ < 1.1 ? "fair" : "rich";

    // Hover tip lines for one building's store.
    public IEnumerable<string> StoreLines(int rank)
    {
        if (rank >= Visible) yield break;
        var parts = new List<string>();
        for (int g = 0; g < 4; g++) if (Stock[rank, g] >= 0.5) parts.Add($"{Math.Round(Stock[rank, g])} {GoodNames[g]}");
        string t = Built[rank];
        if (parts.Count > 0) yield return (IsSeller(t) || t == "bakery" ? "On the counter and in store: " : "In store: ") + string.Join(", ", parts) + ".";
        else if (IsSeller(t) || t == "bakery") yield return "The shelves are bare.";
        if (Content.IsHome(t))
        {
            double m = Stock[rank, (int)Good.Meals];
            yield return m >= 0.5 ? $"Pantry: {Math.Round(m)} meals." : "The pantry is empty.";
            int hungry = Walkers.Count(w => !w.Gone && w.Kind == "" && HomeOf(w) == rank && w.Food < HungryAt);
            if (hungry > 0) yield return hungry == 1 ? "Someone here is hungry." : $"{hungry} here are hungry.";
        }
        if (t == "mill") yield return "The granary: grain keeps here, and is ground to flour for the ovens.";
    }

    // ---------- test ----------
    // --hungry: a town whose stores are nearly empty, with hungry people, for a screenshot.
    public void TestHunger()
    {
        Barn[0] = 6; Barn[3] = 2;
        for (int r = 0; r < MaxLots; r++) for (int g = 0; g < 5; g++) Stock[r, g] *= 0.05;
        int i = 0;
        foreach (var w in Walkers) if (w.Kind == "") w.Food = (i++ % 3) switch { 0 => 0.1, 1 => 0.3, _ => 0.7 };
    }
}
