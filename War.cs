namespace Dotburg;

// A soldier marched out of the west gate: the walker (gone once they pass the gate) and their colour,
// so they can walk home again when the war ends.
class Soldier { public Walker W = null!; public int C; }

// This town's war with one sister.
class WarState
{
    public string Foe = "";
    public double Until;            // EvClock when the fighting ends
    public List<Soldier> Away = new();
    public int Lost;                // soldiers who will not come home
    public bool Hero;               // a travelling hero marched with them (Wanderers.cs): better odds
}

// Two sisters at war (news only; the loser shrinks).
class SisterWar { public string A = "", B = ""; public double Until; }

// A plague or famine somewhere in the region that spreads from town to town.
class Blight
{
    public string Kind = "plague";  // "plague" or "famine"
    public List<string> Towns = new();
    public double Until;
    public Dictionary<string, int> Dead = new();   // people each sister has buried in it
}

// Stage 5 (part): war and blight. Events in progress are not saved; the soldiers away are saved as if home.
partial class Sim
{
    const int MinPop = 4;                   // war and hunger never take the town below this many dot-men
    const double FamineCost = 1.3;          // everything is dearer in a famine
    const double WarCoolDownMs = DayMs * 5; // days of peace before the next war

    public static bool NoStrife;            // test switch: no new wars or blights (to compare a region without them)
    public WarState? WarOn;
    SisterWar? SisterWarOn;
    public Blight? BlightOn;
    double lastDisaster = -1e9;             // EvClock of the last war raid or blight start
    double warEnded = -1e9;
    double lastDefeat = -1e9;               // EvClock of the last lost war (a cause in the coup line, 0.84)

    public bool Famine => BlightOn?.Kind == "famine" && BlightOn.Towns.Contains(Name);
    bool Calm => Fires.Count == 0 && Ill == null && EvClock - lastDisaster > DayMs * 2;

    // Colours of soldiers that have passed the gate, so a save keeps them (they come home on load).
    public IEnumerable<int> AwayColours => WarOn == null ? Enumerable.Empty<int>() : WarOn.Away.Where(a => a.W.Gone).Select(a => a.C);

    // How hard a sister is hit just now: blight, her own war, or a war with us. Drains her gold, draws our aid.
    double Struck(Sister s) =>
        (BlightOn?.Towns.Contains(s.Name) == true ? 1.2 : 0)
        + (SisterWarOn != null && (SisterWarOn.A == s.Name || SisterWarOn.B == s.Name) ? 1.5 : 0)
        + (WarOn?.Foe == s.Name ? 1.5 : 0);

    // Forget wars and blights that involve a sister who is gone (absorbed or collapsed).
    void ForgetSister(Sister s)
    {
        if (WarOn?.Foe == s.Name) { ReturnSoldiers(WarOn); WarOn = null; warEnded = EvClock; Say($"The war ends as {s.Name} falls."); Npc("captain", "gone", false, s.Name); }
        if (SisterWarOn != null && (SisterWarOn.A == s.Name || SisterWarOn.B == s.Name)) SisterWarOn = null;
        BlightOn?.Towns.Remove(s.Name);
    }

    // The new town after a charter starts at peace; the old town keeps its soldiers.
    void ResetWar() { WarOn = null; SisterWarOn = null; BlightOn = null; lastDisaster = -1e9; warEnded = -1e9; lastDefeat = -1e9; }

    // ---------- war between this town and a sister ----------
    // Harsh towns pick fights, and a harsh neighbour is a reason for one. Rolled with the events.
    void MaybeWar()
    {
        if (NoStrife || WarOn != null || Sisters.Count == 0 || EvClock - warEnded < WarCoolDownMs || Headcount <= 10) return;
        double rate = 4 * (0.002 + 0.015 * G.Harsh + 0.004 * Sisters.Count(s => Govs.Get(s.Gov).Harsh >= 0.8));
        if (Odds(rate)) StartWar();
    }

    // Soldiers march out of the west gate; the battles happen off-screen and the survivors come home.
    public void StartWar()
    {
        if (WarOn != null || Sisters.Count == 0) return;
        var foe = Weighted(Sisters, s => 0.2 + Math.Abs(Govs.Get(s.Gov).Harsh - G.Harsh) + Govs.Get(s.Gov).Harsh);
        bool we = G.Harsh >= Govs.Get(foe.Gov).Harsh;
        var ps = Walkers.Where(w => !w.Gone && !w.Lead && BodyOf(w) == null && w.Kid == 0 && !w.Sick && w.Goal != Goal.Leave && w.Goal != Goal.Taken && w.Goal != Goal.Fight).ToList();
        int n = Math.Max(0, Math.Min(6, Math.Min((int)Math.Floor(ps.Count * 0.12), Headcount - MinPop)));
        var war = new WarState { Foe = foe.Name, Until = EvClock + DayMs * (1 + 2 * Chance()) };
        for (int i = 0; i < n; i++)
        {
            var w = Pick(ps);
            ps.Remove(w);
            SendAway(w, Goal.Leave, "#war");
            war.Away.Add(new Soldier { W = w, C = w.C });
        }
        Bump(foe, -0.4, "war between us");
        WarOn = war;
        Nudge(G.Harsh >= 0.8 ? 0.05 : -0.08, "war declared");
        Say(we ? $"{Name} marches to war against {foe.Name}." : $"{foe.Name} declares war on {Name}.");
        Tell(foe, we ? "warwe" : "warthey", !we);
        Npc("captain", we ? "warwe" : "warthey", !we, foe.Name);
    }

