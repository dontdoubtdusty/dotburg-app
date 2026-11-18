# Dotburg App File Map

One entry per file: what it does today. Read this first each session, then open only the files the task needs. Update it when a file is added, split, renamed or deleted, and when a file's job changes. Describe the current state only; history lives in the session log.

Line counts are rough (refreshed Nov 14, 2026, version 0.86.0; Hearsay.cs added in 0.87.0). Files over ~400 lines are marked *. Length only flags a file to look at; a long file with one job is fine.

---

## Code (repo root, flat, namespace `Dotburg`, one `partial class Sim` split by topic)

| File | Lines | What it does |
|---|---|---|
| `Program.cs` | 612* | Window (starts hidden, fitted to the panels after frame 1, then snapped on resize), main loop (fixed 110 ms ticks, pacing by attention), texture upload, test flags (each described in `CLAUDE.md` under its system), `Version`. |
| `Panels.cs` | 1059* | The Windows XP (Luna) UI: window, field, button, progress-bar and bold-text helpers, window fitting (`ChromeSizes`, `FittedSize`, `FitWindow`, `ScaleRange`, `SnapWindow`, `RefitHeight`), the frameless maximised window and its own buttons (`FrameOnMaximize`, `WindowButtons`), `DrawUi` layout filling the whole window (town window with government band and the picture, ledger, Messenger), town picture and hover outline, hover tips (building, `MoodTip` and `SisterTip` from hearsay with `HeardRow`, `MoodTipTrue` and `SisterTipTrue` for `--truth`, `FoodTip`, `ReasonRow`), font and theme. |
| `Sim.cs` | 340 | `Walker` and `Works` classes; sim tuning, state, helpers (`Round`, `Hash`, `Mix`, `Tint`), day and night, seasons, crier (`Say`, `Chatter`) and chronicle queue (`Note`, `Major`), `Tick`, `FreshRegion`. |
| `Layout.cs` | 195 | Roads and lots, `Build`, `LayLots` (land left out), `DoorOf`, `Entrance`, `PaintStatic` (buildings, banners, statues, street lamps by tier into the static picture). |
| `Land.cs` | 363 | Land (0.82): grove, pond, rocks, marsh tiles; `NewLand`, `PaintLand`, `DrawLand`, effects helpers (`LandBy`, `AcrossGrove`, `StoneK`), hover (`LandInfo`). |
| `Walkers.cs` | 256 | Dot-men: `NewWalker`, `SetId`/`Trait01` (habits from a stable id), homes, routes on roads, `Decide`, `Arrive`, `StepWalker`. |
| `Growth.cs` | 189 | Units to buildings (waiting while every site is open), newcomers, upgrades and tiers, scaffolding (`Works`), `Cost`, `HomeCap`, `PopCap`, `Headcount`. |
| `Life.cs` | 401* | Weather (Markov sky by scheme and season), snow cover, puddles, lightning, smoke, cat, ducks, fireflies, birds, aurora, festivals and rallies (rain or snow puts a festival off, `StepFestWeather`; sickness or hunger calls one off, `FestCalledOff`, 0.81). Cosmetic or mood only; not saved except weather amounts. |
| `Gov.cs` | 177 | `Gov` records (nine rules and the hamlet rules: `Govs.All`, `Govs.Hamlet`), `Leader`, `Trait` records and `Traits` (32 traits in heart, work, spirit and wildcard groups, clashes, weights by rule, leader effect columns), succession and festival lines. |
| `Politics.cs` | 822* | Rule and leader state, odds helpers (`PerDay`, `Odds`, `OncePer`), Settlers (`FormRule`), the village choice (`ChooseGovWhy`, `PeopleDraw`, `LeaderSuits`, `MaybeChooseGov`, 0.84), crisis causes (`CrisisCause`, `TurnTo`), `Appoint` (`LeaderFit`), `SetGov`, mood (`UpdateMood`, `MoodParts`, `Nudge`), `Politics()` (revolution, coup, assassination attempts, elections, step down, scandal, reforms, emigration, arrests), `Death` and `Murdered` (succession after a named death), guards (`StepGuards`: patrols, counter posts while rationing, `Hold` and `TakePrisoner` for arrests). |
| `Region.cs` | 653* | Charter, the mother town (`FoundMother`, `MotherTown`) and sister towns, migration, treasury (`Gold`, `Pay`, `Purse`), relations and attitude (`RelParts`, `BaseRel`, `Bump`), envoys, letters, aid, refugees, fairs, spies, caravans, pacts, fates (absorb, collapse), visitors (traders, wanderers to their spots), `Region()` and `Events()`. |
| `Builders.cs` | 95 | Builders (0.86): `MaxSites`, `SiteFront`, `AtWork`, `StepWorks` (sites advance by the builders at them), `JoinWorks`, `KeepBuilding`, `TestWorks` for `--works`, soak counters. |
| `Wanderers.cs` | 137 | Wanderers (0.85): `MaybeWanderer` (drawn by fires, war, sickness, plague, famine, the comet), `Hero`, `Hermit`, `Prophet` (driven out, welcomed or followed by the rule and the listeners), `ForceEvent` for `--event`. |
| `Feuds.cs` | 197 | Feuds (0.85): `Feud` record (saved), `MaybeFeud` (a deep grudge across families), `StepFeuds` (cooling, quarrels), `Quarrel` (ends by the rule: guards, a hearing, fists, a knife), `WeddingEndsFeud`, `ResetTownEvents`, `RestoreFeuds`, `Fam`. |
| `Market.cs` | 108 | Market day (0.85): `MaybeMarket`, `StartMarket` (fees, traders' greens), `MarketSpot`, `JoinMarket`, `Pickpocket`, `DrawMarket`. |
| `Floods.cs` | 127 | Floods (0.85): `StepFlood` (storms raise the water, it drains after), `floodLevel` per road tile from the land's ponds and marshes, `UnderWater`, `LotFlooded`, `MaybeFloodDamage`, `FloodDamage`, `ForceFlood`. |
| `Sky.cs` | 50 | The comet (0.85): `MaybeComet`, `StartComet`, `CometOmen` (omen or wonder by who lives in town), `DrawComet`. |
| `Rulers.cs` | 118 | Sister rulers: a lighter leader for every sister (name, age, traits, epithet, regnal number), their deaths and successors. |
| `Disease.cs` | 264 | Illness: `Disease` records (five), `Outbreak`, `Care()`, spread by shared building, severity against immunity, announce, deaths, recovery, legend line, `TestOutbreak`. |
| `Render.cs` | 359 | `Frame()` into `Out` (72x60, 0xRRGGBB): cached lit scenery (`ComposeBase`, `baseKey`, `LampOn`), then floodwater on low roads (in the base), market stalls, the comet and moving things (dot-men, guards, visitors, smoke, weather) and food signs (`DrawFoodSigns`). |
| `Health.cs` | 557* | Leader health (RimWorld style): `Part`, `Hurt`, `Cond`, `Body` records; bodies for leaders and former leaders (`EnsureBody`, `Retire`), ages (`YearMs`, `AgeRate`), wounds, bleeding, tending, infection, poison, age complaints (heart, growth, stroke, frailty, falls), `Attempt` (assassination), `BurnBody`, `StepHealth`, death lines with causes (`BodyDies`, `BodyCause`), health words and charts for the panels, test hooks. |
| `Lives.cs` | 134 | Citizen health (0.67): ages, ordinary causes of death and accidents, going home to die, the burial summary every 4 days (`Mourn`, `BurialSummary`). |
| `Bonds.cs` | 290 | Character and bonds (0.68): `Bond`, `Feelings` (hover: spouse, friend, grudge), `CitizenTraits` (three traits from a hash of the id), `Meet` (roommates make grudges and friendships), fading, grudge news, motives against the leader, wildcards (firebug, doomsayer), `PeopleAt`. |
| `Births.cs` | 238 | Weddings and births: `Wedding` (friends marry, strict rules arrange, a neighbour's match), `Marry`, `MoveIn`, `SpouseOf`, `Birth` (to a couple, with a family name), `Population()` (rolled from `Events()`; curfew towns marry 1.5x); children grow up after a day. The youth brigade: `JoinBrigade`, `StepBrigade`, `Release`, `TestBrigade`. |
| `Fires.cs` | 322 | Fires: `Fire` and `Ruin` records, `Fuel` table, `StepFires` (grow, rain, fighters, spread, burn down), ignition (`FireDice`, `Strike`, `Arson`), floodwater damps fires, `SendFighters`, `RebuildRuins`, `FireLine`, `TestFire`. |
| `War.cs` | 279 | War with a sister (soldiers out of the west gate, news, raids, victory or defeat), war between two sisters, plague and famine across the region (`BlightOn`, `StepBlight`), `Struck`, `Refugees` callers, `WarLine`, test hooks. |
| `Food.cs` | 1085* | Food and hunger: goods, barn and stores, fields and harvest, mill and bakery, carriers' jobs, eating, starvation, rot, panic and hoarding, queues at counters, rule food policy (`FoodRules`), sharing (`FriendsTable`, `SharedMeal`, `StreetTable`), sales and seed, ledger and hover lines (`GetFoodReport`), `TestHunger`, `SoakFood`. |
| `Names.cs` | 440* | The name maker (0.78): `Names` (five tongues, `Lean` per town, `Person`, `Given`, `Family`, the lists and generators, checks) and the town side: `Christen` (unique names, kin), `GivenFrom`, `LegacyName`, `RenameDuplicates`, `NameSamples` for `--names`. |
| `Hearsay.cs` | 320 | Hearsay (0.87): `Report`, the leader's reports (`MoodHeard`, `SisHeard`), `StepHearsay`, tellers and delays (`MoodTopic`, `Gossip`, `Delay`), `Dread`, the believed mood and regard (`EstMood`, `HeardMood`, `HeardAttitude`), people known or by repute (`KnownAs`, `Repute`), the spymaster's word (`SpyWord`), `--truth`, soak line. |
| `Council.cs` | 330 | The council (0.88): `Seat`, seats by rule (`SeatFor`, `Turnover`), `Skill`, `CouncilFactor` (bends hearsay), `StepCouncil`, `Reshuffle`, `Revote`, `MaybeDismiss`, `Drop`, `FillSeat` (who sits, snubs), councillor chats (`SeatConvo`, `NotableId`, `SyncCouncilChats`, `CouncilInfo`), the daily meeting walk (`GoToCouncil`), save and soak line. |
| `Info.cs` | 73 | Hover: `RankAtPixel`, `LotBox`, `InfoFor` (building tooltip lines). |
| `Content.cs` | 106 | Data only: build order, sprites, palettes, schemes, seasons, town names. |
| `Messenger.cs` | 517* | Messenger data and talk: `ChatLine`, `Convo`, `Toast`, `Presence`; the Town crier chat (`CrierLine`); `Post` (writes each line in its writer's hand), `Tell`, `Gossip`, `Npc`, `SignIn`, `SignOff`, `Pop` (toasts, nudge), `Chatter2`, statuses and personal messages, the dialogue tables (`TheySay`, `WeSay`, `GossipSay`, `NpcSay`, `Mottos`), `TestChat`. |
| `MsnSkin.cs` | 266 | The Messenger skin (0.83): MSN 7 colour schemes (`MsnSchemes`), the leader's pick (`LeaderScheme`, from traits and rule), the glossy pieces (`SkinBack`, `Pane`, `GlossButton`, `Ball`), the big toolbar (`ToolbarH`, `Toolbar`, `ToolIcon`), `DropArrow`, `Grip`. |
| `MessengerUi.cs` | 733* | The Messenger panel (sign-in page, me, contact groups with the Chronicle and Town crier bots, MSN 7.5 conversation), `Rich` text with emoticons, display pictures (photo atlas or pixel portrait, `PicFrame`, `Portrait`) and buddy icons, toasts over the town picture, nudge shake. |
| `Pals.cs` | 294 | The leader's own contacts (0.69): Friends and Blocked from the leader's bonds, the previous leader, `StepPals`, `NewLeader` (how the old leader fell becomes a bond), their chatter and the leader's replies. |
| `Notables.cs` | 92 | Notables off the council are real dot-men (0.89.1): `Keepers` (baker, grocer, ringleader to dot-man id), `NotableOf`, `Free`, `StepKeepers`, `KeepShop`, `KeepRingleader`, `SyncKeeperChats`, `Flatterer` (the hearsay praise teller), `KeepersLine` for the soak. |
| `Chronicler.cs` | 257 | The chronicler (0.69): who keeps the chronicle (`EnsureChronicler`), their voice and remarks, slant toward or against the leader, crier lines they choose to write down (`MaybeChronicle`). |
| `Routine.cs` | 150 | Routine voices (0.65): the steward's morning report, the baker and the grocer, daily counts and voice caps, `RoutineSay`. |
| `Hands.cs` | 268 | Writing hands (0.76): `Hand` (a person's 2006 typing quirks), `HandFor` (rolled from a seed, leaning on traits), `WriteIn` (applies it to a line, bursts, grave lines plainer), `Styled`, `WriterOf`; the shared emoticon codes `Sim.EmoCodes`; `WriteHandSamples` for `--hands`. |
| `Voices.cs` | 461* | Voices, the dialogue generator (0.79): `VoicePart`, `VoiceBook` (reads `resources/voices.txt`), `Voices` (`Score`, `Pick`, `Message`, `ReplyKey`, `Conversation`, the known tags, events and grave events), the town side (`Book`, `MomentTags`, `SisterTags`, `LeaderTags`, `PostTurns`, `VoiceSister` for `Tell`, `VoiceNpc` for `Npc`, `VoicePal` for the leader's friends and enemies), `WriteVoiceSamples` and `SampleA` for `--voices`. |
| `Sounds.cs` | 233 | Messenger sounds (message, nudge, sign-in, chronicle ding, start-up chord, minimise and restore whooshes): loads `resources/sounds` or makes them in code, picks one per frame (`ListenForSounds`), mute (`ToggleMute`, M, `SpeakerButton`). |
| `Restart.cs` | 123 | Restart button in the town title bar, XP confirm dialog, `Restart()` (backup, archived chronicle, new region). |
| `XpText.cs` | 165 | Text drawn by Windows GDI as XP drew it (`GetGlyphOutline`, gasp-table smoothing), copied into the ImGui font atlas as custom glyphs (`Add`, `Paint`). |
| `Offline.cs` | 75 | Work offline (0.89): the player's 0, 1 or 2 hours (`offline` file), `PlanCatchUp`, `StepCatchUp` (plays the capped time away behind the sign-in page), `CatchUpLine`. |
| `Menus.cs` | 190 | The Messenger menu bar (0.89): File (Work offline submenu, Exit), Help (About Dotburg); `MenuBar`, `MenuLayer`, `DropDown`, `AboutBox`. |
| `Storage.cs` | 304 | Save and load (`town.json`, temp file then move), chronicle file and archive (`ArchiveChronicle`), backup before a restart (`Backup`), `DOTBURG_DIR` override for tests. |

## Other files

| File | What it is |
|---|---|
| `CLAUDE.md` | Rules, run and test, gotchas. Loaded every session; kept short. |
| `claude/` | This folder: file map, session log, vision (plus `voices-vision.md`, the planned dialogue generator), systems (`systems.md`, how each system works, read by section), next-session plan, versioning rules (a copy of the GitHub root's), instructions slot, and the mechanics tool (`mechanics-page.html`, data block inside; update it with every feature, see CLAUDE.md). |
| `Build Dotburg.bat` | Publishes Release to `..\Dotburg Town` and makes the Desktop and Start menu shortcuts. Run after meaningful changes (close the app first). |
| `DotburgApp.csproj` | .NET 10, Raylib-cs 8, ImGui.NET, rlImgui-cs; copies `resources/`. |
| `resources/voices.txt` | The Voices phrase file: tagged hand-written parts (format at its top). Check it with `--voices`. |
| `resources/` | `dotburg.ico`, `dotburg.png`, `dotburg-512.png` (icon), `make-icon.mjs` (draws it), `fonts/PixelifySans.ttf`. |
| `resources/avatars.png`, `resources/make-photo-avatars.ps1`, `resources/avatar-photos/` | Display pictures (0.75: 64 cells, 8 across, 96 px): 58 Pexels photos and 6 kept cartoons, listed with crop and 2005-style treatment in `avatar-photos/avatars.txt`; the script builds the atlas from that list. |
| `resources/make-avatars.ps1`, `resources/avatar-sheets/` | The old AI icon sheets and the script that cut them (0.57); kept as the source of the 6 cartoon cells. |
| `resources/sounds/` | `type.mp3` (MSN message), `nudge.mp3` (MSN nudge), `newalert.mp3` (Windows XP ding), copied into the build. |
| `resources/add-emoticons.ps1`, `resources/msn-emoticon-sheet.png` | Adds 26 extra emoticons (rows 4 to 6 of the atlas) cut from the owner's MSN screenshot. |
| `resources/make-emoticons.ps1`, `resources/emoticons.png` | Cuts the 40 classic emoticons out of a reference sheet into the atlas the Messenger draws from (PowerShell, System.Drawing). |
| `../dotburg/` | The mod (v0.7.0: the 0.6.0 spec plus a few app features ported back): `hooks/register.tsx` is the spec for the remaining stage 5 events. |
| `%APPDATA%\Dotburg\` | `town.json` (save), `chronicle.txt` (the chronicle), `chronicles\` (chronicles of towns before a restart), `town-before-restart.json` (backup), `muted` (sound off). |

## Where to find things
- How wars and blights start, run and end: `MaybeWar`, `StepWar`, `StepSisterWar`, `StepBlight` in `War.cs` (called from `Events()` in `Region.cs`); what a struck sister suffers: `Struck`.
- What someone says in the Messenger: `resources/voices.txt` for events moved to Voices (sister aid, notables, the leader's friends and enemies), else the tables at the bottom of `Messenger.cs`; how a part is picked: `Voices.Score` and `Pick` in `Voices.cs`.
- How fires start, spread and are fought: `Fuel` and `StepFires` in `Fires.cs`; how often they start: `FireDice`.
- Event frequencies: the `OncePer(n)` / `Odds(rate)` calls in `Politics()`, `Region()`, `Events()` and `MaybeOutbreak`.
- How deadly or catching an illness is: the `Diseases` table in `Disease.cs`; care levels in `Care()`.
- What a trait does: the `Traits.All` table in `Gov.cs`; how often each rule picks it: `Traits.Weight`; what it does between townsfolk: `Meet` in `Bonds.cs`.
- How a leader dies or ages: `Parts`, `Hurts` and `OldK` at the top of `Health.cs`; assassination weapons and wounds in `Attempt`; the step in `StepHealth`; start ages in `StartAge`. Citizens: `CitOldK` and the accident table in `Lives.cs`.
- How a village picks its rule and the reason the crier gives: `ChooseGovWhy` and `RuleDraw` in `Politics.cs`; whether the leader stays on: `MaybeChooseGov`.
- How often wanderers, markets, floods and the comet come: `WanderEvery`, `MaybeMarket`, `MaybeFloodDamage`, `CometEvery`; feud numbers: the constants at the top of `Feuds.cs`.
- Why the mood or a sister's regard is what it is: `MoodParts` in `Politics.cs`, `RelParts` in `Region.cs`.
- What the leader is told of it, by whom, how late and how wrong: `MoodTopic`, `Delay`, `Dread` and `NewMoodReport` in `Hearsay.cs`; for people, `KnownAs` and `Repute`.
- Food numbers (hunger, harvest, mill, bakery, rot, stores) and each rule's food policy: the constants and `FoodRules` at the top of `Food.cs`.
- What a rule is like: `Govs.All` in `Gov.cs` (harshness, banner, term, cost, curfew, guards, mood).
- Growth speed and caps: `Costs`, `PopCaps` in `Sim.cs`; `OwnClock` in `Program.cs` (one unit per 20 s).
- Day length, seasons: `DayMs`, `DaysPerSeason` in `Sim.cs`.
- Window frame rates: the `want` line at the top of the main loop in `Program.cs`.
- Panel layout and sizes: `DrawUi` and the size constants at the top of `Panels.cs`; colours in the same place; font in `LoadFont`, ImGui style in `Theme`.
- Window size: `FittedSize` (the panels at a given town scale), `ScaleRange` (monitor and minimum-height limits), `SnapWindow` (resize snapping, 0.3 s delay) in `Panels.cs`; called from the main loop in `Program.cs`.
