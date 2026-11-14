using System.Numerics;
using ImGuiNET;

namespace Dotburg;

// The Messenger's skin (0.83): MSN Messenger 7's glossy, airy look (what is now called Frutiger Aero), tinted by one
// of MSN's colour schemes. The leader picks the scheme, leaning on their traits and rule, so the window changes colour
// when the seat passes. Nothing here is saved or touches the town: the pick comes from a hash of the leader's id.
public partial class Program
{
    partial class App
    {
        // One MSN colour scheme: a name as the picker showed it and the base colour every shade is made from.
        // Light is how far the window background is lightened (the dark schemes stay a little darker).
        record MsnScheme(string Key, string Name, int Base, float Light = 0.78f)
        {
            public int Back => Lighten(Base, Light);                 // the window background at its most tinted
            public int Soft => Lighten(Base, 0.9f);                  // pale tint: hover, pane strips
            public int Edge => Lighten(Base, 0.45f);                 // pane borders
            public int Sel => Base;                                  // the selected contact
            public int Deep => Darken(Base, 0.62f);                  // group headers
            public bool Dark => Light < 0.6f;                        // a dark window (Midnight): light text on it
            public int Text => Dark ? 0xf4f6fa : Ink;                // text straight on the background
            public int Dim => Dark ? 0xb8c0cc : Shade;
            public int Motto => Dark ? Lighten(Base, 0.8f) : Deep;   // our personal message
        }

        static readonly MsnScheme[] MsnSchemes =
        {
            new("blue", "Classic blue", 0x3a72d8),
            new("lime", "Lime", 0x4fa82a),
            new("aqua", "Aqua", 0x1c9cb0),
            new("grape", "Grape", 0x7a48b8),
            new("bubblegum", "Bubblegum", 0xe0609a),
            new("cherry", "Cherry", 0xc8283a),
            new("tangerine", "Tangerine", 0xe8761a),
            new("gold", "Gold", 0xc4961a),
            new("silver", "Silver", 0x7c8696),
            new("midnight", "Midnight", 0x2a2f3c, 0.3f),
        };

        // Which schemes each trait leans toward (a person picks the colour that suits them, as people did).
        static readonly Dictionary<string, string[]> TraitSchemes = new()
        {
            ["cruel"] = new[] { "midnight", "cherry" }, ["merciful"] = new[] { "bubblegum", "aqua" },
            ["proud"] = new[] { "gold", "grape" }, ["tolerant"] = new[] { "lime", "aqua" },
            ["humble"] = new[] { "silver", "lime" }, ["jealous"] = new[] { "lime" },
            ["loyal"] = new[] { "blue" }, ["spiteful"] = new[] { "cherry", "midnight" },
            ["generous"] = new[] { "tangerine", "gold" }, ["able"] = new[] { "blue", "silver" },
            ["idle"] = new[] { "aqua", "bubblegum" }, ["honest"] = new[] { "blue" },
            ["cunning"] = new[] { "midnight", "grape" }, ["greedy"] = new[] { "gold" },
            ["thrifty"] = new[] { "silver" }, ["reckless"] = new[] { "cherry", "tangerine" },
            ["careful"] = new[] { "blue", "silver" }, ["ambitious"] = new[] { "grape", "gold" },
            ["brave"] = new[] { "cherry" }, ["craven"] = new[] { "silver" },
            ["pious"] = new[] { "grape", "gold" }, ["cynical"] = new[] { "midnight", "silver" },
            ["cheerful"] = new[] { "tangerine", "bubblegum" }, ["gloomy"] = new[] { "midnight", "silver" },
            ["hotheaded"] = new[] { "cherry" }, ["calm"] = new[] { "aqua", "blue" },
            ["curious"] = new[] { "lime", "aqua" }, ["firebug"] = new[] { "tangerine", "cherry" },
            ["vengeful"] = new[] { "midnight" }, ["doomsayer"] = new[] { "midnight" },
            ["lightfinger"] = new[] { "grape", "silver" }, ["charmer"] = new[] { "bubblegum", "grape" },
        };

        // Which scheme each rule nudges its leader toward (the colours of office).
        static readonly Dictionary<string, string> RuleSchemes = new()
        {
            ["crown"] = "grape", ["party"] = "cherry", ["junta"] = "silver", ["guild"] = "gold", ["temple"] = "gold",
            ["commune"] = "lime", ["council"] = "blue", ["mayor"] = "aqua", ["shrine"] = "grape", ["founders"] = "blue",
        };

        MsnScheme skin = MsnSchemes[0];   // this frame's scheme, set by the Messenger and used by the toasts
        string? forceSkin;                // --skin <key>: every leader uses that scheme (screenshots)

