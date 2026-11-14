using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;

namespace Dotburg;

// Text drawn the way Windows XP drew it. ImGui normally rasterises fonts with stb_truetype: soft grey edges and no
// hinting, which looks like a modern smoothed screen. Here each glyph is drawn by Windows' own GDI text renderer
// (the one XP used, still in Windows 10) through GetGlyphOutline, then copied into ImGui's font atlas as a custom
// glyph. Like XP's "Standard" font smoothing, a font's gasp table decides per size: grid-fitted black and white
// pixels at text sizes, grey smoothing only where the font asks for it.
// Use: Add() while the fonts are loaded (between rlImGui.BeginInitImGui and EndInitImGui), Paint() after.
static class XpText
{
    // One glyph waiting to be copied into the atlas once ImGui has packed it.
    record Pending(int Rect, int W, int H, byte[] Alpha);
    static readonly List<Pending> pending = new();
    static readonly List<IntPtr> keep = new();   // glyph range arrays ImGui reads at build time

    // The characters each font gets: printable ASCII, Latin-1, and the common typographic marks.
    static readonly int[] Chars = Enumerable.Range(0x20, 0x5f).Concat(Enumerable.Range(0xa0, 0x60))
        .Concat(new[] { 0x2013, 0x2014, 0x2018, 0x2019, 0x201c, 0x201d, 0x2022, 0x2026 }).ToArray();

    // A Windows font file name (as in Messenger.ChatStyles) to its family and style.
    static readonly Dictionary<string, (string Family, bool Bold, bool Italic)> Faces = new()
    {
        ["tahoma"] = ("Tahoma", false, false), ["tahomabd"] = ("Tahoma", true, false),
        ["verdana"] = ("Verdana", false, false), ["verdanab"] = ("Verdana", true, false),
        ["arial"] = ("Arial", false, false), ["arialbd"] = ("Arial", true, false),
        ["comic"] = ("Comic Sans MS", false, false), ["georgia"] = ("Georgia", false, false), ["georgiai"] = ("Georgia", false, true),
        ["cour"] = ("Courier New", false, false), ["courbd"] = ("Courier New", true, false),
        ["pala"] = ("Palatino Linotype", false, false), ["palab"] = ("Palatino Linotype", true, false), ["palai"] = ("Palatino Linotype", false, true),
        ["trebuc"] = ("Trebuchet MS", false, false), ["impact"] = ("Impact", false, false), ["times"] = ("Times New Roman", false, false),
        ["lucon"] = ("Lucida Console", false, false),
    };

    // Off with --stb, to compare against ImGui's own smoothed text.
    public static bool On = !Environment.GetCommandLineArgs().Contains("--stb");

    // Adds the font in fontFile (full path) as an ImGui font of line height size, with every glyph drawn by GDI at
    // cell height cell. Returns null if GDI cannot make it (unknown face, font not installed); the caller then
    // loads the file the usual way.
    public static unsafe ImFontPtr? Add(string fontFile, float size, int cell)
    {
        if (!On) return null;
        string key = Path.GetFileNameWithoutExtension(fontFile).ToLowerInvariant();
        if (!Faces.TryGetValue(key, out var f) || !File.Exists(fontFile)) return null;

        IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
        IntPtr hf = CreateFontW(cell, 0, 0, 0, f.Bold ? 700 : 400, f.Italic ? 1u : 0u, 0, 0, 1, 7, 0, 3, 0, f.Family);
        IntPtr old = SelectObject(dc, hf);
        try
        {
            // Make sure Windows gave us the family we asked for, not a substitute.
            var name = new char[64];
            int n = GetTextFaceW(dc, name.Length, name);
            if (n <= 1 || !new string(name, 0, n - 1).Equals(f.Family, StringComparison.OrdinalIgnoreCase)) return null;
            GetTextMetricsW(dc, out var tm);
            bool gray = WantsGray(dc, tm.Height - tm.InternalLeading);

            // A stand-in ImGui font with one glyph (the euro sign, never drawn here) for the size and line height;
            // the real glyphs are custom rects attached to it.
            IntPtr ranges = Marshal.AllocHGlobal(3 * sizeof(ushort));
            ((ushort*)ranges)[0] = 0x20ac; ((ushort*)ranges)[1] = 0x20ac; ((ushort*)ranges)[2] = 0;
            keep.Add(ranges);
            var io = ImGui.GetIO();
            ImFontPtr font = io.Fonts.AddFontFromFileTTF(fontFile, size, null, ranges);

            var ident = new MAT2 { eM11 = 1 << 16, eM22 = 1 << 16 };
            uint fmt = gray ? 6u : 1u;   // GGO_GRAY8_BITMAP or GGO_BITMAP
            foreach (int ch in Chars)
            {
                uint len = GetGlyphOutlineW(dc, (uint)ch, fmt, out var gm, 0, null, ref ident);
                if (len == uint.MaxValue) continue;
                int w = (int)gm.BlackBoxX, h = (int)gm.BlackBoxY;
                byte[] alpha;
                if (len == 0) { w = h = 1; alpha = new byte[1]; }   // a space: nothing to draw
                else
                {
                    var buf = new byte[len];
                    fixed (byte* p = buf) GetGlyphOutlineW(dc, (uint)ch, fmt, out gm, len, p, ref ident);
                    alpha = new byte[w * h];
                    int pitch = gray ? (w + 3) & ~3 : ((w + 31) / 32) * 4;
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                            alpha[y * w + x] = gray
                                ? (byte)Math.Min(255, buf[y * pitch + x] * 255 / 64)
                                : (buf[y * pitch + x / 8] & (0x80 >> (x % 8))) != 0 ? (byte)255 : (byte)0;
                }
                // GDI measures the glyph from the baseline upwards; ImGui places it from the top of the line.
                var offset = new Vector2(gm.GlyphOriginX, tm.Ascent - gm.GlyphOriginY);
                int rect = io.Fonts.AddCustomRectFontGlyph(font, (ushort)ch, w, h, gm.CellIncX, offset);
                pending.Add(new Pending(rect, w, h, alpha));
            }
            return font;
        }
        finally
        {
            SelectObject(dc, old);
            DeleteObject(hf);
            DeleteDC(dc);
        }
    }

