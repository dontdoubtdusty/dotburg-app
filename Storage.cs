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
    class Dot { public int C { get; set; } public int H { get; set; } public bool L { get; set; } public int I { get; set; } }
    class SisterDto
    {
        public string Name { get; set; } = "";
        public int Scheme { get; set; }
        public int[] Counts { get; set; } = Array.Empty<int>();
        public int Cap { get; set; }
        public string Gov { get; set; } = "council";
        public double? Rel { get; set; }
        public double? Gold { get; set; }
    }

    class LineDto { public string F { get; set; } = ""; public string T { get; set; } = ""; public int D { get; set; } public bool O { get; set; } }
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
                Name = s.Name, Scheme = s.Scheme, Tier = s.Tier, Founded = s.Founded, Age = s.Age, Pool = s.Pool, Lifetime = s.Lifetime,
                Built = s.Built.ToList(),
                Dots = s.Walkers.Where(w => !w.Gone).Select(w => new Dot { C = w.C, H = w.H, L = w.Lead, I = w.Id }).Concat(s.AwayColours.Select(c => new Dot { C = c })).ToList(), // soldiers away count as home
                NextId = s.NextId,
                Weather = s.Weather.ToString(), Wx = s.WxAmounts,
                Gov = s.GovKey, Mood = s.Mood, LeaderName = s.Leader?.Name ?? "", LeaderGen = s.Leader?.Gen ?? 1, LeaderSince = s.Leader?.Since ?? 0,
                LeaderTraits = s.Leader?.Traits ?? Array.Empty<string>(),
                LastElection = s.LastElection, Honoured = s.Honoured, Regnal = s.Regnal,
                Gold = s.Gold, EvClock = s.EvClock, LastFate = s.LastFate,
                Sisters = s.Sisters.Select(x => new SisterDto { Name = x.Name, Scheme = x.Scheme, Counts = x.Counts, Cap = x.Cap, Gov = x.Gov, Rel = x.Rel, Gold = x.Gold }).ToList(),
                Ruins = s.Ruins.Select(x => new RuinDto { R = x.Rank, Was = x.Was, In = x.At - s.EvClock }).ToList(),
                Chats = s.Convos.Select(c => new ConvoDto { Key = c.Key, Kind = c.Kind, Title = c.Title, Role = c.Role, Seen = Math.Min(c.Seen, c.Lines.Count(l => l.Due <= s.TickNo)), Gone = c.Gone, Last = c.Last,
                    Lines = c.Lines.Select(l => new LineDto { F = l.From, T = l.Text, D = l.Day, O = l.Ours }).ToList() }).ToList(),
                SavedAtUtc = DateTime.UtcNow,
            };
            string tmp = SavePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, SavePath, true);
        }
        catch (Exception) { /* a failed save must never stop the town */ }
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
            }
            s.Build();
            // Government: an older save has none, so a Village or bigger chooses one now.
            s.GovKey = Govs.Exists(d.Gov) ? d.Gov : "elders";
            s.Mood = Sim.Clamp(d.Mood, 0, 1);
            s.LastElection = d.LastElection;
            s.Honoured = Sim.Clamp(d.Honoured, 0, 6);
            s.Regnal = d.Regnal ?? new();
            s.RestoreLeader(d.LeaderName, d.LeaderGen, d.LeaderSince, d.LeaderTraits);
            s.Gold = Sim.Clamp(d.Gold, 0, 100);
            s.EvClock = d.EvClock;
            s.LastFate = d.LastFate;
            s.RestoreRuins((d.Ruins ?? new()).Select(x => (x.R, x.Was, x.In)));
            s.Sisters = d.Sisters.Select(x => s.SisterFromSave(x.Name, x.Scheme, x.Counts, x.Cap, x.Gov, x.Rel, x.Gold)).ToList();
            s.Convos.Clear();
            foreach (var c in d.Chats ?? new())
                s.RestoreConvo(new Convo { Key = c.Key, Kind = c.Kind, Title = c.Title, Role = c.Role, Seen = c.Seen, Gone = c.Gone, Last = c.Last,
                    Lines = (c.Lines ?? new()).Select(l => new ChatLine { From = l.F, Text = l.T, Day = l.D, Ours = l.O }).ToList() });
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
