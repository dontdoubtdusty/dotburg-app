using System.Numerics;
using ImGuiNET;
using Raylib_cs;

namespace Dotburg;

// The panels, drawn as Windows 98 windows: grey bevelled frames with navy title bars. They fill the whole app window
// (no desktop behind them), so the app looks like one old program, and a bigger window just makes them bigger.
// The chrome (frames, title bars, bars, the town picture) goes on ImGui's background draw list; the text sits in
// borderless, see-through ImGui windows laid over it.
public partial class Program
{
    partial class App
    {
        // ---------- Windows 98 colours ----------
        const int Face = 0xc0c0c0, Hi = 0xffffff, Light = 0xdfdfdf, Shade = 0x808080, Ink = 0x000000;
        const int Navy = 0x000080, Sky = 0x1084d0, Paper = 0xffffff, TipBg = 0xffffe1;
        static readonly Color FaceCol = new(0xc0, 0xc0, 0xc0, 255);

        // ---------- fixed sizes, in pixels ----------
        const float Bd = 4;          // window frame (two bevels and a pixel of face)
        const float Pad = 6;         // inside a window, round its content
        const float Gap = 6;         // between windows, and between parts of one
        const float Outer = 4;       // from the screen edge
        const float MinMainW = 640;  // the town window never gets narrower than this, so the government row fits
        const float MsgMin = 320;    // narrower than this and the Messenger is left out
        const float MsgW = 440;      // the Messenger's width when the window is fitted to the panels
        const int MinWinW = 820, MinWinH = 560;   // the smallest the user can drag the window

        Rectangle townRect;          // where the town picture is drawn this frame
        bool panelsOn = true;        // H hides the panels
        bool ledgerOpen = true;      // the ledger's title-bar button rolls it up

        static Vector4 V(int rgb, float a = 1) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, a);
        static uint U(int rgb, float a = 1) => ImGui.ColorConvertFloat4ToU32(V(rgb, a));
        static Vector2 Px(float x, float y) => new(MathF.Floor(x), MathF.Floor(y));
        static int Darken(int rgb, float k) => ((int)(((rgb >> 16) & 255) * k) << 16) | ((int)(((rgb >> 8) & 255) * k) << 8) | (int)((rgb & 255) * k);

        // Two one-pixel edges: top and left in one colour, bottom and right in another.
        static void Edge(ImDrawListPtr dl, Vector2 a, Vector2 b, int tl, int br)
        {
            dl.AddRectFilled(a, new Vector2(b.X, a.Y + 1), U(tl));
            dl.AddRectFilled(a, new Vector2(a.X + 1, b.Y), U(tl));
            dl.AddRectFilled(new Vector2(a.X, b.Y - 1), b, U(br));
            dl.AddRectFilled(new Vector2(b.X - 1, a.Y), b, U(br));
        }

        // A raised frame, as round a window or a button.
        static void Raised(ImDrawListPtr dl, Vector2 a, Vector2 b, int fill = Face)
        {
            dl.AddRectFilled(a, b, U(fill));
            Edge(dl, a, b, Light, Ink);
            Edge(dl, a + Vector2.One, b - Vector2.One, Hi, Shade);
        }

        // A sunken field, as round a text box, a progress bar or the town picture.
        static void Sunken(ImDrawListPtr dl, Vector2 a, Vector2 b, int fill = -1)
        {
            if (fill >= 0) dl.AddRectFilled(a, b, U(fill));
            Edge(dl, a, b, Shade, Hi);
            Edge(dl, a + Vector2.One, b - Vector2.One, Ink, Light);
        }

        // Text drawn twice a pixel apart: Lucida Console has no bold, so this stands in for it.
        static void BoldText(ImDrawListPtr dl, Vector2 at, int col, string text)
        {
            dl.AddText(at, U(col), text);
            dl.AddText(at + new Vector2(1, 0), U(col), text);
        }

