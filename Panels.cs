using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using Raylib_cs;

namespace Dotburg;

// The panels, drawn as Windows XP windows (Luna blue, the look of MSN Messenger 7): blue frames with rounded top
// corners and glossy title bars over a beige face. They fill the whole app window, so the app looks like a few XP
// windows on a desktop, and a bigger window just makes them bigger. The chrome (frames, title bars, bars, the town
// picture) goes on ImGui's background draw list; the text sits in borderless, see-through ImGui windows laid over it.
public partial class Program
{
    partial class App
    {
        // ---------- Windows XP colours ----------
        const int Face = 0xece9d8, Hi = 0xffffff, Shade = 0x808080, Ink = 0x000000, Etch = 0xaca899;
        const int Navy = 0x0a246a, Field = 0x7f9db9, Paper = 0xffffff, TipBg = 0xffffe1;
        const int Frame = 0x0055e5, FrameEdge = 0x0029b0, Select = 0x316ac5, XpGreen = 0x2eb82e, Desk = 0x5a7fc7;
        static readonly Color DeskCol = new(0x5a, 0x7f, 0xc7, 255);

        // ---------- fixed sizes, in pixels ----------
        const float Bd = 4;          // window frame at the sides and bottom
        const float Pad = 6;         // inside a window, round its content
        const float Gap = 6;         // between windows, and between parts of one
        const float Outer = 4;       // from the screen edge
        const float Corner = 7;      // rounding of a window's top corners
        const float MinMainW = 640;  // the town window never gets narrower than this, so the government row fits
        const float MsgMin = 320;    // narrower than this and the Messenger is left out
        const float MsgW = 440;      // the Messenger's width when the window is fitted to the panels
        const int MinWinW = 820, MinWinH = 560;   // the smallest the user can drag the window

        Rectangle townRect;          // where the town picture is drawn this frame
        bool panelsOn = true;        // H hides the panels
        bool ledgerOpen = true;      // the ledger's title-bar button rolls it up

        static ImFontPtr boldFont;   // Tahoma Bold, when it loaded
        static bool hasBold;
        static readonly Dictionary<string, ImFontPtr> chatFonts = new();   // chat fonts by face, loaded with the main font

        static Vector4 V(int rgb, float a = 1) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, a);
        static uint U(int rgb, float a = 1) => ImGui.ColorConvertFloat4ToU32(V(rgb, a));
        static Vector2 Px(float x, float y) => new(MathF.Floor(x), MathF.Floor(y));
        static int Darken(int rgb, float k) => ((int)(((rgb >> 16) & 255) * k) << 16) | ((int)(((rgb >> 8) & 255) * k) << 8) | (int)((rgb & 255) * k);

        // The height of a window's title bar (under the top frame edge), from the font.
        static float TitleH => ImGui.GetFrameHeight() + 6;

        // A vertical gradient from one colour at the top to another at the bottom.
        static void Grad(ImDrawListPtr dl, Vector2 a, Vector2 b, int top, int bottom) =>
            dl.AddRectFilledMultiColor(a, b, U(top), U(top), U(bottom), U(bottom));

        // A Luna window from a to b: blue frame with rounded top corners, a glossy title bar Bd + titleH tall with the
        // title in white bold (and the app icon), and the face colour inside under it.
        void XpWindow(ImDrawListPtr dl, Vector2 a, Vector2 b, float titleH, string text, bool icon, float rightRoom = 0, int face = Face)
        {
            float h = Bd + titleH, tb = a.Y + h;
            dl.AddRectFilled(a, b, U(Frame), Corner, ImDrawFlags.RoundCornersTop);
            dl.AddRectFilled(a, new Vector2(b.X, a.Y + Corner * 2), U(0x2b7af4), Corner, ImDrawFlags.RoundCornersTop);
            Grad(dl, new Vector2(a.X, a.Y + Corner), new Vector2(b.X, a.Y + h * 0.35f), 0x2b7af4, 0x0055ea);
            Grad(dl, new Vector2(a.X, a.Y + h * 0.35f), new Vector2(b.X, a.Y + h * 0.8f), 0x0055ea, 0x0a63f2);
            Grad(dl, new Vector2(a.X, a.Y + h * 0.8f), new Vector2(b.X, tb), 0x0a63f2, 0x0042d0);
            dl.AddRectFilled(new Vector2(a.X + Corner, a.Y + 1), new Vector2(b.X - Corner, a.Y + 2), U(0x8ab8ff));
            dl.AddRectFilled(new Vector2(a.X + Bd, tb), new Vector2(b.X - Bd, b.Y - Bd), U(face));
            dl.AddRect(a, b, U(FrameEdge), Corner, ImDrawFlags.RoundCornersTop);

            float x = a.X + Bd + 4;
            if (icon && iconTex.Id != 0)
            {
                float s = titleH - 8;
                dl.AddImage((IntPtr)iconTex.Id, Px(x, a.Y + (h - s) / 2), Px(x + s, a.Y + (h - s) / 2 + s));
                x += s + 6;
            }
            dl.PushClipRect(a, new Vector2(b.X - Bd - rightRoom - 4, tb), true);
            var at = Px(x, a.Y + (h - ImGui.GetFontSize()) / 2);
            BoldText(dl, at + Vector2.One, 0x0a1e80, text);
            BoldText(dl, at, Hi, text);
            dl.PopClipRect();
        }

        // A white field with XP's thin blue-grey edge, as round a text box, a list or the town picture.
        static void Sunken(ImDrawListPtr dl, Vector2 a, Vector2 b, int fill = -1, float round = 0)
        {
            if (fill >= 0) dl.AddRectFilled(a, b, U(fill), round);
            dl.AddRect(a, b, U(Field), round);
        }

        // A small coloured tile with a darker edge, as for a banner.
        static void Chip(ImDrawListPtr dl, Vector2 a, Vector2 b, int fill)
        {
            dl.AddRectFilled(a, b, U(fill), 2);
            dl.AddRect(a, b, U(Darken(fill, 0.55f)), 2);
        }

        // Bold text in Tahoma Bold, or drawn twice a pixel apart if that font is missing.
        static void BoldText(ImDrawListPtr dl, Vector2 at, int col, string text, float alpha = 1)
        {
            if (hasBold) { dl.AddText(boldFont, ImGui.GetFontSize(), at, U(col, alpha), text); return; }
            dl.AddText(at, U(col, alpha), text);
            dl.AddText(at + new Vector2(1, 0), U(col, alpha), text);
        }

        // How wide bold text is.
        static float BoldW(string text) =>
            hasBold ? boldFont.CalcTextSizeA(ImGui.GetFontSize(), float.MaxValue, 0, text).X : ImGui.CalcTextSize(text).X + 1;

        // Bold text as an ImGui item in the current window.
        static void BoldItem(string text)
        {
            if (hasBold) { ImGui.PushFont(boldFont); ImGui.Text(text); ImGui.PopFont(); return; }
            ImGui.Text(text);
            ImGui.GetWindowDrawList().AddText(ImGui.GetItemRectMin() + new Vector2(1, 0), U(Ink), text);
        }

