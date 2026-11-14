# Dotburg App File Map

One entry per file: what it does today. Read this first each session, then open only the files the task needs. Update it when a file is added, split, renamed or deleted, and when a file's job changes. Describe the current state only; history lives in the session log.

Line counts are rough (refreshed Nov 11, 2026, version 0.48; Program.cs, Panels.cs, Storage.cs and the Messenger files Nov 12, 0.52.0). Files over ~400 lines are marked *. Length only flags a file to look at; a long file with one job is fine.

---

## Code (repo root, flat, namespace `Dotburg`, one `partial class Sim` split by topic)

| File | Lines | What it does |
|---|---|---|
| `Program.cs` | 503 | Window (starts hidden, fitted to the panels after frame 1, then snapped on resize), main loop (fixed 110 ms ticks, pacing by attention), texture upload, test flags (`--shot`, `--size WxH`, `--hands`, `--chat`, `--open`, `--brigade`, `--fire`, `--soak`, `--ill`, `--sick`, `--gov`, `--wx`, `--fest`, `--war`, `--blight`, `--hurt`, `--hurt-tip`, `--health`, `--hover`, `--fresh`, `--units`, `--ticks`), `Version`. |
| `Panels.cs` | 605* | The Windows XP (Luna) UI: window, field, button, progress-bar and bold-text helpers, window fitting (`ChromeSizes`, `FittedSize`, `FitWindow`, `ScaleRange`, `SnapWindow`, `RefitHeight`), `DrawUi` layout filling the whole window (town window with government band and the picture, ledger, Messenger), town picture and hover outline, hover tip, font and theme. |
| `Sim.cs` | 293 | `Walker` and `Works` classes; sim tuning, state, helpers (`Round`, `Hash`, `Mix`, `Tint`), day and night, seasons, crier (`Say`, `Chatter`) and chronicle queue (`Note`, `Major`), `Tick`, `FreshRegion`. |
| `Layout.cs` | 186 | Roads and lots, `Build`, `DoorOf`, `Entrance`, `PaintStatic` (buildings, banners, statues, street lamps by tier into the static picture). |
| `Walkers.cs` | 227 | Dot-men: `NewWalker`, `SetId`/`Trait01` (habits from a stable id), homes, routes on roads, `Decide`, `Arrive`, `StepWalker`. |
| `Growth.cs` | 174 | Units to buildings, newcomers, upgrades and tiers, scaffolding (`Works`), `Cost`, `HomeCap`, `PopCap`, `Headcount`. |
| `Life.cs` | 369 | Weather (Markov sky by scheme and season), snow cover, puddles, lightning, smoke, cat, ducks, fireflies, birds, aurora, festivals and rallies (rain or snow puts a festival off, `StepFestWeather`). Cosmetic or mood only; not saved except weather amounts. |
| `Gov.cs` | 107 | `Gov` records (nine rules), `Leader`, `Trait` records and `Traits` (eight leader traits, clashes, weights), names, succession and festival lines. |
| `Politics.cs` | 442* | Rule and leader state, odds helpers (`PerDay`, `Odds`, `OncePer`), trait helpers, `Appoint`, `SetGov`, mood (`UpdateMood`), `Politics()` (revolution, coup, assassination attempts, elections, step down, scandal, reforms, emigration, arrests), `Death` and `Murdered` (succession after a named death), guards. |
| `Region.cs` | 560* | Charter and sister towns, migration, treasury (`Gold`, `Pay`, `Purse`), relations and attitude, envoys, letters, aid, refugees, fairs, spies, caravans, pacts, fates (absorb, collapse), visitors, `Region()` and `Events()`. |
| `Disease.cs` | 251 | Illness: `Disease` records (five), `Outbreak`, `Care()`, spread by shared building, severity against immunity, announce, deaths, recovery, legend line, `TestOutbreak`. |
| `Render.cs` | 364 | `Frame()` into `Out` (72x60, 0xRRGGBB): cached lit scenery (`ComposeBase`, `baseKey`, `LampOn`), then moving things (dot-men, guards, visitors, smoke, weather). |
| `Health.cs` | 556* | Leader health (RimWorld style): `Part`, `Hurt`, `Cond`, `Body` records; bodies for leaders and former leaders (`EnsureBody`, `Retire`), ages, wounds, bleeding, tending, infection, poison, age complaints (heart, growth, stroke, frailty, falls), `Attempt` (assassination), `BurnBody`, `StepHealth`, death lines with causes (`BodyDies`, `BodyCause`), health words and charts for the panels, test hooks (`TestAttempt`, `TestLifespan`, `TestHurt`). |
| `Births.cs` | 140 | Weddings and births: `Wedding`, `Birth`, `Population()` (rolled from `Events()`; curfew towns marry 1.5x); children grow up after a day. The youth brigade: `JoinBrigade`, `StepBrigade`, `Release` (children march behind guards, walked home at curfew), `TestBrigade`. |
| `Fires.cs` | 296 | Fires: `Fire` and `Ruin` records, `Fuel` table, `StepFires` (grow, rain, fighters, spread, burn down), ignition (`FireDice`, `Strike`, `Arson`), `SendFighters`, `RebuildRuins`, `FireLine`, `TestFire`. |
| `War.cs` | 271 | War with a sister (soldiers out of the west gate, news, raids, victory or defeat), war between two sisters, plague and famine across the region (`BlightOn`, `StepBlight`), `Struck`, `Refugees` callers, `WarLine`, test hooks. |
| `Info.cs` | 66 | Hover: `RankAtPixel`, `LotBox`, `InfoFor` (building tooltip lines). |
| `Content.cs` | 105 | Data only: build order, sprites, palettes, schemes, seasons, town names. |
| `Messenger.cs` | 500 | Messenger data and talk: `ChatLine`, `Convo`, `Toast`, `Presence`; `Post` (writes each line in its writer's hand), `Tell`, `Gossip`, `Npc`, `SignIn`, `SignOff`, `Pop` (toasts, nudge), `Chatter2`, statuses and personal messages, the dialogue tables (`TheySay`, `WeSay`, `GossipSay`, `NpcSay`, `Mottos`), `TestChat`. |
| `MessengerUi.cs` | 701 | The Messenger panel (me, contact groups, conversation), `Rich` text with emoticons, display pictures (photo atlas or pixel portrait, `IconPic`, `Portrait`) and buddy icons, toasts over the town picture, nudge shake. |
| `Sounds.cs` | 230 | Messenger sounds (message, nudge, sign-in, chronicle ding; 0.74: start-up chord, minimise and restore whooshes): loads `resources/sounds` or makes them in code, picks one per frame from what arrived (`ListenForSounds`), mute (`ToggleMute`, M, `SpeakerButton`). |
| `Food.cs` | 816* | Food and hunger: goods, barn and stores, fields and harvest, mill and bakery, carriers' jobs, eating, starvation, rot, panic and hoarding, rule food policy (`FoodRules`), sales and seed, ledger and hover lines, `TestHunger`, `SoakFood`. |
| `Hands.cs` | 268 | Writing hands (0.76): `Hand` (a person's 2006 typing quirks), `HandFor` (rolled from a seed, leaning on traits), `WriteIn` (applies it to a line, bursts, grave lines plainer), `Styled`, `WriterOf`; the shared emoticon codes `Sim.EmoCodes`; `WriteHandSamples` for `--hands`. |
| `Routine.cs` | 147 | Routine voices (0.65): the steward's morning report, the baker and the grocer, daily counts and voice caps, `RoutineSay`. |
| `Lives.cs` | 130 | Citizen health (0.67): ages, ordinary causes of death and accidents, going home to die, the burial summary every 4 days. |
| `Restart.cs` | 119 | Restart button in the town title bar, XP confirm dialog, `Restart()` (backup, new region). |
| `XpText.cs` | 160 | Text drawn by Windows GDI as XP drew it (`GetGlyphOutline`, gasp-table smoothing), copied into the ImGui font atlas as custom glyphs (`Add`, `Paint`). |
| `Storage.cs` | 180 | Save and load (`town.json`, temp file then move), chronicle file, `DOTBURG_DIR` override for tests. |

## Other files

| File | What it is |
|---|---|
| `CLAUDE.md` | Rules and the design notes for every stage and system. Read first. |
| `claude/` | This folder: file map, session log, vision, next-session plan, instructions slot, and the mechanics tool (`mechanics-page.html`, data block inside; update it with every feature, see CLAUDE.md). |
| `Build Dotburg.bat` | Publishes Release to `..\Dotburg Town` and makes the Desktop and Start menu shortcuts. Run after meaningful changes (close the app first). |
| `DotburgApp.csproj` | .NET 10, Raylib-cs 8, ImGui.NET, rlImgui-cs; copies `resources/`. |
| `resources/` | `dotburg.ico`, `dotburg.png`, `dotburg-512.png` (icon), `make-icon.mjs` (draws it), `fonts/PixelifySans.ttf`. |
| `resources/avatars.png`, `resources/make-photo-avatars.ps1`, `resources/avatar-photos/` | Display pictures (0.75: 64 cells, 8 across, 96 px): 58 Pexels photos and 6 kept cartoons, listed with crop and 2005-style treatment in `avatar-photos/avatars.txt`; the script builds the atlas from that list. |
| `resources/make-avatars.ps1`, `resources/avatar-sheets/` | The old AI icon sheets and the script that cut them (0.57); kept as the source of the 6 cartoon cells. |
| `resources/sounds/` | `type.mp3` (MSN message), `nudge.mp3` (MSN nudge), `newalert.mp3` (Windows XP ding), copied into the build. |
| `resources/add-emoticons.ps1`, `resources/msn-emoticon-sheet.png` | Adds 26 extra emoticons (rows 4 to 6 of the atlas) cut from the owner's MSN screenshot. |
| `resources/make-emoticons.ps1`, `resources/emoticons.png` | Cuts the 40 classic emoticons out of a reference sheet into the atlas the Messenger draws from (PowerShell, System.Drawing). |
| `../dotburg/` | The frozen mod (v0.6.0): `hooks/register.tsx` is the spec for the remaining stage 5 events. |
| `%APPDATA%\Dotburg\` | `town.json` (save) and `chronicle.txt` (major events). |

## Where to find things
- How wars and blights start, run and end: `MaybeWar`, `StepWar`, `StepSisterWar`, `StepBlight` in `War.cs` (called from `Events()` in `Region.cs`); what a struck sister suffers: `Struck`.
- How fires start, spread and are fought: `Fuel` and `StepFires` in `Fires.cs`; how often they start: `FireDice`.
- Event frequencies: the `OncePer(n)` / `Odds(rate)` calls in `Politics()`, `Region()`, `Events()` and `MaybeOutbreak`.
- How deadly or catching an illness is: the `Diseases` table in `Disease.cs`; care levels in `Care()`.
- What a trait does: the `Traits.All` table in `Gov.cs`; how often each rule picks it: `Traits.Weight`.
- How a leader dies or ages: `Parts`, `Hurts` and `OldK` at the top of `Health.cs`; assassination weapons and wounds in `Attempt`; the step in `StepHealth`; start ages in `StartAge`.
- Food numbers (hunger, harvest, mill, bakery, rot, stores) and each rule's food policy: the constants and `FoodRules` at the top of `Food.cs`.
- What a rule is like: `Govs.All` in `Gov.cs` (harshness, banner, term, cost, curfew, guards, mood).
- Growth speed and caps: `Costs`, `PopCaps` in `Sim.cs`; `OwnClock` in `Program.cs` (one unit per 20 s).
- Day length, seasons: `DayMs`, `DaysPerSeason` in `Sim.cs`.
- Window frame rates: the `want` line at the top of the main loop in `Program.cs`.
- Panel layout and sizes: `DrawUi` and the size constants at the top of `Panels.cs`; colours in the same place; font in `LoadFont`, ImGui style in `Theme`.
- Window size: `FittedSize` (the panels at a given town scale), `ScaleRange` (monitor and minimum-height limits), `SnapWindow` (resize snapping, 0.3 s delay) in `Panels.cs`; called from the main loop in `Program.cs`.
