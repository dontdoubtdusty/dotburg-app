using System.Text.RegularExpressions;

namespace Dotburg;

enum Mode { Walk, Wait, Inside }
enum Goal { Home, Visit, Stroll, Fest, Leave, Taken, Fight, Haul, Eat, Build, Fetch }

// One dot-man (or, later, a visitor). Positions are in town pixels and always sit on a road.
class Walker
{
    public int Id;                // stable for life and saved: their habits are drawn from it (Sim.Trait01)
    public int C;                 // colour index
    public int H;                 // home: index into Built
    public int X, Y;
    public List<(int x, int y)> Path = new();
    public Mode Mode = Mode.Inside;
    public Goal Goal = Goal.Home;
    public int Dest = -1;         // building rank being walked to, or -1
    public int At = -1;           // building rank last entered or stood at, or -1
    public long Until;            // tick a wait or a stay inside ends
    public bool Sleep;            // inside at home until morning
    public int Period;           // ticks per step
    public bool Owl;              // sometimes up at night
    public int Lane;              // 0 or 1: which road pixel to use on vertical roads
    public bool Lead;             // the town's leader (later stage)
    public bool Gone;             // left town or died; removed after this tick
    public bool Sick;             // showing illness: goes home to bed (set once incubation ends)
    public bool Hurt;             // someone who matters is wounded or poisoned and keeps to bed (Health.cs)
    public int Dis = -1;          // index into Diseases while infected, else -1
    public long Incub;            // tick the illness shows
    public double Sev, Imm;       // severity and immunity, 0..1: whichever reaches 1 first decides
    public double Resist = 1;     // this dot-man's luck against this illness (immunity speed)
    public int ImmDis = -1;       // the illness they recently beat
    public long ImmUntil;         // ...and until when they cannot catch it again
    public long Kid;             // tick a child grows up, 0 for an adult
    public string To = "";        // for Goal.Leave: the sister town they are moving to, "" for nowhere
    public string Kind = "";      // "" for a citizen; visitors: envoy, team, spy, refugee, cart
    public long Stay;             // visitors: tick they head home
    public Walker? Follow;        // a child marching behind this guard (youth brigade); not saved
    public int PX, PY;            // guards: where they stood before their last step, so a child can walk in their tracks
    public double Food = 0.8;     // how full they are, 0..1 (Food.cs); saved
    public double Starve;         // days spent with an empty stomach; saved
    public int Good = -1;         // carrying: the good (a Good), or -1
    public double Load;           // ...how much of it is in the sack
    public int Src = -1, Dst = -1;   // ...from which store (-1: the barn at the fields) to which
    public long NoFoodUntil;      // found nothing to eat: does not look again until this tick
    public double Born = double.NaN;   // town clock at birth (Lives.cs); NaN until given an age; saved as years
    public string Fading = "";    // dying of an ordinary cause: they go home and die there (Lives.cs); not saved
    public int InLine = -1;       // standing in the queue at this counter, or -1; not saved
    public string First = "", Last = "";   // their name (Names.cs, 0.78); saved
    public int Spouse;            // the id of the one they married, 0 if never; a dead spouse leaves them free (0.78); saved
    public int FedBy;             // walking to this friend's pantry for a meal (0.78); not saved
    public int Bag;               // meals bought for the pantry, carried home in their arms (put away at the door); not saved
    public List<Bond> Bonds = new();   // grudges and friendships, at most three (Bonds.cs); saved
    public bool Held;             // arrested: stands still until a guard reaches them (Politics.cs); not saved
    public Walker? Prisoner;      // guards: the arrested dot-man they are walking to fetch; not saved
}

// Scaffolding over a lot while something is built or upgraded. Up is the old type for an upgrade.
// Need is the work in ticks for two builders; Done grows only while builders stand at the site (Builders.cs).
class Works
{
    public int Rank;
    public double Need, Done;
    public string To = "";
    public string? Up;
}

// The whole simulation. Stage 1: the town, its day and night, its seasons, its people and its growth.
// Weather, government, events and the region of sister towns are ported in later stages.
// The class is split into partial files: Sim (state and helpers), Layout, Walkers, Growth, Render.
partial class Sim
{
    // ---------- tuning ----------
    public const int TickMs = 110;
    public const double DayMs = 360_000;          // one full day and night
    public static readonly int[] Costs = { 3, 5, 7, 9 };         // units per growth event, by tier
    public static readonly int[] PopCaps = { 40, 60, 80, 100 };   // most dot-men a town holds, by tier
    public static readonly string[] TierNames = { "Hamlet", "Village", "Town", "City" };
    public const int MaxLots = 49;
    public const int TileW = 9;                   // 2 road pixels + 7 for a building
    public const int TileH = 8;                   // 2 road pixels + 6 for a building
    public const int DaysPerSeason = 4;
    public const int W = 72;                      // town picture size: exactly 7 x 7 lots
    public const int H = 60;
    static readonly double[] WorksMs = { 8_000, 12_000 };
    static readonly double[] ChatterMs = { 70_000, 140_000 };