        // An XP title-bar button (minimise when open, restore when rolled up): a glossy blue square with a white edge.
        // True when clicked.
        static bool CaptionButton(ImDrawListPtr dl, Vector2 a, float s, bool open)
        {
            var b = a + new Vector2(s, s);
            bool hot = !modal && ImGui.IsMouseHoveringRect(a, b, false);
            bool down = hot && ImGui.IsMouseDown(ImGuiMouseButton.Left);
            int top = down ? 0x1a4fc8 : hot ? 0x6aa6ff : 0x3d8cf7, bot = down ? 0x3a7ef0 : hot ? 0x2a74f2 : 0x1659e0;
            dl.AddRectFilled(a, b, U(bot), 3);
            Grad(dl, a + new Vector2(2, 2), new Vector2(b.X - 2, a.Y + s * 0.55f), top, bot);
            dl.AddRect(a, b, U(Hi), 3);
            if (open) dl.AddRectFilled(Px(a.X + s * 0.26f, a.Y + s * 0.66f), Px(a.X + s * 0.58f, a.Y + s * 0.66f + 3), U(Hi));
            else
            {
                dl.AddRect(Px(a.X + s * 0.26f, a.Y + s * 0.26f), Px(a.X + s * 0.76f, a.Y + s * 0.74f), U(Hi));
                dl.AddRectFilled(Px(a.X + s * 0.26f, a.Y + s * 0.26f), Px(a.X + s * 0.76f, a.Y + s * 0.26f + 3), U(Hi));
            }
            if (hot) Tip(open ? "Roll up the ledger" : "Show the ledger");
            return hot && ImGui.IsMouseReleased(ImGuiMouseButton.Left);
        }

        // An XP progress bar: a rounded white trough filled with glossy blocks, shiny across the middle.
        static void Blocks(ImDrawListPtr dl, Vector2 a, Vector2 b, float frac, int col)
        {
            dl.AddRectFilled(a, b, U(Paper), 3);
            dl.AddRect(a, b, U(0x8e8f8f), 3);
            float x0 = a.X + 3, y0 = a.Y + 3, x1 = b.X - 3, y1 = b.Y - 3, ym = MathF.Floor((y0 + y1) / 2);
            float bw = MathF.Max(4, MathF.Round((y1 - y0) * 0.5f)), gap = 2;
            float end = x0 + (x1 - x0) * Math.Clamp(frac, 0, 1);
            int shine = Lighten(col, 0.5f);
            for (float x = x0; x + bw * 0.5f <= end && x < x1; x += bw + gap)
            {
                float xe = MathF.Min(x + bw, x1);
                Grad(dl, new Vector2(x, y0), new Vector2(xe, ym), col, shine);
                Grad(dl, new Vector2(x, ym), new Vector2(xe, y1), shine, col);
            }
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
            // No padding and no border, so the clip rect starts at the edge: the crisp GDI glyphs use their first
            // pixel column, which a 1 px border would cut off.
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
            ImGui.Begin(id, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoFocusOnAppearing);
            ImGui.PopStyleVar(2);
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
        static int MoodCol(double m) => m < 0.3 ? 0xd02020 : m < 0.5 ? 0xe08a00 : XpGreen;

        // Heights of the fixed parts, from the font: the town window less the picture, the government band, the ledger.
        // (The crier box went in 0.52: crier lines are toasts over the picture now.)
        (float mainExtra, float bandH, float ledgerH) ChromeSizes()
        {
            float row = ImGui.GetFrameHeight(), title = TitleH, sp = ImGui.GetStyle().ItemSpacing.Y;
            float bandH = Pad + row * 2 + sp + Pad;
            float mainExtra = Bd + title + bandH + 4 + Pad + Bd;
            float ledgerH = ledgerOpen ? Bd + title + Pad + row * 4 + sp * 3 + Pad + Bd : Bd + title + Bd;
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

        // The version as shown inside the window ("v0.76.2"): faint in the town window's title bar, or in the bottom-left
        // corner while the panels are hidden.
        static readonly string VersionTag = "v" + Version[(Version.LastIndexOf(' ') + 1)..];

        bool bare;              // the window is maximised and its Windows title bar has been taken off
        Vector2 normalPos, normalSize;   // the window's inner place and size the last time it was a normal window

        // A maximised window drops the Windows title bar, so only the XP panels show; restoring puts it back.
        // With no bar to click, a double-click on the top strip (the XP title bars) restores the window, as in Windows.
        void FrameOnMaximize()
        {
            bool max = Raylib.IsWindowMaximized() && !Raylib.IsWindowState(ConfigFlags.BorderlessWindowMode);
            if (max && !bare)
            {
                // A frameless window maximises over the taskbar; FitWorkArea (below, every frame) pulls it back to the work area.
                bare = true;
                Raylib.SetWindowState(ConfigFlags.UndecoratedWindow);
            }
            else if (!max && bare)
            {
                // Putting the frame back would push the title bar up off the screen; go back to the place and size from before.
                bare = false;
                Raylib.ClearWindowState(ConfigFlags.UndecoratedWindow);
                Raylib.SetWindowSize((int)normalSize.X, (int)normalSize.Y);
                Raylib.SetWindowPosition((int)normalPos.X, (int)normalPos.Y);
            }
            else if (!max && !Raylib.IsWindowMinimized() && !Raylib.IsWindowState(ConfigFlags.BorderlessWindowMode))
            {
                normalPos = Raylib.GetWindowPosition();
                normalSize = new Vector2(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
            }
            if (bare) FitWorkArea();
            if (bare && !modal && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && ImGui.GetMousePos().Y < Outer + Bd + TitleH)
                RestoreFromMax();
        }

        static bool quit;   // the close button of a frameless maximised window was clicked: the main loop ends and saves

        // Puts a maximised window back to its normal size. Asks Windows directly (SW_RESTORE), since Raylib's restore
        // depends on flags it keeps itself; FrameOnMaximize then puts the frame back on the next frame.
        static unsafe void RestoreFromMax() => ShowWindow((IntPtr)Raylib.GetWindowHandle(), 9);

        // How much of the rightmost title bar the window buttons take (none unless the Windows title bar is off).
        float CapsW => bare ? 3 * (TitleH - 6) + 2 * 2 + 4 : 0;

        // Minimise, restore and close, drawn at the right end of a title bar (right edge and top of that window) while
        // the window is maximised without its Windows title bar, so there is always a visible way back.
        void WindowButtons(ImDrawListPtr dl, float right, float top)
        {
            if (!bare) return;
            float s = TitleH - 6, y = top + (Bd + TitleH - s) / 2, x = right - Bd - 2 - s;
            if (WinButton(dl, Px(x, y), s, 2)) quit = true;
            x -= s + 2;
            if (WinButton(dl, Px(x, y), s, 1)) RestoreFromMax();
            x -= s + 2;
            if (WinButton(dl, Px(x, y), s, 0)) Raylib.MinimizeWindow();
        }

        // One XP window button: 0 minimise, 1 restore, 2 close (red). True when clicked.
        static bool WinButton(ImDrawListPtr dl, Vector2 a, float s, int kind)
        {
            var b = a + new Vector2(s, s);
            bool hot = !modal && ImGui.IsMouseHoveringRect(a, b, false);
            bool down = hot && ImGui.IsMouseDown(ImGuiMouseButton.Left);
            int top, bot;
            if (kind == 2) { top = down ? 0xb83a14 : hot ? 0xf2896a : 0xe46b4a; bot = down ? 0xd2522c : hot ? 0xd8502a : 0xc23d1c; }
            else { top = down ? 0x1a4fc8 : hot ? 0x6aa6ff : 0x3d8cf7; bot = down ? 0x3a7ef0 : hot ? 0x2a74f2 : 0x1659e0; }
            dl.AddRectFilled(a, b, U(bot), 3);
            Grad(dl, a + new Vector2(2, 2), new Vector2(b.X - 2, a.Y + s * 0.55f), top, bot);
            dl.AddRect(a, b, U(Hi), 3);
            if (kind == 0) dl.AddRectFilled(Px(a.X + s * 0.26f, a.Y + s * 0.66f), Px(a.X + s * 0.58f, a.Y + s * 0.66f + 3), U(Hi));
            else if (kind == 1)
            {
                // Two overlapping windows, as on XP's restore button.
                dl.AddRect(Px(a.X + s * 0.36f, a.Y + s * 0.22f), Px(a.X + s * 0.78f, a.Y + s * 0.6f), U(Hi));
                dl.AddRectFilled(Px(a.X + s * 0.36f, a.Y + s * 0.22f), Px(a.X + s * 0.78f, a.Y + s * 0.22f + 2), U(Hi));
                dl.AddRectFilled(Px(a.X + s * 0.22f, a.Y + s * 0.38f), Px(a.X + s * 0.64f, a.Y + s * 0.78f), U(bot));
                dl.AddRect(Px(a.X + s * 0.22f, a.Y + s * 0.38f), Px(a.X + s * 0.64f, a.Y + s * 0.78f), U(Hi));
                dl.AddRectFilled(Px(a.X + s * 0.22f, a.Y + s * 0.38f), Px(a.X + s * 0.64f, a.Y + s * 0.38f + 2), U(Hi));
            }
            else
            {
                dl.AddLine(new Vector2(a.X + s * 0.28f, a.Y + s * 0.28f), new Vector2(a.X + s * 0.72f, a.Y + s * 0.72f), U(Hi), 2.4f);
                dl.AddLine(new Vector2(a.X + s * 0.28f, a.Y + s * 0.72f), new Vector2(a.X + s * 0.72f, a.Y + s * 0.28f), U(Hi), 2.4f);
            }
            if (hot) Tip(kind == 0 ? "Minimise" : kind == 1 ? "Restore Down" : "Close");
            return hot && ImGui.IsMouseReleased(ImGuiMouseButton.Left);
        }

        // A window with no frame is maximised over the whole monitor, taskbar included; fit it to the work area instead
        // (the monitor less the taskbar), as Windows does for a framed window. Checked every frame, since Windows sets the
        // full size again after the style change. Raylib has no call for the work area, so Win32.
        static unsafe void FitWorkArea()
        {
            IntPtr hwnd = (IntPtr)Raylib.GetWindowHandle();
            var info = new MonitorInfo { Size = sizeof(MonitorInfo) };
            if (!GetMonitorInfoW(MonitorFromWindow(hwnd, 2), ref info)) return;
            var w = info.Work;
            if (!GetWindowRect(hwnd, out var r) || (r.L == w.L && r.T == w.T && r.R == w.R && r.B == w.B)) return;
            SetWindowPos(hwnd, IntPtr.Zero, w.L, w.T, w.R - w.L, w.B - w.T, 0x0014);   // no z-order change, no activation
        }

        [StructLayout(LayoutKind.Sequential)] struct WinRect { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int Size; public WinRect Monitor, Work; public uint Flags; }
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out WinRect r);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);

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
                bg.AddText(Px(6, sh - ImGui.GetFontSize() - 4), U(0x3a3a3a), VersionTag);
                WindowButtons(bg, sw, 0);
                HoverTip();
                return;
            }

