using System.Numerics;
using ImGuiNET;
using Raylib_cs;

namespace Dotburg;

// The Messenger panel (in place of the old chronicle) and the toasts over the town picture, MSN Messenger style:
// "me" (our leader) on top, contacts grouped below, the open conversation under them. Emoticon codes such as :)
// and (L) in any line are drawn as the classic pictures from resources/emoticons.png.
public partial class Program
{
    partial class App
    {
        // The 40 codes in the order of the emoticon sheet (10 across, 4 down, 24 px cells).
        static readonly string[] EmoCodes =
        {
            ":)", ":D", ";)", ":-O", ":P", "(H)", ":@", ":S", ":$", ":(",
            ":'(", ":|", "(A)", "8o|", "8-|", "+o(", "<:o)", "|-)", "*-)", ":-#",
            ":-*", "^o)", "8-)", "(L)", "(U)", "(M)", "(@)", "(&)", "(sn)", "(bah)",
            "(S)", "(*)", "(#)", "(R)", "({)", "(})", "(K)", "(F)", "(W)", "(O)",
        };
        static readonly Dictionary<string, int> Emo = EmoCodes.Select((c, i) => (c, i)).ToDictionary(p => p.c, p => p.i);
        Texture2D emoTex;

        string selKey = "bot";                       // the open conversation
        int botSeen;                                 // chronicle lines already seen
        bool scrollToEnd = true;                     // jump to the newest line (after switching conversation)
        readonly HashSet<string> folded = new();     // contact groups rolled up
        long lastNudge = -1;
        double nudgeAt = -10;
        class Shown { public Toast T = null!; public double Born; public double Dying = -1; }
        readonly List<Shown> toasts = new();

        const int Online = 0x2f9e3a, Busy = 0xd02c2c, Away = 0xe0951c, OfflineCol = 0x9a9a9a;
        static int PresenceCol(Presence p) => p switch { Presence.Online => Online, Presence.Busy => Busy, Presence.Away => Away, Presence.Blocked => Busy, _ => OfflineCol };
        static string PresenceWord(Presence p) => p switch { Presence.Busy => "Busy", Presence.Away => "Away", Presence.Offline => "Offline", Presence.Blocked => "Blocked", _ => "Online" };

        void LoadEmoticons()
        {
            string p = Path.Combine(AppContext.BaseDirectory, "resources", "emoticons.png");
            if (!File.Exists(p)) return;
            emoTex = Raylib.LoadTexture(p);
            Raylib.SetTextureFilter(emoTex, TextureFilter.Bilinear);
        }

        // ---------- text with emoticons ----------
        // Draws text word by word, wrapping at wrapW (no wrapping if 0), with emoticon codes as pictures.
        // Returns the height used. With draw false it only measures.
        float Rich(ImDrawListPtr dl, Vector2 at, float wrapW, string text, int col, bool draw = true, bool bold = false, float alpha = 1)
        {
            float line = ImGui.GetTextLineHeight(), space = ImGui.CalcTextSize(" ").X, emo = line + 2;
            float x = 0, y = 0;
            foreach (var word in text.Split(' '))
            {
                if (word.Length == 0) { x += space; continue; }
                bool isEmo = emoTex.Id != 0 && Emo.ContainsKey(word);
                float w = isEmo ? emo : ImGui.CalcTextSize(word).X + (bold ? 1 : 0);
                if (wrapW > 0 && x > 0 && x + w > wrapW) { x = 0; y += line + 2; }
                if (draw)
                {
                    var p = Px(at.X + x, at.Y + y);
                    if (isEmo)
                    {
                        int i = Emo[word];
                        float u = (i % 10) * 24f / emoTex.Width, v = (i / 10) * 24f / emoTex.Height, du = 23f / emoTex.Width, dv = 23f / emoTex.Height;
                        dl.AddImage((IntPtr)emoTex.Id, p + new Vector2(0, -1), p + new Vector2(emo, emo - 1), new Vector2(u, v), new Vector2(u + du, v + dv), U(0xffffff, alpha));
                    }
                    else
                    {
                        dl.AddText(p, U(col, alpha), word);
                        if (bold) dl.AddText(p + new Vector2(1, 0), U(col, alpha), word);
                    }
                }
                x += w + space;
            }
            return y + line + 2;
        }