    // ---------- state ----------
    public Random Rng;
    public int Founded = 1;
    public int Scheme;
    public string Name = "";
    public int Tier = 1;
    public double Age = 0.3 * DayMs;              // open time so far, picks the time of day
    public double Pool;                           // unspent units
    public double Lifetime;                       // every unit ever earned
    public List<string> Built = new() { "house" };
    public List<Walker> Walkers = new();
    public int NextId = 1;                        // the next dot-man's Id (saved)
    public List<Works> WorksList = new();
    public long TickNo;
    public string Crier = "";
    public List<string>? SayLog;                  // test runs only: every crier line, in order
    long chatterDue;
    double lastPhase = -1;
    int lastSeason = -1;
    public int MillFrame;
    public bool StaticDirty = true;
    public bool Quiet;                            // true while a save is applied: no scaffolding, no crier lines

    public Sim(int seed)
    {
        Rng = new Random(seed);
    }

    // ---------- helpers ----------
    public static int Clamp(int v, int lo, int hi) => Math.Max(lo, Math.Min(hi, v));
    public static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));
    // JavaScript's Math.round (halves go up), which the mod's pixel maths relies on.
    public static int Round(double v) => (int)Math.Floor(v + 0.5);
    public int RandInt(int a, int b) => a + Rng.Next(b - a + 1);
    public T Pick<T>(IList<T> a) => a[Rng.Next(a.Count)];
    public double Chance() => Rng.NextDouble();
    // Ticks for a duration chosen between a and b seconds.
    public int Secs(double a, double b) => Round((a + Chance() * (b - a)) * 1000 / TickMs);

    public static int Hash(int x, int y) => (int)((uint)(unchecked((int)(x * 73856093L)) ^ unchecked((int)(y * 19349663L))) % 97);

    // Blend two 0xRRGGBB colours.
    public static int Mix(int a, int b, double t)
    {
        int Ch(int s) => Round(((a >> s) & 255) * (1 - t) + ((b >> s) & 255) * t);
        return (Ch(16) << 16) | (Ch(8) << 8) | Ch(0);
    }

    // A colour as seen at night amount n (0..1), with a warm cast at dusk and dawn.
    public static int Tint(int c, double n, double warm)
    {
        double r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255;
        if (warm > 0)
        {
            r += (255 - r) * 0.1 * warm;
            g += (150 - g) * 0.06 * warm;
            b *= 1 - 0.12 * warm;
        }
        if (n > 0)
        {
            r += (r * 0.28 + 14 - r) * n;
            g += (g * 0.33 + 22 - g) * n;
            b += (b * 0.52 + 58 - b) * n;
        }
        return (Round(r) << 16) | (Round(g) << 8) | Round(b);
    }

    Scheme Sc => Content.Schemes[Scheme];

    // ---------- day, night and seasons ----------
    public double Phase => (Age % DayMs) / DayMs;   // 0 midnight, .5 noon

    // Daylight 0..1 in 24 steps, so the whole picture changes in small jumps.
    public double Daylight()
    {
        double d = Clamp(0.5 + 0.8 * Math.Sin(2 * Math.PI * (Phase - 0.25)), 0, 1);
        return Math.Round(d * 24, MidpointRounding.AwayFromZero) / 24;
    }

    public string DayName()
    {
        double p = Phase;
        if (p >= 0.2 && p < 0.3) return "dawn";
        if (p >= 0.3 && p < 0.45) return "morning";
        if (p >= 0.45 && p < 0.7) return "afternoon";
        if (p >= 0.7 && p < 0.8) return "dusk";
        return "night";
    }

    public int SeasonNo => (int)(Math.Floor(Age / DayMs / DaysPerSeason) % 4);
    public Season SeasonNow => Content.Seasons[SeasonNo];
    public int DayNumber => (int)Math.Floor(Age / DayMs) + 1;

    // ---------- the crier and the chronicle ----------
    // A line counts as major if it matches this (tier changes, charters, rule changes, leader deaths, wars, plagues,
    // every harvest, burn-downs, absorptions and collapses, and since 0.66 the good news too: trade agreements, aid
    // sent, tribute paid). First buildings of each kind are noted directly (Growth.cs).
    static readonly Regex Major = new(@"\b(is now an? (hamlet|village|town|city)|chartered|founded|overthr\w*|revolution|coup|assassin\w*|takes the throne|crowned|dies|died|plague|famine|harvest fails|harvest is in|bumper|great fire|burns down|perishes in the fire|lives? (is|are) lost|war|defeats|election day|re-elected|steps down|takes power|absorbs|collapses|chooses|settles on|stabbing|saboteur|taken away|outbreak|life (is|was) lost|victory|defeat|trade agreement|sends aid|pays tribute|comet|washes away|follows the prophet|a feud now|feud is over|ends the feud|feud rest)\b", RegexOptions.IgnoreCase);
    readonly List<string> recentNotes = new();
    public readonly List<string> ChronicleQueue = new();   // lines waiting to be written to the chronicle file

    public void Note(string text)
    {
        if (Quiet || recentNotes.Contains(text)) return;
        recentNotes.Insert(0, text);
        if (recentNotes.Count > 12) recentNotes.RemoveAt(recentNotes.Count - 1);
        // Written by the chronicler (Chronicler.cs): in their voice, signed, or left out if they would rather not.
        EnsureChronicler();
        if (Pen(text) is { } penned) Write(penned);
    }

    // Put a line on the town crier.
    public void Say(string text, bool note = true)
    {
        if (Quiet) return;
        SayLog?.Add(text);
        if (note && Major.IsMatch(text)) Note(text);
        else MaybeChronicle(text);   // the chronicler may write it down anyway, if it is the kind of thing they care about
        Crier = text;
        CrierLine(text);   // every call is kept in the crier's Messenger chat (Messenger.cs)
        Pop("", text, "", false, true);
        chatterDue = TickNo + Secs(ChatterMs[0] / 1000, ChatterMs[1] / 1000);
    }

    static string An(string word) => (Regex.IsMatch(word, "^[aeiou]") ? "an " : "a ") + word;

    // An idle line from what the town has and the hour.
    string Chatter()
    {
        var o = new List<string>();
        double p = Phase;
        bool night = Daylight() < 0.25;
        bool Has(string t) => Built.Contains(t);
        if (Has("bakery") && (p < 0.35 || p > 0.9)) o.Add("The smell of fresh bread drifts by.");
        if (Has("inn") && night) o.Add("Laughter from the inn.");
        if (Has("mill") && !night) o.Add("The mill grinds flour.");
        if ((Has("church") || Has("cathedral")) && p > 0.25 && p < 0.35) o.Add("Bells ring for morning.");
        if (Has("well") && !night) o.Add("Gossip at the well.");
        if (Has("fountain") && !night) o.Add("Coins glint in the fountain.");
        if (Has("school") && !night && p < 0.65) o.Add("The school bell rings.");
        if (Has("market") && !night) { o.Add("Market stalls are busy."); o.Add("Someone haggles over turnips."); }
        if (Has("shop") && !night) o.Add("The shop has new hats in.");
        if (Has("park") && !night && !Stormy && SeasonNo != 3) o.Add("Ducks paddle in the park pond.");
        if (Has("park") && night && SeasonNo == 1 && Weather != Wx.Rain && Weather != Wx.Snow) o.Add("Fireflies over the park.");
        if (LandCount(PondK) > 0 && !night && !Stormy && SeasonNo != 3) o.Add("Ducks paddle on the pond.");
        if (LandCount(PondK) > 0 && SeasonNo == 3) o.Add("Dotlets slide on the frozen pond.");
        if (LandCount(Marsh) > 0 && night && SeasonNo <= 1) o.Add("Frogs sing in the marsh.");
        if (LandCount(Marsh) > 0 && !night && SeasonNo != 3) o.Add("A heron stands in the reeds.");
        if (LandCount(Grove) > 0 && !night && SeasonNo <= 1) o.Add("Birdsong from the grove.");
        if (LandCount(Grove) > 0 && SeasonNo == 2) o.Add("Dotlets gather nuts in the grove.");
        if (LandCount(Rocks) > 0 && !night) o.Add("Masons cut stone at the rocks.");
        if (cat != null) { o.Add("The cat naps on a doorstep."); o.Add("The cat stalks a moth."); }
        if (Weather == Wx.Rain || Weather == Wx.Storm) { o.Add("Puddles on every road."); o.Add("Umbrellas out."); }
        if (Weather == Wx.Storm) { o.Add("Shutters bang in the wind."); o.Add("Dotlets count between the flash and the thunder."); }
        if (Weather == Wx.Fog) o.Add("Lamps glow dimly in the fog.");
        if (flood >= 0.4) { o.Add("Ducks paddle down the flooded street."); o.Add("Dotlets sail paper boats in the low street."); }
        if (Comet) { o.Add("Everyone talks about the star with the tail."); o.Add(night ? "Dotlets stay up to watch the star with the tail." : "Nobody slept much: the star with the tail again."); }   // not "comet": that word is chronicled
        if (MarketOn) { o.Add("Someone haggles over turnips at the stalls."); o.Add("A trader swears the cabbages are fresh."); }
        if (Weather == Wx.Snow) o.Add("Dotlets build a snowman.");
        int se = SeasonNo;
        if (se == 0) { o.Add("Lambs in the fields."); o.Add("Blossom on the hedges."); }
        if (se == 1 && !night) o.Add("A hot afternoon; dogs lie in the shade.");
        if (se == 2) o.Add("Apples are pressed for cider.");
        if (se == 3) o.Add("Firewood is stacked by every door.");
        if (night) { o.Add("The town is quiet."); o.Add("An owl hoots."); }
        else { o.Add("Neighbours wave hello."); o.Add("Someone lost a hat."); o.Add("Washing flaps on a line."); }
        return Pick(o);
    }

    // ---------- the tick ----------
    // One step of the sim. dtMs is the time since the last tick (already scaled by any speed control).
    public void Tick(double dtMs)
    {
        if (Lots.Count == 0) return;
        Age += dtMs;
        EvClock += dtMs;
        TickNo++;
        // A new season repaints the grass and trees.
        int se = SeasonNo;
        if (se != lastSeason)
        {
            if (lastSeason >= 0)
            {
                StaticDirty = true;
                Say($"{Content.Seasons[se].Say} {Name}.");
            }
            lastSeason = se;
        }
        double d = Daylight();
        foreach (var w in Walkers) StepWalker(w, d);
        if (Walkers.Any(w => w.Gone)) Walkers.RemoveAll(w => w.Gone);
        StepWorks();
        StepLife(d, dtMs);
        StepFires(dtMs);
        UpdateMood(dtMs);
        StepFood();
        if (TickNo % DisEvery == 0) StepDisease();
        if (TickNo % HealthEvery == 0) StepHealth();
        polClock += dtMs;
        if (polClock >= PolStepMs) { polClock -= PolStepMs; Politics(); Events(); }
        StepGuards();
        StepBrigade(Daylight());
        StepVisitors();
        if (TickNo % 27 == 0) StepPals();   // the leader's own contacts and the chronicler's pen, every 3 s or so
        if (TickNo % 27 == 13) StepHearsay();   // what reaches the leader (Hearsay.cs), also every 3 s or so
        double p = Phase;
        if (lastPhase >= 0 && lastPhase < 0.25 && p >= 0.25) Say($"Day {DayNumber} dawns over {Name}.");
        lastPhase = p;
        if (TickNo >= chatterDue) Say(Chatter());
        int mf = (int)(TickNo / 6 % 2);
        if (mf != MillFrame)
        {
            MillFrame = mf;
            if (Built.Contains("mill")) StaticDirty = true;
        }
        if (StaticDirty)
        {
            PaintStatic();
            StaticDirty = false;
        }
    }

    // A brand new region: the first hamlet with one native.
    public void FreshRegion()
    {
        Founded = 1;
        Scheme = Rng.Next(Content.Schemes.Length);   // a new region starts in any landscape, so the first dot-men are any colour
        Name = Content.TownName(Rng, new HashSet<string>(), Founded);
        Tier = 1;
        Built = new() { "house" };
        // Ids start somewhere new in every region (0.84): traits, names and habits come from the id, so starting at 1
        // gave every restarted region the same first dot-men.
        NextId = 1 + Rng.Next(1_000_000);
        Walkers = new() { NewWalker(Sc.Dot, 0) };
        Pool = 0;
        WorksList.Clear();
        Sisters.Clear();
        Visitors.Clear();
        Gold = 12;
        EvClock = 0;
        LastFate = 0;
        Weddings = 0;
        ResetFires();
        ResetTownEvents();
        Ill = null;
        ResetWar();
        Age = 0.3 * DayMs;
        NewLand();
        Build();
        ResetGov();
        InitFood();
    }
}
