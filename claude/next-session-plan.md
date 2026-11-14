# Dotburg: Plan

**One home per fact.** `vision.md` holds the design, `systems.md` how each system works, `CLAUDE.md` the rules. This doc holds, for open steps only, the build sketch and the checks. When a step is built, cut it down to what later steps need.

---

## 1. Next steps, in order

**No scope creep (owner, 2026-11-14).** The next few sessions build the major features already planned below, in order. A new idea that comes up mid-session goes to the mechanics page as an idea and is mentioned to the owner, not built, unless the owner explicitly says to build it now.

1. **Hearsay tooltips** (vision, The player): mood, sister and people tips in words and reported reasons instead of exact numbers. Owner's choice: words, reasons without amounts, only those the leader has heard, some late, missing or wrong; friends, spies and the chronicler hear better.
2. **Council** (vision, Rule and people): design it with the owner first (seats, who appoints, votes, numbers), then build. Comes after the hearsay tooltips because councillors are where the hearsay comes from.
3. Optional: cap the hovered frame rate at 30 if 7.8% of a core while hovering matters.

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
