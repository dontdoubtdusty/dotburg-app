# Dotburg: Plan

**One home per fact.** `vision.md` holds the design, `CLAUDE.md` how each system works. This doc holds, for open steps only, the build sketch and the checks. When a step is built, cut it down to what later steps need.

**Session start:** `file-map.md`, `session-log.md`, `vision.md`, then this plan.

---

## 1. Next steps, in order

1. **Wanderers (a hero could beat back fires), feuds, market day, floods, comets** from the mod's Pass 3. Floods share the weather and seasons links; wanderers can use war and blight (a hero marches with soldiers, a prophet in a plague).
2. Fix `--pals`: it no longer seats a leader since Settlers (0.73), so it shows no friends.
3. Optional: cap the hovered frame rate at 30 if 7.8% of a core while hovering matters.

## 2. Patterns to follow

- **Timed odds** go through `OncePer(days)` or `Odds(perDay)`, never a raw per-check `Chance() < x`. One-off branches stay `Chance()`.
- **Per-dot habits** that must survive a reload come from `Trait01(w.Id, salt)` with a new salt, not from `Rng`.
- **New saved fields** go in `Storage.SaveData` with a default, so older saves still load. Events in progress are not saved; only lasting results.
- **Crier and chronicle:** `Say` for the crier; a line is chronicled if it matches `Major` in `Sim.cs`, or call `Note` directly.
- **Tests never touch the real save:** `--shot` never saves; anything longer uses `DOTBURG_DIR`.

- **Mechanics page:** every feature updates the data block in `claude/mechanics-page.html` (links, ideas, version) and republishes the page.

## 3. Checks after a change

1. `dotnet build -c Release`: no errors, no new warnings.
2. `bin\Release\net10.0\Dotburg.exe --soak 60000 --units 400 --ill`: read `soak.txt` (town line, chronicle, illness table, traits table) for anything implausible.
3. A `--shot` of the feature (`--sick`, `--gov`, `--wx`, `--fest`, `--hover` as fits) and look at it.
4. Raise `Version` (minor for a feature, patch for a tweak, see `versioning.md`); run `Build Dotburg.bat` (app closed).