        // Rich text as an ImGui item in the current window, wrapped to the space left on the line.
        void RichItem(string text, int col, float indent = 0, bool bold = false)
        {
            var p = ImGui.GetCursorScreenPos() + new Vector2(indent, 0);
            float w = ImGui.GetContentRegionAvail().X - indent;
            float h = Rich(ImGui.GetWindowDrawList(), p, w, text, col, true, bold);
            ImGui.Dummy(new Vector2(w + indent, h));
        }

        // ---------- pictures ----------
        // Pixel art from rows of characters: '.' is see-through, every other character is looked up in pal.
        static void Art(ImDrawListPtr dl, Vector2 at, float px, string[] rows, Func<char, int> pal)
        {
            for (int y = 0; y < rows.Length; y++)
                for (int x = 0; x < rows[y].Length; x++)
                {
                    char ch = rows[y][x];
                    if (ch == '.') continue;
                    var q = Px(at.X + x * px, at.Y + y * px);
                    dl.AddRectFilled(q, Px(at.X + (x + 1) * px, at.Y + (y + 1) * px), U(pal(ch)));
                }
        }

        static readonly string[] Buddy =
        {
            "..hhh..", ".hhhhh.", ".hhhhh.", "..hhh..", ".bbbbb.", "bbbbbbb", "bbbbbbb", "bbbbbbb",
        };
        static readonly string[] Person =
        {
            "............", "....hhhh....", "...hhhhhh...", "...hhhhhh...", "...hhhhhh...", "....hhhh....",
            "..bbbbbbbb..", ".bbbbbbbbbb.", ".bbbbbbbbbb.", ".bbbbbbbbbb.", ".bbbbbbbbbb.", ".bbbbbbbbbb.",
        };
        static readonly string[] House =
        {
            "............", ".....fff....", ".....f......", ".....r......", "....rrrr....", "...rrrrrr...",
            "..rrrrrrrr..", "...wwwwww...", "...wwddww.h.", "...wwddw.bbb", "gggggggggggg", "gggggggggggg",
        };
        static readonly string[] Scroll =
        {
            "............", ".pPPPPPPPPp.", ".PppppppppP.", "..pllllllp..", "..pppppppp..", "..plllllpp..",
            "..pppppppp..", "..pllllllp..", "..pppppppp..", ".PppppppppP.", ".pPPPPPPPPp.", "............",
        };

