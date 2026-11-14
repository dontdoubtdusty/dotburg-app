namespace Dotburg;

// Wanderers (0.85, from the mod's Pass 3): now and then a stranger comes in at the west gate, likelier when one is
// needed. A hero beats back a fire or marches out with the soldiers; a hermit brings herbs for the sick; a prophet
// preaches in a plague or under a comet, and the rule decides what becomes of them. Everything they do is seen in
// the street and told by the crier, so the leader knows it the way the town does. Not saved: a wanderer in town is
// an event in progress.
partial class Sim
{
    const double WanderEvery = 90;   // days between visits of each kind when nothing calls for them
    public int Heroes, Prophets, Hermits, Followers;   // soak counters

    bool WandererInTown => Visitors.Any(v => v.Kind is "hero" or "prophet" or "hermit" && !v.Gone);

    // Rolled with the events: at most one wanderer at a time, each kind drawn by what the town is going through.
    void MaybeWanderer()
    {
        if (Lots.Count == 0 || WandererInTown) return;
        int sick = Walkers.Count(w => w.Sick && !w.Gone);
        bool plague = (Ill is { Told: true } && sick >= 2) || (BlightOn?.Kind == "plague" && BlightOn.Towns.Contains(Name));
        double hero = 1 / WanderEvery + (Fires.Count > 0 ? 0.6 : 0) + (WarOn is { Hero: false } ? 1 / 8.0 : 0);
        double hermit = 1 / WanderEvery + (sick >= 2 ? 1 / 12.0 : 0);
        double prophet = 1 / WanderEvery + (plague ? 1 / 15.0 : 0) + (Comet ? 1 / 4.0 : 0) + (Famine ? 1 / 8.0 : 0);
        if (Odds(hero)) Hero();
        else if (Odds(hermit)) Hermit(sick);
        else if (Odds(prophet)) Prophet(plague);
    }

    // A travelling hero: beats back a fire, or marches out with the soldiers in a war, or is just cheered.
    void Hero()
    {
        var v = Visitor("hero", Secs(50, 90));
        if (v == null) return;
        Heroes++;
        if (Fires.Count > 0)
        {
            foreach (var f in Fires) f.Size = Math.Min(f.Size, 0.05);   // nearly out; the bucket line finishes it
            var at = Fires.OrderByDescending(f => f.Size).First();
            if (at.Rank < Lots.Count) { var (dx, dy) = DoorOf(at.Rank); GoTo(v, dx, dy, Goal.Stroll, -1); }
            Nudge(0.05, "a hero beat back the flames");
            Say("A travelling hero runs in through the gate and beats back the flames.");
            Npc("warden", "out", false, "the fire");
            return;
        }
        if (WarOn is { } war && !war.Hero)
        {
            war.Hero = true;
            SendAway(v, Goal.Leave);
            Nudge(0.04, "a hero joined the soldiers");
            Say($"A travelling hero hears of the war and marches after our soldiers to {war.Foe}.");
            return;
        }
        Nudge(0.03, "a hero passing through");
        Say(Pick(new[] { "A travelling hero is cheered at the gates.", "A travelling hero stops for a drink; dotlets follow them about." }));
    }

    // A hermit from the hills: herbs for the sick (they mend faster), or just herbs to trade. Sits by water if there is any.
    void Hermit(int sick)
    {
        var v = Visitor("hermit", Secs(90, 150));
        if (v == null) return;
        Hermits++;
        if (sick > 0)
        {
            foreach (var w in Walkers) if (!w.Gone && w.Dis >= 0) w.Imm = Math.Min(0.99, w.Imm + 0.35);
            Nudge(0.03, "a hermit's herbs");
            Say(sick == 1 ? "A hermit comes down from the hills with herbs for the sick dot-man." : $"A hermit comes down from the hills with herbs for the {sick} sick.");
            return;
        }
        Nudge(0.01, "a hermit's visit");
        Say("A hermit comes down from the hills to trade herbs for bread.");
    }

    // A prophet. Harsh rule drives them out; a theocracy welcomes them; anywhere else they preach, the town's
    // doomsayers and the pious listen, and one of them may follow the prophet away.
    void Prophet(bool plague)
    {
        var v = Visitor("prophet", Secs(50, 90));
        if (v == null) return;
        Prophets++;
        string about = plague ? "the sickness is a judgement" : Comet ? "the comet is a sign" : Famine ? "the empty barns are a judgement" : Pick(new[] { "the end of days is near", "a hard winter is coming" });
        if (G.Harsh >= 0.7 && G.Guards > 0)
        {
            v.Stay = TickNo + Secs(4, 8);
            Nudge(-0.01, "a prophet driven out");
            Say($"A ragged prophet cries that {about}. Guards drive them out of town.");
            return;
        }
        if (GovKey == "temple")
        {
            Nudge(0.04, "the priests welcomed a prophet");
            Say($"The priests welcome a prophet from the hills, who says {about}.");
            return;
        }
        Say($"A prophet preaches in the square that {about}.");
        if (plague || Famine) Hear(0.03);   // talk of judgement frightens the market
        // Who listens: the doomsayers and the pious most, the gloomy and the curious too; the cynical never.
        var ears = Walkers.Where(w => !w.Gone && w.Kind == "" && w.Kid == 0 && !w.Lead && BodyOf(w) == null && SpouseOf(w) == null
                                      && w.Mode != Mode.Walk && w.Goal is not (Goal.Leave or Goal.Taken) && !Is(w, "cynical")).ToList();
        double Pull(Walker w) => Is(w, "doomsayer") ? 4 : Is(w, "pious") ? 2 : Is(w, "gloomy") || Is(w, "curious") ? 1 : 0;
        var keen = ears.Where(w => Pull(w) > 0).ToList();
        if (keen.Count == 0 || Walkers.Count(w => !w.Gone && w.Kind == "") <= MinPop + 2 || Chance() > (plague ? 0.4 : 0.2)) return;
        var f = Weighted(keen, Pull);
        v.Stay = TickNo + Secs(20, 30);
        SendAway(f, Goal.Leave);
        Followers++;
        Nudge(-0.01, "someone followed a prophet");
        Say($"{CitizenName(f)} leaves everything and follows the prophet into the hills.");
    }

    // For screenshots and tests (--event): set one of the stage 5 events going now. Returns what happened, for the console.
    public string ForceEvent(string kind)
    {
        switch (kind)
        {
            case "hero": Hero(); break;
            case "prophet": Prophet(false); break;
            case "hermit": Hermit(Walkers.Count(w => w.Sick && !w.Gone)); break;
            case "market": StartMarket(); break;
            case "comet": StartComet(); break;
            case "flood": ForceFlood(); break;
            case "flooddamage": ForceFlood(); FloodDamage(); break;
            case "feud":
            {
                // The two biggest families fall out, then quarrel at once.
                var fams = Walkers.Where(w => !w.Gone && w.Kind == "" && w.Last != "").GroupBy(w => w.Last).OrderByDescending(g => g.Count()).Take(2).ToList();
                if (fams.Count < 2) return "no two families";
                var f = new Feud { A = fams[0].Key, B = fams[1].Key, Day = DayNumber, Heat = 1.5, Why = "a test" };
                Feuds.Add(f);
                Quarrel(f);
                break;
            }
            default: return $"unknown event {kind}";
        }
        return Crier;
    }
}