    // Copies the GDI glyphs into the packed atlas (white with the glyph as alpha). Returns true if anything was
    // copied, so the caller re-uploads the font texture.
    public static unsafe bool Paint()
    {
        if (pending.Count == 0) return false;
        var io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out byte* px, out int tw, out int _);
        foreach (var g in pending)
        {
            var r = io.Fonts.GetCustomRectByIndex(g.Rect);
            for (int y = 0; y < g.H; y++)
                for (int x = 0; x < g.W; x++)
                {
                    byte* p = px + ((r.Y + y) * tw + r.X + x) * 4;
                    p[0] = p[1] = p[2] = 255;
                    p[3] = g.Alpha[y * g.W + x];
                }
        }
        pending.Clear();
        return true;
    }

    // XP's "Standard" smoothing: grey edges only at sizes where the font's gasp table sets the gray flag.
    static unsafe bool WantsGray(IntPtr dc, int ppem)
    {
        uint tag = 'g' | ('a' << 8) | ('s' << 16) | ((uint)'p' << 24);
        uint len = GetFontData(dc, tag, 0, null, 0);
        if (len == uint.MaxValue || len < 4) return false;
        var b = new byte[len];
        fixed (byte* p = b) GetFontData(dc, tag, 0, p, len);
        int U16(int i) => (b[i] << 8) | b[i + 1];
        int count = U16(2);
        for (int i = 0; i < count && 4 + i * 4 + 3 < b.Length; i++)
            if (ppem <= U16(4 + i * 4)) return (U16(6 + i * 4) & 2) != 0;
        return false;
    }

    // ---------- GDI ----------
    [StructLayout(LayoutKind.Sequential)] struct MAT2 { public int eM11, eM12, eM21, eM22; }
    [StructLayout(LayoutKind.Sequential)]
    struct GLYPHMETRICS { public uint BlackBoxX, BlackBoxY; public int GlyphOriginX, GlyphOriginY; public short CellIncX, CellIncY; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct TEXTMETRICW
    {
        public int Height, Ascent, Descent, InternalLeading, ExternalLeading, AveCharWidth, MaxCharWidth, Weight, Overhang, DigitizedAspectX, DigitizedAspectY;
        public char FirstChar, LastChar, DefaultChar, BreakChar;
        public byte Italic, Underlined, StruckOut, PitchAndFamily, CharSetId;
    }

    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline,
        uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string face);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern int GetTextFaceW(IntPtr dc, int count, [Out] char[] name);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern bool GetTextMetricsW(IntPtr dc, out TEXTMETRICW tm);
    [DllImport("gdi32.dll")] static extern unsafe uint GetGlyphOutlineW(IntPtr dc, uint ch, uint format, out GLYPHMETRICS gm, uint size, byte* buf, ref MAT2 mat);
    [DllImport("gdi32.dll")] static extern unsafe uint GetFontData(IntPtr dc, uint table, uint offset, byte* buf, uint size);
}