        // A contact's display picture in a sunken frame: a sister's house, a notable or our leader, the chronicle scroll.
        void DisplayPic(ImDrawListPtr dl, Vector2 a, float size, string key)
        {
            Sunken(dl, a, a + new Vector2(size, size), Paper);
            float px = MathF.Floor((size - 4) / 12);
            var at = Px(a.X + (size - px * 12) / 2, a.Y + (size - px * 12) / 2);
            if (key == "me")
            {
                int dot = sim.LeaderWalker is { } lw ? Content.DotColors[lw.C] : 0xd9a441;
                dl.AddRectFilled(at, at + new Vector2(px * 12, px * 12), U(Lighten(sim.G.Banner & 0xffffff, 0.6f)));
                Art(dl, at, px, Person, c => c == 'h' ? Lighten(dot, 0.25f) : Darken(dot, 0.8f));
                Art(dl, at, px, new[] { "....y.y.y...", "....yyyyy..." }, _ => 0xf2c14e);
                return;
            }
            if (key == "bot")
            {
                dl.AddRectFilled(at, at + new Vector2(px * 12, px * 12), U(0xe8e0c8));
                Art(dl, at, px, Scroll, c => c switch { 'P' => 0x9a7040, 'l' => 0x705030, _ => 0xf4e4b4 });
                return;
            }
            if (key.StartsWith("sis:") || key.StartsWith("gos:"))
            {
                string name = key.StartsWith("sis:") ? key[4..] : key[4..].Split('|')[0];
                var s = sim.Sisters.FirstOrDefault(x => x.Name == name);
                int scheme = s?.Scheme ?? 0;
                var sch = Content.Schemes[scheme];
                int banner = s != null ? Govs.Get(s.Gov).Banner & 0xffffff : 0x808080;
                dl.AddRectFilled(at, at + new Vector2(px * 12, px * 12), U(s != null ? 0xbfe0f8 : 0xd0d0d0));
                Art(dl, at, px, House, c => c switch
                {
                    'f' => banner, 'r' => sch.Roofs[0][0], 'w' => sch.Wall, 'd' => 0x3a2a1a, 'g' => sch.Grass[0],
                    'h' => Lighten(Content.DotColors[sch.Dot], 0.2f), _ => Content.DotColors[sch.Dot],
                });
                if (key.StartsWith("gos:"))
                {
                    // A second dot-man in the other sister's colour.
                    var o = sim.Sisters.FirstOrDefault(x => x.Name == key[4..].Split('|').Last());
                    int oc = Content.DotColors[Content.Schemes[o?.Scheme ?? 0].Dot];
                    Art(dl, at, px, new[] { "", "", "", "", "", "", "", "", ".h.", "bbb" }, c => c == 'h' ? Lighten(oc, 0.2f) : oc);
                }
                return;
            }
            // A notable: a person in the colours of the job.
            string role = key.Split(':').ElementAtOrDefault(1) ?? "";
            var (head, body, bg, extra, ec) = role switch
            {
                "captain" => (0xe0b080, 0x5a6a3a, 0xd8d8c0, new[] { "...gggggg...", "..gggggggg.." }, 0x707a80),
                "physician" => (0xe0b080, 0xf4f4f4, 0xc8e4f0, new[] { "", "", "", "", "", "", "", ".....xx.....", "....xxxx....", ".....xx....." }, 0xd02020),
                "warden" => (0xe0b080, 0x2f62c9, 0xf0d8c0, new[] { "", "", "", "", "", "", "", "", "", "........kkk.", "........kkk." }, 0x3a8ad8),
                _ => (0x404040, 0x202020, 0xe8c8c8, new[] { "", "", "", "...mmmmmm..." }, 0xc02020),
            };
            dl.AddRectFilled(at, at + new Vector2(px * 12, px * 12), U(bg));
            Art(dl, at, px, Person, c => c == 'h' ? head : body);
            Art(dl, at, px, extra, _ => ec);
        }

        static int Lighten(int rgb, float k) =>
            ((int)(((rgb >> 16) & 255) + (255 - ((rgb >> 16) & 255)) * k) << 16) | ((int)(((rgb >> 8) & 255) + (255 - ((rgb >> 8) & 255)) * k) << 8) | (int)((rgb & 255) + (255 - (rgb & 255)) * k);

        // The little MSN buddy in the colour of the status; Blocked gets a red no-entry sign over a grey buddy.
        static void BuddyIcon(ImDrawListPtr dl, Vector2 at, float px, Presence p)
        {
            int col = p == Presence.Blocked ? OfflineCol : PresenceCol(p);
            Art(dl, at, px, Buddy, c => c == 'h' ? Lighten(col, 0.25f) : Darken(col, 0.85f));
            if (p == Presence.Blocked)
            {
                var c = at + new Vector2(px * 3.5f, px * 4);
                dl.AddCircle(c, px * 3.4f, U(Busy), 12, 1.6f);
                dl.AddLine(c + new Vector2(-px * 2.4f, px * 2.4f), c + new Vector2(px * 2.4f, -px * 2.4f), U(Busy), 1.6f);
            }
            if (p == Presence.Away) dl.AddCircleFilled(at + new Vector2(px * 6.5f, px * 7), px * 1.6f, U(Hi));
        }

        // ---------- contacts ----------
        record Row(string Key, string Title, Presence P, string Msg, int Unread, string Group);

        static int Visible(Convo c, long tick) => c.Lines.Count(l => l.Due <= tick);

