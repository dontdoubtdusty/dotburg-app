# Session Log

Session count: 3.

**Writing this log:** one row per session, one or two lines: what was built or decided, anything left open. Design decisions go in `vision.md`; the systems themselves are described in `CLAUDE.md`.

**Version:** the number lives only in `Version` in `Program.cs`; see `versioning.md`.

**Testing:** Claude can check by `--soak` numbers and `--shot` screenshots, not by watching the app for hours; the owner's play is the real test. Open bugs are steps in `next-session-plan.md`.

**Committed:** 0.76.0 committed and pushed (32337c7, Nov 13). 0.77.0 and 0.77.1 (window buttons while maximised) are built and published to `Dotburg Town` (Nov 13) but not committed.

**Workspace tooling:** `dotnet build -c Release` here; `--shot` works (the PC has a GPU). Tests must not touch the real save: use `--shot` (never saves) or set `DOTBURG_DIR` to a scratch folder. No Python on this PC; use PowerShell or C# for scripts.

---

## Sessions

| # | Date | Version | What happened |
|---|---|---|---|
| 1 | 2026-11-11 | 0.4 to 0.43 | Read Songs of Syx and RimWorld (owner has permission; reference only, nothing copied) for ideas. Added illness (0.41: five diseases by scheme and season, spread by shared building, severity racing immunity, care from church, rule and leader, quarantine under harsh rule) and leader traits (0.42: eight traits shaping odds, mood, care, gold; epithets; heirs inherit). Then 0.43: window pacing by attention (minimised 8.6% to 0.4% of a core), event odds written per day (`OncePer`, `Odds`), stable dot-man ids with habits derived from them, `DOTBURG_DIR` test folder. Created this `claude/` folder from the sleepytime_ambience pattern. Next: births from `Weddings`, then the rest of stage 5. |
| 2 | 2026-11-12 | 0.51.0 to 0.51.1 | (Versions 0.44 to 0.50.2, births to the Win98 look, were built in sessions not logged here; `systems.md` describes them.) Dropped the teal desktop: the panels fill the window (spare width to the chronicle, spare height round the picture), the window starts hidden and is fitted to the panels at the largest scale the monitor allows (1270x960 at 11x on 1080p), then snaps to the nearest fitted size after a resize; the ledger roll-up refits the height. `--size WxH` test flag. Checked by screenshots and by resizing a test copy from PowerShell. Next: wanderers, feuds, market day, floods, comets. |
| 3 | 2026-11-13 | 0.75.0 to 0.76.0 | (0.52 to 0.74 were built in sessions not logged here; `systems.md` describes them.) 0.75: display pictures are real photos: 42 of the 48 AI icons replaced and 16 new subjects (64 cells), from Pexels (Unsplash blocks scripted access), about half given a 2005 look (flash, blocky JPEG, webcam, tilted print, sepia, black and white, oversaturated, faded, digicam date stamp); 6 cartoons kept (sun, moon, heart, clover, snowflake by choice; horseshoe as no photo was found). Pixel portraits untouched, still 1 in 3. Built by `resources/make-photo-avatars.ps1` from `avatar-photos/avatars.txt`. 0.76: writing hands (`Hands.cs`): everyone who chats, plus sister rulers, types in a 2006 hand of their own, traits lean and chance decides, quirks drop in grave news, chronicle untouched; `--hands` test flag. Next: a horseshoe photo if the owner finds one; fix `--pals` (no leader since Settlers in 0.73); wanderers, feuds, market day, floods, comets. |

## Decisions and why

- **Death from illness is a race, not a roll.** Severity and immunity both climb; care slows severity. Taken from how RimWorld models disease. Makes care visible in outcomes (plague: about 47% of the ill die in a small town without a church, 7 to 16% with one). Rejected: a flat fatality chance per disease (care would only be a multiplier nobody notices).
- **Quarantine under harsh rule.** Cuts spread to 40% but costs mood: harsh rule copes better and is hated for it, which fits the moral range the owner wants.
- **Traits lean by harshness, never fixed by rule.** A party state can still get a Merciful leader, just rarely (2%). Rejected: one trait set per rule (too predictable).
- **Odds per day, not per check.** So changing `PolStepMs` cannot silently change how often events happen. Large odds now saturate (slightly lower peaks for emigration and arrests); accepted.
- **Habits from a hashed id, not saved fields.** One saved number keeps pace, night-owl, lane and illness resistance stable, and these rolls stop shifting the random stream.
- **Pacing by attention, not a lower fixed frame rate.** Full 60 fps only with the mouse over the window, so hover tips stay smooth. Revisit: the hovered case is still about 7.8% of a core (ImGui panels); capping it at 30 would roughly halve that.
- **This folder lives in `dotburg-app`, not the mod.** The mod is frozen at v0.6.0; all new work is in the app.
- **Real photos, inconsistent on purpose (0.75).** The owner wanted the AI cartoon icons gone and asked that the photos not match in tone; some get a 2005 treatment so the list looks like real people chose them. Cartoons kept only where a photo reads worse at 96 px (a smiling sun, a moon with stars, a heart, a clover, a snowflake). Source: Pexels, because Unsplash's search is behind a bot wall. Rejected: cut-out photos on the pastel squares (rough edges, looks pasted). Revisit if photos make the list too busy next to the pixel portraits.
- **Hands lean on traits but chance decides (0.76).** People don't always write the way they act (the owner's point), so a trait only makes a fitting quirk four times as likely; about 1 in 5 type plainly. Grave lines drop the playful quirks so a plague line never reads as a joke. Rejected: the same style every time regardless of the news (funny once, then wrong), and pure chance (traits would mean nothing). The chronicle stays plain because it is the history.
