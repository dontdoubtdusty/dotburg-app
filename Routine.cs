namespace Dotburg;

// Routine voices (0.65): ordinary days get talked about too, not only disasters. The steward sends a morning
// report from yesterday's real numbers; the baker and the grocer post when something changes at their counters
// (sold out, restocked, a line at the door, a hoarder, the first greens). Every line is tied to a change in the
// town, each voice has a daily cap and a gap between lines, and all of it uses the chat's own random generator,
// so talk never changes what happens.
partial class Sim
{
    const int VoiceCap = 4;                 // lines a day each for the baker and the grocer
    const double VoiceGapDays = 0.12;       // at least this long between two lines from one voice

    // What happened at the counters since the last morning report (not saved: a reload starts a fresh day).
    int bakedToday, soldToday, tripsToday, turnedToday, longestToday;
    int reportDay;
    readonly Dictionary<string, int> voiceCount = new();
    readonly HashSet<string> saidToday = new();   // "role.event" said today: each kind of news once a day (0.87.3)
    readonly List<double> storeLog = new();      // days of food at each morning report since the last trend was told (0.87.3)
    int hungerDay = -99, hungerSaid, millDay = -99;   // when hunger and the idle mill were last reported, and how many hungry then
    string lastCrop = "";                        // the crop word at the last report
    readonly Dictionary<string, long> voiceLast = new();
    bool breadOut, shelvesBare, greensSeen = true;

    // Counted where the food moves (Food.cs calls these).
    void CountBaked(double n) => bakedToday += (int)Math.Round(n);
    void CountSold() => soldToday++;
    void CountTrip() => tripsToday++;
    void CountTurned() => turnedToday++;
    void CountLine(int n) => longestToday = Math.Max(longestToday, n);

    // A notable speaks if they have not said too much today or too recently. Urgent lines skip the gap.
    void Voice(string role, string ev, string a = "", bool urgent = false)
    {
        if (Quiet) return;
        int n = voiceCount.GetValueOrDefault(role);
        if (n >= VoiceCap) return;
        if (ev != "small" && ev != "winter" && saidToday.Contains($"{role}.{ev}")) return;   // no repeat alerts the same day
        if (!urgent && TickNo - voiceLast.GetValueOrDefault(role, -1_000_000) < VoiceGapDays * DayMs / TickMs) return;
        voiceCount[role] = n + 1;
        saidToday.Add($"{role}.{ev}");
        voiceLast[role] = TickNo;
        Npc(role, ev, false, a);
    }

    // Chance per call of something that should happen about `perDay` times a day, given this step's length.
    bool ChatOdds(double perDay, double dtDays) => chatRng.NextDouble() < perDay * dtDays;

    // Once a second with the food step: the morning report, then the baker and the grocer.
    void StepVoices(double dt)
    {
        if (Quiet || Lots.Count == 0) return;
        double d = Daylight();
        if (d > 0.3 && DayNumber != reportDay)
        {
            if (reportDay > 0) MorningReport();
            if (reportDay > 0 && (DayNumber - 1) % WeekDays == 0) BurialSummary();
            reportDay = DayNumber;
            bakedToday = soldToday = tripsToday = turnedToday = longestToday = 0;
            voiceCount.Clear();
            saidToday.Clear();
        }
        if (d < 0.3) return;   // shops are shut at night
        int v = Visible;
        bool bakery = false, shop = false;
        double bread = 0, onSale = 0, greens = 0;
        for (int r = 0; r < v; r++)
        {
            string t = Built[r];
            if (t == "bakery") { bakery = true; bread += Stock[r, (int)Good.Bread]; }
            if (IsSeller(t)) { shop = true; onSale += OnSale(r); greens += Stock[r, (int)Good.Greens]; bread += Stock[r, (int)Good.Bread]; }
        }
        if (bakery) StepBaker(bread, dt);
        if (shop) StepGrocer(onSale, greens, dt);
    }

