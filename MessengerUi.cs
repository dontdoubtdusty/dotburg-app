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
        // The emoticon codes live in Sim.EmoCodes (Hands.cs), so chat styling can leave them alone.
        static readonly string[] EmoCodes = Sim.EmoCodes;
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
        // Returns the height used. With draw false it only measures. face picks one of the chat fonts (null: the main font).
        float Rich(ImDrawListPtr dl, Vector2 at, float wrapW, string text, int col, bool draw = true, bool bold = false, float alpha = 1, string? face = null)
        {
            ImFontPtr font = default;
            bool own = face != null && chatFonts.TryGetValue(face, out font);
            float fs = ImGui.GetFontSize();
            float Width(string s) => own ? font.CalcTextSizeA(fs, float.MaxValue, 0, s).X : ImGui.CalcTextSize(s).X;
            float line = ImGui.GetTextLineHeight(), space = Width(" "), emo = line + 2;
            float x = 0, y = 0;
            foreach (var word in text.Split(' '))
            {
                if (word.Length == 0) { x += space; continue; }
                bool isEmo = emoTex.Id != 0 && Emo.ContainsKey(word);
                float w = isEmo ? emo : bold ? BoldW(word) : Width(word);
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
                    else if (bold) BoldText(dl, p, col, word, alpha);
                    else if (own) dl.AddText(font, fs, p, U(col, alpha), word);
                    else dl.AddText(p, U(col, alpha), word);
                }
                x += w + space;
            }
            return y + line + 2;
        }

        // Rich text as an ImGui item in the current window, wrapped to the space left on the line.
        void RichItem(string text, int col, float indent = 0, bool bold = false, string? face = null)
        {
            var p = ImGui.GetCursorScreenPos() + new Vector2(indent, 0);
            float w = ImGui.GetContentRegionAvail().X - indent;
            float h = Rich(ImGui.GetWindowDrawList(), p, w, text, col, true, bold, 1, face);
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
        // The crier's hand bell: a wooden handle over a brass bell with its clapper.
        static readonly string[] Bell =
        {
            "............", ".....hh.....", ".....hh.....", ".....hh.....", "....BBBB....", "...BbbbbB...",
            "...BbbbbB...", "..BbbbbbbB..", "..BbbbbbbB..", ".BBBBBBBBBB.", ".....cc.....", "............",
        };

        static readonly string[] Scroll =
        {
            "............", ".pPPPPPPPPp.", ".PppppppppP.", "..pllllllp..", "..pppppppp..", "..plllllpp..",
            "..pppppppp..", "..pllllllp..", "..pppppppp..", ".PppppppppP.", ".pPPPPPPPPp.", "............",
        };

        // A contact's display picture in a sunken frame: a sister's house, a notable or our leader, the chronicle scroll.
        void DisplayPic(ImDrawListPtr dl, Vector2 a, float size, string key)
        {
            Sunken(dl, a, a + new Vector2(size, size), Paper, 3);
            float px = MathF.Floor((size - 4) / 12);
            var at = Px(a.X + (size - px * 12) / 2, a.Y + (size - px * 12) / 2);
            if (key.StartsWith("pal:"))
            {
                // A friend or enemy: the same picture they would have as leader (hashed from their id), no prop.
                int id = int.TryParse(key.Split(':').Last(), out var pid) ? pid : 0;
                var pw = sim.Walkers.FirstOrDefault(x => x.Id == id);
                uint ph = PicHash(id * 31 + 7);
                if (IconPic(dl, at, px * 12, ph)) return;
                Portrait(dl, at, px, ph, 0xe4ecf4, Darken(pw != null ? Content.DotColors[pw.C] : 0x909090, 0.8f), Array.Empty<string>(), 0);
                return;
            }
            if (key == "me")
            {
                int dot = sim.LeaderWalker is { } lw ? Content.DotColors[lw.C] : 0xd9a441;
                uint h = PicHash((sim.LeaderWalker?.Id ?? 0) * 31 + 7);
                if (IconPic(dl, at, px * 12, h)) return;
                // A crown for a monarch; anyone else wears the chain of office.
                var prop = sim.GovKey == "crown" ? new[] { "....y.y.y...", "....yyyyy..." } : new[] { "", "", "", "", "", "", "...y....y...", "....yyyy...." };
                Portrait(dl, at, px, h, Lighten(sim.G.Banner & 0xffffff, 0.6f), Darken(dot, 0.8f), prop, 0xf2c14e);
                return;
            }
            if (key == "bot")
            {
                dl.AddRectFilled(at, at + new Vector2(px * 12, px * 12), U(0xe8e0c8));
                Art(dl, at, px, Scroll, c => c switch { 'P' => 0x9a7040, 'l' => 0x705030, _ => 0xf4e4b4 });
                return;
            }
            if (key == Sim.CrierKey)
            {
                dl.AddRectFilled(at, at + new Vector2(px * 12, px * 12), U(0xf6e7c0));
                Art(dl, at, px, Bell, c => c switch { 'B' => 0xb07a1c, 'b' => 0xe0a830, 'h' => 0x6a4a2a, 'c' => 0x4a3a2a, _ => 0xf6e7c0 });
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
                "steward" => (0xe0b080, 0x8a6a3a, 0xf0e6c0, new[] { "", "", "", "", "", "", "", "", "........y.y.", ".........y..", ".........y.." }, 0xd8b040),
                "baker" => (0xe0b080, 0xf4f0e8, 0xf0d8b0, new[] { "....xxxx....", "...xxxxxx..." }, 0xffffff),   // a white cap
                "grocer" => (0xe0b080, 0x3a7a3a, 0xd8f0c8, new[] { "", "", "", "", "", "", "", "", "........g.g.", ".........g.." }, 0x5aa040),   // greens in hand
                _ => (0x404040, 0x202020, 0xe8c8c8, new[] { "", "", "", "...mmmmmm..." }, 0xc02020),
            };
            uint nh = PicHash(Array.IndexOf(Sim.NpcRoles, role) * 977 + sim.Founded * 131 + 3);
            if (IconPic(dl, at, px * 12, nh)) return;
            if (role == "ringleader")
            {
                // The ringleader never shows a face: a hood and a mask.
                dl.AddRectFilled(at, at + new Vector2(px * 12, px * 12), U(bg));
                Art(dl, at, px, Person, c => c == 'h' ? head : body);
                Art(dl, at, px, extra, _ => ec);
                return;
            }
            Portrait(dl, at, px, nh, bg, body, extra, ec);
        }

        // ---------- display pictures ----------
        // About two in three people use a picture from resources/avatars.png (8 across, 96 px cells, 64 of them: 58 real
        // photos with 2005-style treatments and 6 kept cartoons, built by make-photo-avatars.ps1), the rest a pixel
        // portrait. Both are chosen from a hash of who they are, so a person keeps their picture.
        Texture2D avatarTex;
        const int AvatarCell = 96, AvatarAcross = 8;
        static readonly int[] Pastels = { 0xcfe3f7, 0xd8f0d0, 0xf7e3c8, 0xf2d4e4, 0xe4dcf4, 0xf4f0c8, 0xd4ecec, 0xe8e8e8 };
        static readonly int[] Skins = { 0xf1c8a0, 0xe0b080, 0xc68a5a, 0x8d5a3a };
        static readonly int[] Hairs = { 0x2a1a10, 0x6a4020, 0xc89040, 0xa03a20, 0xb8b8b8 };
        static readonly string[][] HairCuts =
        {
            new[] { "", "....HHHH....", "...HHHHHH..." },                                                                // short
            new[] { "", "....HHHH....", "...HHHHHH...", "...H....H...", "...H....H...", "...H....H...", "..HH....HH.." },  // long
            new[] { "", "", "...H....H..." },                                                                           // balding
        };

        void LoadAvatars()
        {
            string p = Path.Combine(AppContext.BaseDirectory, "resources", "avatars.png");
            if (!File.Exists(p)) return;
            avatarTex = Raylib.LoadTexture(p);
            Raylib.SetTextureFilter(avatarTex, TextureFilter.Bilinear);
        }

        // Spreads a number into a well-mixed hash (so neighbouring ids get unrelated pictures).
        static uint PicHash(int seed)
        {
            uint x = unchecked((uint)seed * 0x9E3779B1u);
            x ^= x >> 15; x = unchecked(x * 0x85EBCA77u); x ^= x >> 13; x = unchecked(x * 0xC2B2AE3Du); x ^= x >> 16;
            return x;
        }

        // Draws an icon on a pastel square if this person picked one; false if they use a portrait.
        bool IconPic(ImDrawListPtr dl, Vector2 at, float side, uint h)
        {
            int count = avatarTex.Id == 0 ? 0 : (avatarTex.Width / AvatarCell) * (avatarTex.Height / AvatarCell);
            if (count == 0 || h % 100 < 35) return false;
            int i = (int)((h >> 8) % (uint)count);
            dl.AddRectFilled(at, at + new Vector2(side, side), U(Pastels[(h >> 20) % (uint)Pastels.Length]));
            float cw = AvatarCell / (float)avatarTex.Width, ch = AvatarCell / (float)avatarTex.Height;
            float u = (i % AvatarAcross) * cw, v = (i / AvatarAcross) * ch;
            dl.AddImage((IntPtr)avatarTex.Id, at, at + new Vector2(side, side), new Vector2(u, v), new Vector2(u + cw, v + ch));
            return true;
        }

        // A pixel portrait: skin, hair colour and cut from the hash, eyes, the body in the job's colour, then the prop
        // (crown, helmet, red cross) on top.
        static void Portrait(ImDrawListPtr dl, Vector2 at, float px, uint h, int bg, int body, string[] prop, int propCol)
        {
            int skin = Skins[(h >> 4) % (uint)Skins.Length], hair = Hairs[(h >> 12) % (uint)Hairs.Length];
            dl.AddRectFilled(at, at + new Vector2(px * 12, px * 12), U(bg));
            Art(dl, at, px, Person, c => c == 'h' ? skin : body);
            Art(dl, at, px, new[] { "", "", "", "....e..e...." }, _ => 0x2a1a10);
            Art(dl, at, px, HairCuts[(h >> 16) % (uint)HairCuts.Length], _ => hair);
            Art(dl, at, px, prop, _ => propCol);
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
        // Head: the name in the conversation header when it differs from the list name (a sister's town). Info: an extra tooltip line.
        record Row(string Key, string Title, Presence P, string Msg, int Unread, string Group, string Head = "", string Info = "");

        static int Visible(Convo c, long tick) => c.Lines.Count(l => l.Due <= tick);

        // Everyone on the list, by group: this town, the sisters, group chats, and those gone offline.
        List<Row> Contacts()
        {
            var rows = new List<Row>();
            int botNew = selKey == "bot" ? 0 : Math.Max(0, chronicleView.Count - botSeen);
            rows.Add(new Row("bot", "Chronicle", Presence.Online, sim.Chronicler != null ? $"kept by {sim.ChroniclerName} (*)" : "Major events, kept forever (*)", botNew, sim.Name, "", sim.ChroniclerLine()));
            // The town crier: every call, as it was called (no unread count: it calls all day).
            rows.Add(new Row(Sim.CrierKey, "Town crier", Presence.Online, "Every call, as it is called", 0, sim.Name, "", "Keeps the last 600 calls."));
            foreach (var c in sim.Convos.Where(c => c.Kind == "npc" && !c.Gone).OrderByDescending(c => c.Last))
                rows.Add(new Row(c.Key, c.Title, sim.PresenceOf(c), sim.MessageOf(c), Unread(c), sim.Name));
            // The leader's own people: friends, then those blocked for bad blood (0.69).
            foreach (var c in sim.Convos.Where(c => c.Kind == "pal" && !c.Gone).OrderBy(c => c.Role == "foe").ThenByDescending(c => c.Last))
                rows.Add(new Row(c.Key, c.Title, sim.PresenceOf(c), sim.MessageOf(c), Unread(c), c.Role == "foe" ? "Blocked" : "Friends", "", sim.PalInfo(c)));
            foreach (var s in sim.Sisters)
            {
                var c = sim.Convos.FirstOrDefault(x => x.Key == "sis:" + s.Name);
                rows.Add(new Row("sis:" + s.Name, sim.HandleOf(s), sim.SisterPresence(s), sim.SisterMessage(s), c != null ? Unread(c) : 0, "Sisters", s.Name, sim.RulerLine(s)));
            }
            foreach (var c in sim.Convos.Where(c => c.Kind == "gossip").OrderByDescending(c => c.Last))
                rows.Add(new Row(c.Key, c.Title, sim.PresenceOf(c), sim.MessageOf(c), Unread(c), "Group chats"));
            foreach (var c in sim.Convos.Where(c => c.Gone && c.Kind != "gossip").OrderByDescending(c => c.Last))
                rows.Add(new Row(c.Key, c.Title, Presence.Offline, sim.MessageOf(c), Unread(c), "Offline", c.Key.StartsWith("sis:") ? c.Key[4..] : ""));
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

            float line = ImGui.GetTextLineHeight();
            float spk = TitleH - 6;
            float caps = CapsW;   // the window buttons, while maximised without the Windows title bar
            // The Windows title bar stays Luna blue; inside it, the leader's own MSN colour scheme (MsnSkin.cs).
            var sk = skin = LeaderScheme();
            XpWindow(bg, a, b, TitleH, $"{sim.Name} Messenger", true, caps + (audioOn ? spk + 4 : 0), sk.Back);
            if (audioOn) SpeakerButton(bg, Px(b.X - Bd - caps - spk - 2, a.Y + (Bd + TitleH - spk) / 2), spk);
            WindowButtons(bg, b.X, a.Y);
            float top = a.Y + Bd + TitleH;
            float x0 = a.X + Bd + Pad, x1 = b.X - Bd - Pad;
            float y = top + Pad;
            SkinBack(bg, new Vector2(a.X + Bd, top), new Vector2(b.X - Bd, b.Y - Bd), sk);

            // At launch, MSN's "Signing in..." page for a moment (with the start-up sound), then the contact list.
            if (signInOn)
            {
                double nowT = Raylib.GetTime();
                if (signInUntil == 0) { signInUntil = nowT + 2.6; Play(Sfx.Startup); }
                if (nowT < signInUntil) { SigningIn(bg, new Vector2(a.X + Bd, top), new Vector2(b.X - Bd, b.Y - Bd), nowT); return; }
                if (signInUntil > 0) { signInUntil = -1; Play(Sfx.Online); }
            }

            // Me: our leader's picture, name and status, and personal message, on the tinted top of the window.
            float pic = MathF.Max(48, line * 2 + 12);
            DisplayPic(bg, new Vector2(x0, y), pic, "me");
            var me = sim.MePresence;
            float tx = x0 + pic + 8;
            bg.PushClipRect(new Vector2(tx, y), new Vector2(x1, y + pic), true);
            BoldText(bg, Px(tx, y + 2), sk.Text, sim.MeName);
            float nw = BoldW(sim.MeName);
            bg.AddText(Px(tx + nw + 6, y + 2), U(sk.Dim), $"({PresenceWord(me)})");
            Rich(bg, new Vector2(tx, y + line + 6), 0, sim.MeMessage(), sk.Motto);
            bg.PopClipRect();
            if (ImGui.IsMouseHoveringRect(new Vector2(x0, y), new Vector2(x1, y + pic)))
                Tip($"You are signed in as {sim.LeaderTitle}, {sim.G.Name.ToLower()} of {sim.Name}.\nStatus: {PresenceWord(me)}. Colour scheme: {sk.Name}, the leader's choice.\nThe leader answers for the town; you read along.");
            y += pic + Gap;

            // Contacts: a white pane, about 40% of what is left (a little less when the toolbar fits under it).
            var rows = Contacts();
            float rowH = MathF.Max(line, 16) + 4;
            var groups = rows.Select(r => r.Group).Distinct().ToList();
            float need = groups.Count * rowH + rows.Count(r => !folded.Contains(r.Group)) * rowH + 8;
            float rest = b.Y - Bd - Pad - y;
            float barH = ToolbarH(x1 - x0, out bool labels);
            if (rest < line * 30) barH = 0;                          // a short window keeps its room for the talk
            float listH = Math.Clamp(need, rowH * 4, MathF.Max(rowH * 4, (rest - barH) * 0.4f));
            var lA = new Vector2(x0, y);
            var lB = new Vector2(x1, y + listH);
            Pane(bg, lA, lB, sk);
            Area("contacts", lA + new Vector2(2, 3), lB - new Vector2(2, 3));
            ContactList(rows, groups, rowH);
            ImGui.End();
            y = lB.Y + Gap;

            // MSN 7's big toolbar over the conversation, when there is room for it.
            if (barH > 0) { Toolbar(bg, new Vector2(x0, y), new Vector2(x1, y + barH), labels, sk); y += barH + Gap; }

            // MSN 7.5's conversation: a pane with the "To:" header over the history, the formatting toolbar and the
            // typing box under it; on the right their display picture at the top and ours at the bottom.
            var sel = rows.FirstOrDefault(r => r.Key == selKey) ?? rows[0];
            if (sel.Key != selKey) selKey = sel.Key;
            float footH = line + 8;
            float bottom = b.Y - Bd - footH;
            float frame = MathF.Floor(Math.Clamp((x1 - x0) * 0.24f, 72, 104));
            float frameH = frame + 12;                               // the picture plus the drop-down arrow under it
            float picCol = frame + 8;
            float boxH = MathF.Max(frameH, line * 3 + 30);           // toolbar plus typing box, as tall as our picture
            float tbH = line + 8;
            var cA = new Vector2(x0, y);
            var cB = new Vector2(x1 - picCol, bottom - Pad - boxH - 6);
            Pane(bg, cA, cB, sk);

            // The header: "To:" with a made-up address, and their personal message, over a thin tinted rule.
            float hh = line * 2 + 6;
            float hx = cA.X + 8, hy = cA.Y + 4;
            bg.PushClipRect(new Vector2(hx, hy), new Vector2(cB.X - 6, hy + hh), true);
            bg.AddText(Px(hx, hy + 1), U(Shade), "To:");
            float toW = ImGui.CalcTextSize("To: ").X;
            string head = sel.Head.Length > 0 ? sel.Head : sel.Title;
            BoldText(bg, Px(hx + toW, hy + 1), Ink, head);
            bg.AddText(Px(hx + toW + BoldW(head) + 8, hy + 1), U(Shade), Address(sel));
            Rich(bg, new Vector2(hx, hy + line + 4), 0, sel.Msg, Shade);
            bg.PopClipRect();
            float hist = hy + hh + 2;
            bg.AddLine(new Vector2(cA.X + 1, hist), new Vector2(cB.X - 1, hist), U(sk.Edge));
            hist += 1;

            // A notice bar in pale yellow when the contact is not online, as MSN did.
            if (sel.P != Presence.Online && sel.Key != "bot")
            {
                string note = sel.P switch
                {
                    Presence.Offline => $"{sel.Title} is offline. Old messages are kept.",
                    Presence.Blocked => sel.Key.StartsWith("pal:") ? $"{sel.Title} is blocked: there is bad blood between you." : $"{sel.Title} is blocked: relations are hostile.",
                    _ => $"{sel.Title} may not reply because their status is set to {PresenceWord(sel.P)}.",
                };
                var nA = new Vector2(cA.X + 1, hist);
                var nB = new Vector2(cB.X - 1, hist + line + 6);
                bg.AddRectFilled(nA, nB, U(TipBg));
                bg.AddLine(new Vector2(nA.X, nB.Y), nB, U(0xc8c090));
                bg.PushClipRect(nA, nB, true);
                bg.AddCircleFilled(nA + new Vector2(10, (line + 6) / 2), line * 0.36f, U(0x2050c0));
                bg.AddText(nA + new Vector2(8, 3), U(Hi), "i");
                bg.AddText(nA + new Vector2(20, 3), U(Ink), note);
                bg.PopClipRect();
                hist = nB.Y + 1;
            }

            Area("convo", new Vector2(cA.X + 2, hist), cB - new Vector2(2, 4));
            string foot = Conversation(sel);
            ImGui.End();
            PicFrame(bg, new Vector2(x1 - frame, y), frame, sel.Key, sel.P, sk);
            PicFrame(bg, new Vector2(x1 - frame, bottom - Pad - frameH), frame, "me", me, sk);
            ComposeBox(bg, new Vector2(x0, bottom - Pad - boxH), new Vector2(x1 - picCol, bottom - Pad), tbH, sk);

            // The status bar along the foot of the window, with the resize grip in the corner.
            var fA = new Vector2(a.X + Bd, bottom);
            var fB = new Vector2(b.X - Bd, b.Y - Bd);
            Grad(bg, fA, fB, Lighten(sk.Base, 0.88f), Lighten(sk.Base, 0.74f));
            bg.AddLine(fA, new Vector2(fB.X, fA.Y), U(sk.Edge));
            bg.AddLine(fA + new Vector2(0, 1), new Vector2(fB.X, fA.Y + 1), U(Hi, 0.8f));
            bg.PushClipRect(fA, fB, true);
            Rich(bg, new Vector2(x0 + 2, bottom + 4), 0, foot, Ink);
            bg.PopClipRect();
            Grip(bg, fB, sk);
        }

        // A display picture in MSN 7's frame: a white pane with a tinted edge and a soft gloss, the picture inside
        // with a thin rim in the status colour (green online, orange away, red busy or blocked, grey offline), and
        // the little drop-down arrow under it.
        void PicFrame(ImDrawListPtr dl, Vector2 a, float size, string key, Presence p, MsnScheme sk)
        {
            var b = a + new Vector2(size, size + 12);
            Pane(dl, a, b, sk, Paper, 7);
            GradA(dl, a + new Vector2(1, 1), new Vector2(b.X - 1, a.Y + size * 0.5f), sk.Soft, 0.9f, Hi, 0f);
            int rim = p == Presence.Blocked ? Busy : PresenceCol(p);
            var pa = a + new Vector2(5, 5);
            var pb = pa + new Vector2(size - 10, size - 10);
            dl.AddRectFilled(pa - new Vector2(2, 2), pb + new Vector2(2, 2), U(rim), 4);
            DisplayPic(dl, pa, size - 10, key);
            DropArrow(dl, new Vector2(b.X - 10, b.Y - 6));
        }

        // The bottom of an MSN 7.5 conversation in one pane: the formatting toolbar (font, emoticons, winks, nudge)
        // on a tinted strip, the typing box under it and the Send button beside, none of it does anything: the
        // leader answers for the town; we read along.
        void ComposeBox(ImDrawListPtr dl, Vector2 a, Vector2 b, float tbH, MsnScheme sk)
        {
            float line = ImGui.GetTextLineHeight();
            Pane(dl, a, b, sk);
            // Toolbar: a tinted strip with small buttons, each a picture and a drop-down arrow.
            GradA(dl, a + new Vector2(1, 1), new Vector2(b.X - 1, a.Y + tbH), Lighten(sk.Base, 0.94f), 1, Lighten(sk.Base, 0.84f), 1);
            dl.AddLine(new Vector2(a.X + 1, a.Y + tbH), new Vector2(b.X - 1, a.Y + tbH), U(sk.Edge));
            float x = a.X + 8, cy = a.Y + tbH / 2;
            void Arrow() { dl.AddTriangleFilled(new Vector2(x, cy - 1), new Vector2(x + 6, cy - 1), new Vector2(x + 3, cy + 2), U(0x404040)); x += 14; }
            BoldText(dl, Px(x, cy - line / 2), 0x1f4fa0, "A");
            dl.AddRectFilled(new Vector2(x, cy + line / 2 - 2), new Vector2(x + BoldW("A"), cy + line / 2), U(0xd02020));
            x += BoldW("A") + 3; Arrow();
            foreach (var code in new[] { ":)", ";)" }) { Rich(dl, new Vector2(x, cy - line / 2), 0, code, Ink); x += line + 5; Arrow(); }
            // Nudge: a little window with shake lines.
            var na = Px(x, cy - line * 0.35f);
            dl.AddRectFilled(na, na + new Vector2(line * 0.8f, line * 0.7f), U(sk.Base), 2);
            dl.AddRectFilled(na + new Vector2(2, 4), na + new Vector2(line * 0.8f - 2, line * 0.7f - 2), U(Paper));
            dl.AddLine(na + new Vector2(-4, 2), na + new Vector2(-4, line * 0.6f), U(sk.Deep), 1);
            dl.AddLine(na + new Vector2(line * 0.8f + 3, 2), na + new Vector2(line * 0.8f + 3, line * 0.6f), U(sk.Deep), 1);

            // The typing area (greyed text) and the glossy Send button on its right, inside the same pane.
            float sendW = MathF.Max(64, ImGui.CalcTextSize("Send").X + 24);
            var ia = new Vector2(a.X + 1, a.Y + tbH + 1);
            var ib = new Vector2(b.X - sendW - 12, b.Y - 1);
            dl.PushClipRect(ia, ib, true);
            Rich(dl, ia + new Vector2(6, 4), ib.X - ia.X - 12, "Reading along: the leader answers for the town.", 0xa0a0a0);
            dl.PopClipRect();
            var sa = new Vector2(b.X - sendW - 6, ia.Y + 6);
            var sb = new Vector2(b.X - 6, MathF.Min(b.Y - 6, sa.Y + line * 2 + 8));
            GlossButton(dl, sa, sb, "Send", sk);
        }

        // MSN's sign-in page: the green and blue buddies chasing each other round in a circle, "Signing in..."
        // and our address under it, on a white-to-blue page.
        void SigningIn(ImDrawListPtr dl, Vector2 a, Vector2 b, double now)
        {
            Grad(dl, a, b, Paper, Lighten(LeaderScheme().Base, 0.72f));
            float line = ImGui.GetTextLineHeight();
            var c = new Vector2(MathF.Floor((a.X + b.X) / 2), MathF.Floor(a.Y + (b.Y - a.Y) * 0.38f));
            float px = MathF.Max(4, MathF.Floor((b.X - a.X) / 70));
            float rad = px * 7;
            double ang = now * 4.2;
            for (int i = 0; i < 2; i++)
            {
                double t = ang + i * Math.PI;
                var at = Px(c.X + (float)Math.Cos(t) * rad - px * 3.5f, c.Y + (float)Math.Sin(t) * rad * 0.55f - px * 4);
                BuddyIcon(dl, at, px, i == 0 ? Presence.Online : Presence.Offline);
                if (i == 1) Art(dl, at, px, Buddy, ch => ch == 'h' ? 0x8fb4ee : 0x2a5fc8);   // MSN's blue buddy
            }
            string s = "Signing in...";
            BoldText(dl, Px(c.X - BoldW(s) / 2, c.Y + rad + px * 6), Navy, s);
            string who = $"{sim.MeName} <{Sim.Slug(sim.Name)}@{Sim.Slug(sim.Name)}.town>";
            var ws = ImGui.CalcTextSize(who);
            dl.AddText(Px(c.X - ws.X / 2, c.Y + rad + px * 6 + line + 6), U(Shade), who);
        }

        // A made-up address for the To: line, like <crown@ashford.town>.
        string Address(Row r)
        {
            static string Slug(string s) => Sim.Slug(s);
            string home = Slug(sim.Name) + ".town";
            if (r.Key == "bot") return $"<chronicle@{home}>";
            if (r.Key == Sim.CrierKey) return $"<crier@{home}>";
            if (r.Key.StartsWith("npc:")) return $"<{r.Key.Split(':')[1]}@{home}>";
            if (r.Key.StartsWith("pal:")) return $"<{Slug(r.Title)}@{home}>";
            if (r.Key.StartsWith("gos:")) return "<group chat>";
            var s = r.Key.StartsWith("sis:") ? sim.Sisters.FirstOrDefault(x => "sis:" + x.Name == r.Key) : null;
            return s != null ? $"<{sim.HandleOf(s)}.town>" : r.Title.Contains('@') ? $"<{r.Title}.town>" : $"<{Slug(r.Head.Length > 0 ? r.Head : r.Title)}.town>";
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
                if (open) dl.AddTriangleFilled(c + new Vector2(-4, -2), c + new Vector2(4, -2), c + new Vector2(0, 3), U(skin.Deep));
                else dl.AddTriangleFilled(c + new Vector2(-2, -4), c + new Vector2(-2, 4), c + new Vector2(3, 0), U(skin.Deep));
                int on = gr.Count(r => r.P != Presence.Offline);
                BoldText(dl, Px(p.X + 16, p.Y + (rowH - line) / 2), skin.Deep, $"{g} ({on}/{gr.Count})");
                if (!open) continue;
                foreach (var r in gr)
                {
                    p = ImGui.GetCursorScreenPos();
                    bool selRow = r.Key == selKey;
                    if (ImGui.InvisibleButton("r" + r.Key, new Vector2(w, rowH))) Open(r.Key);
                    bool hot = ImGui.IsItemHovered();
                    var ra = p;
                    var rb = p + new Vector2(w, rowH);
                    if (selRow) dl.AddRectFilled(ra, rb, U(skin.Sel), 3);
                    else if (hot) dl.AddRectFilled(ra, rb, U(skin.Soft), 3);
                    BuddyIcon(dl, Px(p.X + 18, p.Y + (rowH - 8 * bpx) / 2), bpx, r.P);
                    float tx = p.X + 18 + 7 * bpx + 6, ty = p.Y + (rowH - line) / 2;
                    dl.PushClipRect(ra, rb, true);
                    int ink = selRow ? Hi : Ink, grey = selRow ? Lighten(skin.Base, 0.75f) : Shade;
                    string name = r.Unread > 0 ? $"{r.Title} ({r.Unread})" : r.Title;
                    if (r.Unread > 0) BoldText(dl, Px(tx, ty), ink, name); else dl.AddText(Px(tx, ty), U(r.P == Presence.Offline ? grey : ink), name);
                    tx += r.Unread > 0 ? BoldW(name) : ImGui.CalcTextSize(name).X;
                    if (r.P != Presence.Online) { string st = $" ({PresenceWord(r.P)})"; dl.AddText(Px(tx, ty), U(grey), st); tx += ImGui.CalcTextSize(st).X; }
                    if (r.Msg.Length > 0)
                    {
                        dl.AddText(Px(tx, ty), U(grey), " - ");
                        Rich(dl, new Vector2(tx + ImGui.CalcTextSize(" - ").X, ty), 0, r.Msg, grey);
                    }
                    dl.PopClipRect();
                    // A sister town gets the full "why she feels this way" tip; everyone else the short one.
                    if (hot && r.Key.StartsWith("sis:") && sim.Sisters.FirstOrDefault(x => "sis:" + x.Name == r.Key) is { } hs) SisterTip(hs);
                    else if (hot) Tip($"{r.Title} ({PresenceWord(r.P)})\n{(r.Info.Length > 0 ? r.Info + "\n" : "")}{r.Msg}");
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
                    // "- date | town, day N, season | text | ~Writer (Trait)": the middle part and the writer head each
                    // new day or new hand; the line is drawn in the writer's font (lines from before 0.69 are unsigned).
                    var parts = l.Split(" | ").ToList();
                    string writer = "", voice = "";
                    if (parts.Count >= 4 && parts[^1].StartsWith('~') && parts[^1].EndsWith(')') && parts[^1].LastIndexOf(" (") is int op and > 0)
                    {
                        writer = parts[^1][1..op];
                        voice = parts[^1][(op + 2)..^1];
                        parts.RemoveAt(parts.Count - 1);
                    }
                    string head = parts.Count >= 3 ? parts[1] : "";
                    string text = parts.Count >= 3 ? string.Join(" | ", parts.Skip(2)) : l;
                    string top = $"{(writer != "" ? writer + " writes" : "Chronicle says")} ({head}):";
                    if (top != last) { if (last != "") ImGui.Spacing(); RichItem(top, Shade); last = top; }
                    var tr = voice == "" ? null : Traits.All.FirstOrDefault(t => t.Name == voice);
                    var st = tr == null ? null : Sim.ChatStyles.GetValueOrDefault("t:" + tr.Key);
                    RichItem(text, st?.Col ?? Ink, 12, false, st?.Face);
                }
                foot = sim.Chronicler != null ? $"Written by {sim.ChroniclerName}. Also saved to chronicle.txt (*)" : "Also written to chronicle.txt (*)";
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
                    // MSN with timestamps on: "X says (2:14 PM):"; lines from before 0.74 have no time.
                    if (l.From != from) { RichItem(l.H >= 0 ? $"{l.From} says ({Sim.ClockText(l.H)}):" : $"{l.From} says:", Shade); from = l.From; }
                    // Each line in its writer's own font and colour; lines from before 0.57 have none.
                    var st = Sim.ChatStyles.GetValueOrDefault(l.S);
                    RichItem(l.Text, st?.Col ?? (l.Ours ? Navy : Ink), 12, false, st?.Face);
                }
                var typing = c?.Lines.FirstOrDefault(l => l.Due > sim.TickNo);
                foot = typing != null ? $"{typing.From} is typing a message..."
                    : lines.LastOrDefault(l => !l.Ours) is { } got ? (got.H >= 0 ? $"Last message received at {Sim.ClockText(got.H)} on day {got.Day}." : $"Last message received on day {got.Day}.")
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
                // A chat toast shows the sender's display picture on the left, as MSN 7's did; crier toasts have none.
                float pic = !s.T.Crier && s.T.Key.Length > 0 ? MathF.Floor(line * 2 + 8) : 0;
                float textW = pic > 0 ? bodyW - pic - 8 : bodyW;
                float head = s.T.Title.Length > 0 ? line + 4 : 0;
                float bodyH = MathF.Max(pic, head + Rich(dl, Vector2.Zero, textW, s.T.Text, Ink, false));
                float h = Bd + line + 4 + 6 + bodyH + 6 + Bd;
                // Slide: in from below over 0.25 s, out downward over 0.3 s.
                float k = s.Dying >= 0 ? 1 - (float)Math.Clamp((now - s.Dying) / 0.3, 0, 1) : (float)Math.Clamp((now - s.Born) / 0.25, 0, 1);
                float off = (1 - k) * (h + 8);
                var a = Px(areaB.X - 8 - w, bottom - h + off);
                var b = a + new Vector2(w, h);
                // An MSN 7 toast: a small Luna window, white body fading to pale blue.
                XpWindow(dl, a, b, line + 4, s.T.Crier ? $"{sim.Name} crier" : $"{sim.Name} Messenger", false);
                var tA = a + new Vector2(Bd, Bd);
                var tB = new Vector2(b.X - Bd, tA.Y + line + 4);
                Grad(dl, new Vector2(tA.X, tB.Y), b - new Vector2(Bd, Bd), Paper, Lighten(skin.Base, 0.82f));
                var p = new Vector2(tA.X + 8, tB.Y + 6);
                if (pic > 0) { DisplayPic(dl, Px(p.X, p.Y), pic, s.T.Key); p.X += pic + 8; }
                if (s.T.Title.Length > 0) BoldText(dl, Px(p.X, p.Y), s.T.Urgent ? 0xa00000 : Navy, s.T.Title);
                Rich(dl, new Vector2(p.X, p.Y + head), textW, s.T.Text, Ink);
                if (s.Dying < 0 && ImGui.IsMouseHoveringRect(a, b) && !ImGui.IsAnyItemHovered())
                {
                    dl.AddRect(a, b, U(0xffcc33), Corner, ImDrawFlags.RoundCornersTop, 2);
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
