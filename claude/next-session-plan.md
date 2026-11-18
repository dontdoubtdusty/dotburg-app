# Dotburg: Plan

**One home per fact.** `vision.md` holds the design, `systems.md` how each system works, `CLAUDE.md` the rules. This doc holds, for open steps only, the build sketch and the checks. When a step is built, cut it down to what later steps need.

---

## 1. Next steps, in order

**No scope creep (owner, 2026-11-14).** The next few sessions build the major features already planned below, in order. A new idea that comes up mid-session goes to the mechanics page as an idea and is mentioned to the owner, not built, unless the owner explicitly says to build it now.

**Session cycle (owner, 2026-11-15).** Work runs in three kinds of session, each with one job:
- **Build:** one planned feature, to "works and is visible". Run the checks (section 3), bump minor, commit. No polish. Anything noticed along the way goes on a "noticed" list in the session log row, not into the code.
- **Hone:** the feature just built, after the owner has watched it run in the real app for a while. Work only from a hone list written at the start of the session (the noticed list plus what the owner saw). Patch bumps. Anything that adds a mechanic goes to the mechanics page.
- **Bug and health:** after every third build. Long soaks with the stress flags, an old save loaded, `crash.txt` and the chronicle read for oddities, frame rate checked, the known stand-ins cleared. Nothing new is added.
- Rule: a bug fix never carries a new mechanic. If a fix needs a design change, that change becomes a planned step here.
- Rough split: 40% build, 35% hone, 25% bugs. Builds since the last bug session: 3 (hearsay 0.87.0, council 0.88.0, mother town 0.90.0; count resets after each bug session).

1. **Hone: council** (built 0.88.0, `systems.md` Council). Start list: is the accuracy range (0.8 to 1.5 seen) visible in the tips at all; no dismissal happened in four soaks (check the thresholds against real grudge strengths); the meeting walk on the picture (does anyone notice it); reshuffle lines when a new leader comes in the same tick as a rule change; old saves' hashed notables going Offline on load.
2. **Bug and health session** (builds since the last: 2, hearsay and council).
3. Research before designing the larger world (vision, The player): read Dwarf Fortress and RimWorld for how they build the world outside the settlement. The known stand-ins (vanishing patrols, a spy walking out alone) were cleared in the 0.87.2 hone.

## 2. Patterns to follow

- **Timed odds** go through `OncePer(days)` or `Odds(perDay)`, never a raw per-check `Chance() < x`. One-off branches stay `Chance()`.
- **Per-dot habits** that must survive a reload come from `Trait01(w.Id, salt)` with a new salt, not from `Rng`.
- **New saved fields** go in `Storage.SaveData` with a default, so older saves still load. Events in progress are not saved; only lasting results.
- **Crier and chronicle:** `Say` for the crier; a line is chronicled if it matches `Major` in `Sim.cs`, or call `Note` directly.
- **Tests never touch the real save:** `--shot` never saves; anything longer uses `DOTBURG_DIR`.

## 3. Checks after a change

1. `dotnet build -c Release`: no errors, no new warnings.
2. `bin\Release\net10.0\Dotburg.exe --soak 60000 --units 400 --ill`: read `soak.txt` (town line, chronicle, illness table, traits table) for anything implausible.
3. A `--shot` of the feature (`--sick`, `--gov`, `--wx`, `--fest`, `--hover` as fits) and look at it.
4. Raise `Version` (minor for a feature, patch for a tweak, see `versioning.md`); run `Build Dotburg.bat` (app closed).
