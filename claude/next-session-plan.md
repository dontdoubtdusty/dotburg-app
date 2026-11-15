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
- Rough split: 40% build, 35% hone, 25% bugs. Builds since the last bug session: 1 (hearsay, 0.87.0; count resets after each bug session).

1. **Hone: hearsay tooltips** (built in 0.87.0; how it works in `systems.md`, Hearsay). Watch it in the real app first. Noticed during the build:
   - The believed mood can sit far from the truth: a spiteful sovereign believed 59 when it was 42 (soak). Intended, but check it does not read as a bug; maybe cap the bias or let a crisis (riot, coup attempt) snap the estimate back.
   - Sister tips are thin (often one or two reasons) and every sister reason arrives "21 hours ago" after a pre-grown start.
   - Strangers with no repute and no deep feelings show only a name and age; check a full house reads well.
   - The feared band (dread 0.66+) has not been measured yet: the soak's town never had one. Run soaks until one does, or add a test flag.
   - "Lately" rows still come from everyone's eyes (no teller); fine for public events, but some may deserve one.
3. **Build: council** (vision, Rule and people): a councillor is a named source of information plus rare stories; four seats decided (2026-11-15, see vision). Settle the rest with the owner first (who appoints, numbers), then build. Comes after the hearsay tooltips because councillors are where the hearsay comes from.
4. **Hone: council.**
5. **Bug and health session.** Known stand-ins to clear: patrols trimmed by a rule change vanish; a caught spy is not walked out.
6. Optional: cap the hovered frame rate at 30 if 7.8% of a core while hovering matters.

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