        // A title bar: navy fading to blue, the app icon, the title in white.
        void TitleBar(ImDrawListPtr dl, Vector2 a, Vector2 b, string text, bool icon, float rightRoom = 0)
        {
            dl.AddRectFilledMultiColor(a, b, U(Navy), U(Sky), U(Sky), U(Navy));
            float h = b.Y - a.Y, x = a.X + 4;
            if (icon && iconTex.Id != 0)
            {
                float s = h - 6;
                dl.AddImage((IntPtr)iconTex.Id, Px(x, a.Y + 3), Px(x + s, a.Y + 3 + s));
                x += s + 6;
            }
            dl.PushClipRect(a, new Vector2(b.X - rightRoom - 4, b.Y), true);
            BoldText(dl, Px(x, a.Y + (h - ImGui.GetFontSize()) / 2), Hi, text);
            dl.PopClipRect();
        }

        // A title-bar button (minimise when open, maximise when rolled up). True when clicked.
        static bool CaptionButton(ImDrawListPtr dl, Vector2 a, float s, bool open)
        {
            var b = a + new Vector2(s + 2, s);
            bool hot = ImGui.IsMouseHoveringRect(a, b);
            bool down = hot && ImGui.IsMouseDown(ImGuiMouseButton.Left);
            if (down) { dl.AddRectFilled(a, b, U(Face)); Edge(dl, a, b, Ink, Hi); Edge(dl, a + Vector2.One, b - Vector2.One, Shade, Light); }
            else Raised(dl, a, b);
            var o = down ? Vector2.One : Vector2.Zero;
            float gx = a.X + (s + 2) / 2 - 4, gy = a.Y + s / 2 - 4;
            if (open) dl.AddRectFilled(Px(gx, gy + 6) + o, Px(gx + 7, gy + 8) + o, U(Ink));
            else
            {
                dl.AddRect(Px(gx - 1, gy - 1) + o, Px(gx + 9, gy + 8) + o, U(Ink));
                dl.AddRectFilled(Px(gx - 1, gy - 1) + o, Px(gx + 9, gy + 1) + o, U(Ink));
            }
            if (hot) Tip(open ? "Roll up the ledger" : "Show the ledger");
            return hot && ImGui.IsMouseReleased(ImGuiMouseButton.Left);
        }

        // A Windows 98 progress bar: a sunken trough filled with square blocks.
        static void Blocks(ImDrawListPtr dl, Vector2 a, Vector2 b, float frac, int col)
        {
            Sunken(dl, a, b, Paper);
            float x0 = a.X + 3, y0 = a.Y + 3, x1 = b.X - 3, y1 = b.Y - 3;
            float bw = MathF.Max(4, MathF.Round((y1 - y0) * 0.6f)), gap = 2;
            float end = x0 + (x1 - x0) * Math.Clamp(frac, 0, 1);
            for (float x = x0; x + bw * 0.5f <= end && x < x1; x += bw + gap)
                dl.AddRectFilled(new Vector2(x, y0), new Vector2(MathF.Min(x + bw, x1), y1), U(col));
        }

        // Reserves a bar on the current line and draws it. True when the mouse is on it.
        static bool BarItem(float w, float frac, int col)
        {
            var p = ImGui.GetCursorScreenPos();
            float h = ImGui.GetFrameHeight();
            ImGui.Dummy(new Vector2(w, h));
            Blocks(ImGui.GetWindowDrawList(), Px(p.X, p.Y), Px(p.X + w, p.Y + h), frac, col);
            return ImGui.IsItemHovered();
        }

        // A see-through, borderless ImGui window over a rectangle, for the text of one part. Call ImGui.End after.
        static void Area(string id, Vector2 a, Vector2 b)
        {
            ImGui.SetNextWindowPos(a);
            ImGui.SetNextWindowSize(b - a);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.Begin(id, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoFocusOnAppearing);
            ImGui.PopStyleVar();
        }

