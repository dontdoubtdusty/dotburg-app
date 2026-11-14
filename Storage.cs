using System.Text.Json;

namespace Dotburg;

// Where the app keeps its files, and how the town is saved and the chronicle written.
static class Storage
{
    // DOTBURG_DIR overrides the folder (for tests, so they never touch the real town).
    public static readonly string Dir = Environment.GetEnvironmentVariable("DOTBURG_DIR") is { Length: > 0 } dir ? dir : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dotburg");
    public static string SavePath => Path.Combine(Dir, "town.json");
    public static string ChroniclePath => Path.Combine(Dir, "chronicle.txt");

    class RuinDto { public int R { get; set; } public string Was { get; set; } = "house"; public double In { get; set; } }
    class Dot { public int C { get; set; } public int H { get; set; } public bool L { get; set; } public int I { get; set; } public double F { get; set; } = 0.8; public double S { get; set; } public double Y { get; set; } public List<Bond>? B { get; set; } public string? Fn { get; set; } public string? Ln { get; set; } public int P { get; set; } }   // Y: age in years, 0 if unknown (older saves)
    class StoreDto { public int R { get; set; } public double[] G { get; set; } = Array.Empty<double>(); }
    class FoodDto
    {
        public double[] Barn { get; set; } = Array.Empty<double>();
        public List<StoreDto> Stores { get; set; } = new();
        public double CropQ { get; set; } = 1;
        public double Standing { get; set; }
        public double Seed { get; set; }
        public double Panic { get; set; }
        public int Season { get; set; } = -1;
    }
    class SisterDto
    {
        public string Name { get; set; } = "";
        public int Scheme { get; set; }
        public int[] Counts { get; set; } = Array.Empty<int>();
        public int Cap { get; set; }
        public string Gov { get; set; } = "council";
        public double? Rel { get; set; }
        public double? Gold { get; set; }
        public string? RName { get; set; }
        public int RGen { get; set; } = 1;
        public double RSince { get; set; }
        public double RBorn { get; set; }
        public string[]? RTraits { get; set; }
    }