            float row = ImGui.GetFrameHeight(), title = TitleH, sp = ImGui.GetStyle().ItemSpacing.Y;
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
            float rbtn = title - 6;
            float verW = ImGui.CalcTextSize(VersionTag).X;
            float caps = chron ? 0 : CapsW;   // the window buttons go in the rightmost title bar
            XpWindow(bg, mA, mB, title, $"{sim.Name} - {Sim.TierNames[sim.Tier - 1]}", true, caps + rbtn + 4 + verW + 10);
            RestartButton(bg, Px(mB.X - Bd - caps - rbtn - 2, mA.Y + (Bd + title - rbtn) / 2), rbtn);
            bg.AddText(Px(mB.X - Bd - caps - rbtn - 10 - verW, mA.Y + (Bd + title - ImGui.GetFontSize()) / 2), U(0x9cc0ff), VersionTag);
            if (!chron) WindowButtons(bg, mB.X, mA.Y);
            float bandTop = mA.Y + Bd + title;
            var picA = Px(x0 + (mainW - picW) / 2, bandTop + bandH + 2 + extraH / 2);
            Sunken(bg, picA - new Vector2(1, 1), picA + new Vector2(picW + 1, picH + 1));
            townRect = new Rectangle(picA.X, picA.Y, picW, picH);
            DrawTown(bg, scale);
            DrawToasts(bg);

            Area("gov", new Vector2(x0 + Bd + Pad, bandTop + Pad), new Vector2(mB.X - Bd - Pad, bandTop + Pad + row * 2 + sp));
            GovBand();
            ImGui.End();

            // The ledger: day and weather in its title; people, buildings, troubles and sisters inside.
            var lA = new Vector2(x0, mB.Y + Gap);
            var lB = new Vector2(x0 + mainW, lA.Y + ledgerH);
            var ltB = new Vector2(lB.X - Bd, lA.Y + Bd + title);
            float btn = title - 6;
            XpWindow(bg, lA, lB, title, $"Day {sim.DayNumber} - {sim.SeasonNow.Name} {sim.DayName()}, {sim.WxWord}{(sim.Festival ? ", festival" : "")}", false, btn + 4);
            if (CaptionButton(bg, Px(ltB.X - btn - 2, lA.Y + (Bd + title - btn) / 2), btn, ledgerOpen)) { ledgerOpen = !ledgerOpen; Play(ledgerOpen ? Sfx.Restore : Sfx.Minimize); RefitHeight(); }
            if (ledgerOpen)
            {
                float inX0 = lA.X + Bd + Pad, inX1 = lB.X - Bd - Pad, inY0 = ltB.Y + Pad, inY1 = lB.Y - Bd - Pad;
                float split = MathF.Floor(inX0 + (inX1 - inX0) * 0.56f);
                float colY1 = inY0 + row * 2 + sp;   // two rows in columns, then troubles across the whole width
                bg.AddRectFilled(new Vector2(split + Gap - 1, inY0), new Vector2(split + Gap, colY1), U(Etch));
                bg.AddRectFilled(new Vector2(split + Gap, inY0), new Vector2(split + Gap + 1, colY1), U(Hi));
                Area("ledgerL", new Vector2(inX0, inY0), new Vector2(split, colY1));
                LedgerTown();
                ImGui.End();
                Area("ledgerR", new Vector2(split + Gap * 2 + 1, inY0), new Vector2(inX1, colY1));
                LedgerSisters();
                ImGui.End();
                Area("ledgerT", new Vector2(inX0, colY1 + sp), new Vector2(inX1, inY1));
                LedgerFood();
                LedgerTroubles();
                ImGui.End();
            }