        // The scheme our leader chose: about 1 in 6 never change the default blue; the rest pick by chance, weighted
        // toward their traits (the first trait most) and their rule. Settlers without a leader keep the default.
        MsnScheme LeaderScheme()
        {
            if (forceSkin != null && MsnSchemes.FirstOrDefault(s => s.Key == forceSkin) is { } f) return f;
            var lw = sim.LeaderWalker;
            if (lw == null || sim.Leader == null) return MsnSchemes[0];
            uint h = PicHash(lw.Id * 53 + 11);
            if (h % 100 < 17) return MsnSchemes[0];
            var w = MsnSchemes.ToDictionary(s => s.Key, _ => 1.0);
            for (int i = 0; i < sim.Leader.Traits.Length; i++)
                foreach (var k in TraitSchemes.GetValueOrDefault(sim.Leader.Traits[i]) ?? Array.Empty<string>())
                    w[k] += i == 0 ? 4 : 2.5;
            if (RuleSchemes.TryGetValue(sim.GovKey, out var rk)) w[rk] += 2;
            double pick = ((h >> 8) % 10000) / 10000.0 * w.Values.Sum();
            foreach (var s in MsnSchemes) { pick -= w[s.Key]; if (pick < 0) return s; }
            return MsnSchemes[0];
        }

        // A gradient that fades in alpha as well as colour (for the light bands over the background).
        static void GradA(ImDrawListPtr dl, Vector2 a, Vector2 b, int top, float topA, int bot, float botA) =>
            dl.AddRectFilledMultiColor(a, b, U(top, topA), U(top, topA), U(bot, botA), U(bot, botA));

        // The window's background: tinted at the top, nearly white through the middle, tinted again at the foot,
        // with the soft white swooshes of MSN 7 sweeping across the lower right.
        static void SkinBack(ImDrawListPtr dl, Vector2 a, Vector2 b, MsnScheme s)
        {
            float mid = a.Y + (b.Y - a.Y) * 0.45f;
            // A dark scheme stays dark all the way down (light text sits on it); the light ones fade to near white.
            int light = s.Dark ? Lighten(s.Base, 0.42f) : Lighten(s.Base, 0.96f);
            Grad(dl, a, new Vector2(b.X, mid), s.Back, light);
            Grad(dl, new Vector2(a.X, mid), b, light, s.Back);
            dl.PushClipRect(a, b, true);
            float w = b.X - a.X, h = b.Y - a.Y, k = s.Dark ? 0.3f : 1;
            Vector2 P(float x, float y) => new(a.X + w * x, a.Y + h * y);
            dl.AddBezierCubic(P(-0.1f, 0.95f), P(0.35f, 0.55f), P(0.7f, 0.85f), P(1.1f, 0.35f), U(Hi, 0.55f * k), 38, 48);
            dl.AddBezierCubic(P(-0.1f, 1.02f), P(0.4f, 0.62f), P(0.75f, 0.95f), P(1.1f, 0.5f), U(s.Base, 0.10f), 14, 48);
            dl.AddBezierCubic(P(0.2f, 1.05f), P(0.5f, 0.8f), P(0.8f, 1.0f), P(1.1f, 0.72f), U(Hi, 0.45f * k), 22, 48);
            dl.PopClipRect();
        }

        // A white pane with soft round corners and a tinted edge, as round MSN 7's lists, history and typing box.
        static void Pane(ImDrawListPtr dl, Vector2 a, Vector2 b, MsnScheme s, int fill = Paper, float r = 6)
        {
            dl.AddRect(a + new Vector2(0, 1), b + new Vector2(0, 1), U(s.Base, 0.12f), r);   // a faint drop shadow
            dl.AddRectFilled(a, b, U(fill), r);
            dl.AddRect(a, b, U(s.Edge), r);
        }

        // A glossy white button with a tinted edge (Send, Search), shown greyed: the player never sends.
        static void GlossButton(ImDrawListPtr dl, Vector2 a, Vector2 b, string text, MsnScheme s)
        {
            float r = 5;
            dl.AddRectFilled(a, b, U(Lighten(s.Base, 0.93f)), r);
            GradA(dl, a + new Vector2(2, 2), new Vector2(b.X - 2, a.Y + (b.Y - a.Y) * 0.5f), Hi, 0.95f, Hi, 0.35f);
            dl.AddRect(a, b, U(s.Edge), r);
            var ts = ImGui.CalcTextSize(text);
            dl.AddText(Px(a.X + (b.X - a.X - ts.X) / 2, a.Y + (b.Y - a.Y - ts.Y) / 2), U(0xa0a0a0), text);
        }

