using System.Numerics;
using ImGuiNET;
using Raylib_cs;

namespace Dotburg;

// The Messenger's menu bar (0.89), as MSN had under its title bar: File (Work offline, Exit) and Help (About).
// The bar is drawn with the Messenger; the drop-downs and the About box are drawn last, over everything.
public partial class Program
{
    partial class App
    {
        // One row of a drop-down: a label, what a click does, or a submenu; Check draws a bullet before it.
        record MenuRow(string Label, Action? Do = null, bool Sub = false, bool Check = false, bool Line = false);

        int menuOpen;                 // 0 none, 1 File, 2 Help
        bool subOpen;                 // File > Work offline's submenu is showing
        bool aboutOpen;               // the About box is up
        Vector2 menuAt;               // where the open drop-down hangs (under its label)
        Vector2 barA, barB;           // the menu bar this frame, so a click on it does not close the menu

        static float MenuH => ImGui.GetTextLineHeight() + 6;
        static readonly string[] MenuNames = { "File", "Help" };

        // The bar itself, from a across to right. Click a name to open its menu; while one is open, moving onto
        // another name opens that one instead, as Windows does.
        void MenuBar(ImDrawListPtr dl, Vector2 a, float right)
        {
            barA = a;
            barB = new Vector2(right, a.Y + MenuH);
            dl.AddRectFilled(barA, barB, U(Face));
            dl.AddLine(new Vector2(a.X, barB.Y - 1), new Vector2(right, barB.Y - 1), U(Etch));
            float x = a.X + 2;
            for (int i = 0; i < MenuNames.Length; i++)
            {
                float w = ImGui.CalcTextSize(MenuNames[i]).X + 14;
                var la = new Vector2(x, a.Y + 1);
                var lb = new Vector2(x + w, barB.Y - 2);
                bool hot = !askRestart && !aboutOpen && ImGui.IsMouseHoveringRect(la, lb, false);
                if (hot && menuOpen != 0 && menuOpen != i + 1) { menuOpen = i + 1; subOpen = false; }
                else if (hot && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) { menuOpen = menuOpen == i + 1 ? 0 : i + 1; subOpen = false; }
                bool open = menuOpen == i + 1;
                if (open) { dl.AddRectFilled(la, lb, U(Select)); menuAt = new Vector2(la.X, lb.Y + 1); }
                else if (hot) dl.AddRect(la, lb, U(Etch));
                dl.AddText(Px(x + 7, a.Y + 3), U(open ? Hi : Ink), MenuNames[i]);
                x += w;
            }
        }

        // The rows of each menu.
        MenuRow[] FileRows() => new MenuRow[]
        {
            new("Work offline", Sub: true),
            new("", Line: true),
            new("Exit", () => quit = true),
        };
        MenuRow[] OfflineRows() => OfflineChoices.Select(h => new MenuRow(
            h == 0 ? "Off: the town waits while closed" : $"Keep going for up to {h} hour{(h > 1 ? "s" : "")}",
            () => SetOffline(h), Check: offlineHours == h)).ToArray();
        MenuRow[] HelpRows() => new MenuRow[] { new("About Dotburg", () => aboutOpen = true) };

        // Drawn after everything else: the open drop-down (and submenu), the About box, and whether the rest of the
        // window should ignore the mouse.
        void MenuLayer()
        {
            if (menuOpen != 0) OpenMenu();
            if (aboutOpen) AboutBox();
            modal = askRestart || menuOpen != 0 || aboutOpen;
        }

        // The open drop-down. A see-through window under it catches the mouse; a click anywhere off the menus,
        // or Escape, closes it.
        void OpenMenu()
        {
            float sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(new Vector2(sw, sh));
            ImGui.Begin("menuBlock", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings);
            ImGui.InvisibleButton("block", new Vector2(sw, sh));
            ImGui.End();

            var fg = ImGui.GetForegroundDrawList();
            var rows = menuOpen == 1 ? FileRows() : HelpRows();
            var (a, b, hot, rowTop) = DropDown(fg, menuAt, rows);
            bool over = ImGui.IsMouseHoveringRect(a, b, false) || ImGui.IsMouseHoveringRect(barA, barB, false);
            if (hot >= 0) subOpen = rows[hot].Sub;
            if (subOpen)
            {
                int si = Array.FindIndex(rows, r => r.Sub);
                var sub = OfflineRows();
                var (sa, sb, shot, _) = DropDown(fg, new Vector2(b.X - 3, rowTop[si] - 3), sub);
                over |= ImGui.IsMouseHoveringRect(sa, sb, false);
                if (shot >= 0 && ImGui.IsMouseReleased(ImGuiMouseButton.Left)) { sub[shot].Do?.Invoke(); menuOpen = 0; return; }
            }
            if (hot >= 0 && rows[hot].Do != null && ImGui.IsMouseReleased(ImGuiMouseButton.Left)) { rows[hot].Do!(); menuOpen = 0; return; }
            if ((!over && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) || Raylib.IsKeyPressed(KeyboardKey.Escape)) menuOpen = 0;
        }