            // The Messenger: beside the other two, as tall as both (MessengerUi.cs).
            if (chron) Messenger(bg, new Vector2(mB.X + Gap, y0), new Vector2(mB.X + Gap + chronW, lB.Y));
            HoverTip();
            RestartDialog();
        }

        // The town picture, and an outline on the building under the mouse.
        void DrawTown(ImDrawListPtr dl, int scale)
        {
            var a = new Vector2(townRect.X, townRect.Y);
            var size = new Vector2(townRect.Width, townRect.Height);
            dl.AddImage((IntPtr)tex.Id, a, a + size);
            hoverRank = -1;
            hoverLand = -1;
            if (testHover >= 0 && testHover < sim.Built.Count && testHover < sim.Lots.Count)
            {
                var b = sim.LotBox(testHover);
                testMouse = new Vector2(a.X + (b.x + b.w / 2f) * scale, a.Y + (b.y + b.h / 2f) * scale);
            }
            var m = testMouse ?? Raylib.GetMousePosition();
            if (m.X >= a.X && m.Y >= a.Y && m.X < a.X + size.X && m.Y < a.Y + size.Y)
            {
                hoverRank = sim.RankAtPixel((int)((m.X - a.X) / scale), (int)((m.Y - a.Y) / scale));
                if (hoverRank < 0) hoverLand = sim.LandAtPixel((int)((m.X - a.X) / scale), (int)((m.Y - a.Y) / scale));
            }
            if (hoverRank >= 0 || hoverLand >= 0)
            {
                var b = hoverRank >= 0 ? sim.LotBox(hoverRank) : sim.LandBox(hoverLand);
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
            Chip(dl, Px(p.X, p.Y + 3), Px(p.X + s, p.Y + 3 + s), gv.Banner & 0xffffff);
            if (ImGui.IsItemHovered()) Tip($"The banner of the {gv.Lower}");
            ImGui.SameLine(0, 8);
            ImGui.AlignTextToFramePadding();
            BoldItem(gv.Name);
            if (ImGui.IsItemHovered() || showGovTip)
            {
                if (showGovTip) ImGui.SetNextWindowPos(ImGui.GetItemRectMin() + new Vector2(0, ImGui.GetFrameHeight()));
                Tip(string.Join("\n", sim.RuleLines()));
            }
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
            // The leader's age and health, with the full chart on hover; shortened to the age if the row is tight.
            if (sim.LeaderBody is { } lb)
            {
                string word = sim.HealthWord(lb);
                int col = word switch { "gravely ill" => 0xb02a2a, "wounded" => 0xb05a1a, "ailing" => 0x8a6a1a, _ => 0x3a7a3a };
                float room = ImGui.GetWindowWidth() - (labW + barW + wordW + sty.ItemSpacing.X * 2) - (ImGui.GetItemRectMax().X - ImGui.GetWindowPos().X) - 24;
                string txt = $"{sim.YearsOf(lb)}, {word}";
                if (ImGui.CalcTextSize(txt).X > room) txt = $"{sim.YearsOf(lb)}";
                if (ImGui.CalcTextSize(txt).X <= room)
                {
                    ImGui.SameLine(0, 12);
                    ImGui.TextColored(V(col), txt);
                    if (ImGui.IsItemHovered() || showHealthTip) Tip(string.Join("\n", sim.HealthLines(lb)));
                }
            }
            RightAlign(labW + barW + wordW + sty.ItemSpacing.X * 2);
            ImGui.Text("Townsfolk:");
            ImGui.SameLine();
            // The bar is the mood as the leader believes it (Hearsay.cs), in steps of 5: an estimate, not a reading.
            double hm = Sim.Truth ? sim.Mood : Math.Round(sim.HeardMood * 20) / 20;
            bool hov = BarItem(barW, (float)hm, MoodCol(hm));
            ImGui.SameLine();
            ImGui.TextColored(V(Darken(MoodCol(hm), 0.8f)), MoodWord(hm));
            hov |= ImGui.IsItemHovered();
            if (hov || showMoodTip) { if (Sim.Truth) MoodTipTrue(); else MoodTip(); }
        }

        // How much a reason weighs, in a word, for the hearsay tips (no amounts: the leader is told words).
        static string Weighs(double d) { double a = Math.Abs(d); return a < 0.03 ? "a little" : a < 0.08 ? "somewhat" : a < 0.15 ? "much" : "greatly"; }

        // One "reason ... weight" row of a hearsay table: the weight as a word, green when it helps, red when it hurts,
        // and under it in grey who said so and when.
        static void HeardRow(string why, double d, string under)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.PushTextWrapPos(0);   // 0: wrap at the column's right edge, not the tooltip's
            ImGui.TextWrapped(Esc(why));
            if (under != "") ImGui.TextDisabled(Esc(under));
            ImGui.PopTextWrapPos();
            ImGui.TableNextColumn();
            string s = Weighs(d);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(s).X);
            ImGui.TextColored(V(d > 0 ? 0x207020 : 0xb00000), s);
        }

        // The mood tooltip as the leader knows it: how they seem, which way word says it is going, every reason the
        // leader has been told with who told it and when (late, missing or wrong as it reached the seat), what lately
        // moved it at once, and what low spirits lead to. --truth shows MoodTipTrue instead.
        void MoodTip()
        {
            double hm = sim.HeardMood;
            float w = ImGui.CalcTextSize("0").X * 46, wordW = ImGui.CalcTextSize("somewhat").X;
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + w);

            // Headline: the word, then which way word says it is drifting.
            ImGui.PushStyleColor(ImGuiCol.Text, V(Darken(MoodCol(hm), 0.8f)));
            BoldItem($"The townsfolk seem {MoodWord(hm)}");
            ImGui.PopStyleColor();
            double to = sim.HeardTarget;
            ImGui.TextDisabled(to > hm + 0.05 ? "Word is their spirits are rising." : to < hm - 0.05 ? "Word is their spirits are sinking." : "Settled, by all accounts.");

            // Standing reasons as told, weightiest first.
            // (The rule's own level is the baseline everything moves from, not a reason, so it is left out.)
            var heard = sim.MoodHeard.Values.Where(r => r.Arrived(sim.Age) && !r.Text.StartsWith("The rule:") && !r.Text.StartsWith("No rule yet"))
                .OrderByDescending(r => Math.Abs(r.D)).ToList();
            ImGui.Spacing();
            BoldItem("Why, as you have heard it");
            if (heard.Count == 0) ImGui.TextDisabled("Nobody has told you anything.");
            else if (ImGui.BeginTable("moodheard", 2, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoHostExtendX))
            {
                ImGui.TableSetupColumn("why", ImGuiTableColumnFlags.WidthFixed, w - wordW - 12);
                ImGui.TableSetupColumn("n", ImGuiTableColumnFlags.WidthFixed, wordW);
                foreach (var r in heard) HeardRow(r.Text, r.D, r.Teller == "" ? "" : $"{char.ToUpperInvariant(r.Teller[0])}{r.Teller[1..]}, {sim.HeardWhen(r)}");
                ImGui.EndTable();
            }

            // Lately: what everyone saw move the mood at once, newest first, alike ones together.
            var recent = sim.MoodEvents.Where(e => sim.Age - e.At <= Sim.DayMs).GroupBy(e => e.Why)
                .Select(g => (Why: g.Count() > 1 ? $"{g.Key} (x{g.Count()})" : g.Key, D: g.Sum(e => e.D), At: g.Max(e => e.At)))
                .OrderByDescending(e => e.At).Take(8).ToList();
            if (recent.Count > 0)
            {
                ImGui.Spacing();
                BoldItem("Lately");
                if (ImGui.BeginTable("moodlately", 2, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoHostExtendX))
                {
                    ImGui.TableSetupColumn("why", ImGuiTableColumnFlags.WidthFixed, w - wordW - 12);
                    ImGui.TableSetupColumn("n", ImGuiTableColumnFlags.WidthFixed, wordW);
                    foreach (var e in recent)
                    {
                        int hours = (int)((sim.Age - e.At) / Sim.DayMs * 24);
                        HeardRow($"{char.ToUpperInvariant(e.Why[0])}{e.Why[1..]}, {(hours < 1 ? "just now" : hours == 1 ? "an hour ago" : $"{hours} hours ago")}", e.D, "");
                    }
                    ImGui.EndTable();
                }
            }

            // Footnote: what the mood does, and what this list is.
            ImGui.Separator();
            ImGui.TextDisabled("What you have been told, not the whole truth. An angry town breeds plots and coups, and under harsh rule, revolt. Low spirits also slow weddings.");
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }

        // A signed whole number on the 0 to 100 scale the panels use ("+4", "-8", "0").
        static string Signed(double d) { int n = (int)Math.Round(d * 100); return n > 0 ? $"+{n}" : $"{n}"; }

        // One "reason ... amount" row of a breakdown table: the amount right-aligned, green when it helps, red when it hurts.
        static void ReasonRow(string why, double d, bool grey = false)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            if (grey) ImGui.TextDisabled(Esc(why)); else ImGui.Text(Esc(why));
            ImGui.TableNextColumn();
            string s = Signed(d);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(s).X);
            ImGui.TextColored(V(s == "0" ? Shade : d > 0 ? 0x207020 : 0xb00000), s);
        }

        // The true mood tooltip, for --truth: how they feel now, where it is heading and why (each standing reason with
        // its amount), what lately moved it at once, and what low spirits lead to.
        void MoodTipTrue()
        {
            var parts = sim.MoodParts();
            double target = sim.MoodTarget, sum = parts.Sum(p => p.D);
            float w = ImGui.CalcTextSize("0").X * 44;
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + w);

            // Headline: the word and number, then which way it is drifting.
            ImGui.PushStyleColor(ImGuiCol.Text, V(Darken(MoodCol(sim.Mood), 0.8f)));
            BoldItem($"The townsfolk are {MoodWord(sim.Mood)}: {sim.Mood * 100:F0} of 100");
            ImGui.PopStyleColor();
            int now = (int)Math.Round(sim.Mood * 100), to = (int)Math.Round(target * 100);
            ImGui.TextDisabled(now == to ? "Steady: this is where the reasons below put it." :
                $"Drifting {(to > now ? "up" : "down")} to {to}, about 2 an hour (a town day is 24 hours).");

            // Standing reasons: what holds the mood where it is.
            ImGui.Spacing();
            BoldItem("Why");
            if (ImGui.BeginTable("moodwhy", 2, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoHostExtendX))
            {
                ImGui.TableSetupColumn("why", ImGuiTableColumnFlags.WidthFixed, w - ImGui.CalcTextSize("-100").X - 12);
                ImGui.TableSetupColumn("n", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("-100").X);
                foreach (var (why, d) in parts) if (Math.Abs(d) >= 0.005) ReasonRow(why, d);
                ImGui.EndTable();
            }
            ImGui.Separator();
            string total = $"Adds up to {Math.Round(sum * 100):F0}" + (sum > 1 ? ", held at 100" : sum < 0 ? ", held at 0" : "");
            ImGui.Text(total);

            // Lately: events that moved the mood at once, newest first, alike ones taken together.
            var recent = sim.MoodEvents.Where(e => sim.Age - e.At <= Sim.DayMs).GroupBy(e => e.Why)
                .Select(g => (Why: g.Count() > 1 ? $"{g.Key} (x{g.Count()})" : g.Key, D: g.Sum(e => e.D), At: g.Max(e => e.At)))
                .OrderByDescending(e => e.At).Take(8).ToList();
            if (recent.Count > 0)
            {
                ImGui.Spacing();
                BoldItem("Lately");
                ImGui.TextDisabled("Felt at once, then worn off as the mood drifts back.");
                if (ImGui.BeginTable("moodlately", 2, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoHostExtendX))
                {
                    ImGui.TableSetupColumn("why", ImGuiTableColumnFlags.WidthFixed, w - ImGui.CalcTextSize("-100").X - 12);
                    ImGui.TableSetupColumn("n", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("-100").X);
                    foreach (var e in recent)
                    {
                        int hours = (int)((sim.Age - e.At) / Sim.DayMs * 24);
                        ReasonRow($"{char.ToUpperInvariant(e.Why[0])}{e.Why[1..]}, {(hours < 1 ? "just now" : hours == 1 ? "an hour ago" : $"{hours} hours ago")}", e.D);
                    }
                    ImGui.EndTable();
                }
            }

            // Footnote: what the mood does.
            ImGui.Separator();
            ImGui.TextDisabled(Esc("Under 30 the town is angry: coups and plots grow likelier, and under harsh rule a revolt can start below 20. Low spirits also slow weddings."));
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }

        // The width of the ledger's label column: the widest label plus a gap.
        static float LabelW() => new[] { "Dot-men", "Buildings", "Troubles", "Food" }.Max(s => ImGui.CalcTextSize(s).X) + 8;

        // Ledger, left: dot-men by colour, buildings and the next thing to come, and any troubles.
        void LedgerTown()
        {
            float labW = LabelW(), valW = ImGui.CalcTextSize("000/000 ").X;
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
            bool hov = BarItem(barW, frac, XpGreen);
            ImGui.SameLine();
            ImGui.Text(Esc(shortNext));
            hov |= ImGui.IsItemHovered();
            if (hov) Tip(GrowthTip(next, frac));
        }

        // The growth bar's tooltip: what comes next, what the bar means, and how long until it fills.
        string GrowthTip(string next, float frac)
        {
            string head = $"Coming next: {next}.";
            string how = "The town grows one step at a time (a newcomer, a building, an upgrade\nor a new tier). This bar fills while the app is open; when it is full,\nthe next step happens.";
            double secs = Math.Max(0, sim.Cost - sim.Pool) * OwnClock.ThinkMs / 1000;
            string left = frac >= 1
                ? (sim.FedEnough ? "Full: the step happens any moment." : "Full, but waiting until there is food to spare.")
                : $"{frac * 100:F0}% full, about {(secs < 90 ? $"{Math.Ceiling(secs)} seconds" : $"{Math.Ceiling(secs / 60)} minutes")} to go.";
            return $"{head}\n{how}\n{left}";
        }

        // Ledger, a row across both columns: days of food in store as a bar split by kind (grain, flour, bread, greens,
        // pantries; full at two weeks), then who is hungry. The tip has the whole chain.
        void LedgerFood()
        {
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Food");
            float labW = LabelW(), valW = ImGui.CalcTextSize("000/000 ").X;
            double days = Math.Max(0, sim.FoodDays);
            ImGui.SameLine(labW);
            ImGui.TextColored(V(days < 3 ? 0xb00000 : days < 6 ? 0x806000 : Ink), $"{days:F0} days");
            bool hov = ImGui.IsItemHovered();
            ImGui.SameLine(labW + valW);
            string note = sim.FoodLine() is { Length: > 0 } fl ? fl : sim.Carriers > 0 ? $"fed; {sim.Carriers} carrying" : "fed";
            float roomX = ImGui.GetContentRegionAvail().X;
            float barW = Math.Clamp(roomX - ImGui.CalcTextSize(note).X - ImGui.GetStyle().ItemSpacing.X, 60, roomX * 0.6f);
            var p = ImGui.GetCursorScreenPos();
            float h = ImGui.GetFrameHeight();
            ImGui.Dummy(new Vector2(barW, h));
            hov |= ImGui.IsItemHovered();
            var dl = ImGui.GetWindowDrawList();
            var a = Px(p.X, p.Y);
            var b = Px(p.X + barW, p.Y + h);
            dl.AddRectFilled(a, b, U(Paper), 3);
            var t = sim.GoodsTotals();
            double sum = t.Sum();
            float full = (b.X - a.X - 4) * (float)Math.Min(1, days / 14), x = a.X + 2;
            for (int g = 0; g < t.Length && sum > 0; g++)
            {
                float wg = MathF.Round(full * (float)(t[g] / sum));
                if (wg < 1) continue;
                dl.AddRectFilled(new Vector2(x, a.Y + 2), new Vector2(x + wg, b.Y - 2), U(Sim.GoodColors[g]));
                dl.AddRectFilled(new Vector2(x, a.Y + 2), new Vector2(x + wg, a.Y + (b.Y - a.Y) * 0.4f), U(0xffffff, 0.25f));
                x += wg;
            }
            dl.AddRect(a, b, U(0x7a96df), 3);
            ImGui.SameLine();
            ImGui.TextColored(V(sim.WeakCount > 0 ? 0xb00000 : sim.HungryCount > 2 ? 0x806000 : 0x004000), Esc(note));
            hov |= ImGui.IsItemHovered();
            if (hov || showFoodTip) FoodTip();
        }

        // The food tooltip: days of food against what the rule holds back, the goods by where they are, then who is hungry.
        void FoodTip()
        {
            var f = sim.GetFoodReport();
            float w = ImGui.CalcTextSize("0").X * 46;
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + w);

            // Headline: days of food, large and coloured, with who eats beside it.
            int dayCol = f.Days < 3 ? 0xb00000 : f.Days < f.Keep ? 0x806000 : Ink;
            ImGui.PushStyleColor(ImGuiCol.Text, V(dayCol));
            BoldItem($"{f.Days:F1} days of food");
            ImGui.PopStyleColor();
            ImGui.TextDisabled(Esc(f.Who));

            // Bar: food on hand, with a marker where the rule's held-back grain ends.
            double scale = Math.Max(14, Math.Max(f.Days, f.Keep) * 1.15);
            var p = ImGui.GetCursorScreenPos();
            float h = ImGui.GetTextLineHeight();
            ImGui.Dummy(new Vector2(w, h + 4));
            var dl = ImGui.GetWindowDrawList();
            var a = Px(p.X, p.Y + 2);
            var b = Px(p.X + w, p.Y + 2 + h);
            int barCol = f.Days < 3 ? 0xd02020 : f.Days < f.Keep ? 0xe08a00 : XpGreen;
            Blocks(dl, a, b, (float)(f.Days / scale), barCol);
            float mx = MathF.Floor(a.X + 3 + (b.X - a.X - 6) * (float)(f.Keep / scale));
            dl.AddRectFilled(new Vector2(mx - 1, a.Y - 2), new Vector2(mx + 1, b.Y + 2), U(Ink));
            ImGui.TextDisabled(Esc($"Marker: {f.Keep:F0} days held back ({f.ToHarvest:F0} to the harvest + {f.Reserve:F0} spare)"));

            // Goods table: a coloured swatch per good, the barn and the town as rows, nothing shown as a dash.
            ImGui.Spacing();
            string[] heads = { "Grain", "Flour", "Bread", "Greens", "Pantries" };
            if (ImGui.BeginTable("foodgoods", 6, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoHostExtendX))
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                for (int g = 0; g < 5; g++)
                {
                    ImGui.TableNextColumn();
                    var s = ImGui.GetCursorScreenPos();
                    float sw = MathF.Floor(h * 0.6f), sy = MathF.Floor(s.Y + (h - sw) / 2);
                    ImGui.GetWindowDrawList().AddRectFilled(new Vector2(s.X, sy), new Vector2(s.X + sw, sy + sw), U(Sim.GoodColors[g]));
                    ImGui.GetWindowDrawList().AddRect(new Vector2(s.X, sy), new Vector2(s.X + sw, sy + sw), U(Shade));
                    ImGui.Dummy(new Vector2(sw + 3, h));
                    ImGui.SameLine(0, 0);
                    ImGui.Text(heads[g]);
                }
                GoodsRow("Barn", new double[] { f.BarnGrain, -1, -1, f.BarnGreens, -1 });
                GoodsRow("In town", f.Town);
                ImGui.EndTable();
            }
            if (f.Seed >= 0.5) ImGui.TextDisabled(Esc($"Seed corn kept back: {Math.Round(f.Seed)}"));

            // Fields: the season's state, and the crop's look in a colour while it grows.
            ImGui.Text("Fields:");
            ImGui.SameLine();
            ImGui.Text(Esc(f.Fields));
            if (f.Crop != "")
            {
                ImGui.SameLine();
                ImGui.TextColored(V(f.CropQ < 0.55 ? 0xb00000 : f.CropQ < 0.85 ? 0x806000 : 0x207020), $"crop {f.Crop}");
            }

            // Who is out and who is hungry: grey when nothing is wrong, orange and red when something is.
            StatusCount("Carriers", f.Carriers, f.Carriers > 0 ? Ink : Shade, false);
            StatusCount("Hungry", f.Hungry, f.Hungry > 0 ? 0xc06000 : Shade, true);
            StatusCount("Starving", f.Weak, f.Weak > 0 ? 0xb00000 : Shade, true);
            if (f.Dead > 0) ImGui.TextColored(V(0xb00000), $"Starved since the app opened: {f.Dead}");

            // Footnote: how this rule handles food.
            ImGui.Separator();
            ImGui.TextDisabled(Esc(f.RuleLine));
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }

        // One row of the food tooltip's goods table: a label, then each amount (a dash for none, blank where it cannot be).
        static void GoodsRow(string label, double[] vals)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(label);
            foreach (var v in vals)
            {
                ImGui.TableNextColumn();
                if (v < 0) continue;
                if (v < 0.5) ImGui.TextDisabled("-");
                else ImGui.Text($"{Math.Round(v)}");
            }
        }

        // A label and a number on the food tooltip's status line; same says whether it follows the last one on the line.
        static void StatusCount(string label, int n, int col, bool same)
        {
            if (same) ImGui.SameLine(0, 16);
            ImGui.TextColored(V(col), $"{label} {n}");
        }

        // Ledger, bottom row across both columns: fires and ruins, war or famine, illness, or "none".
        void LedgerTroubles()
        {
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Troubles");
            ImGui.SameLine(LabelW());
            var troubles = new List<(string text, int col)>();
            if (sim.FireLine() is { Length: > 0 } f) troubles.Add((f, sim.Fires.Count > 0 ? 0xb00000 : 0x704000));
            if (sim.WarLine() is { Length: > 0 } w) troubles.Add((w, 0x800000));
            if (sim.IllnessLine() is { Length: > 0 } ill) troubles.Add((ill, 0x405000));
            if (sim.FoodLine() is { Length: > 0 } fo) troubles.Add((fo, 0xb00000));
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

        // A rough count, as news of a sister carries it: "about 40".
        static string About(int n) => n < 10 ? $"{n}" : $"about {(int)Math.Round(n / 10.0) * 10}";

        // A sister town's tooltip as the leader knows it: who she is, how she seems to regard us and why, as letters and
        // travellers told it (late, missing or wrong), what lately moved her, and what regard leads to. --truth shows
        // SisterTipTrue instead.
        void SisterTip(Sister s)
        {
            if (Sim.Truth) { SisterTipTrue(s); return; }
            var (word, wc) = sim.HeardAttitude(s);
            var lord = sim.RulerOf(s);
            float w = ImGui.CalcTextSize("0").X * 46, wordW = ImGui.CalcTextSize("somewhat").X;
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + w);

            // Headline: her regard as it seems, in its colour, then what is known of her.
            ImGui.PushStyleColor(ImGuiCol.Text, V(Darken(wc, 0.55f)));
            BoldItem($"{s.Name} seems {word}");
            ImGui.PopStyleColor();
            ImGui.TextDisabled(Esc($"{About(s.Total)} dot-men, {Govs.Get(s.Gov).Name}, {Sim.WealthWord(sim.GoldOf(s))}, ruled by {lord.Name}."));

            // Standing reasons as told.
            var heard = sim.SisHeard.Where(kv => kv.Key.StartsWith(s.Name + "|") && kv.Value.Arrived(sim.Age)).Select(kv => kv.Value)
                .OrderByDescending(r => Math.Abs(r.D)).ToList();
            ImGui.Spacing();
            BoldItem("Why, as you have heard it");
            if (heard.Count == 0) ImGui.TextDisabled("Old neighbourly ties; no other word has come.");
            else if (ImGui.BeginTable("relheard", 2, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoHostExtendX))
            {
                ImGui.TableSetupColumn("why", ImGuiTableColumnFlags.WidthFixed, w - wordW - 12);
                ImGui.TableSetupColumn("n", ImGuiTableColumnFlags.WidthFixed, wordW);
                foreach (var r in heard) HeardRow(r.Text, r.D, r.Teller == "" ? "" : $"{char.ToUpperInvariant(r.Teller[0])}{r.Teller[1..]}, {sim.HeardWhen(r)}");
                ImGui.EndTable();
            }

            // Lately: what moved her regard at once (gifts, spies, aid: things the town saw), newest first.
            var recent = s.RelEvents.GroupBy(e => e.Why)
                .Select(g => (Why: g.Count() > 1 ? $"{g.Key} (x{g.Count()})" : g.Key, D: g.Sum(e => e.D), At: g.Max(e => e.At)))
                .OrderByDescending(e => e.At).Take(8).ToList();
            if (recent.Count > 0)
            {
                ImGui.Spacing();
                BoldItem("Lately");
                if (ImGui.BeginTable("rellately", 2, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoHostExtendX))
                {
                    ImGui.TableSetupColumn("why", ImGuiTableColumnFlags.WidthFixed, w - wordW - 12);
                    ImGui.TableSetupColumn("n", ImGuiTableColumnFlags.WidthFixed, wordW);
                    foreach (var e in recent)
                    {
                        int days = (int)((sim.EvClock - e.At) / Sim.DayMs);
                        HeardRow($"{char.ToUpperInvariant(e.Why[0])}{e.Why[1..]}, {(days < 1 ? "today" : days == 1 ? "yesterday" : $"{days} days ago")}", e.D, "");
                    }
                    ImGui.EndTable();
                }
            }

            // Footnote: what regard leads to, in words.
            ImGui.Separator();
            ImGui.TextDisabled("What you have been told, not the whole truth. A friendly sister sends gifts and aid, holds fairs and may propose a wedding; an ally stands by us. A cold, harsh sister sends spies; a colder one demands tribute; a hostile one blocks us. Wars follow harsh rule, not regard.");
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }

        // The true sister tooltip, for --truth: who she is, how she regards us (-100 to 100) and why (the standing
        // reasons she drifts toward, then what lately moved her), and what each level of regard leads to.
        void SisterTipTrue(Sister s)
        {
            var parts = sim.RelParts(s);
            double rel = sim.RelOf(s), sum = parts.Sum(p => p.D), settle = Math.Clamp(sum, -1, 1);
            var (word, wc) = sim.Attitude(s);
            var lord = sim.RulerOf(s);
            float w = ImGui.CalcTextSize("0").X * 46, numW = ImGui.CalcTextSize("-100").X;
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + w);

            // Headline: her regard, in her attitude's colour, then plain facts about her.
            ImGui.PushStyleColor(ImGuiCol.Text, V(Darken(wc, 0.55f)));
            BoldItem($"{s.Name} is {word}: {Signed(rel)} (from -100 to 100)");
            ImGui.PopStyleColor();
            ImGui.TextDisabled(Esc($"{s.Total} dot-men, {Govs.Get(s.Gov).Name}, {Sim.WealthWord(sim.GoldOf(s))}, ruled by {lord.Name}."));
            int now = (int)Math.Round(rel * 100), to = (int)Math.Round(settle * 100);
            ImGui.TextDisabled(now == to ? "Steady: this is where the reasons below put her." :
                $"Drifting {(to > now ? "up" : "down")} toward {Signed(settle)}, closing about a tenth of the gap a day.");

            // Standing reasons, then their sum.
            ImGui.Spacing();
            BoldItem("Why");
            if (ImGui.BeginTable("relwhy", 2, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoHostExtendX))
            {
                ImGui.TableSetupColumn("why", ImGuiTableColumnFlags.WidthFixed, w - numW - 12);
                ImGui.TableSetupColumn("n", ImGuiTableColumnFlags.WidthFixed, numW);
                foreach (var (why, d) in parts) if (Math.Abs(d) >= 0.005) ReasonRow(why, d);
                ImGui.EndTable();
            }
            ImGui.Separator();
            ImGui.Text($"Settles at {Signed(settle)}" + (sum > 1 ? " (held at 100)" : sum < -1 ? " (held at -100)" : ""));

            // Lately: what moved her regard at once, newest first, alike ones together.
            var recent = s.RelEvents.GroupBy(e => e.Why)
                .Select(g => (Why: g.Count() > 1 ? $"{g.Key} (x{g.Count()})" : g.Key, D: g.Sum(e => e.D), At: g.Max(e => e.At)))
                .OrderByDescending(e => e.At).Take(8).ToList();
            if (recent.Count > 0)
            {
                ImGui.Spacing();
                BoldItem("Lately");
                ImGui.TextDisabled("Felt at once, then worn off as she drifts back.");
                if (ImGui.BeginTable("rellately", 2, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoHostExtendX))
                {
                    ImGui.TableSetupColumn("why", ImGuiTableColumnFlags.WidthFixed, w - numW - 12);
                    ImGui.TableSetupColumn("n", ImGuiTableColumnFlags.WidthFixed, numW);
                    foreach (var e in recent)
                    {
                        int days = (int)((sim.EvClock - e.At) / Sim.DayMs);
                        ReasonRow($"{char.ToUpperInvariant(e.Why[0])}{e.Why[1..]}, {(days < 1 ? "today" : days == 1 ? "yesterday" : $"{days} days ago")}", e.D);
                    }
                    ImGui.EndTable();
                }
            }

            // Footnote: what her regard leads to.
            ImGui.Separator();
            ImGui.TextDisabled(Esc("At +25 or more she is friendly: gifts, fairs, and aid both ways; at +40 she proposes weddings; at +60 allied. Below 0 a harsh sister sends spies; below -30 she demands tribute; at -45 or less she is hostile and blocks us. Wars follow harsh rule, not regard."));
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
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
                    SisterTip(s);
            }
            ImGui.Dummy(new Vector2(Math.Max(1, icons * step), row));
            // The newest sister by name and how she feels about us; the rest is in the icon tips.
            var ns = sim.Sisters[^1];
            var (word, wc) = sim.HeardAttitude(ns);
            ImGui.AlignTextToFramePadding();
            ImGui.Text(Sim.Truth ? $"{ns.Name} {ns.Total}," : $"{ns.Name} {About(ns.Total)},");
            bool hov = ImGui.IsItemHovered();
            ImGui.SameLine(0, 8);
            ImGui.TextColored(V(Darken(wc, 0.55f)), word);
            if (hov || ImGui.IsItemHovered() || showSisterTip) SisterTip(ns);
        }

        // The tooltip for the building under the mouse.
        void HoverTip()
        {
            if (modal) return;
            bool land = hoverLand >= 0 && hoverLand < sim.Land.Count;
            if (!land && (hoverRank < 0 || hoverRank >= sim.Built.Count)) return;
            var (title, lines) = land ? sim.LandInfo(hoverLand) : sim.InfoFor(hoverRank);
            if (testMouse != null) ImGui.SetNextWindowPos(testMouse.Value + new Vector2(16, 16));
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 24);
            BoldItem(title);
            ImGui.Separator();
            foreach (var l in lines) ImGui.TextWrapped(Esc(l));
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }

        // The panels use Tahoma (the XP and MSN Messenger font, ships with Windows) at 18 px, with Tahoma Bold for
        // titles and names. Falls back to Lucida Console, Segoe UI, Pixelify Sans, then the ImGui default (faux bold).
        static unsafe void LoadFont()
        {
            var io = ImGui.GetIO();
            string fonts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
            string[] tries = { Path.Combine(fonts, "tahoma.ttf"), Path.Combine(fonts, "lucon.ttf"), Path.Combine(fonts, "segoeui.ttf"), Path.Combine(AppContext.BaseDirectory, "resources", "fonts", "PixelifySans.ttf") };
            string? path = tries.FirstOrDefault(File.Exists);
            if (path == null) { io.Fonts.AddFontDefault(); io.FontGlobalScale = 1.5f; return; }
            // Each font is drawn by Windows' GDI renderer when it can be (XpText, XP-style crisp text), else by ImGui.
            // cell is the GDI cell height; Impact is a size smaller because it runs tall.
            ImFontPtr Load(string file, int cell = 18) => XpText.Add(file, 18f, cell) ?? io.Fonts.AddFontFromFileTTF(file, cell);
            Load(path);
            string bold = Path.Combine(fonts, "tahomabd.ttf");
            if (path.EndsWith("tahoma.ttf") && File.Exists(bold)) { boldFont = Load(bold); hasBold = true; }
            // The chat fonts people pick (Messenger.ChatStyles); a missing one falls back to the main font.
            foreach (var face in Sim.ChatFaces)
            {
                string f = Path.Combine(fonts, face + ".ttf");
                if (File.Exists(f)) chatFonts[face] = Load(f, face == "impact" ? 17 : 18);
            }
        }

        // XP style for what ImGui draws itself: square pale yellow tooltips with a black edge, rounded blue scrollbars.
        // The windows themselves are see-through: their frames are drawn by DrawUi.
        static void Theme()
        {
            var st = ImGui.GetStyle();
            st.WindowRounding = st.ChildRounding = st.FrameRounding = st.PopupRounding = 0;
            st.ScrollbarRounding = 6;
            st.GrabRounding = 3;
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
            c[(int)ImGuiCol.Separator] = V(Etch);
            c[(int)ImGuiCol.ScrollbarBg] = V(0xf4f3ee);
            c[(int)ImGuiCol.ScrollbarGrab] = V(0xc1d3fb);
            c[(int)ImGuiCol.ScrollbarGrabHovered] = V(0xd6e3ff);
            c[(int)ImGuiCol.ScrollbarGrabActive] = V(0x9db8f0);
        }
    }
}
