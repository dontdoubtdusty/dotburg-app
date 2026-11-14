using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using Raylib_cs;

namespace Dotburg;

// MSN Messenger sounds: a two-note "bloop" for a new message, a rising chime when a contact signs in, a rattle
// for a nudge (an urgent message), and a soft two-note bell when the Chronicle gets a major event.
// Each sound is read from resources/sounds/<name>.wav, .mp3 or .ogg if that file exists (type: the MSN message
// sound, nudge: the MSN nudge, newalert: the Windows XP ding, online: none yet), otherwise it is made in code on start.
// Sounds only listen to the Messenger; they never change the town. Muted state is a file flag next to the save.
public partial class Program
{
    partial class App
    {
        enum Sfx { Message, Online, Nudge, Alert, Startup, Minimize, Restore }
        static readonly string[] SfxFiles = { "type", "online", "nudge", "newalert", "startup", "minimize", "restore" };
        readonly Sound[] sfx = new Sound[7];
        bool audioOn, muted;
        long heardTick = -1;          // chat lines due up to this tick have been heard
        long heardNudge = -1;
        string heardChron = "";
        double lastMsgSound = -10;
        static string MutePath => Path.Combine(Storage.Dir, "muted");

        // Opens the audio device and loads or makes the four sounds. Screenshot runs stay silent.
        void InitSounds()
        {
            Raylib.InitAudioDevice();
            if (!Raylib.IsAudioDeviceReady()) return;
            audioOn = true;
            muted = File.Exists(MutePath);
            string dir = Path.Combine(AppContext.BaseDirectory, "resources", "sounds");
            for (int i = 0; i < sfx.Length; i++)
            {
                string? p = new[] { ".wav", ".mp3", ".ogg" }.Select(e => Path.Combine(dir, SfxFiles[i] + e)).FirstOrDefault(File.Exists);
                sfx[i] = p != null ? Raylib.LoadSound(p) : Synth((Sfx)i);
                if (sfx[i].FrameCount == 0) sfx[i] = Synth((Sfx)i);   // a file Raylib could not read
                Raylib.SetSoundVolume(sfx[i], 0.7f);
            }
        }

        void CloseSounds()
        {
            if (!audioOn) return;
            foreach (var s in sfx) Raylib.UnloadSound(s);
            Raylib.CloseAudioDevice();
        }