    // The baker: sold out, back in business, no flour, the rush in a panic, and now and then small talk.
    void StepBaker(double bread, double dt)
    {
        if (!breadOut && bread < 1 && soldToday > 0) { breadOut = true; Voice("baker", "out"); }
        else if (breadOut && bread >= 12) { breadOut = false; Voice("baker", "back"); }
        int mill = Built.FindIndex(t => t == "mill");
        bool noFlour = true;
        for (int r = 0; r < Visible; r++) if (Built[r] == "bakery" && Stock[r, (int)Good.Flour] >= 1) noFlour = false;
        if (mill >= 0 && mill < Visible && noFlour && Stock[mill, (int)Good.Flour] < 1 && ChatOdds(2, dt)) Voice("baker", "noflour");
        if (Panic > 0.5 && FoodRule.hoard > 0 && ChatOdds(1.5, dt)) Voice("baker", "rush");
        if (Panic < 0.2 && !breadOut && ChatOdds(0.8, dt)) Voice("baker", SeasonNo == 3 ? "winter" : "small");
    }

    // The grocer: shelves bare and restocked, the first and last greens, and small talk.
    void StepGrocer(double onSale, double greens, double dt)
    {
        if (!shelvesBare && onSale < 1 && soldToday > 0) { shelvesBare = true; Voice("grocer", "bare"); }
        else if (shelvesBare && onSale >= 15) { shelvesBare = false; Voice("grocer", "restock"); }
        if (!greensSeen && greens >= 5) { greensSeen = true; Voice("grocer", "greens"); }
        else if (greensSeen && greens < 1 && SeasonNo == 3) { greensSeen = false; Voice("grocer", "nogreens"); }
        if (Panic < 0.2 && !shelvesBare && ChatOdds(0.8, dt)) Voice("grocer", "small");
    }

    // A queue reached the door (called from Food.cs as it grows).
    void LineNews(int n)
    {
        CountLine(n);
        if (n == 5) Voice("grocer", "line", n.ToString());
    }

    // Someone bought far more than a meal (called from Food.cs).
    void HoardNews(int bought)
    {
        if (bought >= 3) Voice("grocer", "hoard", bought.ToString());
    }

    // The steward's morning report (0.87.3): one headline about the stores, the fields or the coffers, with its reason,
    // and only when something changed; a quiet morning says nothing. Lines, hoarding and the counters are the grocer's;
    // running low has its own alarm ("low" in Food.cs).
    void MorningReport()
    {
        double days = Math.Max(0, Math.Round(FoodDays)), th = DaysToHarvest();
        storeLog.Add(days);
        if (storeLog.Count > 8) storeLog.RemoveAt(0);
        double then = storeLog[0];
        int span = storeLog.Count - 1;
        string Days(double n) => n == 1 ? "1 day" : $"{n} days";
        static string Ago(int n) => n switch { 3 => "three days ago", 4 => "four days ago", 5 => "five days ago", 6 => "six days ago", _ => "a week ago" };
        int mill = Built.FindIndex(t => t == "mill");
        string? why = mill >= 0 && mill < Visible && Stock[mill, (int)Good.Grain] < 5 && GrainTotal() > 20 ? "the mill stood idle for want of grain" : null;
        // Hunger at the counters is news when it starts or gets clearly worse, not every morning it goes on.
        bool hungerNews = turnedToday >= 3 && (DayNumber - hungerDay > 3 || turnedToday >= hungerSaid * 1.5);
        if (turnedToday < 3 && DayNumber - hungerDay > 3) hungerSaid = 0;
        string? say = null;
        if (hungerNews)
        {
            (hungerDay, hungerSaid) = (DayNumber, turnedToday);
            say = $"{turnedToday} went home hungry from empty counters :S" + (why != null ? $" Cause: {why}" : "");
        }
        else if (span >= 3 && Math.Abs(then - days) >= 3)
        {
            say = then > days
                ? $"Stores are falling: {Days(days)} of food left, down from {then} {Ago(span)}" + (th > days ? ". At this rate we run short before the reaping" : "")
                : $"Stores are rising: {Days(days)} of food now, up from {then} {Ago(span)}";
            storeLog.RemoveRange(0, storeLog.Count - 1);   // the next trend is measured from here
        }
        else if (why != null && DayNumber - millDay > 2) { millDay = DayNumber; say = char.ToUpper(why[0]) + why[1..]; }
        else if (SeasonNo <= 1 && th > 0 && lastCrop != "" && CropWord != lastCrop) say = $"The crop looks {CropWord} now, {Days(Math.Round(th))} to the reaping";
        else if (Gold < 8 && chatRng.NextDouble() < 0.34) say = $"The treasury is down to {Math.Round(Gold)} gold";
        lastCrop = SeasonNo <= 1 ? CropWord : "";
        if (say != null) Npc("steward", "report", false, say + ".");
    }

