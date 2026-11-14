# Dotburg App File Map

One entry per file: what it does today. Read this first each session, then open only the files the task needs. Update it when a file is added, split, renamed or deleted, and when a file's job changes. Describe the current state only; history lives in the session log.

Line counts are rough (refreshed Nov 13, 2026, version 0.77.0). Files over ~400 lines are marked *. Length only flags a file to look at; a long file with one job is fine.

---

## Code (repo root, flat, namespace `Dotburg`, one `partial class Sim` split by topic)

| File | Lines | What it does |
|---|---|---|
| `Program.cs` | 504* | Window (starts hidden, fitted to the panels after frame 1, then snapped on resize), main loop (fixed 110 ms ticks, pacing by attention), texture upload, test flags (each described in `CLAUDE.md` under its system), `Version`. |
| `Panels.cs` | 1054* | The Windows XP (Luna) UI: window, field, button, progress-bar and bold-text helpers, window fitting (`ChromeSizes`, `FittedSize`, `FitWindow`, `ScaleRange`, `SnapWindow`, `RefitHeight`), the frameless maximised window and its own buttons (`FrameOnMaximize`, `WindowButtons`), `DrawUi` layout filling the whole window (town window with government band and the picture, ledger, Messenger), town picture and hover outline, hover tips (building, `MoodTip`, `FoodTip`, `SisterTip`, `ReasonRow`), font and theme. |
| `Sim.cs` | 319 | `Walker` and `Works` classes; sim tuning, state, helpers (`Round`, `Hash`, `Mix`, `Tint`), day and night, seasons, crier (`Say`, `Chatter`) and chronicle queue (`Note`, `Major`), `Tick`, `FreshRegion`. |
| `Layout.cs` | 186 | Roads and lots, `Build`, `DoorOf`, `Entrance`, `PaintStatic` (buildings, banners, statues, street lamps by tier into the static picture). |
| `Walkers.cs` | 249 | Dot-men: `NewWalker`, `SetId`/`Trait01` (habits from a stable id), homes, routes on roads, `Decide`, `Arrive`, `StepWalker`. |
| `Growth.cs` | 187 | Units to buildings, newcomers, upgrades and tiers, scaffolding (`Works`), `Cost`, `HomeCap`, `PopCap`, `Headcount`. |
| `Life.cs` | 364 | Weather (Markov sky by scheme and season), snow cover, puddles, lightning, smoke, cat, ducks, fireflies, birds, aurora, festivals and rallies (rain or snow puts a festival off, `StepFestWeather`). Cosmetic or mood only; not saved except weather amounts. |
| `Gov.cs` | 191 | `Gov` records (nine rules and the hamlet rules: `Govs.All`, `Govs.Hamlet`), `Leader`, `Trait` records and `Traits` (32 traits in heart, work, spirit and wildcard groups, clashes, weights by rule, leader effect columns), names, succession and festival lines. |
| `Politics.cs` | 608* | Rule and leader state, odds helpers (`PerDay`, `Odds`, `OncePer`), Settlers (`FormRule`), `Appoint` (`LeaderFit`), `SetGov`, mood (`UpdateMood`, `MoodParts`, `Nudge`), `Politics()` (revolution, coup, assassination attempts, elections, step down, scandal, reforms, emigration, arrests), `Death` and `Murdered` (succession after a named death), guards. |
| `Region.cs` | 641* | Charter and sister towns, migration, treasury (`Gold`, `Pay`, `Purse`), relations and attitude (`RelParts`, `BaseRel`, `Bump`), envoys, letters, aid, refugees, fairs, spies, caravans, pacts, fates (absorb, collapse), visitors, `Region()` and `Events()`. |
| `Rulers.cs` | 118 | Sister rulers: a lighter leader for every sister (name, age, traits, epithet, regnal number), their deaths and successors. |
| `Disease.cs` | 257 | Illness: `Disease` records (five), `Outbreak`, `Care()`, spread by shared building, severity against immunity, announce, deaths, recovery, legend line, `TestOutbreak`. |
| `Render.cs` | 364 | `Frame()` into `Out` (72x60, 0xRRGGBB): cached lit scenery (`ComposeBase`, `baseKey`, `LampOn`), then moving things (dot-men, guards, visitors, smoke, weather) and food signs (`DrawFoodSigns`). |
| `Health.cs` | 557* | Leader health (RimWorld style): `Part`, `Hurt`, `Cond`, `Body` records; bodies for leaders and former leaders (`EnsureBody`, `Retire`), ages (`YearMs`, `AgeRate`), wounds, bleeding, tending, infection, poison, age complaints (heart, growth, stroke, frailty, falls), `Attempt` (assassination), `BurnBody`, `StepHealth`, death lines with causes (`BodyDies`, `BodyCause`), health words and charts for the panels, test hooks. |
| `Lives.cs` | 134 | Citizen health (0.67): ages, ordinary causes of death and accidents, going home to die, the burial summary every 4 days (`Mourn`, `BurialSummary`). |
| `Bonds.cs` | 264 | Character and bonds (0.68): `Bond`, `CitizenTraits` (three traits from a hash of the id), `Meet` (roommates make grudges and friendships), fading, grudge news, motives against the leader, wildcards (firebug, doomsayer), `PeopleAt`. |
| `Births.cs` | 149 | Weddings and births: `Wedding`, `Birth`, `Population()` (rolled from `Events()`; curfew towns marry 1.5x); children grow up after a day. The youth brigade: `JoinBrigade`, `StepBrigade`, `Release`, `TestBrigade`. |
| `Fires.cs` | 309 | Fires: `Fire` and `Ruin` records, `Fuel` table, `StepFires` (grow, rain, fighters, spread, burn down), ignition (`FireDice`, `Strike`, `Arson`), `SendFighters`, `RebuildRuins`, `FireLine`, `TestFire`. |
| `War.cs` | 276 | War with a sister (soldiers out of the west gate, news, raids, victory or defeat), war between two sisters, plague and famine across the region (`BlightOn`, `StepBlight`), `Struck`, `Refugees` callers, `WarLine`, test hooks. |
| `Food.cs` | 982* | Food and hunger: goods, barn and stores, fields and harvest, mill and bakery, carriers' jobs, eating, starvation, rot, panic and hoarding, queues at counters, rule food policy (`FoodRules`), sales and seed, ledger and hover lines (`GetFoodReport`), `TestHunger`, `SoakFood`. |
| `Info.cs` | 73 | Hover: `RankAtPixel`, `LotBox`, `InfoFor` (building tooltip lines). |
| `Content.cs` | 106 | Data only: build order, sprites, palettes, schemes, seasons, town names. |
| `Messenger.cs` | 500* | Messenger data and talk: `ChatLine`, `Convo`, `Toast`, `Presence`; `Post` (writes each line in its writer's hand), `Tell`, `Gossip`, `Npc`, `SignIn`, `SignOff`, `Pop` (toasts, nudge), `Chatter2`, statuses and personal messages, the dialogue tables (`TheySay`, `WeSay`, `GossipSay`, `NpcSay`, `Mottos`), `TestChat`. |
| `MessengerUi.cs` | 703* | The Messenger panel (sign-in page, me, contact groups, MSN 7.5 conversation), `Rich` text with emoticons, display pictures (photo atlas or pixel portrait, `PicFrame`, `Portrait`) and buddy icons, toasts over the town picture, nudge shake. |
| `Pals.cs` | 284 | The leader's own contacts (0.69): Friends and Blocked from the leader's bonds, the previous leader, `StepPals`, `NewLeader` (how the old leader fell becomes a bond), their chatter and the leader's replies. |
| `Chronicler.cs` | 257 | The chronicler (0.69): who keeps the chronicle (`EnsureChronicler`), their voice and remarks, slant toward or against the leader, crier lines they choose to write down (`MaybeChronicle`). |
| `Routine.cs` | 150 | Routine voices (0.65): the steward's morning report, the baker and the grocer, daily counts and voice caps, `RoutineSay`. |
| `Hands.cs` | 268 | Writing hands (0.76): `Hand` (a person's 2006 typing quirks), `HandFor` (rolled from a seed, leaning on traits), `WriteIn` (applies it to a line, bursts, grave lines plainer), `Styled`, `WriterOf`; the shared emoticon codes `Sim.EmoCodes`; `WriteHandSamples` for `--hands`. |
| `Sounds.cs` | 233 | Messenger sounds (message, nudge, sign-in, chronicle ding, start-up chord, minimise and restore whooshes): loads `resources/sounds` or makes them in code, picks one per frame (`ListenForSounds`), mute (`ToggleMute`, M, `SpeakerButton`). |
| `Restart.cs` | 123 | Restart button in the town title bar, XP confirm dialog, `Restart()` (backup, archived chronicle, new region). |
| `XpText.cs` | 165 | Text drawn by Windows GDI as XP drew it (`GetGlyphOutline`, gasp-table smoothing), copied into the ImGui font atlas as custom glyphs (`Add`, `Paint`). |
| `Storage.cs` | 291 | Save and load (`town.json`, temp file then move), chronicle file and archive (`ArchiveChronicle`), backup before a restart (`Backup`), `DOTBURG_DIR` override for tests. |

## Other files

| File | What it is |
|---|---|
| `CLAUDE.md` | Rules, run and test, gotchas. Loaded every session; kept short. |
| `claude/` | This folder: file map, session log, vision, systems (`systems.md`, how each system works, read by section), next-session plan, versioning rules (a copy of the GitHub root's), instructions slot, and the mechanics tool (`mechanics-page.html`, data block inside; update it with every feature, see CLAUDE.md). |
| `Build Dotburg.bat` | Publishes Release to `..\Dotburg Town` and makes the Desktop and Start menu shortcuts. Run after meaningful changes (close the app first). |
| `DotburgApp.csproj` | .NET 10, Raylib-cs 8, ImGui.NET, rlImgui-cs; copies `resources/`. |
| `resources/` | `dotburg.ico`, `dotburg.png`, `dotburg-512.png` (icon), `make-icon.mjs` (draws it), `fonts/PixelifySans.ttf`. |
| `resources/avatars.png`, `resources/make-photo-avatars.ps1`, `resources/avatar-photos/` | Display pictures (0.75: 64 cells, 8 across, 96 px): 58 Pexels photos and 6 kept cartoons, listed with crop and 2005-style treatment in `avatar-photos/avatars.txt`; the script builds the atlas from that list. |
| `resources/make-avatars.ps1`, `resources/avatar-sheets/` | The old AI icon sheets and the script that cut them (0.57); kept as the source of the 6 cartoon cells. |
| `resources/sounds/` | `type.mp3` (MSN message), `nudge.mp3` (MSN nudge), `newalert.mp3` (Windows XP ding), copied into the build. |
| `resources/add-emoticons.ps1`, `resources/msn-emoticon-sheet.png` | Adds 26 extra emoticons (rows 4 to 6 of the atlas) cut from the owner's MSN screenshot. |
| `resources/make-emoticons.ps1`, `resources/emoticons.png` | Cuts the 40 classic emoticons out of a reference sheet into the atlas the Messenger draws from (PowerShell, System.Drawing). |
| `../dotburg/` | The frozen mod (v0.6.0): `hooks/register.tsx` is the spec for the remaining stage 5 events. |
| `%APPDATA%\Dotburg\` | `town.json` (save), `chronicle.txt` (the chronicle), `chronicles\` (chronicles of towns before a restart), `town-before-restart.json` (backup), `muted` (sound off). |

## Where to find things
- How wars and blights start, run and end: `MaybeWar`, `StepWar`, `StepSisterWar`, `StepBlight` in `War.cs` (called from `Events()` in `Region.cs`); what a struck sister suffers: `Struck`.
- How fires start, spread and are fought: `Fuel` and `StepFires` in `Fires.cs`; how often they start: `FireDice`.
- Event frequencies: the `OncePer(n)` / `Odds(rate)` calls in `Politics()`, `Region()`, `Events()` and `MaybeOutbreak`.
- How deadly or catching an illness is: the `Diseases` table in `Disease.cs`; care levels in `Care()`.
- What a trait does: the `Traits.All` table in `Gov.cs`; how often each rule picks it: `Traits.Weight`; what it does between townsfolk: `Meet` in `Bonds.cs`.
- How a leader dies or ages: `Parts`, `Hurts` and `OldK` at the top of `Health.cs`; assassination weapons and wounds in `Attempt`; the step in `StepHealth`; start ages in `StartAge`. Citizens: `CitOldK` and the accident table in `Lives.cs`.
- Why the mood or a sister's regard is what it is: `MoodParts` in `Politics.cs`, `RelParts` in `Region.cs`.
- Food numbers (hunger, harvest, mill, bakery, rot, stores) and each rule's food policy: the constants and `FoodRules` at the top of `Food.cs`.
- What a rule is like: `Govs.All` in `Gov.cs` (harshness, banner, term, cost, curfew, guards, mood).
- Growth speed and caps: `Costs`, `PopCaps` in `Sim.cs`; `OwnClock` in `Program.cs` (one unit per 20 s).
- Day length, seasons: `DayMs`, `DaysPerSeason` in `Sim.cs`.
- Window frame rates: the `want` line at the top of the main loop in `Program.cs`.
- Panel layout and sizes: `DrawUi` and the size constants at the top of `Panels.cs`; colours in the same place; font in `LoadFont`, ImGui style in `Theme`.
- Window size: `FittedSize` (the panels at a given town scale), `ScaleRange` (monitor and minimum-height limits), `SnapWindow` (resize snapping, 0.3 s delay) in `Panels.cs`; called from the main loop in `Program.cs`.