    class CondDto
    {
        public string K { get; set; } = "";
        public string P { get; set; } = "";
        public double S { get; set; }
        public double Pk { get; set; }
        public double T { get; set; } = -1;
        public double In { get; set; } = -1;
        public double Im { get; set; }
        public double Since { get; set; }
        public string Why { get; set; } = "";
    }
    class BodyDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Was { get; set; } = "";
        public double Born { get; set; }
        public double Blood { get; set; } = 1;
        public int Attacks { get; set; }
        public List<CondDto> Conds { get; set; } = new();
    }

    class LineDto { public string F { get; set; } = ""; public string T { get; set; } = ""; public int D { get; set; } public bool O { get; set; } public string S { get; set; } = ""; public int H { get; set; } = -1; }
    class ConvoDto
    {
        public string Key { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Title { get; set; } = "";
        public string Role { get; set; } = "";
        public int Seen { get; set; }
        public bool Gone { get; set; }
        public double Last { get; set; }
        public List<LineDto> Lines { get; set; } = new();
    }

    class SaveData
    {
        public int V { get; set; } = 1;
        public string Name { get; set; } = "";
        public int Scheme { get; set; }
        public int Tier { get; set; } = 1;
        public int Founded { get; set; } = 1;
        public double Age { get; set; }
        public double Pool { get; set; }
        public double Lifetime { get; set; }
        public int NextId { get; set; }
        public List<string> Built { get; set; } = new();
        public List<Dot> Dots { get; set; } = new();
        public string Weather { get; set; } = "Clear";
        public double[] Wx { get; set; } = Array.Empty<double>();
        public string Gov { get; set; } = "elders";
        public double Mood { get; set; } = 0.6;
        public string LeaderName { get; set; } = "";
        public int LeaderGen { get; set; } = 1;
        public double LeaderSince { get; set; }
        public string[] LeaderTraits { get; set; } = Array.Empty<string>();
        public double LastElection { get; set; }
        public int Honoured { get; set; }
        public Dictionary<string, int> Regnal { get; set; } = new();
        public double Gold { get; set; } = 12;
        public double EvClock { get; set; }
        public double LastFate { get; set; }
        public List<SisterDto> Sisters { get; set; } = new();
        public List<RuinDto> Ruins { get; set; } = new();
        public List<ConvoDto> Chats { get; set; } = new();
        public List<BodyDto> Bodies { get; set; } = new();
        public FoodDto? Food { get; set; }
        public int Chronicler { get; set; }            // 0.69: the dot-man holding the pen, and their name
        public string ChroniclerName { get; set; } = "";
        public int PalLeader { get; set; }             // 0.69: whose personal contacts the saved pal chats are
        public int PrevLeader { get; set; }
        public DateTime SavedAtUtc { get; set; }
    }

    // Write the town to disk (to a temp file first, so a crash cannot leave half a save).
    public static void Save(Sim s)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var d = new SaveData
            {
                V = 2, Name = s.Name, Scheme = s.Scheme, Tier = s.Tier, Founded = s.Founded, Age = s.Age, Pool = s.Pool, Lifetime = s.Lifetime,
                Built = s.Built.ToList(),
                Dots = s.Walkers.Where(w => !w.Gone).Select(w => new Dot { C = w.C, H = w.H, L = w.Lead, I = w.Id, F = Math.Round(w.Food, 3), S = Math.Round(w.Starve, 3), Y = double.IsNaN(w.Born) ? 0 : Math.Round((s.Age - w.Born) / Sim.YearMs, 3), B = w.Bonds.Count > 0 ? w.Bonds : null, Fn = w.First, Ln = w.Last, P = w.Spouse }).Concat(s.AwayColours.Select(c => new Dot { C = c })).ToList(), // soldiers away count as home
                NextId = s.NextId,
                Weather = s.Weather.ToString(), Wx = s.WxAmounts,
                Gov = s.GovKey, Mood = s.Mood, LeaderName = s.Leader?.Name ?? "", LeaderGen = s.Leader?.Gen ?? 1, LeaderSince = s.Leader?.Since ?? 0,
                LeaderTraits = s.Leader?.Traits ?? Array.Empty<string>(),
                LastElection = s.LastElection, Honoured = s.Honoured, Regnal = s.Regnal,
                Gold = s.Gold, EvClock = s.EvClock, LastFate = s.LastFate,
                Chronicler = s.ChroniclerId, ChroniclerName = s.ChroniclerName, PalLeader = s.PalLeader, PrevLeader = s.PrevLeader,
                Sisters = s.Sisters.Select(x => new SisterDto { Name = x.Name, Scheme = x.Scheme, Counts = x.Counts, Cap = x.Cap, Gov = x.Gov, Rel = x.Rel, Gold = x.Gold,
                    RName = x.Lord?.Name, RGen = x.Lord?.Gen ?? 1, RSince = x.Lord?.Since ?? 0, RBorn = x.Lord?.Born ?? 0, RTraits = x.Lord?.Traits }).ToList(),
                Ruins = s.Ruins.Select(x => new RuinDto { R = x.Rank, Was = x.Was, In = x.At - s.EvClock }).ToList(),
                Chats = s.Convos.Select(c => new ConvoDto { Key = c.Key, Kind = c.Kind, Title = c.Title, Role = c.Role, Seen = Math.Min(c.Seen, c.Lines.Count(l => l.Due <= s.TickNo)), Gone = c.Gone, Last = c.Last,
                    Lines = c.Lines.Select(l => new LineDto { F = l.From, T = l.Text, D = l.Day, O = l.Ours, S = l.S, H = l.H }).ToList() }).ToList(),
                Bodies = s.Bodies.Select(b => new BodyDto { Id = b.Id, Name = b.Name, Was = b.Was, Born = b.Born, Blood = b.Blood, Attacks = b.Attacks,
                    Conds = b.Conds.Select(c => new CondDto { K = c.Kind, P = c.Part, S = c.Sev, Pk = c.Peak, T = c.TendAt, In = c.Infect, Im = c.Imm, Since = c.Since, Why = c.Why }).ToList() }).ToList(),
                Food = FoodOf(s),
                SavedAtUtc = DateTime.UtcNow,
            };
            string tmp = SavePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, SavePath, true);
        }
        catch (Exception) { /* a failed save must never stop the town */ }
    }

    // The food stores, with whatever carriers are holding put where they were taking it.
    static FoodDto FoodOf(Sim s)
    {
        var st = (double[,])s.Stock.Clone();
        var barn = (double[])s.Barn.Clone();
        foreach (var w in s.Walkers)
            if (!w.Gone && w.Load > 0 && w.Good >= 0)
            {
                if (w.Dst >= 0 && w.Dst < Sim.MaxLots) st[w.Dst, w.Good] += w.Load; else barn[w.Good] += w.Load;
            }
        var stores = new List<StoreDto>();
        for (int r = 0; r < Sim.MaxLots; r++)
        {
            var g = new double[5];
            bool any = false;
            for (int k = 0; k < 5; k++) { g[k] = Math.Round(st[r, k], 2); any |= g[k] > 0; }
            if (any) stores.Add(new StoreDto { R = r, G = g });
        }
        return new FoodDto { Barn = barn.Select(x => Math.Round(x, 2)).ToArray(), Stores = stores, CropQ = s.CropQ, Standing = s.Standing, Panic = s.Panic, Season = s.FoodSeason, Seed = s.Seed };
    }

    // Copy the current save to another file in the save folder (before a restart overwrites it).
    public static void Backup(string name)
    {
        try { if (File.Exists(SavePath)) File.Copy(SavePath, Path.Combine(Dir, name), true); }
        catch (Exception) { /* a failed copy must never stop the restart */ }
    }

    // Move the chronicle into the "chronicles" folder under the old town's name and the date, so a new game starts a
    // fresh one. Nothing happens if there is no chronicle yet; a failed move never stops the restart.
    public static void ArchiveChronicle(string town)
    {
        try
        {
            if (!File.Exists(ChroniclePath)) return;
            string dir = Path.Combine(Dir, "chronicles");
            Directory.CreateDirectory(dir);
            string safe = string.Concat(town.Where(char.IsLetterOrDigit));
            string to = Path.Combine(dir, $"chronicle-{(safe == "" ? "town" : safe)}-{DateTime.Now:yyyy-MM-dd-HHmm}.txt");
            if (File.Exists(to)) to = to[..^4] + $"-{DateTime.Now:ss}.txt";
            File.Move(ChroniclePath, to);
        }
        catch (Exception) { /* the chronicle is a keepsake, not worth a crash */ }
    }

    // Load the saved town into the sim. Returns how long the app was closed, or null if there was no save.
    public static TimeSpan? Load(Sim s)
    {
        try
        {
            if (!File.Exists(SavePath)) return null;
            var d = JsonSerializer.Deserialize<SaveData>(File.ReadAllText(SavePath));
            if (d == null || d.Built.Count == 0) return null;
            s.Quiet = true;
            s.Name = d.Name;
            s.Scheme = Sim.Clamp(d.Scheme, 0, Content.Schemes.Length - 1);
            s.Tier = Sim.Clamp(d.Tier, 1, 4);
            s.Founded = Math.Max(1, d.Founded);
            s.Age = d.Age;
            s.Pool = Math.Max(0, d.Pool);
            s.Lifetime = d.Lifetime;
            s.Built = d.Built.Where(t => Content.Sprites.ContainsKey(t) || t == "mill").ToList();
            if (s.Built.Count == 0) s.Built = new() { "house" };
            s.Walkers = d.Dots.Select(x => s.NewWalker(Sim.Clamp(x.C, 0, Content.DotColors.Length - 1), Math.Max(0, x.H))).ToList();
            if (s.Walkers.Count == 0) s.Walkers.Add(s.NewWalker(Content.Schemes[s.Scheme].Dot, 0));
            // Ids: keep saved ones (older saves have none, so those dot-men get new ones now and keep them after).
            s.NextId = Math.Max(Math.Max(1, d.NextId), d.Dots.Count == 0 ? 1 : d.Dots.Max(x => x.I) + 1);
            for (int i = 0; i < d.Dots.Count && i < s.Walkers.Count; i++)
            {
                s.Walkers[i].Lead = d.Dots[i].L;
                Sim.SetId(s.Walkers[i], d.Dots[i].I > 0 ? d.Dots[i].I : s.NextId++);
                s.Walkers[i].Food = Sim.Clamp(d.Dots[i].F, 0, 1);
                s.Walkers[i].Starve = Math.Max(0, d.Dots[i].S);
                if (d.Dots[i].Y > 0) s.Walkers[i].Born = d.Age - d.Dots[i].Y * Sim.YearMs;
                if (d.Dots[i].B != null) s.Walkers[i].Bonds = d.Dots[i].B!;
                // Names (0.78): an older save has none, so the old name from the id is kept (duplicates renamed below).
                if (!string.IsNullOrEmpty(d.Dots[i].Fn)) { s.Walkers[i].First = d.Dots[i].Fn!; s.Walkers[i].Last = d.Dots[i].Ln ?? ""; }
                else Sim.LegacyName(s.Walkers[i]);
                s.Walkers[i].Spouse = d.Dots[i].P;
            }
            s.Build();
            // Food: an older save has none, so the town starts with a few days of grain in the barn.
            if (d.Food == null) s.FreshFood();
            else s.RestoreFood(d.Food.Barn, d.Food.Stores.Select(x => (x.R, x.G)), d.Food.CropQ, d.Food.Standing, d.Food.Panic, d.Food.Season, d.Food.Seed);
            // Health of the leader and former leaders (an older save has none: the leader gets a body on the first health step).
            // Saves before version 2 counted a year per town day; ages now run a year per 4 days, so stretch the birth
            // times back to keep everyone the same age.
            double oldYears = d.V < 2 ? Sim.YearMs / Sim.DayMs : 1;
            s.Bodies = (d.Bodies ?? new()).Where(b => s.Walkers.Any(w => w.Id == b.Id)).Select(b => new Body { Id = b.Id, Name = b.Name, Was = b.Was, Born = d.Age - (d.Age - b.Born) * oldYears, Blood = Sim.Clamp(b.Blood, 0.05, 1), Attacks = b.Attacks,
                Conds = (b.Conds ?? new()).Select(c => new Cond { Kind = c.K, Part = c.P, Sev = c.S, Peak = c.Pk, TendAt = c.T, Infect = c.In, Imm = c.Im, Since = c.Since, Why = c.Why }).ToList() }).ToList();
            // Government: an older save has none, so a Village or bigger chooses one now.
            s.GovKey = Govs.Exists(d.Gov) ? d.Gov : "elders";
            s.Mood = Sim.Clamp(d.Mood, 0, 1);
            s.LastElection = d.LastElection;
            s.Honoured = Sim.Clamp(d.Honoured, 0, 6);
            s.Regnal = d.Regnal ?? new();
            s.RestoreLeader(d.LeaderName, d.LeaderGen, d.LeaderSince, d.LeaderTraits);
            // A save from before 0.78 used 40 first names: anyone sharing a name is named anew (not the leader or former leaders).
            if (d.Dots.Any(x => string.IsNullOrEmpty(x.Fn))) s.RenameDuplicates();
            s.Gold = Sim.Clamp(d.Gold, 0, 100);
            s.EvClock = d.EvClock;
            s.LastFate = d.LastFate;
            s.ChroniclerId = d.Chronicler; s.ChroniclerName = d.ChroniclerName ?? "";
            s.PalLeader = d.PalLeader; s.PrevLeader = d.PrevLeader;
            s.RestoreRuins((d.Ruins ?? new()).Select(x => (x.R, x.Was, x.In)));
            s.Sisters = d.Sisters.Select(x => s.SisterFromSave(x.Name, x.Scheme, x.Counts, x.Cap, x.Gov, x.Rel, x.Gold,
                x.RName == null ? null : new Leader { Name = x.RName, Gen = Math.Max(1, x.RGen), Since = x.RSince, Born = d.EvClock - (d.EvClock - x.RBorn) * oldYears, Traits = x.RTraits ?? Array.Empty<string>() })).ToList();
            s.Convos.Clear();
            foreach (var c in d.Chats ?? new())
                s.RestoreConvo(new Convo { Key = c.Key, Kind = c.Kind, Title = c.Title, Role = c.Role, Seen = c.Seen, Gone = c.Gone, Last = c.Last,
                    Lines = (c.Lines ?? new()).Select(l => new ChatLine { From = l.F, Text = l.T, Day = l.D, Ours = l.O, S = l.S ?? "", H = l.H }).ToList() });
            if (Enum.TryParse<Wx>(d.Weather, out var wx)) { s.Weather = wx; s.WxAmounts = d.Wx; }
            s.Quiet = false;
            return DateTime.UtcNow - d.SavedAtUtc;
        }
        catch (Exception) { return null; }
    }

    // Append any queued chronicle lines to the text file; returns the lines for the on-screen view.
    public static void FlushChronicle(Sim s, List<string> view)
    {
        if (s.ChronicleQueue.Count == 0) return;
        var lines = s.ChronicleQueue.ToList();
        s.ChronicleQueue.Clear();
        view.AddRange(lines);
        try
        {
            Directory.CreateDirectory(Dir);
            if (!File.Exists(ChroniclePath)) File.WriteAllText(ChroniclePath, "DOTBURG CHRONICLE - major events, newest last\n\n");
            File.AppendAllLines(ChroniclePath, lines);
        }
        catch (Exception) { /* the chronicle is a keepsake, not worth a crash */ }
    }

    // The last few lines of the chronicle file, for the on-screen panel when the app starts.
    public static List<string> ReadChronicleTail(int n)
    {
        try
        {
            if (!File.Exists(ChroniclePath)) return new();
            var all = File.ReadAllLines(ChroniclePath).Where(l => l.StartsWith("- ")).ToList();
            return all.Skip(Math.Max(0, all.Count - n)).ToList();
        }
        catch (Exception) { return new(); }
    }
}
