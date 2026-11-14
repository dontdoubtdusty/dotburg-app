# Dotburg app: notes for Claude
The shared rules in `D:\My Documents\rpret\GitHub\CLAUDE.md` also apply; this file wins where they disagree.

A standalone C# / .NET 10 / Raylib-cs + ImGui.NET port of the `/town` Claude Code mod in `../dotburg` (same stack as birb-box and srd-desk). The mod was frozen at v0.6.0 and is the spec; since its 0.7.0 (2026-11-14) a few finished app features are ported back to it (list in `../dotburg/CLAUDE.md`, "Ported from the app"). New work still starts in the app. When in doubt, read `../dotburg/hooks/register.tsx` and `../dotburg/CLAUDE.md`. The app is powered by its own clock only (one growth unit per 20 s while open, `OwnClock` in `Program.cs`, behind the `IPowerSource` interface so another source could be added); no Claude link.

## Words
- The townsfolk are dot-men; one is a dot-man or dot-woman; children are dotlets and dotlettes. Use these in every player-facing line (no "mouths", "people" counts or "children"). There is no sex field yet, so a lone child is "a dotlet".

## Version
- `Version` in `Program.cs` is the one version number (window title and `crash.txt`); never write the number anywhere else. Stages 1 to 4 were 0.1 to 0.4.
- Versions follow `claude/versioning.md` (`major.minor.patch`): a finished feature bumps minor, a small tweak bumps patch, in the same change (not at wrap up). Bigger jumps only when the owner says. The Win98 UI pass made it `0.50.0` (0.49 had been bumped in code only). Note the new number in the change summary and in the session log row.
- `README.md` follows `claude/readme-format.md` (user-facing only, Drowse format); convert an old README the next time you work here.
- After raising it, run `Build Dotburg.bat` so the published copy matches.

## The claude/ folder
- At session start, read `claude/file-map.md`, `claude/session-log.md` and `claude/vision.md` before searching the code, then `claude/next-session-plan.md` if the task is the next step. The code wins if they disagree; flag the mismatch.
- When the owner writes "Instructions:", read `claude/instructions-for-claude.md` first.
- At wrap up: update `claude/file-map.md` if files or their jobs changed (and stale line counts); add a dated row to `claude/session-log.md` with the version and add 1 to the session count; update `vision.md` tags for anything built and the `systems.md` section of any system changed; cut finished steps from `next-session-plan.md`. Also add a row to the root `../claude/session-log.md` (the GitHub-wide log) tagged dotburg-app.
- Before writing a helper, check the file map's "Where to find things" and grep; say "reused X" or "no existing equivalent, added Y" in the change summary.
- `vision.md` and the session log record why, not only what: the choice made, what was rejected, what would make us revisit it.

## Run and test
- Normal use: the Dotburg shortcut (Desktop and Start menu), made by `Build Dotburg.bat`, which publishes to `..\Dotburg Town` (not `Dotburg`: that would clash with the `dotburg` mod folder, since Windows ignores case). Run the bat again after every meaningful change (close the app first). For development: `dotnet build -c Release`.
- `--shot out.png [--units N] [--ticks N]`: render a few frames after pre-growing the town and save a screenshot, then exit (never touches the save). `--soak N`: headless run of N ticks, writes `soak.txt` next to the exe. `--fresh`: ignore the save.
- Files: `%APPDATA%\Dotburg\town.json` (save, every 30 s and on exit; on start the town carries on exactly where it was left: no growth while closed, the owner's choice in 0.55 so new features can be watched as they happen) and `chronicle.txt` (major events, appended, plain text).

## Layout (partial class `Sim`)
`Content.cs` data (sprites, palettes, schemes, seasons) / `Sim.cs` state, helpers, day-night, seasons, crier, chronicle queue, `Tick` / `Layout.cs` roads, lots, `PaintStatic` / `Walkers.cs` dot-men / `Growth.cs` units to buildings, upgrades, tiers, scaffolding / `Render.cs` `Frame()` into `Out` (0xRRGGBB, 72x60 px, exactly 7x7 lots) / `Storage.cs` save, chronicle file / `Program.cs` window, main loop, texture upload (integer scale) / `Panels.cs` and `MessengerUi.cs` the XP panels. Every other file is one system; `claude/file-map.md` lists them all.
- Faithful port first: names and numbers follow the TS. `Round` mimics JS `Math.round`; `Hash` mimics the TS `hash`. The RNG is a seeded `Random` (`Sim.Rng`) so runs can be replayed.

## Systems (`claude/systems.md`)
- How each system works (numbers, saved fields, what was measured, test flags) is in `claude/systems.md`, one section per system. Read only the sections the task touches, and update that section in the same change. Not loaded every session on purpose: it is long.
- Stages: 1 to 4 (town, life and weather, rule, region) are done; stage 5 events are done (wanderers, feuds, market day, floods and the comet came last, in 0.85). Illness, food, character and bonds, health and the Messenger go beyond the mod (the owner's choice, shaped by reading Songs of Syx and RimWorld data for reference only: no code or text copied).
- Each stage is checked against the mod (same behaviour, not the same RNG stream) before the next.

## Gotchas
- Buttons drawn on a draw list (not ImGui items: `RestartButton`, `XpPush`, `CaptionButton`, `SpeakerButton`, `WinButton`) must call `IsMouseHoveringRect(a, b, false)`. With the default `clip: true` the test is clipped to the current ImGui window, which outside an `Area` is the hidden fallback window, so the button never sees the mouse (the 0.61.1 restart bug).
- The Messenger, hands, routine voices and the chronicler use `chatRng` only, never `Rng` or `RelOf`, so talk never changes the town (the one exception is `NewLeader` in `Pals.cs`).
- Timed odds go through `OncePer(days)` or `Odds(perDay)`; per-dot habits that must survive a reload come from `Trait01(w.Id, salt)`. More patterns in `claude/next-session-plan.md`.

## Mechanics page (kept up to date)
- `claude/mechanics-page.html` is the mechanic intersectionality tool (shared kit page, kit in the GitHub root `claude/kit/mechanics/`): which systems visibly change which (level 1 flavor, 2 nudge, 3 drives), plus ideas that are not built. Its data is the JSON block between the MECHANICS DATA markers at the top of the file. A local file only (open it in a browser); it is not published as an artifact.
- **Keep it updated** (full rule in the kit README): whenever a feature adds, removes or changes a mechanic or a link, edit the data block in the same change (move a finished idea into `links`, add new mechanics and ideas, bump `updated` and `version`). Check new feature ideas against it first: prefer ones that add links to existing mechanics. Every idea must also pass the two questions under "The player" in `claude/vision.md` (could the leader know this, and how; does it give the player a lever): no levers, and what the leader could not know arrives as hearsay or not at all.