        // One drop-down at its top-left corner: white, a thin edge and a soft shadow, the hovered row in XP blue.
        // Returns its box, the hovered row (or -1) and the top of each row.
        (Vector2 a, Vector2 b, int hot, float[] rowTop) DropDown(ImDrawListPtr dl, Vector2 at, MenuRow[] rows)
        {
            float line = ImGui.GetTextLineHeight(), rowH = line + 8, sepH = 7;
            float w = rows.Max(r => ImGui.CalcTextSize(r.Label).X) + 58;
            float h = rows.Sum(r => r.Line ? sepH : rowH) + 6;
            var a = Px(at.X, at.Y);
            var b = a + new Vector2(w, h);
            dl.AddRectFilled(a + new Vector2(3, 3), b + new Vector2(3, 3), U(0x000000, 0.18f));
            dl.AddRectFilled(a, b, U(Paper));
            dl.AddRect(a, b, U(Shade));
            int hot = -1;
            var tops = new float[rows.Length];
            float y = a.Y + 3;
            for (int i = 0; i < rows.Length; i++)
            {
                var r = rows[i];
                tops[i] = y;
                if (r.Line) { dl.AddLine(new Vector2(a.X + 4, y + 3), new Vector2(b.X - 4, y + 3), U(Etch)); y += sepH; continue; }
                var ra = new Vector2(a.X + 3, y);
                var rb = new Vector2(b.X - 3, y + rowH);
                bool on = ImGui.IsMouseHoveringRect(ra, rb, false) || (r.Sub && subOpen);
                if (ImGui.IsMouseHoveringRect(ra, rb, false)) hot = i;
                if (on) dl.AddRectFilled(ra, rb, U(Select));
                int ink = on ? Hi : Ink;
                if (r.Check) dl.AddCircleFilled(new Vector2(a.X + 14, y + rowH / 2), 3, U(ink));
                dl.AddText(Px(a.X + 26, y + 4), U(ink), r.Label);
                if (r.Sub)
                {
                    float cy = y + rowH / 2, cx = b.X - 12;
                    dl.AddTriangleFilled(new Vector2(cx, cy - 4), new Vector2(cx + 4, cy), new Vector2(cx, cy + 4), U(ink));
                }
                y += rowH;
            }
            return (a, b, hot, tops);
        }

        // Help > About: the version, what the town is, and what Work offline is set to. OK or Escape closes it.
        void AboutBox()
        {
            float sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
            var fg = ImGui.GetForegroundDrawList();
            fg.AddRectFilled(Vector2.Zero, new Vector2(sw, sh), U(0x000000, 0.35f));
            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(new Vector2(sw, sh));
            ImGui.Begin("aboutBlock", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings);
            ImGui.InvisibleButton("block", new Vector2(sw, sh));
            ImGui.End();

            float line = ImGui.GetTextLineHeight();
            string[] text =
            {
                Version,
                "A town of dot-men that grows on its own clock.",
                "",
                offlineHours == 0
                    ? "Work offline is off: the town waits while Dotburg is closed."
                    : $"Work offline: while Dotburg is closed the town keeps going for up to {offlineHours} hour{(offlineHours > 1 ? "s" : "")},",
                offlineHours == 0 ? "Change it in File > Work offline." : "played through when you open it again. Change it in File > Work offline.",
            };
            float textW = text.Max(t => ImGui.CalcTextSize(t).X);
            float bw = ImGui.CalcTextSize("OK").X + 60, bh = line + 12;
            float w = textW + 40 + 2 * (Bd + Pad), h = Bd + TitleH + Pad * 2 + text.Length * (line + 2) + Pad + bh + Pad + Bd;
            var a = Px((sw - w) / 2, (sh - h) / 2);
            var b = a + new Vector2(w, h);
            XpWindowOn(fg, a, b, "About Dotburg");
            float y = a.Y + Bd + TitleH + Pad;
            // A blue round "i" like XP's information box.
            var ic = new Vector2(a.X + Bd + Pad + 12, y + 12);
            fg.AddCircleFilled(ic, 12, U(0x2a64d8));
            fg.AddCircle(ic, 12, U(0x123f9a));
            var isz = ImGui.CalcTextSize("i");
            BoldText(fg, Px(ic.X - isz.X / 2, ic.Y - isz.Y / 2), Hi, "i");
            float tx = a.X + Bd + Pad + 32;
            for (int i = 0; i < text.Length; i++)
            {
                if (i == 0) BoldText(fg, Px(tx, y), Ink, text[i]); else fg.AddText(Px(tx, y), U(Ink), text[i]);
                y += line + 2;
            }
            y += Pad;
            if (XpPush(fg, Px(a.X + (w - bw) / 2, y), new Vector2(bw, bh), "OK", true) || Raylib.IsKeyPressed(KeyboardKey.Escape) || Raylib.IsKeyPressed(KeyboardKey.Enter))
                aboutOpen = false;
        }
    }
}