        // Everyone on the list, by group: this town, the sisters, group chats, and those gone offline.
        List<Row> Contacts()
        {
            var rows = new List<Row>();
            int botNew = selKey == "bot" ? 0 : Math.Max(0, chronicleView.Count - botSeen);
            rows.Add(new Row("bot", "Chronicle", Presence.Online, "Major events, kept forever (*)", botNew, sim.Name));
            foreach (var c in sim.Convos.Where(c => c.Kind == "npc" && !c.Gone).OrderByDescending(c => c.Last))
                rows.Add(new Row(c.Key, c.Title, sim.PresenceOf(c), sim.MessageOf(c), Unread(c), sim.Name));
            foreach (var s in sim.Sisters)
            {
                var c = sim.Convos.FirstOrDefault(x => x.Key == "sis:" + s.Name);
                rows.Add(new Row("sis:" + s.Name, s.Name, sim.SisterPresence(s), sim.SisterMessage(s), c != null ? Unread(c) : 0, "Sisters"));
            }
            foreach (var c in sim.Convos.Where(c => c.Kind == "gossip").OrderByDescending(c => c.Last))
                rows.Add(new Row(c.Key, c.Title, sim.PresenceOf(c), sim.MessageOf(c), Unread(c), "Group chats"));
            foreach (var c in sim.Convos.Where(c => c.Gone && c.Kind != "gossip").OrderByDescending(c => c.Last))
                rows.Add(new Row(c.Key, c.Title, Presence.Offline, sim.MessageOf(c), Unread(c), "Offline"));
            return rows;
        }

        int Unread(Convo c) => c.Key == selKey ? 0 : Math.Max(0, Visible(c, sim.TickNo) - c.Seen);

        void Open(string key)
        {
            if (key == selKey) return;
            selKey = key;
            scrollToEnd = true;
        }

        // ---------- the panel ----------
        // The Messenger window between a and b: title, "me", contacts, the open conversation.
        void Messenger(ImDrawListPtr bg, Vector2 a, Vector2 b)
        {
            // An urgent message shakes the window for a moment, like a nudge.
            if (sim.NudgeTick != lastNudge) { lastNudge = sim.NudgeTick; nudgeAt = Raylib.GetTime(); }
            double t = Raylib.GetTime() - nudgeAt;
            if (t < 0.6) { var d = new Vector2(MathF.Round(MathF.Sin((float)t * 70) * 5 * (1 - (float)t / 0.6f)), 0); a += d; b += d; }

            float row = ImGui.GetFrameHeight(), line = ImGui.GetTextLineHeight();
            Raised(bg, a, b);
            var tA = a + new Vector2(Bd, Bd);
            var tB = new Vector2(b.X - Bd, tA.Y + row);
            TitleBar(bg, tA, tB, $"{sim.Name} Messenger", true);
            float x0 = a.X + Bd + Pad, x1 = b.X - Bd - Pad;
            float y = tB.Y + Pad;

            // Me: our leader's picture, name and status, and personal message.
            float pic = MathF.Max(48, line * 2 + 12);
            DisplayPic(bg, new Vector2(x0, y), pic, "me");
            var me = sim.MePresence;
            float tx = x0 + pic + 8;
            bg.PushClipRect(new Vector2(tx, y), new Vector2(x1, y + pic), true);
            BoldText(bg, Px(tx, y + 2), Ink, sim.MeName);
            float nw = ImGui.CalcTextSize(sim.MeName).X + 1;
            bg.AddText(Px(tx + nw + 6, y + 2), U(Shade), $"({PresenceWord(me)})");
            Rich(bg, new Vector2(tx, y + line + 6), 0, sim.MeMessage(), Navy);
            bg.PopClipRect();
            if (ImGui.IsMouseHoveringRect(new Vector2(x0, y), new Vector2(x1, y + pic)))
                Tip($"You are signed in as {sim.LeaderTitle}, {sim.G.Name.ToLower()} of {sim.Name}.\nStatus: {PresenceWord(me)}. The leader answers for the town; you read along.");
            y += pic + Gap;

            // Contacts: a sunken white list, about 40% of what is left.
            var rows = Contacts();
            float rowH = MathF.Max(line, 16) + 4;
            var groups = rows.Select(r => r.Group).Distinct().ToList();
            float need = groups.Count * rowH + rows.Count(r => !folded.Contains(r.Group)) * rowH + 8;
            float rest = b.Y - Bd - Pad - y;
            float listH = Math.Clamp(need, rowH * 4, MathF.Max(rowH * 4, rest * 0.42f));
            var lA = new Vector2(x0, y);
            var lB = new Vector2(x1, y + listH);
            Sunken(bg, lA, lB, Paper);
            Area("contacts", lA + new Vector2(2, 2), lB - new Vector2(2, 2));
            ContactList(rows, groups, rowH);
            ImGui.End();
            y = lB.Y + Gap;

            // The conversation header: picture, "To:" with a made-up address, and their personal message.
            var sel = rows.FirstOrDefault(r => r.Key == selKey) ?? rows[0];
            if (sel.Key != selKey) selKey = sel.Key;
            float hp = MathF.Max(40, line * 2 + 6);
            DisplayPic(bg, new Vector2(x0, y), hp, sel.Key);
            tx = x0 + hp + 8;
            bg.PushClipRect(new Vector2(tx, y), new Vector2(x1, y + hp), true);
            bg.AddText(Px(tx, y + 1), U(Shade), "To:");
            float toW = ImGui.CalcTextSize("To: ").X;
            BoldText(bg, Px(tx + toW, y + 1), Ink, sel.Title);
            bg.AddText(Px(tx + toW + ImGui.CalcTextSize(sel.Title).X + 8, y + 1), U(Shade), Address(sel));
            Rich(bg, new Vector2(tx, y + line + 4), 0, sel.Msg, Shade);
            bg.PopClipRect();
            y += hp + 4;

            // A notice bar in pale yellow when the contact is not online, as MSN did.
            if (sel.P != Presence.Online && sel.Key != "bot")
            {
                string note = sel.P switch
                {
                    Presence.Offline => $"{sel.Title} is offline. Old messages are kept.",
                    Presence.Blocked => $"{sel.Title} is blocked: relations are hostile.",
                    _ => $"{sel.Title} may not reply because their status is set to {PresenceWord(sel.P)}.",
                };
                var nA = new Vector2(x0, y);
                var nB = new Vector2(x1, y + line + 6);
                bg.AddRectFilled(nA, nB, U(TipBg));
                bg.AddRect(nA, nB, U(0xc8c090));
                bg.PushClipRect(nA, nB, true);
                bg.AddCircleFilled(nA + new Vector2(10, (line + 6) / 2), line * 0.36f, U(0x2050c0));
                bg.AddText(nA + new Vector2(8, 3), U(Hi), "i");
                bg.AddText(nA + new Vector2(20, 3), U(Ink), note);
                bg.PopClipRect();
                y = nB.Y + 4;
            }

            // The conversation, then the status line under it.
            float footH = line + 8;
            var cA = new Vector2(x0, y);
            var cB = new Vector2(x1, b.Y - Bd - Pad - footH);
            Sunken(bg, cA, cB, Paper);
            Area("convo", cA + new Vector2(2, 2), cB - new Vector2(2, 2));
            string foot = Conversation(sel);
            ImGui.End();
            bg.PushClipRect(new Vector2(x0, cB.Y), new Vector2(x1, b.Y - Bd), true);
            Rich(bg, new Vector2(x0 + 2, cB.Y + 5), 0, foot, Shade);
            bg.PopClipRect();
        }

