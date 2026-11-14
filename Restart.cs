using System.Numerics;
using ImGuiNET;
using Raylib_cs;

namespace Dotburg;

// Starting over: a button in the town window's title bar, an XP-style "are you sure" box, and the restart itself
// (a brand new region; the old save is kept as town-before-restart.json, the chronicle is filed in chronicles\).
public partial class Program
{
    partial class App
    {
        bool askRestart;              // the confirm box is up
        static bool modal;            // something modal is up: title-bar buttons and the town ignore the mouse

        // A title-bar button with a circular arrow; a click opens the confirm box.
        void RestartButton(ImDrawListPtr dl, Vector2 a, float s)
        {
            var b = a + new Vector2(s, s);
            bool hot = !modal && ImGui.IsMouseHoveringRect(a, b, false);
            bool down = hot && ImGui.IsMouseDown(ImGuiMouseButton.Left);
            int top = down ? 0x1a4fc8 : hot ? 0x6aa6ff : 0x3d8cf7, bot = down ? 0x3a7ef0 : hot ? 0x2a74f2 : 0x1659e0;
            dl.AddRectFilled(a, b, U(bot), 3);
            Grad(dl, a + new Vector2(2, 2), new Vector2(b.X - 2, a.Y + s * 0.55f), top, bot);
            dl.AddRect(a, b, U(Hi), 3);
            // Three quarters of a circle with an arrow head at the open end.
            var c = a + new Vector2(s / 2, s / 2);
            float r = s * 0.27f;
            dl.PathArcTo(c, r, -MathF.PI * 0.35f, MathF.PI * 1.25f, 14);
            dl.PathStroke(U(Hi), ImDrawFlags.None, 2);
            float e = -MathF.PI * 0.35f;
            var tip = c + new Vector2(MathF.Cos(e), MathF.Sin(e)) * r;
            dl.AddTriangleFilled(tip + new Vector2(-s * 0.2f, -s * 0.04f), tip + new Vector2(s * 0.06f, -s * 0.2f), tip + new Vector2(s * 0.06f, s * 0.12f), U(Hi));
            if (hot) Tip("Start over: a brand new hamlet in a brand new region.");
            if (hot && ImGui.IsMouseReleased(ImGuiMouseButton.Left)) askRestart = true;
        }

        // The confirm box, drawn last so it sits over everything. A see-through window under it catches the mouse.
        void RestartDialog()
        {
            modal = askRestart;
            if (!askRestart) return;
            float sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
            var fg = ImGui.GetForegroundDrawList();
            fg.AddRectFilled(Vector2.Zero, new Vector2(sw, sh), U(0x000000, 0.35f));

            float line = ImGui.GetTextLineHeight();
            string[] text =
            {
                $"Start over? {sim.Name}, its sister towns and every",
                "conversation will be gone, and a new hamlet founded.",
                "The chronicle is filed away in the chronicles folder",
                "and a fresh one begins; a copy of the old save is",
                "kept too (town-before-restart.json).",
            };
            float textW = text.Max(t => ImGui.CalcTextSize(t).X);
            float bw = MathF.Max(ImGui.CalcTextSize("Keep my town").X, ImGui.CalcTextSize("Start over").X) + 28, bh = line + 12;
            float w = MathF.Max(textW + 40, bw * 2 + 12) + 2 * (Bd + Pad), h = Bd + TitleH + Pad * 2 + text.Length * (line + 2) + Pad + bh + Pad + Bd;
            var a = Px((sw - w) / 2, (sh - h) / 2);
            var b = a + new Vector2(w, h);

            // Catch the mouse everywhere so nothing under the box reacts.
            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(new Vector2(sw, sh));
            ImGui.Begin("restartBlock", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings);
            ImGui.InvisibleButton("block", new Vector2(sw, sh));
            ImGui.End();

            XpWindowOn(fg, a, b, "Dotburg");
            float y = a.Y + Bd + TitleH + Pad;
            // A blue round "?" like XP's question box.
            var qc = new Vector2(a.X + Bd + Pad + 12, y + 12);
            fg.AddCircleFilled(qc, 12, U(0x2a64d8));
            fg.AddCircle(qc, 12, U(0x123f9a));
            var qs = ImGui.CalcTextSize("?");
            BoldText(fg, Px(qc.X - qs.X / 2, qc.Y - qs.Y / 2), Hi, "?");
            float tx = a.X + Bd + Pad + 32;
            foreach (var t in text) { fg.AddText(Px(tx, y), U(Ink), t); y += line + 2; }
            y += Pad;
            float bx = a.X + (w - (bw * 2 + 12)) / 2;
            if (XpPush(fg, Px(bx, y), new Vector2(bw, bh), "Start over")) { askRestart = false; Restart(); }
            if (XpPush(fg, Px(bx + bw + 12, y), new Vector2(bw, bh), "Keep my town", true)) askRestart = false;
            if (Raylib.IsKeyPressed(KeyboardKey.Escape)) askRestart = false;
        }

        // XpWindow drawn onto any draw list (the confirm box uses the foreground one).
        void XpWindowOn(ImDrawListPtr dl, Vector2 a, Vector2 b, string title) => XpWindow(dl, a, b, TitleH, title, true);

        // An XP push button: rounded, pale, with a blue edge when hovered (or when it is the default). True on click.
        static bool XpPush(ImDrawListPtr dl, Vector2 a, Vector2 size, string label, bool isDefault = false)
        {
            var b = a + size;
            bool hot = ImGui.IsMouseHoveringRect(a, b, false);
            bool down = hot && ImGui.IsMouseDown(ImGuiMouseButton.Left);
            Grad(dl, a, b, down ? 0xd6d0c4 : 0xffffff, down ? 0xf2f0ea : 0xe6e3d6);
            dl.AddRect(a, b, U(0x003c74), 3);
            if (hot || isDefault) dl.AddRect(a + new Vector2(1, 1), b - new Vector2(1, 1), U(hot ? 0xf8b330 : 0x7da2ce), 3, ImDrawFlags.None, 2);
            var ts = ImGui.CalcTextSize(label);
            dl.AddText(Px(a.X + (size.X - ts.X) / 2, a.Y + (size.Y - ts.Y) / 2), U(Ink), label);
            return hot && ImGui.IsMouseReleased(ImGuiMouseButton.Left);
        }

        // A brand new region: keeps a copy of the old save, swaps in a new simulation, saves it at once.
        void Restart()
        {
            Storage.Backup("town-before-restart.json");
            Storage.FlushChronicle(sim, chronicleView);   // the old town's last lines go into its own chronicle
            Storage.ArchiveChronicle(sim.Name);
            chronicleView.Clear();
            sim = new Sim(Environment.TickCount);
            sim.FreshRegion();
            sim.Note($"A new region begins with the hamlet of {sim.Name}.");
            sim.Tick(0);
            acc = 0; unitAcc = 0; saveAcc = 0;
            selKey = "bot";
            toasts.Clear();
            heardTick = -1;
            Storage.FlushChronicle(sim, chronicleView);
            botSeen = chronicleView.Count;
            Storage.Save(sim);
        }
    }
}