        // Puts the next item at the right edge of the current line, if it is that wide.
        static void RightAlign(float width)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosX(MathF.Max(ImGui.GetCursorPosX(), ImGui.GetWindowWidth() - width));
        }

        // A tooltip with plain text: ImGui reads the text as a C format string, so a % must be doubled.
        static string Esc(string text) => text.Replace("%", "%%");
        static void Tip(string text) => ImGui.SetTooltip(Esc(text));

        static string MoodWord(double m) => m < 0.3 ? "angry" : m < 0.5 ? "uneasy" : m < 0.7 ? "content" : "joyful";
        static int MoodCol(double m) => m < 0.3 ? 0xa00000 : m < 0.5 ? 0xa06000 : 0x008000;

        // Heights of the fixed parts, from the font: the town window less the picture, the government band, the ledger.
        // (The crier box went in 0.52: crier lines are toasts over the picture now.)
        (float mainExtra, float bandH, float ledgerH) ChromeSizes()
        {
            float row = ImGui.GetFrameHeight(), title = row, sp = ImGui.GetStyle().ItemSpacing.Y;
            float bandH = Pad + row * 2 + sp + Pad;
            float mainExtra = Bd + title + bandH + 4 + Pad + Bd;
            float ledgerH = ledgerOpen ? Bd + title + Pad + row * 3 + sp * 2 + Pad + Bd : Bd + title + Bd;
            return (mainExtra, bandH, ledgerH);
        }

        // The window size that holds the panels exactly, with the town at this scale and the Messenger at its usual width.
        Vector2 FittedSize(int scale)
        {
            var c = ChromeSizes();
            float mainW = MathF.Max(Sim.W * scale + 4 + 2 * (Bd + Pad), MinMainW);
            return new Vector2(MathF.Ceiling(2 * Outer + mainW + Gap + MsgW), MathF.Ceiling(2 * Outer + c.mainExtra + Gap + c.ledgerH + Sim.H * scale));
        }

        // Sizes the app window to the panels at the largest town scale that fits the monitor (less the taskbar and the
        // window's own title bar), and centres it. Needs one ImGui frame first, for the font sizes.
        void FitWindow()
        {
            int m = Raylib.GetCurrentMonitor();
            float mw = Raylib.GetMonitorWidth(m), mh = Raylib.GetMonitorHeight(m);
            fitScale = ScaleRange().max;
            var size = FittedSize(fitScale);
            var at = Raylib.GetMonitorPosition(m);
            Raylib.SetWindowSize((int)size.X, (int)size.Y);
            Raylib.SetWindowPosition((int)(at.X + (mw - size.X) / 2), (int)(at.Y + MathF.Max(32, (mh - 40 - size.Y) / 2)));
        }

        int fitScale = 1;            // the town scale the window was last fitted to
        double resizedAt = -1;       // when the user last resized the window, or -1 if a snap is not waiting

        // The town scales the fitted window can take: at least the window's minimum height, at most what fits the monitor.
        (int min, int max) ScaleRange()
        {
            int m = Raylib.GetCurrentMonitor();
            float mw = Raylib.GetMonitorWidth(m), mh = Raylib.GetMonitorHeight(m);
            int min = 1;
            while (min < 30 && FittedSize(min).Y < MinWinH) min++;
            int max = min;
            while (max < 30 && FittedSize(max + 1).X <= mw * 0.95f && FittedSize(max + 1).Y <= mh - 110) max++;
            return (min, max);
        }

        // Is the window free to be snapped (not maximised, minimised or full screen)?
        static bool Snappable() => !Raylib.IsWindowMaximized() && !Raylib.IsWindowMinimized() && !Raylib.IsWindowState(ConfigFlags.BorderlessWindowMode);

        // After the user resizes the window (and has stopped for a moment), snaps it to the fitted size of the nearest
        // town scale, so a drag grows or shrinks the town a step at a time and never leaves bare space. Top-left stays put.
        void SnapWindow()
        {
            if (Raylib.IsWindowResized()) resizedAt = Raylib.GetTime();
            if (resizedAt < 0 || Raylib.GetTime() - resizedAt < 0.3) return;
            resizedAt = -1;
            if (!Snappable()) return;
            float w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
            var (min, max) = ScaleRange();
            int best = min;
            float bestOff = float.MaxValue;
            for (int s = min; s <= max; s++)
            {
                var n = FittedSize(s);
                float off = MathF.Abs(n.X - w) + MathF.Abs(n.Y - h);
                if (off < bestOff) { bestOff = off; best = s; }
            }
            fitScale = best;
            var size = FittedSize(best);
            if ((int)size.X != (int)w || (int)size.Y != (int)h) Raylib.SetWindowSize((int)size.X, (int)size.Y);
        }

        // Rolling the ledger up or down changes the panels' height, so the window follows at the same town scale.
        void RefitHeight()
        {
            if (!Snappable()) return;
            var size = FittedSize(fitScale);
            Raylib.SetWindowSize((int)size.X, (int)size.Y);
        }

        // The whole window: the town window (government on top, the picture with toasts), the ledger under it, and the
        // Messenger beside them. They always fill the window: spare width goes to the Messenger, spare height to the town window
        // round the picture (the picture keeps a whole-number scale so its pixels stay square).
        void DrawUi()
        {
            float sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
            var bg = ImGui.GetBackgroundDrawList();
            if (!panelsOn)
            {
                int s0 = Math.Max(1, (int)Math.Min(sw / Sim.W, sh / Sim.H));
                townRect = new Rectangle(MathF.Floor((sw - Sim.W * s0) / 2), MathF.Floor((sh - Sim.H * s0) / 2), Sim.W * s0, Sim.H * s0);
                DrawTown(bg, s0);
                DrawToasts(bg);
                HoverTip();
                return;
            }

            float row = ImGui.GetFrameHeight(), title = row, sp = ImGui.GetStyle().ItemSpacing.Y;
            var (mainExtra, bandH, ledgerH) = ChromeSizes();
            float chromeH = Outer + mainExtra + Gap + ledgerH + Outer;
            int scale = Math.Max(1, (int)Math.Min((sh - chromeH) / Sim.H, (sw - 2 * Outer - 2 * (Bd + Pad) - 4) / Sim.W));
            float picW = Sim.W * scale, picH = Sim.H * scale;
            float availW = sw - 2 * Outer;
            float tightW = MathF.Max(picW + 4 + 2 * (Bd + Pad), MinMainW);
            float spare = availW - tightW - Gap;
            bool chron = spare >= MsgMin;
            float mainW = chron ? tightW : MathF.Max(availW, tightW);
            float chronW = chron ? MathF.Floor(spare) : 0;
            float extraH = MathF.Max(0, MathF.Floor(sh - chromeH - picH));
            float x0 = Outer, y0 = Outer;

            // The town window: title, government band, picture (toasts rise over it).
            var mA = new Vector2(x0, y0);
            var mB = new Vector2(x0 + mainW, y0 + mainExtra + picH + extraH);
            Raised(bg, mA, mB);
            var tA = mA + new Vector2(Bd, Bd);
            var tB = new Vector2(mB.X - Bd, tA.Y + title);
            TitleBar(bg, tA, tB, $"{sim.Name} - {Sim.TierNames[sim.Tier - 1]}", true);
            float bandTop = tB.Y;
            var picA = Px(x0 + (mainW - picW) / 2, bandTop + bandH + 2 + extraH / 2);
            Sunken(bg, picA - new Vector2(2, 2), picA + new Vector2(picW + 2, picH + 2));
            townRect = new Rectangle(picA.X, picA.Y, picW, picH);
            DrawTown(bg, scale);
            DrawToasts(bg);

            Area("gov", new Vector2(x0 + Bd + Pad, bandTop + Pad), new Vector2(mB.X - Bd - Pad, bandTop + Pad + row * 2 + sp));
            GovBand();
            ImGui.End();

            // The ledger: day and weather in its title; people, buildings, troubles and sisters inside.
            var lA = new Vector2(x0, mB.Y + Gap);
            var lB = new Vector2(x0 + mainW, lA.Y + ledgerH);
            Raised(bg, lA, lB);
            var ltA = lA + new Vector2(Bd, Bd);
            var ltB = new Vector2(lB.X - Bd, ltA.Y + title);
            float btn = title - 6;
            TitleBar(bg, ltA, ltB, $"Day {sim.DayNumber} - {sim.SeasonNow.Name} {sim.DayName()}, {sim.WxWord}{(sim.Festival ? ", festival" : "")}", false, btn + 4);
            if (CaptionButton(bg, Px(ltB.X - btn - 4, ltA.Y + 3), btn, ledgerOpen)) { ledgerOpen = !ledgerOpen; RefitHeight(); }
            if (ledgerOpen)
            {
                float inX0 = lA.X + Bd + Pad, inX1 = lB.X - Bd - Pad, inY0 = ltB.Y + Pad, inY1 = lB.Y - Bd - Pad;
                float split = MathF.Floor(inX0 + (inX1 - inX0) * 0.56f);
                float colY1 = inY0 + row * 2 + sp;   // two rows in columns, then troubles across the whole width
                bg.AddRectFilled(new Vector2(split + Gap - 1, inY0), new Vector2(split + Gap, colY1), U(Shade));
                bg.AddRectFilled(new Vector2(split + Gap, inY0), new Vector2(split + Gap + 1, colY1), U(Hi));
                Area("ledgerL", new Vector2(inX0, inY0), new Vector2(split, colY1));
                LedgerTown();
                ImGui.End();
                Area("ledgerR", new Vector2(split + Gap * 2 + 1, inY0), new Vector2(inX1, colY1));
                LedgerSisters();
                ImGui.End();
                Area("ledgerT", new Vector2(inX0, colY1 + sp), new Vector2(inX1, inY1));
                LedgerTroubles();
                ImGui.End();
            }

            // The Messenger: beside the other two, as tall as both (MessengerUi.cs).
            if (chron) Messenger(bg, new Vector2(mB.X + Gap, y0), new Vector2(mB.X + Gap + chronW, lB.Y));
            HoverTip();
        }

        // The town picture, and an outline on the building under the mouse.
        void DrawTown(ImDrawListPtr dl, int scale)
        {
            var a = new Vector2(townRect.X, townRect.Y);
            var size = new Vector2(townRect.Width, townRect.Height);
            dl.AddImage((IntPtr)tex.Id, a, a + size);
            hoverRank = -1;
            if (testHover >= 0 && testHover < sim.Built.Count && testHover < sim.Lots.Count)
            {
                var b = sim.LotBox(testHover);
                testMouse = new Vector2(a.X + (b.x + b.w / 2f) * scale, a.Y + (b.y + b.h / 2f) * scale);
            }
            var m = testMouse ?? Raylib.GetMousePosition();
            if (m.X >= a.X && m.Y >= a.Y && m.X < a.X + size.X && m.Y < a.Y + size.Y)
                hoverRank = sim.RankAtPixel((int)((m.X - a.X) / scale), (int)((m.Y - a.Y) / scale));
            if (hoverRank >= 0)
            {
                var b = sim.LotBox(hoverRank);
                dl.AddRect(new Vector2(a.X + b.x * scale - 1, a.Y + b.y * scale - 1), new Vector2(a.X + (b.x + b.w) * scale + 1, a.Y + (b.y + b.h) * scale + 1), U(0xffeca0, 0.9f), 0, ImDrawFlags.None, 2);
            }
        }

        // Government band, two rows: banner, rule and leader with the treasury at the right; the leader's traits,
        // then the townsfolk's mood at the right.
        void GovBand()
        {
            var gv = sim.G;
            var dl = ImGui.GetWindowDrawList();
            float row = ImGui.GetFrameHeight();
            var p = ImGui.GetCursorScreenPos();
            float s = row - 6;
            ImGui.Dummy(new Vector2(s, row));
            Raised(dl, Px(p.X, p.Y + 3), Px(p.X + s, p.Y + 3 + s), gv.Banner & 0xffffff);
            if (ImGui.IsItemHovered()) Tip($"The banner of the {gv.Lower}");
            ImGui.SameLine(0, 8);
            ImGui.AlignTextToFramePadding();
            ImGui.Text(gv.Name);
            dl.AddText(ImGui.GetItemRectMin() + new Vector2(1, 0), U(Ink), gv.Name);
            ImGui.SameLine(0, 14);
            ImGui.Text(sim.LeaderTitle);
            float used = ImGui.GetItemRectMax().X - ImGui.GetWindowPos().X;
            string tr = $"Treasury: {Sim.WealthWord(sim.Gold)}, {sim.Gold:F0} gold";
            foreach (var t in new[] { tr, $"{Sim.WealthWord(sim.Gold)}, {sim.Gold:F0} gold", $"{sim.Gold:F0} gold" }) { tr = t; if (used + ImGui.CalcTextSize(t).X + 16 <= ImGui.GetWindowWidth()) break; }
            RightAlign(ImGui.CalcTextSize(tr).X);
            ImGui.Text(tr);
            if (ImGui.IsItemHovered()) Tip($"The town's purse: {Sim.WealthWord(sim.Gold)} ({sim.Gold:F0} of 100 gold).");

            ImGui.AlignTextToFramePadding();
            ImGui.Dummy(new Vector2(s, row));
            ImGui.SameLine(0, 8);
            ImGui.AlignTextToFramePadding();
            if (sim.TraitWords != "")
            {
                ImGui.TextColored(V(Navy), sim.TraitWords);
                if (ImGui.IsItemHovered() && sim.TraitBlurb != "") Tip(sim.TraitBlurb);
            }
            else ImGui.TextColored(V(Shade), "no leader sits");
            float barW = 150, wordW = ImGui.CalcTextSize("content").X, labW = ImGui.CalcTextSize("Townsfolk:").X;
            var sty = ImGui.GetStyle();
            RightAlign(labW + barW + wordW + sty.ItemSpacing.X * 2);
            ImGui.Text("Townsfolk:");
            ImGui.SameLine();
            bool hov = BarItem(barW, (float)sim.Mood, MoodCol(sim.Mood));
            ImGui.SameLine();
            ImGui.TextColored(V(Darken(MoodCol(sim.Mood), 0.8f)), MoodWord(sim.Mood));
            hov |= ImGui.IsItemHovered();
            if (hov) Tip($"How the townsfolk feel ({sim.Mood * 100:F0} of 100). The rule, the weather, crowding,\nsickness, hunger, war and ruins all count. Low spirits breed unrest.");
        }

        // Ledger, left: dot-men by colour, buildings and the next thing to come, and any troubles.
        void LedgerTown()
        {
            float labW = ImGui.CalcTextSize("Buildings ").X, valW = ImGui.CalcTextSize("000/000 ").X;
            int pop = sim.Walkers.Count(w => !w.Gone);

            ImGui.AlignTextToFramePadding();
            ImGui.Text("Dot-men");
            ImGui.SameLine(labW);
            ImGui.Text($"{pop}/{Math.Max(pop, sim.PopCap)}");
            if (ImGui.IsItemHovered()) Tip($"{pop} living here, room for {Math.Max(pop, sim.PopCap)}.");
            ImGui.SameLine(labW + valW);
            PopBar(ImGui.GetContentRegionAvail().X);

            ImGui.AlignTextToFramePadding();
            ImGui.Text("Buildings");
            ImGui.SameLine(labW);
            ImGui.Text($"{sim.Built.Count}");
            ImGui.SameLine(labW + valW);
            string next = sim.NextThing();
            // An upgrade ("two-storey house to row houses") shows only what it becomes; the tip has the whole line.
            int to = next.IndexOf(" to ", StringComparison.Ordinal);
            string shortNext = to >= 0 ? next[(to + 1)..] : next;
            float frac = (float)Math.Clamp(sim.Pool / Math.Max(1, sim.Cost), 0, 1);
            float roomX = ImGui.GetContentRegionAvail().X;
            float barW = Math.Clamp(roomX - ImGui.CalcTextSize(shortNext).X - ImGui.GetStyle().ItemSpacing.X, 60, roomX * 0.45f);
            bool hov = BarItem(barW, frac, Navy);
            ImGui.SameLine();
            ImGui.Text(Esc(shortNext));
            hov |= ImGui.IsItemHovered();
            if (hov) Tip($"Next: {next} ({frac * 100:F0}% saved up).");
        }

        // Ledger, bottom row across both columns: fires and ruins, war or famine, illness, or "none".
        void LedgerTroubles()
        {
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Troubles");
            ImGui.SameLine(ImGui.CalcTextSize("Buildings ").X);
            var troubles = new List<(string text, int col)>();
            if (sim.FireLine() is { Length: > 0 } f) troubles.Add((f, sim.Fires.Count > 0 ? 0xb00000 : 0x704000));
            if (sim.WarLine() is { Length: > 0 } w) troubles.Add((w, 0x800000));
            if (sim.IllnessLine() is { Length: > 0 } ill) troubles.Add((ill, 0x405000));
            if (troubles.Count == 0) { ImGui.TextColored(V(0x004000), "none"); return; }
            for (int i = 0; i < troubles.Count; i++)
            {
                if (i > 0) { ImGui.SameLine(0, 0); ImGui.Text("; "); ImGui.SameLine(0, 0); }
                ImGui.TextColored(V(troubles[i].col), Esc(troubles[i].text));
                if (ImGui.IsItemHovered()) Tip(string.Join("\n", troubles.Select(t => t.text)));
            }
        }

        // One bar split by colour: each colour's share of the town, with its count where it fits.
        void PopBar(float w)
        {
            var counts = new int[Content.DotColors.Length];
            foreach (var wk in sim.Walkers) if (!wk.Gone) counts[wk.C]++;
            int total = counts.Sum();
            var p = ImGui.GetCursorScreenPos();
            float h = ImGui.GetFrameHeight();
            ImGui.Dummy(new Vector2(w, h));
            bool hov = ImGui.IsItemHovered();
            var dl = ImGui.GetWindowDrawList();
            var a = Px(p.X, p.Y);
            var b = Px(p.X + w, p.Y + h);
            Sunken(dl, a, b, Paper);
            if (total == 0) { dl.AddText(a + new Vector2(6, (h - ImGui.GetFontSize()) / 2), U(Shade), "nobody yet"); return; }
            float x0 = a.X + 2, x1 = b.X - 2, y0 = a.Y + 2, y1 = b.Y - 2;
            var order = Enumerable.Range(0, counts.Length).Where(c => counts[c] > 0).OrderByDescending(c => counts[c]).ToList();
            int acc = 0;
            var mouse = ImGui.GetMousePos();
            foreach (int c in order)
            {
                float xa = MathF.Floor(x0 + (x1 - x0) * acc / total);
                acc += counts[c];
                float xb = MathF.Floor(x0 + (x1 - x0) * acc / total);
                int col = Content.DotColors[c];
                dl.AddRectFilled(new Vector2(xa, y0), new Vector2(xb, y1), U(col));
                if (acc < total) dl.AddRectFilled(new Vector2(xb - 1, y0), new Vector2(xb, y1), U(Ink, 0.35f));
                string label = counts[c].ToString();
                var ts = ImGui.CalcTextSize(label);
                if (ts.X + 6 <= xb - xa)
                {
                    int lum = (((col >> 16) & 255) * 299 + ((col >> 8) & 255) * 587 + (col & 255) * 114) / 1000;
                    dl.AddText(Px(xa + (xb - xa - ts.X) / 2, y0 + (y1 - y0 - ts.Y) / 2), U(lum > 140 ? Ink : Hi), label);
                }
                if (hov && mouse.X >= xa && mouse.X < xb) dl.AddRect(new Vector2(xa, y0), new Vector2(xb, y1), U(Ink), 0, ImDrawFlags.None, 2);
            }
            if (hov) Tip(string.Join("\n", order.Select(c => $"{counts[c],3} {Content.DotNames[c]}")));
        }

        // Ledger, right: a strip of sister-town icons (hover for each), then the newest two by name.
        void LedgerSisters()
        {
            ImGui.AlignTextToFramePadding();
            if (sim.Sisters.Count == 0)
            {
                ImGui.Text("Sister towns");
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(V(Shade), "none yet");
                if (ImGui.IsItemHovered()) Tip("When this town becomes a City and earns its charter,\nit becomes a sister and a new hamlet is founded.");
                return;
            }
            ImGui.Text("Sisters");
            ImGui.SameLine();
            var dl = ImGui.GetWindowDrawList();
            float row = ImGui.GetFrameHeight();
            float px = MathF.Floor((row - 4) / Sim.IconSize), step = Sim.IconSize * px + 4;
            var origin = ImGui.GetCursorScreenPos();
            int icons = Math.Min(sim.Sisters.Count, (int)(ImGui.GetContentRegionAvail().X / step));
            for (int k = 0; k < icons; k++)
            {
                var s = sim.Sisters[sim.Sisters.Count - icons + k];
                var ip = sim.IconPixels(s);
                var at = Px(origin.X + k * step, origin.Y + (row - Sim.IconSize * px) / 2);
                for (int j = 0; j < ip.Length; j++)
                {
                    if (ip[j] < 0) continue;
                    var q = at + new Vector2(j % Sim.IconSize * px, j / Sim.IconSize * px);
                    dl.AddRectFilled(q, q + new Vector2(px, px), U(ip[j]));
                }
                if (ImGui.IsMouseHoveringRect(at, at + new Vector2(Sim.IconSize * px, Sim.IconSize * px)))
                    Tip($"{s.Name}: {s.Total} dot-men, {Govs.Get(s.Gov).Name}, {Sim.WealthWord(sim.GoldOf(s))}, {sim.Attitude(s).word}");
            }
            ImGui.Dummy(new Vector2(Math.Max(1, icons * step), row));
            // The newest sister by name and how she feels about us; the rest is in the icon tips.
            var ns = sim.Sisters[^1];
            var (word, wc) = sim.Attitude(ns);
            ImGui.AlignTextToFramePadding();
            ImGui.Text($"{ns.Name} {ns.Total},");
            bool hov = ImGui.IsItemHovered();
            ImGui.SameLine(0, 8);
            ImGui.TextColored(V(Darken(wc, 0.55f)), word);
            if (hov || ImGui.IsItemHovered()) Tip($"{ns.Name}: {ns.Total} dot-men, {Govs.Get(ns.Gov).Name}, {Sim.WealthWord(sim.GoldOf(ns))}, {word}");
        }

        // The tooltip for the building under the mouse.
        void HoverTip()
        {
            if (hoverRank < 0 || hoverRank >= sim.Built.Count) return;
            var (title, lines) = sim.InfoFor(hoverRank);
            if (testMouse != null) ImGui.SetNextWindowPos(testMouse.Value + new Vector2(16, 16));
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 24);
            var tp = ImGui.GetCursorScreenPos();
            ImGui.Text(title);
            ImGui.GetWindowDrawList().AddText(tp + new Vector2(1, 0), U(Ink), title);
            ImGui.Separator();
            foreach (var l in lines) ImGui.TextWrapped(Esc(l));
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }

        // The panels use Lucida Console (the font of the PowerShell console the mods run in) at 18 px.
        // Falls back to Segoe UI, then Pixelify Sans, then the ImGui default.
        static unsafe void LoadFont()
        {
            var io = ImGui.GetIO();
            string fonts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
            string[] tries = { Path.Combine(fonts, "lucon.ttf"), Path.Combine(fonts, "segoeui.ttf"), Path.Combine(AppContext.BaseDirectory, "resources", "fonts", "PixelifySans.ttf") };
            string? path = tries.FirstOrDefault(File.Exists);
            if (path == null) { io.Fonts.AddFontDefault(); io.FontGlobalScale = 1.5f; return; }
            io.Fonts.AddFontFromFileTTF(path, 18f);
        }

        // Square corners, black text, Windows 98 tooltips (pale yellow, black edge). The windows themselves are
        // see-through: their frames are drawn by DrawUi.
        static void Theme()
        {
            var st = ImGui.GetStyle();
            st.WindowRounding = st.ChildRounding = st.FrameRounding = st.PopupRounding = st.ScrollbarRounding = st.GrabRounding = 0;
            st.WindowBorderSize = 1;
            st.ChildBorderSize = 0;
            st.PopupBorderSize = 1;
            st.WindowPadding = new Vector2(8, 6);
            st.FramePadding = new Vector2(4, 3);
            st.ItemSpacing = new Vector2(8, 4);
            st.ScrollbarSize = 16;
            var c = st.Colors;
            c[(int)ImGuiCol.Text] = V(Ink);
            c[(int)ImGuiCol.TextDisabled] = V(Shade);
            c[(int)ImGuiCol.WindowBg] = V(0, 0);
            c[(int)ImGuiCol.ChildBg] = V(0, 0);
            c[(int)ImGuiCol.PopupBg] = V(TipBg);
            c[(int)ImGuiCol.Border] = V(Ink);
            c[(int)ImGuiCol.Separator] = V(Shade);
            c[(int)ImGuiCol.ScrollbarBg] = V(Light);
            c[(int)ImGuiCol.ScrollbarGrab] = V(Face);
            c[(int)ImGuiCol.ScrollbarGrabHovered] = V(0xd0d0d0);
            c[(int)ImGuiCol.ScrollbarGrabActive] = V(Shade);
        }
    }
}