        // A made-up address for the To: line, like <crown@ashford.town>.
        string Address(Row r)
        {
            static string Slug(string s) => new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            string home = Slug(sim.Name) + ".town";
            if (r.Key == "bot") return $"<chronicle@{home}>";
            if (r.Key.StartsWith("npc:")) return $"<{r.Key.Split(':')[1]}@{home}>";
            if (r.Key.StartsWith("gos:")) return "<group chat>";
            var s = sim.Sisters.FirstOrDefault(x => x.Name == r.Title);
            return $"<{(s != null ? s.Gov : "nobody")}@{Slug(r.Title)}.town>";
        }

        // The contact list: group headers that fold, then one row per contact. Click a row to open it.
        void ContactList(List<Row> rows, List<string> groups, float rowH)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4, 4));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 0));
            ImGui.BeginChild("contactsC", Vector2.Zero, ImGuiChildFlags.AlwaysUseWindowPadding);
            var dl = ImGui.GetWindowDrawList();
            float line = ImGui.GetTextLineHeight();
            float bpx = MathF.Max(2, MathF.Floor(line / 8));
            foreach (var g in groups)
            {
                var gr = rows.Where(r => r.Group == g).ToList();
                bool open = !folded.Contains(g);
                var p = ImGui.GetCursorScreenPos();
                float w = ImGui.GetContentRegionAvail().X;
                if (ImGui.InvisibleButton("g" + g, new Vector2(w, rowH))) { if (open) folded.Add(g); else folded.Remove(g); }
                // A small triangle: pointing down when open, right when folded.
                var c = p + new Vector2(7, rowH / 2);
                if (open) dl.AddTriangleFilled(c + new Vector2(-4, -2), c + new Vector2(4, -2), c + new Vector2(0, 3), U(Navy));
                else dl.AddTriangleFilled(c + new Vector2(-2, -4), c + new Vector2(-2, 4), c + new Vector2(3, 0), U(Navy));
                int on = gr.Count(r => r.P != Presence.Offline);
                BoldText(dl, Px(p.X + 16, p.Y + (rowH - line) / 2), Navy, $"{g} ({on}/{gr.Count})");
                if (!open) continue;
                foreach (var r in gr)
                {
                    p = ImGui.GetCursorScreenPos();
                    bool selRow = r.Key == selKey;
                    if (ImGui.InvisibleButton("r" + r.Key, new Vector2(w, rowH))) Open(r.Key);
                    bool hot = ImGui.IsItemHovered();
                    var ra = p;
                    var rb = p + new Vector2(w, rowH);
                    if (selRow) dl.AddRectFilled(ra, rb, U(Navy));
                    else if (hot) dl.AddRectFilled(ra, rb, U(0xe4ecf8));
                    BuddyIcon(dl, Px(p.X + 18, p.Y + (rowH - 8 * bpx) / 2), bpx, r.P);
                    float tx = p.X + 18 + 7 * bpx + 6, ty = p.Y + (rowH - line) / 2;
                    dl.PushClipRect(ra, rb, true);
                    int ink = selRow ? Hi : Ink, grey = selRow ? 0xc8d0e8 : Shade;
                    string name = r.Unread > 0 ? $"{r.Title} ({r.Unread})" : r.Title;
                    if (r.Unread > 0) BoldText(dl, Px(tx, ty), ink, name); else dl.AddText(Px(tx, ty), U(r.P == Presence.Offline ? grey : ink), name);
                    tx += ImGui.CalcTextSize(name).X + (r.Unread > 0 ? 1 : 0);
                    if (r.P != Presence.Online) { string st = $" ({PresenceWord(r.P)})"; dl.AddText(Px(tx, ty), U(grey), st); tx += ImGui.CalcTextSize(st).X; }
                    if (r.Msg.Length > 0)
                    {
                        dl.AddText(Px(tx, ty), U(grey), " - ");
                        Rich(dl, new Vector2(tx + ImGui.CalcTextSize(" - ").X, ty), 0, r.Msg, grey);
                    }
                    dl.PopClipRect();
                    if (hot) Tip($"{r.Title} ({PresenceWord(r.P)})\n{r.Msg}");
                }
            }
            ImGui.EndChild();
            ImGui.PopStyleVar(2);
        }

        // The open conversation: "X says:" headers and lines, with day breaks. Returns the status line for under it.
        string Conversation(Row sel)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 6));
            ImGui.BeginChild("convoC" + sel.Key, Vector2.Zero, ImGuiChildFlags.AlwaysUseWindowPadding);
            string foot;
            if (sel.Key == "bot")
            {
                botSeen = chronicleView.Count;
                if (chronicleView.Count == 0) ImGui.TextColored(V(Shade), "Nothing of note yet.");
                string last = "";
                foreach (var l in chronicleView)
                {
                    // "- date | town, day N, season | text": the middle part heads each new day.
                    var parts = l.Split(" | ");
                    string head = parts.Length >= 3 ? parts[1] : "";
                    string text = parts.Length >= 3 ? string.Join(" | ", parts[2..]) : l;
                    if (head != last) { if (last != "") ImGui.Spacing(); RichItem($"Chronicle says ({head}):", Shade); last = head; }
                    RichItem(text, Ink, 12);
                }
                foot = "Also written to chronicle.txt (*)";
            }
            else
            {
                var c = sim.Convos.FirstOrDefault(x => x.Key == sel.Key);
                var lines = c?.Lines.Where(l => l.Due <= sim.TickNo).ToList() ?? new();
                if (c != null) c.Seen = lines.Count;
                if (lines.Count == 0) ImGui.TextColored(V(Shade), "No messages yet.");
                int day = -1;
                string from = "";
                foreach (var l in lines)
                {
                    if (l.Day != day)
                    {
                        if (day != -1) ImGui.Spacing();
                        string d = $"- Day {l.Day} -";
                        ImGui.SetCursorPosX(MathF.Max(0, (ImGui.GetWindowWidth() - ImGui.CalcTextSize(d).X) / 2));
                        ImGui.TextColored(V(Shade), d);
                        day = l.Day;
                        from = "";
                    }
                    if (l.From != from) { RichItem($"{l.From} says:", Shade); from = l.From; }
                    RichItem(l.Text, l.Ours ? Navy : Ink, 12);
                }
                var typing = c?.Lines.FirstOrDefault(l => l.Due > sim.TickNo);
                foot = typing != null ? $"{typing.From} is typing a message..."
                    : lines.Count > 0 ? $"Last message received on day {lines[^1].Day}."
                    : sel.Key.StartsWith("gos:") ? "Group chat. They can't see you ;)" : $"{sim.MeName} answers for {sim.Name}.";
            }
            if (scrollToEnd || ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 4) ImGui.SetScrollHereY(1f);
            scrollToEnd = false;
            ImGui.EndChild();
            ImGui.PopStyleVar();
            return foot;
        }

        // ---------- toasts ----------
        // New toasts rise from the bottom-right of the town picture, stay six seconds and sink away. At most three
        // at once; clicking one opens its conversation.
        void DrawToasts(ImDrawListPtr dl)
        {
            double now = Raylib.GetTime();
            foreach (var t in sim.Toasts) toasts.Add(new Shown { T = t, Born = now });
            sim.Toasts.Clear();
            foreach (var s in toasts.Where(s => s.Dying < 0 && now - s.Born > 6)) s.Dying = now;
            int live = toasts.Count(s => s.Dying < 0);
            foreach (var s in toasts.Where(s => s.Dying < 0).Take(Math.Max(0, live - 3))) s.Dying = now;
            toasts.RemoveAll(s => s.Dying >= 0 && now - s.Dying > 0.3);
            if (toasts.Count == 0) return;

            float line = ImGui.GetTextLineHeight();
            var area = new Vector2(townRect.X, townRect.Y);
            var areaB = area + new Vector2(townRect.Width, townRect.Height);
            float w = MathF.Min(300, townRect.Width - 16);
            float bodyW = w - 2 * Bd - 16;
            dl.PushClipRect(area, areaB, true);
            float bottom = areaB.Y - 8;
            for (int i = toasts.Count - 1; i >= 0; i--)
            {
                var s = toasts[i];
                float bodyH = line + 4 + Rich(dl, Vector2.Zero, bodyW, s.T.Text, Ink, false);
                float h = Bd + line + 4 + 6 + bodyH + 6 + Bd;
                // Slide: in from below over 0.25 s, out downward over 0.3 s.
                float k = s.Dying >= 0 ? 1 - (float)Math.Clamp((now - s.Dying) / 0.3, 0, 1) : (float)Math.Clamp((now - s.Born) / 0.25, 0, 1);
                float off = (1 - k) * (h + 8);
                var a = Px(areaB.X - 8 - w, bottom - h + off);
                var b = a + new Vector2(w, h);
                Raised(dl, a, b);
                var tA = a + new Vector2(Bd, Bd);
                var tB = new Vector2(b.X - Bd, tA.Y + line + 4);
                dl.AddRectFilledMultiColor(tA, tB, U(Navy), U(Sky), U(Sky), U(Navy));
                BoldText(dl, Px(tA.X + 4, tA.Y + 2), Hi, s.T.Crier ? $"{sim.Name} crier" : $"{sim.Name} Messenger");
                var p = new Vector2(tA.X + 8, tB.Y + 6);
                if (s.T.Title.Length > 0) BoldText(dl, Px(p.X, p.Y), s.T.Urgent ? 0xa00000 : Navy, s.T.Title);
                Rich(dl, new Vector2(p.X, p.Y + line + 4), bodyW, s.T.Text, Ink);
                if (s.Dying < 0 && ImGui.IsMouseHoveringRect(a, b) && !ImGui.IsAnyItemHovered())
                {
                    dl.AddRect(a, b, U(Navy), 0, ImDrawFlags.None, 2);
                    if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    {
                        if (s.T.Key.Length > 0) Open(s.T.Key);
                        s.Dying = now;
                    }
                }
                bottom = a.Y - 6;
            }
            dl.PopClipRect();
        }
    }
}