    // Lines for the new voices and the report (merged into NpcSay).
    static readonly Dictionary<string, string[]> RoutineSay = new()
    {
        ["steward.report"] = new[] { "{a}", "(case) {a}", "(meal) {a}" },
        ["physician.week"] = new[] { ":( {a} these last days. I did what I could.", "(bouquet) {a} since I last wrote. Flowers at the churchyard.", ":| {a}. The old go, the young carry on." },
        ["physician.noweek"] = new[] { "(*) Not a single funeral in four days!", ":D Nobody buried this week. I'll take it.", "(apple) Clean bill of health for the whole town." },
        ["baker.out"] = new[] { ":S Sold out of bread already!", "Not a crumb left on my racks :(", "(!) Out of bread. More in an hour, if the flour comes." },
        ["baker.back"] = new[] { "(food) Fresh bread! The ovens are hot again :D", "Loaves out of the oven. Come and get them :)", "(Y) Back in business. Racks are full." },
        ["baker.noflour"] = new[] { ":| No flour from the mill today. I'm grinding by hand (hammer)", "Where is my flour? The ovens are cold :@" },
        ["baker.rush"] = new[] { ":S They're buying loaves by the armful. I can't bake fast enough!", "(!) Six loaves each! Who eats six loaves?" },
        ["baker.small"] = new[] { "Up at four, as always (zz)", "Rye today (food)", "Smells good out there, doesn't it? :)", "Burnt my thumb on the oven again :(", "Seed loaves this morning (clover)" },
        ["baker.winter"] = new[] { "Warmest spot in town is my bakery :)", "Cold out. Come in by the ovens (food)" },
        ["grocer.bare"] = new[] { ":( The shelves are bare. Nothing but crumbs.", "(!) Sold out. Tell them not to queue.", "Empty counter. Waiting on the carriers :|" },
        ["grocer.restock"] = new[] { "(Y) Restocked! Bread and greens on the counter.", "Shelves full again :)", "(food) Fresh in. Come early." },
        ["grocer.line"] = new[] { ":S {a} in line and out the door.", "(!) There's a line of {a} outside. I'm going as fast as I can." },
        ["grocer.hoard"] = new[] { ":@ Someone just bought {a} meals in one go. Leave some for the rest!", "(case) {a} meals to one family. They're scared, and it's catching." },
        ["grocer.greens"] = new[] { "(clover) First greens of the year on the counter!", "Fresh greens are in :D" },
        ["grocer.nogreens"] = new[] { ":| No greens till spring. Bread and porridge it is.", "Last of the greens is gone. Winter :(" },
        ["grocer.small"] = new[] { "Slow morning (zz)", "The usual crowd, the usual gossip :)", "(apple) Apples are good this week.", "Someone's cat is in the cabbages again :@", "Prices steady. Nobody's complaining... yet 8-)" },
    };
}