        // A glossy ball: the colour, a darker lower rim and a white sheen on top (the Aero look of the icons).
        static void Ball(ImDrawListPtr dl, Vector2 c, float r, int col)
        {
            dl.AddCircleFilled(c, r, U(Darken(col, 0.8f)), 20);
            dl.AddCircleFilled(c - new Vector2(0, r * 0.08f), r * 0.9f, U(col), 20);
            dl.AddCircleFilled(c - new Vector2(r * 0.15f, r * 0.4f), r * 0.45f, U(Hi, 0.55f), 16);
        }

        static readonly string[] ToolNames = { "Invite", "Send Files", "Video", "Voice", "Activities", "Games" };

        // One of the big toolbar pictures, s pixels square with its top-left at at.
        void ToolIcon(ImDrawListPtr dl, Vector2 at, float s, int i, MsnScheme sk)
        {
            var c = at + new Vector2(s / 2, s / 2);
            switch (i)
            {
                case 0:   // Invite: the green and blue buddies, side by side
                {
                    float px = MathF.Max(2, MathF.Floor(s / 9));
                    Art(dl, Px(at.X + s * 0.42f, at.Y + s * 0.1f), px, Buddy, ch => ch == 'h' ? 0x8fb4ee : 0x2a5fc8);
                    Art(dl, Px(at.X + s * 0.02f, at.Y + s * 0.22f), px, Buddy, ch => ch == 'h' ? 0x8ee07a : 0x2f9e3a);
                    break;
                }
                case 1:   // Send Files: a photo in front of a folder
                {
                    dl.AddRectFilled(at + new Vector2(s * 0.05f, s * 0.15f), at + new Vector2(s * 0.75f, s * 0.75f), U(0xe8b84a), 3);
                    dl.AddRectFilled(at + new Vector2(s * 0.05f, s * 0.08f), at + new Vector2(s * 0.35f, s * 0.2f), U(0xe8b84a), 2);
                    var pa = at + new Vector2(s * 0.3f, s * 0.32f);
                    var pb = at + new Vector2(s * 0.98f, s * 0.92f);
                    dl.AddRectFilled(pa, pb, U(Hi), 2);
                    dl.AddRect(pa, pb, U(0x8090a8), 2);
                    Grad(dl, pa + new Vector2(3, 3), pb - new Vector2(3, 3), 0x5aa8f0, 0xc8e4ff);
                    dl.AddTriangleFilled(new Vector2(pa.X + 3, pb.Y - 3), new Vector2(pa.X + (pb.X - pa.X) * 0.45f, pa.Y + (pb.Y - pa.Y) * 0.45f), new Vector2(pb.X - 3, pb.Y - 3), U(0x4aa040));
                    break;
                }
                case 2:   // Video: a round webcam on a little stand
                {
                    dl.AddRectFilled(at + new Vector2(s * 0.3f, s * 0.82f), at + new Vector2(s * 0.7f, s * 0.95f), U(0x8090a0), 3);
                    dl.AddRectFilled(at + new Vector2(s * 0.45f, s * 0.6f), at + new Vector2(s * 0.55f, s * 0.85f), U(0xa0aab8));
                    Ball(dl, c - new Vector2(0, s * 0.1f), s * 0.36f, 0xc8d0dc);
                    dl.AddCircleFilled(c - new Vector2(0, s * 0.1f), s * 0.17f, U(0x203048), 16);
                    dl.AddCircleFilled(c - new Vector2(s * 0.05f, s * 0.15f), s * 0.05f, U(0x7ab8ff), 8);
                    break;
                }
                case 3:   // Voice: a microphone in its holder
                {
                    float r = s * 0.16f;
                    var ma = at + new Vector2(s * 0.5f - r, s * 0.08f);
                    var mb = at + new Vector2(s * 0.5f + r, s * 0.6f);
                    dl.AddRectFilled(ma, mb, U(0x2a9a8a), r);
                    dl.AddRectFilled(ma + new Vector2(r * 0.3f, r * 0.3f), new Vector2(ma.X + r, mb.Y - r * 0.6f), U(Hi, 0.5f), r * 0.6f);
                    dl.PathArcTo(c + new Vector2(0, s * 0.12f), s * 0.28f, 0, MathF.PI, 16);
                    dl.PathStroke(U(0x707a88), ImDrawFlags.None, 2);
                    dl.AddLine(c + new Vector2(0, s * 0.4f), c + new Vector2(0, s * 0.48f), U(0x707a88), 2);
                    dl.AddRectFilled(at + new Vector2(s * 0.32f, s * 0.92f), at + new Vector2(s * 0.68f, s * 0.98f), U(0x707a88), 2);
                    break;
                }
                case 4:   // Activities: three glossy balls
                    Ball(dl, at + new Vector2(s * 0.3f, s * 0.66f), s * 0.24f, 0x8a4ad0);
                    Ball(dl, at + new Vector2(s * 0.72f, s * 0.62f), s * 0.22f, 0xf08a1a);
                    Ball(dl, at + new Vector2(s * 0.5f, s * 0.3f), s * 0.24f, 0x2a8ae0);
                    break;
                default:  // Games: two playing cards, a heart on the front one
                {
                    dl.AddQuadFilled(at + new Vector2(s * 0.08f, s * 0.2f), at + new Vector2(s * 0.5f, s * 0.08f), at + new Vector2(s * 0.62f, s * 0.8f), at + new Vector2(s * 0.2f, s * 0.92f), U(0x3a62c8));
                    var fa = at + new Vector2(s * 0.38f, s * 0.14f);
                    var fb = at + new Vector2(s * 0.92f, s * 0.94f);
                    dl.AddRectFilled(fa, fb, U(Hi), 3);
                    dl.AddRect(fa, fb, U(0x8090a8), 3);
                    var hc = (fa + fb) / 2;
                    float hr = s * 0.09f;
                    dl.AddCircleFilled(hc + new Vector2(-hr * 0.9f, -hr * 0.4f), hr, U(0xd02030), 12);
                    dl.AddCircleFilled(hc + new Vector2(hr * 0.9f, -hr * 0.4f), hr, U(0xd02030), 12);
                    dl.AddTriangleFilled(hc + new Vector2(-hr * 1.85f, -hr * 0.1f), hc + new Vector2(hr * 1.85f, -hr * 0.1f), hc + new Vector2(0, hr * 2), U(0xd02030));
                    break;
                }
            }
        }

