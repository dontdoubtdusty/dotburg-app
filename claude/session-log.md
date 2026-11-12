# Session Log

Session count: 2.

**Writing this log:** one row per session, one or two lines: what was built or decided, anything left open. Design decisions go in `vision.md`; the systems themselves are described in `CLAUDE.md`.

**Version:** 0.51.1 (`Version` in `Program.cs`). `major.minor.patch`, see `versioning.md`.

**Open bugs:** none known. Claude can check by `--soak` numbers and `--shot` screenshots, not by watching the app for hours; the owner's play is the real test.

**Committed:** nothing; this folder is not a git repo.

**Published:** 0.51.1 is in `Dotburg Town` (Nov 12).

**Workspace tooling:** `dotnet build -c Release` here; `--shot` works (the PC has a GPU). Tests must not touch the real save: use `--shot` (never saves) or set `DOTBURG_DIR` to a scratch folder. No Python on this PC; use PowerShell or C# for scripts.

---

## Sessions

| # | Date | Version | What happened |
|---|---|---|---|
| 1 | 2026-11-11 | 0.4 to 0.43 | Read Songs of Syx and RimWorld (owner has permission; reference only, nothing copied) for ideas. Added illness (0.41: five diseases by scheme and season, spread by shared building, severity racing immunity, care from church, rule and leader, quarantine under harsh rule) and leader traits (0.42: eight traits shaping odds, mood, care, gold; epithets; heirs inherit). Then 0.43: window pacing by attention (minimised 8.6% to 0.4% of a core), event odds written per day (`OncePer`, `Odds`), stable dot-man ids with habits derived from them, `DOTBURG_DIR` test folder. Created this `claude/` folder from the sleepytime_ambience pattern. Next: births from `Weddings`, then the rest of stage 5. |
| 2 | 2026-11-12 | 0.51.0 to 0.51.1 | (Versions 0.44 to 0.50.2, births to the Win98 look, were built in sessions not logged here; `CLAUDE.md` describes them.) Dropped the teal desktop: the panels fill the window (spare width to the chronicle, spare height round the picture), the window starts hidden and is fitted to the panels at the largest scale the monitor allows (1270x960 at 11x on 1080p), then snaps to the nearest fitted size after a resize; the ledger roll-up refits the height. `--size WxH` test flag. Checked by screenshots and by resizing a test copy from PowerShell. Next: wanderers, feuds, market day, floods, comets. |

## Decisions and why

- **Death from illness is a race, not a roll.** Severity and immunity both climb; care slows severity. Taken from how RimWorld models disease. Makes care visible in outcomes (plague: about 47% of the ill die in a small town without a church, 7 to 16% with one). Rejected: a flat fatality chance per disease (care would only be a multiplier nobody notices).
- **Quarantine under harsh rule.** Cuts spread to 40% but costs mood: harsh rule copes better and is hated for it, which fits the moral range the owner wants.
- **Traits lean by harshness, never fixed by rule.** A party state can still get a Merciful leader, just rarely (2%). Rejected: one trait set per rule (too predictable).
- **Odds per day, not per check.** So changing `PolStepMs` cannot silently change how often events happen. Large odds now saturate (slightly lower peaks for emigration and arrests); accepted.
- **Habits from a hashed id, not saved fields.** One saved number keeps pace, night-owl, lane and illness resistance stable, and these rolls stop shifting the random stream.
- **Pacing by attention, not a lower fixed frame rate.** Full 60 fps only with the mouse over the window, so hover tips stay smooth. Revisit: the hovered case is still about 7.8% of a core (ImGui panels); capping it at 30 would roughly halve that.
- **This folder lives in `dotburg-app`, not the mod.** The mod is frozen at v0.6.0; all new work is in the app.
