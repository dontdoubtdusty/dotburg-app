namespace Dotburg;

// The comet (0.85, from the mod's Pass 3): about once in two months a comet hangs in the night sky for two days.
// It is only a sight, but people make of it what they are: a town of the pious, the gloomy and doomsayers takes it as
// an ill omen (mood down, doomsayers cry louder, prophets come), anyone else as a wonder. Under a harsh rule or a
// theocracy the crier calls it a sign. Not saved: a comet in the sky is an event in progress.
partial class Sim
{
    const double CometEvery = 60;    // days between comets on average
    double cometUntil = -1e9;
    public int Comets;               // soak counter

    public bool Comet => EvClock < cometUntil;

    // Rolled with the events.
    void MaybeComet()
    {
        if (Comet || EvClock - cometUntil < 20 * DayMs) return;
        if (OncePer(CometEvery)) StartComet();
    }

    public void StartComet()
    {
        cometUntil = EvClock + 2 * DayMs;
        Comets++;
        Say(GovKey == "temple" ? "A comet hangs in the sky. The priests call it a sign."
            : G.Harsh >= 0.5 ? "A comet hangs in the sky. Some call it an omen."
            : CometOmen ? "A comet hangs in the night sky, and half the town calls it an ill omen."
            : "A comet hangs in the night sky.");
    }

    // Does the town read the comet as an ill omen? When three in ten of the grown dot-men are pious, gloomy or doomsayers.
    bool CometOmen
    {
        get
        {
            var adults = Walkers.Where(w => !w.Gone && w.Kind == "" && w.Kid == 0).ToList();
            return adults.Count > 0 && adults.Count(w => Is(w, "pious") || Is(w, "gloomy") || Is(w, "doomsayer")) >= 0.3 * adults.Count;
        }
    }

    // A comet with a fading tail, drifting across the sky over its two days; by night, through clear or thin cloud.
    void DrawComet(double nq)
    {
        if (!Comet || nq < 0.6 || Weather == Wx.Storm || fogAmt >= 0.5 || rainAmt > 0.8) return;
        double t = 1 - (cometUntil - EvClock) / (2 * DayMs);
        int cx = Round(W * 0.2 + t * W * 0.5), cy = Round(H * 0.15);
        for (int k = 6; k >= 0; k--) Blend(cx - k, cy - (k >> 1), 0xf0f6ff, k == 0 ? 0.95 : 0.5 - k * 0.06);
    }
}
