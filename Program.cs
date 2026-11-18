using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;

namespace Dotburg;

// What feeds the town with growth units. Stage 1 has only the own clock; later a hook or log could plug in here.
interface IPowerSource
{
    // Units earned over dtMs of real time.
    double Earned(double dtMs);
}

// The town works on its own: one unit every ThinkMs while the app is open.
class OwnClock : IPowerSource
{
    public const double ThinkMs = 20_000;
    public double Earned(double dtMs) => dtMs / ThinkMs;
}

public partial class Program
{
    // The one version number: shows in the window title and crash.txt. major.minor.patch, see CLAUDE.md and claude/versioning.md.
    const string Version = "Dotburg 0.90.1";

    [STAThread]
    static void Main(string[] args)
    {
        try { new App().Run(args); }
        catch (Exception e)
        {
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "crash.txt"), $"{Version} crashed at {DateTime.Now}\n\n{e}"); }
            catch (Exception) { }
            throw;
        }
    }

    partial class App
    {
        Sim sim = new(Environment.TickCount);   // replaced on a restart (Restart.cs)
        readonly IPowerSource power = new OwnClock();
        readonly List<string> chronicleView = new();
        Texture2D tex;
        Texture2D iconTex;
        readonly byte[] rgba = new byte[Sim.W * Sim.H * 4];
        double acc;          // real time not yet turned into ticks
        double unitAcc;      // fractional growth units waiting
        double saveAcc;
        double flushAcc;
        int hoverRank = -1;            // lot under the mouse, or -1
        int hoverLand = -1;            // land tile under the mouse (index into sim.Land), or -1
        Vector2? testMouse;            // --hover: pretend the mouse is here (screenshot tests)
        int testHover = -1;
        bool showHealthTip;          // --hurt: show the leader's health chart in the screenshot
        bool showFoodTip;            // --food-tip: show the ledger's food tooltip in the screenshot
        bool showMoodTip;            // --mood-tip: show the mood tooltip in the screenshot
        bool showSisterTip;          // --sister-tip: show the newest sister's tooltip in the screenshot
        bool showGovTip;             // --gov-tip: show the government tooltip in the screenshot
        bool signInOn;               // the MSN "Signing in..." screen at launch (off in screenshots unless --signin)
        double signInUntil;          // when it ends (GetTime), set on the first frame

        public unsafe void Run(string[] args)
        {
            string? shot = null;
            int preTicks = 0;
            double preUnits = 0;
            bool fresh = false;
            int soak = 0;
            double catchMin = 0;
            Wx? forceWx = null;
            bool forceFest = false;
            int forceScheme = -1, forceSeason = -1;   // --scheme N, --season N: landscape and season for a screenshot
            bool bigPic = false;                      // --pic: also save the bare town picture at 8x next to --shot
            double forcePhase = -1;   // --phase: time of day for a screenshot (0 midnight, 0.5 noon)
            string? forceGov = null;
            string? forceSick = null;
            bool forceFire = false, forceWar = false, forceChat = false, forceHurt = false, forceBrigade = false;
            bool forceHungry = false;
            bool forceHoard = false;
            string? forceBlight = null;
            string? forceEvent = null;   // --event hero|prophet|hermit|market|comet|flood|flooddamage|feud (0.85)
            string? openChat = null;   // --open <key start>: open that conversation (with --chat)
            (int w, int h)? forceSize = null;   // --size WxH: a screenshot at this window size instead of the fitted one
            // --hands: write sample chat hands (Hands.cs) to hands.txt next to the exe and exit.
            if (Array.IndexOf(args, "--hands") >= 0) { Sim.WriteHandSamples(Path.Combine(AppContext.BaseDirectory, "hands.txt")); return; }
            // --voices: check resources/voices.txt and write sample conversations (Voices.cs) to voice-samples.txt next to the exe, then exit.
            if (Array.IndexOf(args, "--voices") >= 0) { Sim.WriteVoiceSamples(Path.Combine(AppContext.BaseDirectory, "voice-samples.txt")); return; }
            // --names: sample names to names.txt; with a save in DOTBURG_DIR, also load it and list its townsfolk (never saves).
            if (Array.IndexOf(args, "--names") >= 0)
            {
                var lines = Sim.NameSamples().ToList();
                var ns = new Sim(1);
                Storage.Load(ns);
                lines.Add($"save: {ns.Name}, leader {ns.Leader?.Name}; " + string.Join(", ", ns.Walkers.Where(w => w.Kind == "").Select(w => Sim.CitizenName(w) + (ns.SpouseOf(w) != null ? " (wed)" : ""))));
                File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "names.txt"), lines);
                return;
            }
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--soak" && i + 1 < args.Length) soak = int.Parse(args[++i]);
                if (args[i] == "--shot" && i + 1 < args.Length) shot = args[++i];
                else if (args[i] == "--ticks" && i + 1 < args.Length) preTicks = int.Parse(args[++i]);
                else if (args[i] == "--units" && i + 1 < args.Length) preUnits = double.Parse(args[++i]);
                else if (args[i] == "--fresh") fresh = true;                else if (args[i] == "--ask-restart") askRestart = true;
                else if (args[i] == "--hungry") forceHungry = true;
                else if (args[i] == "--hoard") forceHoard = true;   // three homes hoarding, to see the sack (0.87.3)
                else if (args[i] == "--wx" && i + 1 < args.Length) forceWx = Enum.Parse<Wx>(args[++i], true);
                else if (args[i] == "--fest") forceFest = true;
                else if (args[i] == "--phase" && i + 1 < args.Length) forcePhase = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                else if (args[i] == "--hover" && i + 1 < args.Length) testHover = int.Parse(args[++i]);
                else if (args[i] == "--gov" && i + 1 < args.Length) forceGov = args[++i];
                else if (args[i] == "--sick" && i + 1 < args.Length) forceSick = args[++i];
                else if (args[i] == "--fire") forceFire = true;
                else if (args[i] == "--size" && i + 1 < args.Length) { var wh = args[++i].Split('x'); forceSize = (int.Parse(wh[0]), int.Parse(wh[1])); }
                else if (args[i] == "--war") forceWar = true;
                else if (args[i] == "--hurt") forceHurt = true;
                else if (args[i] == "--hurt-tip") { forceHurt = true; showHealthTip = true; }
                else if (args[i] == "--food-tip") showFoodTip = true;
                else if (args[i] == "--mood-tip") showMoodTip = true;
                else if (args[i] == "--sister-tip") showSisterTip = true;
                else if (args[i] == "--gov-tip") showGovTip = true;
                else if (args[i] == "--chat") forceChat = true;
                else if (args[i] == "--brigade") forceBrigade = true;
                else if (args[i] == "--open" && i + 1 < args.Length) openChat = args[++i];
                else if (args[i] == "--blight" && i + 1 < args.Length) forceBlight = args[++i];
                else if (args[i] == "--event" && i + 1 < args.Length) forceEvent = args[++i];
                else if (args[i] == "--pic") bigPic = true;
                else if (args[i] == "--scheme" && i + 1 < args.Length) forceScheme = int.Parse(args[++i]);
                else if (args[i] == "--season" && i + 1 < args.Length) forceSeason = int.Parse(args[++i]);
                else if (args[i] == "--skin" && i + 1 < args.Length) forceSkin = args[++i];
                else if (args[i] == "--catchup" && i + 1 < args.Length) catchMin = double.Parse(args[++i]);   // play N minutes away on start (Offline.cs)
                else if (args[i] == "--menu" && i + 1 < args.Length) { string m = args[++i]; menuOpen = m == "help" ? 2 : m == "about" ? 0 : 1; subOpen = m == "offline"; aboutOpen = m == "about"; }   // screenshot an open menu (Menus.cs)
                else if (args[i] == "--truth") Sim.Truth = true;   // tips show the real numbers, not hearsay (Hearsay.cs)
                else if (args[i] == "--dread") Sim.ForceDread = 0.8;   // every leader feared (Hearsay.cs), to measure the feared band
            }
            if (soak > 0) { Soak(soak, preUnits); SoakTraits(); if (args.Contains("--ill")) SoakIllness(preUnits > 0 ? preUnits : 150); if (args.Contains("--fires")) SoakFires(preUnits > 0 ? preUnits : 150); if (args.Contains("--wars")) SoakWars(preUnits > 0 ? preUnits : 1500, soak); if (args.Contains("--health")) SoakHealth(preUnits > 0 ? preUnits : 1500, soak); if (args.Contains("--food")) SoakFood(soak); if (args.Contains("--rules")) SoakRules(150); if (args.Contains("--events")) SoakEvents(preUnits > 0 ? preUnits : 400, soak); return; }
            bool testing = shot != null;   // screenshot runs never touch the saved town
            signInOn = !testing || args.Contains("--signin");

            // Start from the save, exactly where it was left (the town does not grow while the app is closed), else a new region.
            TimeSpan? away = (fresh || testing) ? null : Storage.Load(sim);
            if (away == null) sim.FreshRegion();
            if (!testing) { LoadOffline(); PlanCatchUp(away); }
            if (catchMin > 0) { offlineHours = 2; PlanCatchUp(TimeSpan.FromMinutes(catchMin)); }
            if (forceScheme >= 0) { sim.Scheme = forceScheme; sim.NewLand(); sim.Build(); }   // land leans by the forced landscape
            if (forceSeason >= 0) sim.Age = (forceSeason * Sim.DaysPerSeason + 1) * Sim.DayMs + sim.Age % Sim.DayMs;
            if (preUnits > 0) { sim.Quiet = true; sim.Earn(preUnits); sim.Grow(); sim.Quiet = false; sim.FreshFood(); }
            sim.Tick(0);
            for (int i = 0; i < preTicks; i++) sim.Tick(Sim.TickMs);
            if (forceWx != null) sim.ForceWeather(forceWx.Value);
            if (forceGov != null) sim.ForceGov(forceGov);
            if (forceFest) sim.StartFestival();
            if (forcePhase >= 0) sim.Age = Math.Floor(sim.Age / Sim.DayMs) * Sim.DayMs + forcePhase * Sim.DayMs;
            if (forceGov != null) for (int i = 0; i < 200; i++) sim.Tick(Sim.TickMs);
            if (forceWar) { sim.StartWar(); for (int i = 0; i < 120; i++) sim.Tick(Sim.TickMs); }
            if (forceHurt) sim.TestHurt();
            if (forceBlight != null) { sim.ForceBlight(forceBlight); for (int i = 0; i < 200; i++) sim.Tick(Sim.TickMs); }
            if (forceFire) { sim.ForceFire(); for (int i = 0; i < 90; i++) sim.Tick(Sim.TickMs); }
            if (args.Contains("--works")) { sim.TestWorks(); for (int i = 0; i < 150; i++) sim.Tick(Sim.TickMs); }
            if (forceEvent != null) { Console.WriteLine(sim.ForceEvent(forceEvent)); for (int i = 0; i < 80; i++) sim.Tick(Sim.TickMs); }
            if (forceSick != null) { sim.ForceOutbreak(forceSick); for (int i = 0; i < 300; i++) sim.Tick(Sim.TickMs); }
            if (forceHungry) { sim.TestHunger(); for (int i = 0; i < 400; i++) sim.Tick(Sim.TickMs); }
            if (forceHoard) sim.TestHoard();
            if (forceBrigade) Console.WriteLine(sim.TestBrigade());
            if (forceChat) selKey = sim.TestChat();
            if (args.Contains("--pals")) { selKey = sim.TestPals(); chronicleView.AddRange(sim.ChronicleQueue); }
            if (openChat != null && sim.Convos.FirstOrDefault(c => c.Key.StartsWith(openChat)) is { } oc) selKey = oc.Key;
            if (openChat == "bot") selKey = "bot";
            if (!testing) chronicleView.AddRange(Storage.ReadChronicleTail(200));
            botSeen = chronicleView.Count;

            // Starts hidden: after the first frame (which knows the font sizes) the window is sized to the panels, then shown.
            Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.VSyncHint | ConfigFlags.HiddenWindow);
            Raylib.InitWindow(1600, 900, Version);
            Raylib.SetWindowMinSize(MinWinW, MinWinH);
            Raylib.SetTargetFPS(60);
            Raylib.SetExitKey(KeyboardKey.Null);

            Image img = Raylib.GenImageColor(Sim.W, Sim.H, Color.Black);
            tex = Raylib.LoadTextureFromImage(img);
            Raylib.UnloadImage(img);
            Raylib.SetTextureFilter(tex, TextureFilter.Point);

            // The icon: on the window and taskbar, and beside the town name in the legend.
            string iconPath = Path.Combine(AppContext.BaseDirectory, "resources", "dotburg.png");
            if (File.Exists(iconPath))
            {
                Image icon = Raylib.LoadImage(iconPath);
                Raylib.SetWindowIcon(icon);
                iconTex = Raylib.LoadTextureFromImage(icon);
                Raylib.SetTextureFilter(iconTex, TextureFilter.Point);
                Raylib.UnloadImage(icon);
            }

            LoadEmoticons();
            LoadAvatars();
            if (!testing) InitSounds();
            rlImGui.BeginInitImGui();
            LoadFont();
            rlImGui.EndInitImGui();
            if (XpText.Paint()) rlImGui.ReloadFonts();   // the GDI glyphs go into the atlas after ImGui packed it
            // --font-sheet out.png: save the font atlas (every glyph of every font) as black on white, then exit.
            if (Array.IndexOf(args, "--font-sheet") is int fsi && fsi >= 0 && fsi + 1 < args.Length)
            {
                ImGui.GetIO().Fonts.GetTexDataAsRGBA32(out byte* px, out int tw, out int th);
                var sheet = Raylib.GenImageColor(tw, th, Color.White);
                for (int y = 0; y < th; y++)
                    for (int x = 0; x < tw; x++) { byte a = px[(y * tw + x) * 4 + 3]; if (a > 0) Raylib.ImageDrawPixel(ref sheet, x, y, new Color(255 - a, 255 - a, 255 - a, 255)); }
                Raylib.ExportImage(sheet, args[fsi + 1]);
                return;
            }
            ImGui.GetIO().NativePtr->IniFilename = null;
            Theme();

            int frameNo = 0;
            int fps = 60;
            while (!Raylib.WindowShouldClose() && !quit)
            {
                frameNo++;
                // Pace the window by attention: full speed with the mouse over it (hover tips, scrolling), slower in the
                // background (still above the sim's 9 ticks a second, so nothing is skipped), very slow minimised.
                int want = testing ? 60 : Raylib.IsWindowMinimized() ? 2 : Raylib.IsCursorOnScreen() ? 60 : Raylib.IsWindowFocused() ? 30 : 15;
                if (want != fps) { fps = want; Raylib.SetTargetFPS(fps); }
                // A long frame (slow pacing, or the window being dragged) is caught up in whole ticks, up to a second.
                double dt = Math.Min(Raylib.GetFrameTime() * 1000.0, 1000);
                acc += dt;
                bool ticked = catchLeft > 0;
                StepCatchUp();
                while (acc >= Sim.TickMs)
                {
                    ticked = true;
                    acc -= Sim.TickMs;
                    sim.Tick(Sim.TickMs);
                    unitAcc += power.Earned(Sim.TickMs);
                    if (unitAcc >= 1) { sim.Earn(Math.Floor(unitAcc)); unitAcc -= Math.Floor(unitAcc); sim.Grow(); }
                }
                saveAcc += dt;
                flushAcc += dt;
                if (flushAcc > 1000) { flushAcc = 0; if (!testing) Storage.FlushChronicle(sim, chronicleView); }
                if (saveAcc > 30_000) { saveAcc = 0; if (!testing) Storage.Save(sim); }

                // The town picture only changes on a tick, so it is rebuilt and sent to the GPU only then.
                if (ticked || frameNo <= 2)
                {
                    sim.Frame();
                    UploadFrame();
                }

                if (Raylib.IsKeyPressed(KeyboardKey.H)) panelsOn = !panelsOn;
                FrameOnMaximize();
                if (Raylib.IsKeyPressed(KeyboardKey.M)) ToggleMute();
                ListenForSounds();
                if (Raylib.IsKeyPressed(KeyboardKey.F)) Raylib.ToggleBorderlessWindowed();
                Raylib.BeginDrawing();
                Raylib.ClearBackground(panelsOn ? DeskCol : Color.Black);
                rlImGui.Begin();
                DrawUi();
                rlImGui.End();
                Raylib.EndDrawing();
                if (frameNo == 1)
                {
                    if (forceSize != null) Raylib.SetWindowSize(forceSize.Value.w, forceSize.Value.h); else FitWindow();
                    Raylib.ClearWindowState(ConfigFlags.HiddenWindow);
                }
                else if (!testing) SnapWindow();

                if (shot != null && frameNo == (forceChat ? 40 : 6))
                {
                    Image s = Raylib.LoadImageFromScreen();
                    Raylib.ExportImage(s, shot);
                    Raylib.UnloadImage(s);
                    if (bigPic)
                    {
                        // The bare town picture at 8x next to the screenshot.
                        Image pic = Raylib.GenImageColor(Sim.W * 8, Sim.H * 8, Color.Black);
                        for (int p = 0; p < sim.Out.Length; p++)
                        {
                            int c = sim.Out[p];
                            Raylib.ImageDrawRectangle(ref pic, p % Sim.W * 8, p / Sim.W * 8, 8, 8, new Color((byte)(c >> 16), (byte)(c >> 8), (byte)c, (byte)255));
                        }
                        Raylib.ExportImage(pic, Path.ChangeExtension(shot, null) + "-pic.png");
                        Raylib.UnloadImage(pic);
                    }
                    break;
                }
            }
            if (!testing) { Storage.FlushChronicle(sim, chronicleView); Storage.Save(sim); }
            rlImGui.Shutdown();
            Raylib.UnloadTexture(tex);
            if (iconTex.Id != 0) Raylib.UnloadTexture(iconTex);
            if (emoTex.Id != 0) Raylib.UnloadTexture(emoTex);
            if (avatarTex.Id != 0) Raylib.UnloadTexture(avatarTex);
            CloseSounds();
            Raylib.CloseWindow();
        }

        // Headless long run for checking: no window, no files. Prints what the town became.
        string[] Regions = { "Envoys", "letter", "Refugees", "team", "spy", "caravan", "aid", "tribute", "agreement", "settlement", "moves in", "slips", "revolution", "absorbs", "collapses", "quarrel", "Guards turn", "baby", "welcomes a dotlet", "Wedding bells", "neighbours marry", "Fire!", "burns down", "puts out", "spreads to", "Lightning", "great fire", "perishes", "saboteur", "is rebuilt", "collection", "war", "front", "Victory", "Defeat", "soldier", "Raiders", "Plague", "plague", "Famine", "famine", "hunger", "overrun", "sends aid" };
        string[] EventWords = { "hero", "prophet", "hermit", "feud", "Market day", "market", "flood", "low roads", "pond spills", "marsh creeps", "comet", "chooses", "strongman", "coup", "Revolution", "never forgive", "crying that", "running from", "was near the" };
        void Soak(int ticks, double preUnits)
        {
            sim.FreshRegion();
            sim.SayLog = new();
            if (preUnits > 0) { sim.Quiet = true; sim.Earn(preUnits); sim.Grow(); sim.Quiet = false; sim.FreshFood(); }
            double acc2 = 0;
            for (int i = 0; i < ticks; i++)
            {
                sim.Tick(Sim.TickMs);
                acc2 += power.Earned(Sim.TickMs);
                if (acc2 >= 1) { sim.Earn(Math.Floor(acc2)); acc2 -= Math.Floor(acc2); sim.Grow(); }
                if (i % 50 == 0) sim.Frame();            }
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "soak.txt"),
                $"rule {sim.G.Name}, {sim.LeaderTitle}, mood {sim.Mood:F2}; weather {sim.WxWord}; {ticks} ticks = {sim.Age / Sim.DayMs:F1} days: {sim.Name}, {Sim.TierNames[sim.Tier - 1]}, {sim.Walkers.Count} dot-men, {sim.Built.Count} buildings, pool {sim.Pool:F1}, founded {sim.Founded}, gold {sim.Gold:F0}\n" +
                $"land: {string.Join(", ", sim.Land.Select(l => $"{Sim.LandKinds[l.K]} ({l.Tx},{l.Ty})"))}; {string.Join(", ", sim.LandTally.Select(t => $"{t.Key} {t.Value}"))}\n" +
                $"builders: {sim.SitesDone} sites finished, {(sim.SiteTicks == 0 ? 0 : 100.0 * sim.IdleSiteTicks / sim.SiteTicks):F0}% of site time idle, avg {(sim.SitesDone == 0 ? 0 : sim.SiteTicks * Sim.TickMs / 1000.0 / sim.SitesDone):F0} s a site open, {sim.GrowWaits} growth steps held back; {sim.SayLog!.Count(l => l.Contains("fall from the scaffolding"))} scaffold falls\n" +
                string.Join("\n", sim.Sisters.Select(s => $"sister {s.Name} {s.Total}/{s.Cap} {s.Gov} rel {sim.RelOf(s):F2} gold {sim.GoldOf(s):F0}")) + "\n" +
                string.Join("\n", sim.ChronicleQueue.Take(40)) + "\n--- region lines ---\n" +
                string.Join("\n", sim.SayLog!.Where(l => Regions.Any(k => l.Contains(k))).Take(120)) + "\n--- illness and leaders ---\n" +
                string.Join("\n", sim.SayLog!.Where(l => Illness.Any(k => l.Contains(k)))) +
                $"\n--- sister rulers ---\n{sim.SayLog!.Count(Sim.RulerDeathLine)} ruler deaths in {ticks * Sim.TickMs / Sim.DayMs:F0} days\n" +
                string.Join("\n", sim.SayLog!.Where(Sim.RulerDeathLine).Take(30)));
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "soak.txt"), $"\n--- hearsay ---\n{sim.HearsayLine()}\n--- council ---\n{sim.CouncilLine()}\n--- notables ---\n{sim.KeepersLine()}\n{string.Join("\n", sim.SayLog!.Where(l => l.Contains("council") || l.Contains("elders of")).TakeLast(25))}\n");
            // How chatty the Messenger is: lines per town day over the last 10 days, by kind of chat, and a sample.
            double days = Math.Min(10, Math.Max(1, sim.Age / Sim.DayMs));
            var kinds = sim.Convos.GroupBy(c => c.Kind).Select(g =>
                $"{g.Key,-8} chats {g.Count(),2}  lines kept {g.Sum(c => c.Lines.Count),4}  per day {g.Sum(c => c.Lines.Count(l => l.Day > sim.DayNumber - 10)) / days:F1}");
            var sample = sim.Convos.Where(c => c.Kind == "pal").Concat(sim.Convos.Take(4)).SelectMany(c => c.Lines.TakeLast(6).Select(l => $"[{c.Title}] {l.From}: {l.Text}"));
            sample = sample.Concat(sim.Convos.Where(c => c.Title.StartsWith("Steward")).SelectMany(c => c.Lines.TakeLast(15).Select(l => $"[steward] {l.Text}")));   // the morning report (0.87.3)
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "soak.txt"), $"\n--- messenger ---\nleaders signed in {sim.PalLeaders}, friends added {sim.PalFriendsMade}, blocked added {sim.PalFoesMade}, best friendship with a leader {sim.PalBestFriend:F2}, worst grudge {sim.PalBestGrudge:F2}\n" + string.Join("\n", kinds) + "\n" + string.Join("\n", sample) + "\n");
            // Every crier line, for judging variety: the full list, and the most common openings.
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "soak-say.txt"), sim.SayLog!);
            var heads = sim.SayLog!.GroupBy(l => string.Join(' ', l.Split(' ').Take(3))).OrderByDescending(g => g.Count()).Take(40).Select(g => $"{g.Count(),4}  {g.Key}");
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "soak.txt"), $"\n--- crier lines: {sim.SayLog!.Count} in {sim.Age / Sim.DayMs:F0} days, commonest openings ---\n" + string.Join("\n", heads) + "\n");
            // Bonds and grudges now (0.68): how many, how deep, how many against the leader, and the story lines.
            var live = sim.Walkers.Where(w => !w.Gone && w.Kind == "").ToList();
            var all = live.SelectMany(w => w.Bonds).ToList();
            int lid = sim.LeaderWalker?.Id ?? -1;
            string[] story = { "marry", "married", "match is made", "table", "pantry", "born to", "name their", "never forgive", "trade words", "Fists fly", "went back to day", "running from", "was near the", "crying that", "assassin" };
            var wild = live.SelectMany(Sim.CitizenTraits).Where(k => Traits.Get(k).Cat == 3).GroupBy(k => k).Select(g => $"{g.Key} {g.Count()}");
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "soak.txt"),
                $"\n--- bonds ---\n{live.Count} dot-men; grudges {all.Count(b => b.Kind == "grudge")} (deep {all.Count(b => b.Kind == "grudge" && b.Str >= 0.6)}, against the leader {all.Count(b => b.Kind == "grudge" && b.Who == lid)}), friendships {all.Count(b => b.Kind == "friend")}; named assassins {sim.AssassinsKnown}; checks {sim.MeetChecks}, alone {sim.MeetAlone}, meetings {sim.Meetings}, bonds made {sim.BondsMade}\n" +
                $"wildcards now: {string.Join(", ", wild)}\n" +
                $"festivals: held {sim.FestsHeld}, called off for sickness {sim.FestsOffIll}, for hunger {sim.FestsOffHunger}\n" +
                $"weddings: friends {sim.FriendWeddings}, arranged {sim.ArrangedWeddings}, matched {sim.MatchWeddings}; married now {live.Count(w => sim.SpouseOf(w) != null)}; meals at a friend's table {sim.SharedMeals}; street tables {sim.StreetTables} (broken up {sim.TablesBroken}), fed {sim.StreetFed}\n" +
                $"names: {live.Count} living, {live.Select(w => w.First).Distinct().Count()} distinct first names, {live.Select(w => w.First + " " + w.Last).Distinct().Count()} distinct full names, {live.Select(w => w.Last).Distinct().Count()} families\n" +
                $"events (0.85): heroes {sim.Heroes}, prophets {sim.Prophets} (followers {sim.Followers}), hermits {sim.Hermits}; markets {sim.MarketsHeld}, pickpockets {sim.Pickpockets} (caught {sim.PickpocketsCaught}); floods {sim.Floods}, damaging {sim.FloodDamages}, drowned {sim.Drowned}; comets {sim.Comets}; feuds begun {sim.FeudsBegun}, quarrels {sim.FeudQuarrels}, settled {sim.FeudsSettled}, ended by a wedding {sim.FeudsWed}, cooled {sim.FeudsCooled}, stabbings {sim.FeudStabbings}, taken {sim.FeudTaken}, open now {sim.Feuds.Count}; rule choice: leader kept {sim.RuleKept}, new {sim.RuleNew}\n" +
                string.Join("\n", sim.SayLog!.Where(l => EventWords.Any(l.Contains)).Take(80)) + "\n" +
                string.Join("\n", sim.SayLog!.Where(l => story.Any(l.Contains)).Take(60)) + "\n");
        }
        // Food over whole years: several regions grown from a hamlet by the own clock, with a timeline every two days
        // (people, days of food, hungry, starving, barn, carriers) and the harvest, hunger and hoarding lines.
        void SoakFood(int ticks)
        {
            var o = new List<string> { "\n--- food ---" };
            int regions = 6, deadAll = 0, fledAll = 0;
            double hungryAll = 0, panicAll = 0; int samples = 0, panicHigh = 0, maxQueue = 0, lineSamples = 0;
            for (int r = 0; r < regions; r++)
            {
                var s = new Sim(4000 + r * 19);
                s.FreshRegion();
                s.SayLog = new();
                double acc2 = 0;
                o.Add($"region {r}:");
                for (int i = 0; i < ticks; i++)
                {
                    s.Tick(Sim.TickMs);
                    acc2 += power.Earned(Sim.TickMs);
                    if (acc2 >= 1) { s.Earn(Math.Floor(acc2)); acc2 -= Math.Floor(acc2); s.Grow(); }
                    if (i % 300 == 0)
                    {
                        int pop = s.Walkers.Count(w => !w.Gone && w.Kind == "");
                        hungryAll += pop == 0 ? 0 : (double)s.HungryCount / pop; samples++;
                        panicAll += s.Panic; if (s.Panic > 0.5) panicHigh++;
                        int ql = 0; for (int rk = 0; rk < s.Built.Count && rk < 49; rk++) ql = Math.Max(ql, s.QueueLength(rk));
                        maxQueue = Math.Max(maxQueue, ql); if (ql >= 3) lineSamples++;
                    }
                    if (i % (int)(2 * Sim.DayMs / Sim.TickMs) == (int)(0.2 * Sim.DayMs / Sim.TickMs))
                    {
                        var t = s.GoodsTotals();
                        o.Add($"  day {s.DayNumber,3} {s.SeasonNow.Name,-6} {s.G.Key,-8} pop {s.Walkers.Count(w => !w.Gone && w.Kind == ""),3} food {s.FoodDays,5:F1} d  hungry {s.HungryCount,2} weak {s.WeakCount,2} panic {s.Panic,4:F2} shelf {s.ShelfFill(),4:F2}  barn {t[0] - (t[0] - s.Barn[0]),5:F0} grain {t[0],5:F0} flour {t[1],4:F0} bread {t[2],4:F0} greens {t[3],4:F0} pantry {t[4],4:F0}  standing {s.Standing,4:F0} seed {s.Seed,3:F0} jobs {s.JobCount} carriers {s.Carriers} gold {s.Gold:F0}");
                    }
                }
                int dead = s.SayLog!.Count(l => l.Contains("dies of hunger"));
                int fled = s.SayLog!.Count(l => l.Contains("hungry family"));
                deadAll += dead; fledAll += fled;
                o.Add($"  starved {dead}, left hungry {fled}, panics {s.SayLog!.Count(l => l.StartsWith("Queues at"))}, grain withheld {s.SayLog!.Count(l => l.Contains("keeps its harvest at home"))}");
                o.Add($"  births {s.SayLog!.Count(l => l.Contains("baby") || l.Contains("dotlet is born") || l.Contains("name their") || l.Contains("welcomes a dotlet") || l.Contains("A new ") && l.Contains("born to"))}, weddings {s.SayLog!.Count(l => l.Contains("marr") || l.Contains("Wedding bells") || l.Contains("match is made"))}, pop {s.Walkers.Count(w => !w.Gone && w.Kind == "")}");
                o.Add($"  weddings: friends {s.FriendWeddings}, arranged {s.ArrangedWeddings}, matched {s.MatchWeddings}; meals at a friend's table {s.SharedMeals}; street tables {s.StreetTables} (broken up {s.TablesBroken}), fed {s.StreetFed}; names {s.Walkers.Where(w => !w.Gone && w.Kind == "").Select(w => w.First + " " + w.Last).Distinct().Count()} of {s.Walkers.Count(w => !w.Gone && w.Kind == "")} distinct");
                o.AddRange(s.SayLog!.Where(l => l.Contains("harvest") || l.Contains("seed") || l.Contains("rot") || l.Contains("sells") || l.Contains("Famine")).Take(25).Select(l => "    " + l));
            }
            o.Add($"all: starved {deadAll}, left hungry {fledAll}, mean hungry share {hungryAll / Math.Max(1, samples):P1}, mean panic {panicAll / Math.Max(1, samples):F2}, panic over 0.5 {(double)panicHigh / Math.Max(1, samples):P1} of the time, a line of 3+ {(double)lineSamples / Math.Max(1, samples):P1} of the time, longest {maxQueue}");
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "soak.txt"), string.Join("\n", o) + "\n");
        }
        string[] Illness = { "fever", "cough", "sickness", "lung", "plague", "succumbs", "has passed", "sick", "elected", "dies", "assassinated", "takes", "crowned", "steps down", "taken away", "Revolution", "coup", "anointed", "named Protector", "becomes the elder", "speaks for", "wins" };
        // Fires, many times, in a grown town: dry clear weather and rain, a small town and a grown one with a brigade.
        void SoakFires(double units)
        {
            var lines = new List<string>();
            foreach (var (label, wx) in new[] { ("clear", Wx.Clear), ("rain", Wx.Rain) })
                foreach (var units2 in new[] { 50.0, 90.0, units })
                {
                    int runs = 60, lost = 0, dead = 0, touched = 0, burnt = 0, left = 0; double secs = 0;
                    for (int r = 0; r < runs; r++)
                    {
                        var s = new Sim(2000 + r * 11);
                        s.FreshRegion();
                        s.Quiet = true; s.Earn(units2); s.Grow(); s.Quiet = false; s.FreshFood();
                        s.Tick(0);
                        s.ForceWeather(wx);
                        var o = s.TestFire();
                        lost += o.lost; dead += o.dead; touched += o.touched; secs += o.secs; left += o.left;
                        if (o.lost > 0) burnt++;
                    }
                    lines.Add($"{label,-6} units {units2,4:F0}  burns down {100.0 * burnt / runs,3:F0}%  lots lost {lost / (double)runs,4:F2}  lots touched {touched / (double)runs,4:F2}  lives {dead / (double)runs,5:F2}  {secs / runs,5:F0} s  ruins left after 3 days {left}");
                }
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "soak.txt"), "\n--- one fire, 60 times each ---\n" + string.Join("\n", lines) + "\n");
        }
        // Many regions run for a while: how often wars, blights, aid and refugees happen, and what they cost.
        void SoakWars(double units, int ticks)
        {
            string[] keys = { "marches to war", "declares war", "will not come home", "Victory", "Defeat", "Raiders", "goes to war with", "defeats", "overrun", "Plague breaks out", "Famine in", "The plague reaches", "Famine spreads", "dies of hunger", "burns itself out", "eases", "sends aid", "Wagons of aid", "grain and timber", "put off", "breaks up the festival" };
            var lines = new List<string>();
            foreach (bool calm in new[] { false, true })
            {
                Sim.NoStrife = calm;
                var tot = new int[keys.Length];
                int runs = 8; double days = 0, popSum = 0, samples = 0, goldSum = 0, sisSum = 0;
                for (int r = 0; r < runs; r++)
                {
                    var s = new Sim(3000 + r * 13);
                    s.FreshRegion();
                    s.SayLog = new();
                    s.Quiet = true; s.Earn(units); s.Grow(); s.Quiet = false; s.FreshFood();
                    for (int i = 0; i < ticks; i++)
                    {
                        s.Tick(Sim.TickMs);
                        if (i % 182 == 0) { s.Earn(1); s.Grow(); }
                        if (i % 5000 == 0) { popSum += s.Walkers.Count; goldSum += s.Gold; sisSum += s.Sisters.Count; samples++; }
                    }
                    days += s.EvClock / Sim.DayMs;
                    for (int k = 0; k < keys.Length; k++) tot[k] += s.SayLog.Count(l => l.Contains(keys[k]));
                }
                lines.Add($"--- {(calm ? "WITHOUT war and blight" : "WITH war and blight")}: {runs} regions, {days / runs:F0} days each (counts per 100 region-days) ---");
                lines.AddRange(keys.Select((k, i) => $"{k,-22} {tot[i] * 100.0 / days:F2}"));
                lines.Add($"mean dot-men {popSum / samples:F1}, mean gold {goldSum / samples:F1}, mean sisters {sisSum / samples:F1}");
            }
            Sim.NoStrife = false;
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "soak.txt"), "\n" + string.Join("\n", lines) + "\n");
        }
        // Health of leaders: assassination outcomes by care, lifespan by age at appointment, and how reigns end in long runs.
        void SoakHealth(double units, int ticks)
        {
            var lines = new List<string> { "--- assassination attempts, 300 each ---" };
            foreach (var (label, gov, church) in new[] { ("mayor, no church", "mayor", false), ("council, church", "council", true), ("party, no church", "party", false) })
            {
                int now = 0, later = 0, runs = 300;
                var samples = new List<string>();
                for (int r = 0; r < runs; r++)
                {
                    var s = new Sim(5000 + r * 7);
                    s.FreshRegion();
                    s.SayLog = new();
                    s.Quiet = true; s.Earn(church ? 150 : 40); s.Grow(); s.Quiet = false; s.FreshFood();
                    s.ForceGov(gov);
                    var o = s.TestAttempt();
                    if (o.now) now++;
                    if (o.later) later++;
                    if (samples.Count < 4 && (o.now || o.later)) samples.Add("    " + o.line);
                }
                lines.Add($"{label,-18} killed at once {100.0 * now / runs,3:F0}%  died later {100.0 * later / runs,3:F0}%  survived {100.0 * (runs - now - later) / runs,3:F0}%");
                lines.AddRange(samples);
            }
            lines.Add("--- lifespan from age alone (days until death, 200 each; mayor town with a church) ---");
            foreach (double age in new[] { 25.0, 40, 50, 60, 70 })
            {
                var days = new List<double>();
                for (int r = 0; r < 200; r++)
                {
                    var s = new Sim(7000 + r * 3);
                    s.FreshRegion();
                    s.Quiet = true; s.Earn(150); s.Grow(); s.Quiet = false; s.FreshFood();
                    s.ForceGov("mayor");
                    days.Add(s.TestLifespan(age));
                }
                days.Sort();
                lines.Add($"appointed at {age,2:F0}: median {days[days.Count / 2],5:F1}  mean {days.Average(),5:F1}  tenth {days[days.Count / 10],5:F1}");
            }
            // Long runs: how reigns end and sample causes of death.
            string[] ends = { "dies of", "is assassinated", "An assassin", "steps down", "Election day", "re-elected", "Revolution", "A coup", "takes charge", "survives a heart attack", "survives a stroke", "finds a growth", "is cut from", "takes a fall", "pulled from the fire" };
            var tot = new int[ends.Length];
            var deaths = new List<string>();
            double totDays = 0;
            for (int r = 0; r < 6; r++)
            {
                var s = new Sim(9000 + r * 17);
                s.FreshRegion();
                s.SayLog = new();
                s.Quiet = true; s.Earn(units); s.Grow(); s.Quiet = false; s.FreshFood();
                for (int i = 0; i < ticks; i++) { s.Tick(Sim.TickMs); if (i % 182 == 0) { s.Earn(1); s.Grow(); } }
                totDays += s.EvClock / Sim.DayMs;
                for (int k = 0; k < ends.Length; k++) tot[k] += s.SayLog.Count(l => l.Contains(ends[k]));
                deaths.AddRange(s.SayLog.Where(l => l.Contains("dies of") || l.Contains("assassin") || l.Contains("An assassin")).Take(12));
            }
            lines.Add($"--- 6 regions, {totDays / 6:F0} days each (per 100 days) ---");
            lines.AddRange(ends.Select((k, i) => $"{k,-24} {tot[i] * 100.0 / totDays:F2}"));
            lines.Add("--- sample lines ---");
            lines.AddRange(deaths.Take(40));
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "soak.txt"), "\n" + string.Join("\n", lines) + "\n");
        }
        // How villages choose their rule (0.84): many regions grown to a Village. The new choice against the old roll,
        // how often the leader stays, and whether a gentle or a harsh leader leans the choice their way.
        void SoakRules(int runs)
        {
            Sim.RuleFactors = new();
            var count = new Dictionary<string, int>();
            var oldSum = new Dictionary<string, double>();
            int kept = 0, done = 0;
            double gentleH = 0, harshH = 0; int gentleN = 0, harshN = 0;
            var lines = new List<string>();
            for (int r = 0; r < runs; r++)
            {
                var s = new Sim(6000 + r * 23);
                var o = s.TestRuleChoice();
                if (o == null) continue;
                var (rule, k, line, lh, old) = o.Value;
                done++;
                count[rule] = count.GetValueOrDefault(rule) + 1;
                foreach (var kv in old) oldSum[kv.Key] = oldSum.GetValueOrDefault(kv.Key) + kv.Value;
                if (k) kept++;
                double h = Govs.Get(rule).Harsh;
                if (lh < -0.15) { gentleH += h; gentleN++; } else if (lh > 0.15) { harshH += h; harshN++; }
                if (lines.Count < 30) lines.Add("    " + line);
            }
            var o2 = new List<string> { $"\n--- village rule choice, {done} of {runs} regions reached a Village ---" };
            foreach (var g in Govs.All.Where(g => !Govs.IsHamlet(g.Key)))
                o2.Add($"{g.Key,-8} now {100.0 * count.GetValueOrDefault(g.Key) / Math.Max(1, done),5:F1}%   old roll {100.0 * oldSum.GetValueOrDefault(g.Key) / Math.Max(1, done),5:F1}%");
            o2.Add($"leader stays on {100.0 * kept / Math.Max(1, done):F0}%; mean harshness of the rule chosen: gentle leaders {gentleH / Math.Max(1, gentleN):F2} ({gentleN}), harsh leaders {harshH / Math.Max(1, harshN):F2} ({harshN})");
            if (Sim.RuleFactors != null) foreach (var kv in Sim.RuleFactors) o2.Add($"  {kv.Key,-8} people x{kv.Value[0] / kv.Value[2]:F2}  leader x{kv.Value[1] / kv.Value[2]:F2}");
            o2.AddRange(lines);
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "soak.txt"), string.Join("\n", o2) + "\n");
        }
        // The stage 5 events (0.85) over many regions: how often each happens per 100 town days, and sample lines.
        void SoakEvents(double units, int ticks)
        {
            int runs = 8; double days = 0;
            var tot = new Dictionary<string, double>();
            var samples = new List<string>();
            void Add(string k, double v) => tot[k] = tot.GetValueOrDefault(k) + v;
            for (int r = 0; r < runs; r++)
            {
                var s = new Sim(8000 + r * 29);
                s.FreshRegion();
                s.SayLog = new();
                s.Quiet = true; s.Earn(units); s.Grow(); s.Quiet = false; s.FreshFood();
                for (int i = 0; i < ticks; i++) { s.Tick(Sim.TickMs); if (i % 182 == 0) { s.Earn(1); s.Grow(); } }
                days += s.EvClock / Sim.DayMs;
                Add("heroes", s.Heroes); Add("prophets", s.Prophets); Add("followers", s.Followers); Add("hermits", s.Hermits);
                Add("markets", s.MarketsHeld); Add("pickpockets", s.Pickpockets); Add("floods", s.Floods); Add("flood damage", s.FloodDamages); Add("drowned", s.Drowned);
                Add("comets", s.Comets); Add("feuds begun", s.FeudsBegun); Add("quarrels", s.FeudQuarrels); Add("feuds settled", s.FeudsSettled);
                Add("feuds ended by a wedding", s.FeudsWed); Add("feuds cooled", s.FeudsCooled); Add("feud stabbings", s.FeudStabbings); Add("taken in a feud", s.FeudTaken);
                Add("firebug fires", s.SayLog.Count(l => l.Contains("running from") || l.Contains("was near the")));
                Add("doomsayer cries", s.SayLog.Count(l => l.Contains("crying that")));
                Add("deep grudges said", s.SayLog.Count(l => l.Contains("never forgive")));
                Add("fires", s.SayLog.Count(l => l.StartsWith("Fire!") || l.Contains("Lightning strikes") || l.Contains("running from")));
                samples.AddRange(s.SayLog.Where(l => EventWords.Take(11).Any(l.Contains)).Take(25).Select(l => $"  [{r}] {l}"));
            }
            var o = new List<string> { $"\n--- stage 5 events: {runs} regions, {days / runs:F0} days each (per 100 town days) ---" };
            o.AddRange(tot.Select(kv => $"{kv.Key,-26} {kv.Value * 100 / days,6:F2}"));
            o.AddRange(samples);
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "soak.txt"), string.Join("\n", o) + "\n");
        }
        void SoakTraits()
        {
            // How often each trait shows up, by rule, over many appointments.
            var lines = new List<string>();
            foreach (var g in Govs.All)
            {
                sim.ForceGov(g.Key);
                var count = Traits.All.ToDictionary(t => t.Key, _ => 0);
                for (int i = 0; i < 2000; i++) foreach (var k in sim.TestAppoint()) count[k]++;
                lines.Add($"{g.Key,-8} " + string.Join(" ", count.Select(kv => $"{kv.Key} {kv.Value / 40.0,4:F0}%")));
            }
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "soak.txt"), "\n--- traits per 100 leaders ---\n" + string.Join("\n", lines) + "\n");
        }
        // Each illness, many times, in a grown town under three kinds of rule: how many fall ill and die.
        void SoakIllness(double units)
        {
            var lines = new List<string>();
            foreach (var gk in new[] { "mayor", "council", "junta" })
                for (int d = 0; d < Sim.Diseases.Length; d++)
                {
                    int cases = 0, deaths = 0, pop = 0, runs = 40; double days = 0, care = 0;
                    for (int r = 0; r < runs; r++)
                    {
                        var s = new Sim(1000 + r * 7 + d);
                        s.FreshRegion();
                        s.Quiet = true; s.Earn(units); s.Grow(); s.Quiet = false; s.FreshFood();
                        s.Tick(0);
                        s.ForceGov(gk);
                        care += s.Care();
                        var o = s.TestOutbreak(d);
                        cases += o.cases; deaths += o.deaths; pop += o.pop; days += o.days;
                    }
                    lines.Add($"{gk,-8} {Sim.Diseases[d].Name,-18} town {pop / (double)runs,5:F1}  ill {cases / (double)runs,5:F1}  dead {deaths / (double)runs,5:F1}  ({100.0 * deaths / Math.Max(1, cases),3:F0}% of ill)  {days / runs,4:F1} days  care {care / runs:F2}");
                }
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "soak.txt"), "\n--- illness, 40 outbreaks each ---\n" + string.Join("\n", lines) + "\n");
        }

        // Copy the sim's 0xRRGGBB frame into the texture.
        unsafe void UploadFrame()
        {
            var px = sim.Out;
            for (int i = 0; i < px.Length; i++)
            {
                int c = px[i];
                rgba[i * 4] = (byte)(c >> 16);
                rgba[i * 4 + 1] = (byte)(c >> 8);
                rgba[i * 4 + 2] = (byte)c;
                rgba[i * 4 + 3] = 255;
            }
            fixed (byte* p = rgba) Raylib.UpdateTexture(tex, p);
        }
    }
}
