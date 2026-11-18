using System.Diagnostics;

namespace Dotburg;

// Work offline (File menu, 0.89): the town may keep going while the app is closed, for up to 0, 1 or 2 hours.
// Nothing runs in the background: on the next start the time away (capped) is played through behind the
// "Signing in..." page, as fast as the machine allows, before the town is shown.
public partial class Program
{
    partial class App
    {
        public static readonly int[] OfflineChoices = { 0, 1, 2 };
        int offlineHours;            // the player's pick, kept in the "offline" file next to the save
        long catchLeft, catchTotal;  // ticks still to play through on start, and how many there were
        TimeSpan catchAway;          // the time played through (away time, capped)

        static string OfflinePath => Path.Combine(Storage.Dir, "offline");

        // Reads the player's pick; no file means 0 (the town waits while closed).
        void LoadOffline()
        {
            try { if (File.Exists(OfflinePath) && int.TryParse(File.ReadAllText(OfflinePath).Trim(), out int h)) offlineHours = Math.Clamp(h, 0, 2); }
            catch (Exception) { }
        }

        // Stores a new pick from the menu.
        void SetOffline(int hours)
        {
            offlineHours = hours;
            try
            {
                if (hours == 0) File.Delete(OfflinePath);
                else { Directory.CreateDirectory(Storage.Dir); File.WriteAllText(OfflinePath, hours.ToString()); }
            }
            catch (Exception) { }
        }

        // On start: how much of the time away the town plays through.
        void PlanCatchUp(TimeSpan? away)
        {
            if (away == null || offlineHours == 0) return;
            double ms = Math.Min(away.Value.TotalMilliseconds, offlineHours * 3_600_000.0);
            catchTotal = catchLeft = (long)(ms / Sim.TickMs);
            catchAway = TimeSpan.FromMilliseconds(catchLeft * Sim.TickMs);
        }

        // One frame's share of the catch-up: whole ticks, with growth, for up to budgetMs of real time.
        // Toasts and the nudge shake from the time away are dropped when it ends; the chats and chronicle keep it all.
        void StepCatchUp(double budgetMs = 40)
        {
            if (catchLeft <= 0) return;
            var sw = Stopwatch.StartNew();
            while (catchLeft > 0 && sw.Elapsed.TotalMilliseconds < budgetMs)
            {
                catchLeft--;
                sim.Tick(Sim.TickMs);
                unitAcc += power.Earned(Sim.TickMs);
                if (unitAcc >= 1) { sim.Earn(Math.Floor(unitAcc)); unitAcc -= Math.Floor(unitAcc); sim.Grow(); }
            }
            if (catchLeft == 0) { sim.Toasts.Clear(); lastNudge = sim.NudgeTick; }
        }

        // The line under "Signing in..." while catching up.
        string CatchUpLine()
        {
            int pct = catchTotal == 0 ? 100 : (int)(100 * (catchTotal - catchLeft) / catchTotal);
            return $"Catching up on {AwayWords(catchAway)} away: {pct}%";
        }

        static string AwayWords(TimeSpan t)
        {
            int m = (int)Math.Round(t.TotalMinutes);
            return m < 1 ? "under a minute" : m >= 60 ? $"{m / 60} h {m % 60:00} min" : $"{m} min";
        }
    }
}