        // How tall the toolbar is with labels under the pictures, or 0 if it does not fit across.
        float ToolbarH(float width, out bool labels)
        {
            float line = ImGui.GetTextLineHeight(), s = MathF.Floor(line * 1.5f);
            float need = ToolNames.Sum(n => MathF.Max(s, ImGui.CalcTextSize(n).X) + 10) + 8;
            labels = need <= width;
            if (labels) return s + line + 10;
            return ToolNames.Length * (s + 14) + 8 <= width ? s + 10 : 0;
        }

        // MSN 7's big toolbar: Invite, Send Files, Video, Voice, Activities, Games, each a glossy picture with its name
        // under it on a pale band. Clicking does nothing: the leader answers for the town.
        void Toolbar(ImDrawListPtr dl, Vector2 a, Vector2 b, bool labels, MsnScheme sk)
        {
            float line = ImGui.GetTextLineHeight(), s = MathF.Floor(line * 1.5f);
            GradA(dl, a, b, Hi, sk.Dark ? 0.25f : 0.85f, Hi, 0.0f);
            dl.AddLine(new Vector2(a.X + 4, b.Y - 1), new Vector2(b.X - 4, b.Y - 1), U(sk.Edge, 0.6f));
            float x = a.X + 4;
            for (int i = 0; i < ToolNames.Length; i++)
            {
                float cw = labels ? MathF.Max(s, ImGui.CalcTextSize(ToolNames[i]).X) + 10 : s + 14;
                var ca = new Vector2(x, a.Y + 2);
                var cb = new Vector2(x + cw, b.Y - 3);
                bool hot = !modal && ImGui.IsMouseHoveringRect(ca, cb, false);
                if (hot)
                {
                    dl.AddRectFilled(ca, cb, U(sk.Soft), 4);
                    dl.AddRect(ca, cb, U(sk.Edge), 4);
                }
                ToolIcon(dl, Px(x + (cw - s) / 2, a.Y + 5), s, i, sk);
                if (labels)
                {
                    float tw = ImGui.CalcTextSize(ToolNames[i]).X;
                    dl.AddText(Px(x + (cw - tw) / 2, a.Y + 5 + s + 2), U(sk.Text), ToolNames[i]);
                }
                x += cw;
            }
        }

        // The little drop-down arrow under a display picture, as MSN 7 had for changing it.
        static void DropArrow(ImDrawListPtr dl, Vector2 c) =>
            dl.AddTriangleFilled(c + new Vector2(-4, -2), c + new Vector2(4, -2), c + new Vector2(0, 2), U(0x404040));

        // The resize grip at the bottom-right corner: six little dots in a triangle.
        static void Grip(ImDrawListPtr dl, Vector2 br, MsnScheme s)
        {
            for (int r = 0; r < 3; r++)
                for (int k = 0; k <= r; k++)
                {
                    var p = br - new Vector2(4 + (r - k) * 4, 4 + k * 4);
                    dl.AddRectFilled(p, p + new Vector2(2, 2), U(Darken(s.Back, 0.7f)));
                }
        }
    }
}
