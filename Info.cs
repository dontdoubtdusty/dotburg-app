namespace Dotburg;

// What the town can tell you about a building when the mouse rests on it.
partial class Sim
{
    // The lot rank under a town pixel, or -1 for roads, countryside and lots with nothing built on them.
    public int RankAtPixel(int px, int py)
    {
        int X = px - Ox, Y = py - Oy;
        if (X < 0 || Y < 0 || X >= Cols * TileW || Y >= RowsT * TileH) return -1;
        if (X % TileW < 2 || Y % TileH < 2) return -1;
        int rank = rankAt[Y / TileH * Cols + X / TileW];
        return rank < Math.Min(Built.Count, Lots.Count) ? rank : -1;
    }

    // The screen box of a lot's building in town pixels (x, y, width, height).
    public (int x, int y, int w, int h) LotBox(int rank) => (Ox + Lots[rank].tx * TileW + 2, Oy + Lots[rank].ty * TileH + 2, TileW - 2, TileH - 2);

    static string Cap(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];

    // Title and detail lines for the building on lot rank.
    public (string title, List<string> lines) InfoFor(int rank)
    {
        var lines = new List<string>();
        string type = Built[rank];
        var works = WorksList.FirstOrDefault(w => w.Rank == rank);
        if (works != null)
        {
            lines.Add(works.Up != null ? $"Being rebuilt as {An(Content.Label(works.To))}." : "Under construction.");
            return (Cap(Content.Label(works.To)), lines);
        }
        var burning = FireAt(rank);
        if (burning != null) lines.Add($"On fire! {Math.Round(burning.Size * 100)}% fierce, {Math.Round(burning.Burn * 100)}% burnt away.");
        var ru = Ruins.FirstOrDefault(x => x.Rank == rank);
        if (ru != null) lines.Add($"What is left of the {Content.Label(ru.Was)}; it will be rebuilt.");
        lines.Add($"Lot {rank + 1} of {Built.Count} built.");

        if (Content.IsHome(type))
        {
            int live = Walkers.Count(w => !w.Gone && !w.Lead && HomeOf(w) == rank);
            int cap = HomeCap(type);
            lines.Add($"Home: {live} of {cap} places filled.");
        }
        int inside = Walkers.Count(w => !w.Gone && w.Mode == Mode.Inside && w.At == rank);
        if (inside > 0) lines.Add($"{inside} dot-men inside now ({Walkers.Count(w => !w.Gone && w.Mode == Mode.Inside && w.At == rank && w.Sleep)} asleep).");
        int outside = Walkers.Count(w => !w.Gone && w.Mode != Mode.Inside && w.At == rank);
        if (outside > 0) lines.Add($"{outside} standing outside.");

        if (rank == SeatRank()) lines.Add($"Seat of government: {G.Name}, led by {LeaderTitle}.");
        else if (Leader != null && LeaderWalker != null && HomeOf(LeaderWalker) == rank) lines.Add($"Home of {LeaderTitle}.");
        if (Leader != null && LeaderWalker != null && HomeOf(LeaderWalker) == rank && TraitWords != "") lines.Add($"{TraitWords}: {TraitBlurb}.");
        int ill = Walkers.Count(w => !w.Gone && w.Sick && HomeOf(w) == rank);
        if (ill > 0) lines.Add(ill == 1 ? "Someone is ill in bed here." : $"{ill} are ill in bed here.");

        if (Content.Outdoor.Contains(type)) lines.Add("Open air: people stand beside it.");
        if (type == "park")
        {
            if (GovKey == "party" || Honoured > Built.Take(rank).Count(t => t == "park")) lines.Add("A statue stands by the pond.");
            if (SeasonNo == 3) lines.Add("The ducks have gone for winter.");
        }
        if (Chimney.ContainsKey(type)) lines.Add(Weather == Wx.Snow || cover > 0.3 ? "Smoke from the chimney keeps out the cold." : "Has a chimney.");

        double p = Phase;
        if (Daylight() < 0.6) lines.Add(LightsOn(rank, p) ? "Windows are lit." : "Dark; everyone is in bed.");

        var up = UpgradeFor(rank);
        if (up != null) lines.Add($"Next: becomes {An(Content.Label(up))}.");
        return (Cap(Content.Label(type)), lines);
    }
}
