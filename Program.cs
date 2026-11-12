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
    const string Version = "Dotburg 0.52.0";

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
        readonly Sim sim = new(Environment.TickCount);
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
        Vector2? testMouse;            // --hover: pretend the mouse is here (screenshot tests)
        int testHover = -1;

        public unsafe void Run(string[] args)
        {
            string? shot = null;
            int preTicks = 0;
            double preUnits = 0;
            bool fresh = false;
            int soak = 0;
            Wx? forceWx = null;
            bool forceFest = false;
            string? forceGov = null;
            string? forceSick = null;
            bool forceFire = false, forceWar = false, forceChat = false;
            string? forceBlight = null;
            (int w, int h)? forceSize = null;   // --size WxH: a screenshot at this window size instead of the fitted one
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--soak" && i + 1 < args.Length) soak = int.Parse(args[++i]);
                if (args[i] == "--shot" && i + 1 < args.Length) shot = args[++i];
                else if (args[i] == "--ticks" && i + 1 < args.Length) preTicks = int.Parse(args[++i]);
                else if (args[i] == "--units" && i + 1 < args.Length) preUnits = double.Parse(args[++i]);
                else if (args[i] == "--fresh") fresh = true;
                else if (args[i] == "--wx" && i + 1 < args.Length) forceWx = Enum.Parse<Wx>(args[++i], true);
                else if (args[i] == "--fest") forceFest = true;
                else if (args[i] == "--hover" && i + 1 < args.Length) testHover = int.Parse(args[++i]);
                else if (args[i] == "--gov" && i + 1 < args.Length) forceGov = args[++i];
                else if (args[i] == "--sick" && i + 1 < args.Length) forceSick = args[++i];
                else if (args[i] == "--fire") forceFire = true;
                else if (args[i] == "--size" && i + 1 < args.Length) { var wh = args[++i].Split('x'); forceSize = (int.Parse(wh[0]), int.Parse(wh[1])); }
                else if (args[i] == "--war") forceWar = true;
                else if (args[i] == "--chat") forceChat = true;
                else if (args[i] == "--blight" && i + 1 < args.Length) forceBlight = args[++i];
            }
            if (soak > 0) { Soak(soak, preUnits); SoakTraits(); if (args.Contains("--ill")) SoakIllness(preUnits > 0 ? preUnits : 150); if (args.Contains("--fires")) SoakFires(preUnits > 0 ? preUnits : 150); if (args.Contains("--wars")) SoakWars(preUnits > 0 ? preUnits : 1500, soak); return; }
            bool testing = shot != null;   // screenshot runs never touch the saved town

            // Start from the save (with a capped catch-up for the time the app was closed), else a new region.
            TimeSpan? away = (fresh || testing) ? null : Storage.Load(sim);
            if (away == null) sim.FreshRegion();
            else
            {
                sim.Quiet = true;
                sim.Earn(Math.Min(away.Value.TotalMilliseconds, 2 * 3_600_000.0) / (OwnClock.ThinkMs * 2));
                sim.Grow();
                sim.Quiet = false;
            }
            if (preUnits > 0) { sim.Quiet = true; sim.Earn(preUnits); sim.Grow(); sim.Quiet = false; }
            sim.Tick(0);
            for (int i = 0; i < preTicks; i++) sim.Tick(Sim.TickMs);
            if (forceWx != null) sim.ForceWeather(forceWx.Value);
            if (forceGov != null) sim.ForceGov(forceGov);
            if (forceFest) sim.StartFestival();
            if (forceGov != null) for (int i = 0; i < 200; i++) sim.Tick(Sim.TickMs);
            if (forceWar) { sim.StartWar(); for (int i = 0; i < 120; i++) sim.Tick(Sim.TickMs); }
            if (forceBlight != null) { sim.ForceBlight(forceBlight); for (int i = 0; i < 200; i++) sim.Tick(Sim.TickMs); }
            if (forceFire) { sim.ForceFire(); for (int i = 0; i < 90; i++) sim.Tick(Sim.TickMs); }
            if (forceSick != null) { sim.ForceOutbreak(forceSick); for (int i = 0; i < 300; i++) sim.Tick(Sim.TickMs); }
            if (forceChat) selKey = sim.TestChat();
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
            rlImGui.BeginInitImGui();
            LoadFont();
            rlImGui.EndInitImGui();
            ImGui.GetIO().NativePtr->IniFilename = null;
            Theme();

            int frameNo = 0;
            int fps = 60;
            while (!Raylib.WindowShouldClose())
            {
                frameNo++;
                // Pace the window by attention: full speed with the mouse over it (hover tips, scrolling), slower in the
                // background (still above the sim's 9 ticks a second, so nothing is skipped), very slow minimised.
                int want = testing ? 60 : Raylib.IsWindowMinimized() ? 2 : Raylib.IsCursorOnScreen() ? 60 : Raylib.IsWindowFocused() ? 30 : 15;
                if (want != fps) { fps = want; Raylib.SetTargetFPS(fps); }
                // A long frame (slow pacing, or the window being dragged) is caught up in whole ticks, up to a second.
                double dt = Math.Min(Raylib.GetFrameTime() * 1000.0, 1000);
                acc += dt;
                bool ticked = false;
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
                if (Raylib.IsKeyPressed(KeyboardKey.F11)) Raylib.ToggleBorderlessWindowed();
                Raylib.BeginDrawing();
                Raylib.ClearBackground(panelsOn ? FaceCol : Color.Black);
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
                    break;
                }
            }
            if (!testing) { Storage.FlushChronicle(sim, chronicleView); Storage.Save(sim); }
            rlImGui.Shutdown();
            Raylib.UnloadTexture(tex);
            if (iconTex.Id != 0) Raylib.UnloadTexture(iconTex);
            if (emoTex.Id != 0) Raylib.UnloadTexture(emoTex);
            Raylib.CloseWindow();
        }

        // Headless long run for checking: no window, no files. Prints what the town became.
        string[] Regions = { "Envoys", "letter", "Refugees", "team", "spy", "caravan", "aid", "tribute", "agreement", "settlement", "moves in", "slips", "revolution", "absorbs", "collapses", "quarrel", "Guards turn", "baby", "welcomes a child", "Wedding bells", "neighbours marry", "Fire!", "burns down", "puts out", "spreads to", "Lightning", "great fire", "perishes", "saboteur", "is rebuilt", "collection", "war", "front", "Victory", "Defeat", "soldier", "Raiders", "Plague", "plague", "Famine", "famine", "hunger", "overrun", "sends aid" };
        void Soak(int ticks, double preUnits)
        {
            sim.FreshRegion();
            sim.SayLog = new();
            if (preUnits > 0) { sim.Quiet = true; sim.Earn(preUnits); sim.Grow(); sim.Quiet = false; }
            double acc2 = 0;
            for (int i = 0; i < ticks; i++)
            {
                sim.Tick(Sim.TickMs);
                acc2 += power.Earned(Sim.TickMs);
                if (acc2 >= 1) { sim.Earn(Math.Floor(acc2)); acc2 -= Math.Floor(acc2); sim.Grow(); }
                if (i % 50 == 0) sim.Frame();
            }
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "soak.txt"),
                $"rule {sim.G.Name}, {sim.LeaderTitle}, mood {sim.Mood:F2}; weather {sim.WxWord}; {ticks} ticks = {sim.Age / Sim.DayMs:F1} days: {sim.Name}, {Sim.TierNames[sim.Tier - 1]}, {sim.Walkers.Count} dot-men, {sim.Built.Count} buildings, pool {sim.Pool:F1}, founded {sim.Founded}, gold {sim.Gold:F0}\n" +
                string.Join("\n", sim.Sisters.Select(s => $"sister {s.Name} {s.Total}/{s.Cap} {s.Gov} rel {sim.RelOf(s):F2} gold {sim.GoldOf(s):F0}")) + "\n" +
                string.Join("\n", sim.ChronicleQueue.Take(40)) + "\n--- region lines ---\n" +
                string.Join("\n", sim.SayLog!.Where(l => Regions.Any(k => l.Contains(k))).Take(120)) + "\n--- illness and leaders ---\n" +
                string.Join("\n", sim.SayLog!.Where(l => Illness.Any(k => l.Contains(k)))));
            // How chatty the Messenger is: lines per town day over the last 10 days, by kind of chat, and a sample.
            double days = Math.Min(10, Math.Max(1, sim.Age / Sim.DayMs));
            var kinds = sim.Convos.GroupBy(c => c.Kind).Select(g =>
                $"{g.Key,-8} chats {g.Count(),2}  lines kept {g.Sum(c => c.Lines.Count),4}  per day {g.Sum(c => c.Lines.Count(l => l.Day > sim.DayNumber - 10)) / days:F1}");
            var sample = sim.Convos.Take(4).SelectMany(c => c.Lines.TakeLast(6).Select(l => $"[{c.Title}] {l.From}: {l.Text}"));
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "soak.txt"), "\n--- messenger ---\n" + string.Join("\n", kinds) + "\n" + string.Join("\n", sample) + "\n");
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
                        s.Quiet = true; s.Earn(units2); s.Grow(); s.Quiet = false;
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
                    s.Quiet = true; s.Earn(units); s.Grow(); s.Quiet = false;
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
                        s.Quiet = true; s.Earn(units); s.Grow(); s.Quiet = false;
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