        void ToggleMute()
        {
            muted = !muted;
            try { if (muted) { Directory.CreateDirectory(Storage.Dir); File.WriteAllText(MutePath, "Messenger sounds off"); } else File.Delete(MutePath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // Called once a frame before the panels are drawn (they take the toasts): picks at most one sound for what
        // arrived since the last frame, the most important first: nudge, sign-in, message, chronicle.
        void ListenForSounds()
        {
            long now = sim.TickNo;
            string chronLast = chronicleView.Count > 0 ? chronicleView[^1] : "";
            if (heardTick < 0) { heardTick = now; heardNudge = sim.NudgeTick; heardChron = chronLast; return; }

            bool nudge = sim.NudgeTick != heardNudge;
            bool online = sim.Toasts.Any(t => t.Text == "has just signed in.");
            // A message: a line someone else wrote that became visible since the last frame, in a conversation the
            // reader is not looking at right now (MSN stays quiet for the window you are typing in).
            bool reading = panelsOn && Raylib.IsWindowFocused() && !Raylib.IsWindowMinimized();
            bool message = sim.Convos.Any(c => c.Kind != "crier" && !(reading && c.Key == selKey) &&c.Lines.Any(l => !l.Ours && l.Due > heardTick && l.Due <= now));
            bool alert = chronLast != heardChron && !(reading && selKey == "bot");
            heardTick = now; heardNudge = sim.NudgeTick; heardChron = chronLast;

            if (!audioOn || muted) return;
            double t = Raylib.GetTime();
            if (nudge) Play(Sfx.Nudge);
            else if (online) Play(Sfx.Online);
            else if (message && t - lastMsgSound > 1.2) { Play(Sfx.Message); lastMsgSound = t; }
            else if (alert) Play(Sfx.Alert);
        }

        void Play(Sfx s) { if (audioOn && !muted) Raylib.PlaySound(sfx[(int)s]); }

        // A small speaker button for a title bar: click to mute or unmute the Messenger's sounds.
        void SpeakerButton(ImDrawListPtr dl, Vector2 a, float s)
        {
            var b = a + new Vector2(s, s);
            bool hot = !modal && ImGui.IsMouseHoveringRect(a, b, false);
            bool down = hot && ImGui.IsMouseDown(ImGuiMouseButton.Left);
            int top = down ? 0x1a4fc8 : hot ? 0x6aa6ff : 0x3d8cf7, bot = down ? 0x3a7ef0 : hot ? 0x2a74f2 : 0x1659e0;
            dl.AddRectFilled(a, b, U(bot), 3);
            Grad(dl, a + new Vector2(2, 2), new Vector2(b.X - 2, a.Y + s * 0.55f), top, bot);
            dl.AddRect(a, b, U(Hi), 3);
            // The speaker: a box and a cone, then sound waves, or a cross when muted.
            float cy = a.Y + s / 2;
            dl.AddRectFilled(Px(a.X + s * 0.18f, cy - s * 0.1f), Px(a.X + s * 0.32f, cy + s * 0.1f), U(Hi));
            dl.AddTriangleFilled(new Vector2(a.X + s * 0.3f, cy - s * 0.1f), new Vector2(a.X + s * 0.5f, cy - s * 0.28f), new Vector2(a.X + s * 0.5f, cy + s * 0.28f), U(Hi));
            dl.AddTriangleFilled(new Vector2(a.X + s * 0.3f, cy - s * 0.1f), new Vector2(a.X + s * 0.5f, cy + s * 0.28f), new Vector2(a.X + s * 0.3f, cy + s * 0.1f), U(Hi));
            if (muted)
            {
                dl.AddLine(new Vector2(a.X + s * 0.58f, cy - s * 0.14f), new Vector2(a.X + s * 0.84f, cy + s * 0.14f), U(Hi), 2);
                dl.AddLine(new Vector2(a.X + s * 0.58f, cy + s * 0.14f), new Vector2(a.X + s * 0.84f, cy - s * 0.14f), U(Hi), 2);
            }
            else
            {
                var c = new Vector2(a.X + s * 0.46f, cy);
                dl.PathArcTo(c, s * 0.18f, -0.8f, 0.8f, 8); dl.PathStroke(U(Hi), ImDrawFlags.None, 1.6f);
                dl.PathArcTo(c, s * 0.34f, -0.8f, 0.8f, 10); dl.PathStroke(U(Hi), ImDrawFlags.None, 1.6f);
            }
            if (hot) Tip(muted ? "Sounds are off. Click (or press M) to turn them on." : "Sounds are on. Click (or press M) to mute.");
            if (hot && ImGui.IsMouseReleased(ImGuiMouseButton.Left)) ToggleMute();
        }

        // ---------- making the sounds ----------
        const int Rate = 44100;

        // Builds one of the four sounds as 16-bit mono samples and hands it to Raylib.
        static unsafe Sound Synth(Sfx kind)
        {
            float[] buf = kind switch
            {
                // New message: two quick marimba notes going up, the classic "bloo-bleep".
                Sfx.Message => Mix(0.55f, (0f, 784f, 0.28f, 0.8f), (0.085f, 1175f, 0.42f, 0.9f)),
                // Sign-in: a soft rising arpeggio over a breath of noise.
                Sfx.Online => Whoosh(Mix(1.0f, (0.02f, 659f, 0.7f, 0.5f), (0.09f, 831f, 0.7f, 0.5f), (0.16f, 988f, 0.7f, 0.55f), (0.23f, 1319f, 0.9f, 0.6f))),
                // Nudge: a ding, then the window rattling.
                Sfx.Nudge => Rattle(Mix(0.75f, (0f, 1047f, 0.3f, 0.6f))),
                // Start-up, in the spirit of XP's: a warm chord swelling up under a few bell notes, then fading.
                Sfx.Startup => Add(Swell(3.6f, 0.9f, 1.6f, 0.5f, 155.6f, 233.1f, 311.1f, 392f, 466.2f),
                    Mix(3.6f, (0.05f, 622.3f, 0.9f, 0.5f), (0.45f, 932.3f, 0.9f, 0.45f), (0.85f, 1244.5f, 1.2f, 0.4f), (1.35f, 1046.5f, 1.8f, 0.45f))),
                // Minimise and restore: XP's short whooshes, falling and rising.
                Sfx.Minimize => Sweep(0.32f, 3200, 500),
                Sfx.Restore => Sweep(0.32f, 500, 3200),
                // Chronicle: a slow two-note bell coming down.
                _ => Mix(0.9f, (0f, 1319f, 0.6f, 0.55f), (0.16f, 1047f, 0.7f, 0.55f)),
            };
            float peak = buf.Max(MathF.Abs);
            short* data = (short*)Marshal.AllocHGlobal(buf.Length * 2);
            for (int i = 0; i < buf.Length; i++) data[i] = (short)(buf[i] / MathF.Max(peak, 1e-4f) * 0.8f * short.MaxValue);
            var w = new Wave { SampleCount =(uint)buf.Length, SampleRate = Rate, SampleSize = 16, Channels = 1, Data = data };
            var snd = Raylib.LoadSoundFromWave(w);
            Marshal.FreeHGlobal((IntPtr)data);
            return snd;
        }

        // Mallet notes laid over each other: (start s, frequency, decay s, loudness). A quick attack, an
        // exponential fade, and a few inharmonic overtones so it rings like wood or glass rather than a beep.
        static float[] Mix(float len, params (float at, float f, float decay, float amp)[] notes)
        {
            var buf = new float[(int)(len * Rate)];
            foreach (var (at, f, decay, amp) in notes)
                for (int i = (int)(at * Rate); i < buf.Length; i++)
                {
                    float t = (i - at * Rate) / Rate;
                    float env = MathF.Min(1, t / 0.004f) * MathF.Exp(-t / decay * 3);
                    float p = 2 * MathF.PI * f * t;
                    float tone = MathF.Sin(p) + 0.35f * MathF.Sin(p * 2.01f) * MathF.Exp(-t * 18) + 0.18f * MathF.Sin(p * 3.98f) * MathF.Exp(-t * 30);
                    buf[i] += amp * env * tone;
                }
            return buf;
        }

        // A soft synth pad: the given notes as detuned sine pairs, swelling in over attack s, holding, then
        // fading out over release s at the end of len s.
        static float[] Swell(float len, float attack, float release, float amp, params float[] freqs)
        {
            var buf = new float[(int)(len * Rate)];
            for (int i = 0; i < buf.Length; i++)
            {
                float t = (float)i / Rate;
                float env = MathF.Min(1, t / attack) * MathF.Min(1, (len - t) / release);
                float v = 0;
                foreach (float f in freqs) v += MathF.Sin(2 * MathF.PI * f * t) + 0.6f * MathF.Sin(2 * MathF.PI * f * 1.004f * t);
                buf[i] = amp * env * env * v / freqs.Length;
            }
            return buf;
        }

        // Two sounds of the same length or less laid over each other (b into a).
        static float[] Add(float[] a, float[] b)
        {
            for (int i = 0; i < Math.Min(a.Length, b.Length); i++) a[i] += b[i];
            return a;
        }

        // A whoosh: noise through a low-pass filter whose cutoff glides from f0 to f1 Hz, rising then dying away.
        static float[] Sweep(float len, float f0, float f1)
        {
            var buf = new float[(int)(len * Rate)];
            var r = new Random(9);
            float lp1 = 0, lp2 = 0;
            for (int i = 0; i < buf.Length; i++)
            {
                float t = (float)i / buf.Length;
                float f = f0 * MathF.Pow(f1 / f0, t);
                float k = 1 - MathF.Exp(-2 * MathF.PI * f / Rate);
                lp1 += k * ((float)r.NextDouble() * 2 - 1 - lp1);
                lp2 += k * (lp1 - lp2);
                buf[i] = lp2 * MathF.Sin(MathF.PI * t) * MathF.Sin(MathF.PI * t);
            }
            return buf;
        }

        // Adds a soft swell of filtered noise under the first half, like the sign-in sound's breath.
        static float[] Whoosh(float[] buf)
        {
            var r = new Random(5);
            float lp = 0, n = buf.Length * 0.45f;
            for (int i = 0; i < n; i++)
            {
                lp += 0.06f * ((float)r.NextDouble() * 2 - 1 - lp);
                buf[i] += 0.5f * lp * MathF.Sin(MathF.PI * i / n);
            }
            return buf;
        }

        // Adds a low buzzing rattle that shakes and dies away, like the window being shaken.
        static float[] Rattle(float[] buf)
        {
            int from = (int)(0.06f * Rate), len = (int)(0.6f * Rate);
            for (int i = 0; i < len && from + i < buf.Length; i++)
            {
                float t = (float)i / Rate;
                float saw = 2 * (t * 95 % 1) - 1;
                float shake = 0.5f + 0.5f * MathF.Sign(MathF.Sin(2 * MathF.PI * 24 * t));
                buf[from + i] += 0.45f * saw * shake * (1 - t / 0.6f) * MathF.Min(1, t / 0.01f);
            }
            return buf;
        }
    }
}