    // Soldiers walk home through the west gate (anyone still on the road out is simply turned round).
    void ReturnSoldiers(WarState war)
    {
        foreach (var s in war.Away)
        {
            s.W.Gone = true;
            SettleColour(s.C);
        }
        war.Away.Clear();
    }

    // News from the front while it lasts, a raid now and then, and the end: odds favour the harsher, bigger side.
    void StepWar()
    {
        var war = WarOn;
        if (war == null) return;
        var foe = SisterNamed(war.Foe);
        if (foe == null) { ReturnSoldiers(war); WarOn = null; warEnded = EvClock; return; }
        if (EvClock < war.Until)
        {
            if (war.Away.Count > 0 && Odds(1.2))
            {
                if (Chance() < 0.4)
                {
                    var s = Pick(war.Away);
                    war.Away.Remove(s);
                    s.W.Gone = true;
                    war.Lost++;
                    Say(Pick(new[] { "News from the front: a soldier will not come home.", "A letter arrives with black edges." }));
                    Npc("captain", "loss");
                }
                else { Say(Pick(new[] { $"Skirmishes on the road to {foe.Name}.", "The front holds." })); Npc("captain", "hold"); }
            }
            // A raid on the town: the devastating turn of a war.
            if (Calm && Odds(0.35))
            {
                lastDisaster = EvClock;
                Arson($"Raiders from {foe.Name} set a building alight!");
                Tell(foe, "raid");
                Npc("captain", "raid");
            }
            return;
        }
        double odds = 0.5 + (G.Harsh - Govs.Get(foe.Gov).Harsh) * 0.2 + (Headcount - foe.Total) * 0.005 + (war.Hero ? 0.15 : 0);
        bool won = Chance() < Clamp(odds, 0.15, 0.85);
        int lost = war.Lost;
        ReturnSoldiers(war);
        WarOn = null;
        warEnded = EvClock;
        if (won)
        {
            int fell = Bury(foe, (int)Math.Ceiling(foe.Total * 0.2), 2);
            Pay(5);
            Nudge(0.1, "victory in war");
            StartFestival($"Victory over {foe.Name}!{(lost > 0 ? $" {lost} did not come home." : "")}{(fell > 0 ? $" {foe.Name} buries {fell}." : "")}");
            Npc("captain", "won");
            Tell(foe, "warwon");
            if (Chance() < 0.4) SisterChange(foe);
            return;
        }
        Pool = 0;
        lastDefeat = EvClock;
        Pay(-6);
        Nudge(-0.15, "defeat in war");
        Say($"Defeat: {foe.Name} wins the war{(lost > 0 ? $", and {lost} did not come home" : "")}.");
        Npc("captain", "lost");
        Tell(foe, "warlost");
        AskAid(false, "war");
        var lw = LeaderWalker;
        if (lw != null && G.Harsh >= 0.5 && Chance() < 0.3) Revolution(lw);
        else if (G.Harsh < 0.5 && Chance() < 0.2) SetGov("junta", $"After the defeat, the army takes over {Name}.", false);
    }

    // ---------- war between two sisters ----------
    // Two harsh-ruled towns fall out; when it ends the loser shrinks, may change its rule, may be absorbed,
    // and its people may arrive here as refugees.
    void StepSisterWar()
    {
        if (SisterWarOn != null)
        {
            if (EvClock < SisterWarOn.Until) return;
            var a = SisterNamed(SisterWarOn.A);
            var b = SisterNamed(SisterWarOn.B);
            SisterWarOn = null;
            if (a == null || b == null) return;
            var (win, lose) = Chance() < 0.5 + (Govs.Get(a.Gov).Harsh - Govs.Get(b.Gov).Harsh) * 0.2 ? (a, b) : (b, a);
            int fell = Bury(lose, (int)Math.Ceiling(lose.Total * 0.2), 2);
            Say($"News: {win.Name} defeats {lose.Name}{(fell > 0 ? $"; {lose.Name} buries {fell}" : "")}.");
            Gossip(win, lose, "warend");
            if (FateReady && lose.Total < win.Total && Chance() < 0.1) Absorb(win, lose);
            else
            {
                if (Chance() < 0.4) SisterChange(lose);
                if (Chance() < 0.5) Refugees(lose, $"{lose.Name} is overrun");
            }
            return;
        }
        var harsh = Sisters.Where(s => Govs.Get(s.Gov).Harsh >= 0.5).ToList();
        if (NoStrife || Sisters.Count < 2 || harsh.Count == 0 || !Odds(0.024 * harsh.Count)) return;
        var x = Pick(harsh);
        var y = Pick(Sisters.Where(s => s != x).ToList());
        SisterWarOn = new SisterWar { A = x.Name, B = y.Name, Until = EvClock + DayMs * (1 + 2 * Chance()) };
        Say($"News: {x.Name} goes to war with {y.Name}.");
        Gossip(x, y, "war");
    }

