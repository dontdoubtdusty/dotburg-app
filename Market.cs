namespace Dotburg;

// Market day (0.85, from the mod's Pass 3): once a town has a shop, every few days stalls go up on the road above
// the centre lot, country traders come in with greens for the shop shelves, stall fees go to the treasury, about
// half the awake dot-men drift over, and a light-fingered one may work the crowd. A merchant guild holds them more
// often, a harsh rule less, and never in a storm or at night. Feuding families meet there and quarrel more.
// Not saved: a market day is an event in progress.
partial class Sim
{
    const double MarketRate = 1.2;           // market days a day, once the gap has passed, by day and in fair weather
    static readonly int[] MarketColors = { 0xd9432f, 0xf5f5f5, 0x2f62c9, 0xf2c14e };
    long marketUntil;
    double lastMarket = -1e12;
    public int MarketsHeld, Pickpockets, PickpocketsCaught;   // soak counters

    public bool MarketOn => TickNo < marketUntil;

    // A spot among the stalls: the road just above the centre lot.
    (int x, int y) MarketSpot()
    {
        var l = Lots[0];
        return (RandInt(l.tx * TileW + 1, (l.tx + 1) * TileW + 1), l.ty * TileH);
    }

    void JoinMarket(Walker w)
    {
        var (x, y) = MarketSpot();
        GoTo(w, x, y, Goal.Fest, -1);
    }

    // Rolled with the events.
    void MaybeMarket()
    {
        if (MarketOn || Festival || Lots.Count == 0 || Daylight() < 0.5 || Stormy) return;
        if (!Built.Take(Lots.Count).Any(t => t is "shop" or "market")) return;
        if (EvClock - lastMarket < (GovKey == "guild" ? 1.5 : 2) * DayMs) return;
        if (Odds(MarketRate * (G.Harsh >= 0.8 ? 0.4 : 1))) StartMarket();
    }

    // Stalls go up; the traders bring greens; people gather.
    public void StartMarket()
    {
        if (Quiet || Lots.Count == 0) return;
        marketUntil = TickNo + Secs(40, 60);
        lastMarket = EvClock;
        MarketsHeld++;
        Nudge(0.03, "market day");
        Pay(GovKey == "guild" ? 2 : 1);   // stall fees
        // Country traders sell greens: the shop shelves fill (less in a famine, when the country is hungry too).
        var sellers = Enumerable.Range(0, Math.Min(Built.Count, Lots.Count)).Where(r => IsSeller(Built[r]) && !Burning(r)).ToList();
        double brought = Famine ? 4 : 14;
        foreach (int r in sellers)
            Stock[r, (int)Good.Greens] = Math.Max(Stock[r, (int)Good.Greens], Math.Min(RoomFor(r, Good.Greens), Stock[r, (int)Good.Greens] + brought / sellers.Count));
        for (int i = 0; i < 2; i++) Visitor("trader", Secs(30, 50));
        Say(GovKey switch
        {
            "guild" => "The guild opens market day: stalls line the main road, and every stall pays its fee.",
            "party" => "A state market: stalls under the Party's eye, prices fixed.",
            "junta" => "Market day, with soldiers at each end of the road.",
            _ => Famine ? "Market day, but the traders bring little: the country is hungry too." : Pick(new[] { "Market day: stalls line the main road.", "The market opens; the square is busy.", "Traders set up stalls along the road." }),
        });
        foreach (var w in Walkers)
        {
            if (w.Sleep || w.Gone || w.Sick || w.Hurt || w.Kind != "" || w.Mode == Mode.Walk || w.Follow != null || Chance() > 0.5) continue;
            if (w.Mode == Mode.Inside && w.At >= 0 && w.At < Lots.Count) (w.X, w.Y) = DoorOf(w.At);
            JoinMarket(w);
        }
        Pickpocket();
    }

    // A light-fingered dot-man works the crowd. Guards may catch them; otherwise only the one robbed knows who it was.
    void Pickpocket()
    {
        var thieves = Walkers.Where(w => !w.Gone && w.Kind == "" && w.Kid == 0 && !w.Lead && !w.Sick && Is(w, "lightfinger")).ToList();
        if (thieves.Count == 0 || Chance() > 0.5) return;
        var t = Pick(thieves);
        var marks = Walkers.Where(w => !w.Gone && w.Kind == "" && w != t && w.Goal == Goal.Fest && !w.Lead).ToList();
        if (marks.Count == 0) return;
        var m = Pick(marks);
        Pickpockets++;
        if (G.Guards > 0 && Chance() < 0.6)
        {
            PickpocketsCaught++;
            Arrest(t, true);
            Say($"Guards catch {CitizenName(t)} with a hand in {CitizenName(m)}'s purse at the market and lead them away.");
            return;
        }
        Pay(-0.5);
        AddBond(m, t, "grudge", 0.3 * GrudgeGain(m), $"{CitizenName(t)} picked their purse on market day");
        Say(Pick(new[] { "Purses go missing at the market.", "Someone's purse is cut at the stalls." }), false);
    }

    // Awning stalls (a shadow below each) on the road above the centre lot, in the rule's colours under a harsh rule.
    void DrawMarket(double nq, double warm)
    {
        if (!MarketOn || Lots.Count == 0) return;
        int x0 = Ox + Lots[0].tx * TileW, y0 = Oy + Lots[0].ty * TileH;
        for (int i = 0; i < 3; i++)
        {
            int c = G.Harsh >= 0.8 ? (i % 2 == 0 ? G.Banner : 0xf5f5f5) : MarketColors[(i + DayNumber) % MarketColors.Length];
            int col = Tint(c, nq, warm);
            int sx = x0 + 1 + i * 3;
            for (int k = 0; k < 2; k++) Blend(sx + k, y0, col, 1);
            Blend(sx, y0 + 1, 0x000000, 0.25);
            Blend(sx + 1, y0 + 1, 0x000000, 0.25);
        }
    }
}