    // ---------- plague and famine across the region ----------
    int PlagueIndex => Array.FindIndex(Diseases, d => d.Key == "plague");

    // One plague or famine at a time starts in a random town and creeps from town to town.
    void StepBlight()
    {
        var towns = Sisters.Select(s => s.Name).Prepend(Name).ToList();
        if (BlightOn == null)
        {
            if (NoStrife || !Calm) return;
            // Plague likes winter and damp; famine likes dry spells and hard winters.
            bool wetSky = Weather == Wx.Rain || Weather == Wx.Storm || wet > 0.4;
            double plague = 0.025 * (SeasonNo == 3 ? 2 : 1) * (wetSky ? 1.5 : 1);
            double famine = 0.025 * (dryDays >= 2 ? 3 : SeasonNo == 3 ? 2 : 0.4);
            string kind = Odds(plague) ? "plague" : Odds(famine) ? "famine" : "";
            if (kind == "") return;
            var cand = Walkers.Count(w => !w.Gone) >= MinPop ? towns : towns.Where(t => t != Name).ToList();
            if (cand.Count == 0) return;
            string start = Pick(cand);
            BlightOn = new Blight { Kind = kind, Towns = new() { start }, Until = EvClock + DayMs * (3 + 3 * Chance()) };
            lastDisaster = EvClock;
            if (start == Name && kind == "plague") CatchPlague(false);
            else if (start == Name) { Say($"Famine in {Name}."); Hear(0.3); if (AskAid(false, "hungry")) AidGrain(); }
            else { Say(kind == "plague" ? $"Plague breaks out in {start}." : $"Famine in {start}."); if (kind == "famine") Hear(0.1); Tell(SisterNamed(start), kind); }
            return;
        }
        var b = BlightOn;
        if (EvClock >= b.Until)
        {
            string toll = Buried(b.Dead);
            Say((b.Kind == "plague" ? "The plague burns itself out across the region." : "The famine eases; carts of grain reach every town.") + (toll.Length > 0 ? $" {toll}." : ""));
            BlightOn = null;
            return;
        }
        // It reaches one more town now and then.
        var clean = towns.Where(t => !b.Towns.Contains(t)).ToList();
        if (clean.Count > 0 && Odds(0.6))
        {
            string t = Pick(clean);
            b.Towns.Add(t);
            if (t == Name && b.Kind == "plague") CatchPlague(true);
            else if (t == Name) { Say($"Famine spreads to {Name}."); Hear(0.3); if (AskAid(false, "hungry")) AidGrain(); }
            else { Say(b.Kind == "plague" ? $"The plague reaches {t}." : $"Famine spreads to {t}."); if (b.Kind == "famine") Hear(0.1); Tell(SisterNamed(t), b.Kind); }
        }
        // Sister towns lose people. Here plague runs through the sickness, and famine through the fields: it thins
        // the greens and the growing crop (Food.cs), and the hunger that follows is real.
        foreach (var s in Sisters)
            if (b.Towns.Contains(s.Name) && s.Total > 3 && Odds(b.Kind == "plague" ? 2 : 1.2)) b.Dead[s.Name] = b.Dead.GetValueOrDefault(s.Name) + Bury(s, 1, 3);
    }

    // The plague reaches this town: the grey sickness takes hold (unless another illness already holds it).
    void CatchPlague(bool spread)
    {
        if (Ill == null) StartOutbreak(PlagueIndex);
        if (Ill == null || Diseases[Ill.D].Key != "plague") Say(spread ? $"The plague reaches {Name}." : $"Plague breaks out in {Name}.");
    }

    // Region-wide status for the legend: a war, or a blight this town is not (yet) part of.
    public string WarLine()
    {
        if (WarOn != null) return $"At war with {WarOn.Foe}: {WarOn.Away.Count + WarOn.Lost} sent, {WarOn.Lost} fallen";
        if (Famine) return "Famine: bread is dear";
        if (BlightOn != null && !BlightOn.Towns.Contains(Name)) return $"{(BlightOn.Kind == "plague" ? "Plague" : "Famine")} in {BlightOn.Towns.Count} nearby town{(BlightOn.Towns.Count == 1 ? "" : "s")}";
        return "";
    }

    // For screenshot and soak tests.
    public void ForceBlight(string kind)
    {
        BlightOn = new Blight { Kind = kind, Towns = new() { Name }, Until = EvClock + DayMs * 4 };
        lastDisaster = EvClock;
        if (kind == "plague") CatchPlague(false);
    }
}
