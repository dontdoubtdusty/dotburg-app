# Dotburg Vision

Status tags: [Built] [In progress] [Planned] [Undecided]. This doc describes the app, not the builds: when something is built, change its tag and keep the description current. How each system works in detail is in `CLAUDE.md`.

## The concept
A quaint top-down pixel town of dot-men that grows on its own while the app is open, with a day and night, seasons, weather, governments and a region of sister towns. Something to glance at, not to play: no buttons that steer the town. It began as the `/town` mod for Claude Code (`../dotburg`, frozen at v0.6.0), which is the spec for the port.

## Tone
- Towns range from egalitarian to harsh, even fascist; not all friendly utopias. Harsh rule is shown, not dwelt on: curfews, guards, rallies, people fleeing, "someone is taken away". No purges or crackdowns that empty the streets.
- Real setbacks are allowed (plague, fire), but rare.
- No emojis anywhere; plain characters in the legend.

## The town
- [Built] 7x7 lots, roads, buildings filling from the middle; tiers Hamlet, Village, Town, City with upgrades in place.
- [Built] Day and night (6 minutes), four seasons of four days, weather by scheme and season.
- [Built] Dot-men with homes, visits, night owls, festivals; each has a stable id and habits from it.
- [Built] Scaffolding and builders; crier line; chronicle file of major events.

## Rule and people
- [Built] Nine rules chosen on becoming a Village, a leader dot-man, mood, politics every quarter day.
- [Built] Leader traits (eight), epithets after a day in office, heirs inheriting a trait.
- [Built] Leader health (0.54, RimWorld style): leaders and former leaders have bodies with age, wounds on body parts, bleeding, tending, infection, poison, a weak heart, growths, strokes, frailty and falls, so every death has a clear cause. Assassination is an attempt that can fail.
- [Undecided] Whether traits should also shape the crier's idle chatter.
- [Undecided] Bodies for the notables (captain, physician, warden, ringleader) and for sister rulers; parked as ideas on the mechanics page.

## The region
- [Built] Charter: a finished City becomes a sister town and a new hamlet is founded.
- [Built] Sisters with population by colour, rule, treasury and relations; migration, envoys, letters, aid, refugees, fairs, spies, caravans, pacts, absorption and collapse.

## Events (stage 5)
- [Built] Illness: five diseases, spread by shared building, care and quarantine, deaths.
- [Built] Curfew babies and the youth brigade (0.56): curfew towns marry half again as often; under a junta or party state children march behind the patrols and are walked home at curfew. Chosen to link two of the least-connected mechanics (guards and births); small life and construction were put off for now.
- [Built] Weddings and births (`Births.cs`; children drawn lighter for a day, up to 6 beyond the cap).
- [Built] Fires and ruins (`Fires.cs`): RimWorld-style size and spread, rain and bucket lines, ruins rebuilt after a day or two.
- [Built] War with a sister, war between sisters, plague and famine across the region (`War.cs`): soldiers march out, raiders burn, struck sisters ask for and get aid, a beaten sister's people arrive as refugees.
- [Built] Weather and festivals: rain or snow puts a festival off until it clears and breaks one up; a harsh rule's rally goes on regardless.
- [Built] Food and hunger (0.61, `Food.cs`): fields, barn, mill, bakery, counters, carriers, stomachs; each rule's reserve, hoarding and granary policy.
- [Built] Panic and imports (0.63): panic follows bare shelves and bad news and outlasts the shortage; in a pressed region caravan grain gets scarce and dear, and harsh or guild sisters keep theirs. Chosen from a study of food-crisis and political-survival literature as the cheapest, most visible part. Rejected for now: a money economy with wages and prices (the biggest system in the app for something you only glance at). Next candidates, on the mechanics page: coalition-first feeding and coercion that fails when guards go unfed, counter queues, extraction growing with tenure, war ratcheting harshness. Revisit the panic curve if bread queues feel like the town's normal state (`ShelfNormal`, `PanicAt`).
- [Planned] Floods, wanderers (hero, prophet, hermit), feuds, market day, comets. Spec: `../dotburg/hooks/register.tsx` and `../dotburg/CLAUDE.md` Pass 3.
- [Undecided] Whether a sick migrant or caravan can carry illness to or from a sister town.

## The app
- [Built] Window with the town at integer scale, legend and chronicle panels, hover tips on buildings.
- [Built] The window is the program (0.51): no desktop behind the panels; on start it is fitted to the panels at the largest town scale that fits the monitor, and after a resize it snaps to the nearest fitted size. Maximise and F11 stretch the same layout (spare width to the chronicle).
- [Built] Own clock: one growth unit per 20 s while open. Nothing happens while closed (0.55; the 2 h catch-up at half rate was removed so the owner can watch everything happen).
- [Built] Saves every 30 s and on exit; outbreaks and weather in progress are cosmetic or reset.
- [Built] Frame rate by attention (60 hovered, 30 focused, 15 background, 2 minimised).
- [Built] Messenger display pictures are mostly real photos, deliberately uneven in look (0.75): some flash-lit, blocky, tilted, sepia or date-stamped like 2005 pictures; a few cartoons kept; pixel portraits for one in three.
- [Built] Everyone types in their own 2006 hand in the Messenger (0.76): traits lean, chance decides, grave news reads plainer; the chronicle stays plain.
- [Undecided] Any link back to Claude Code (the `IPowerSource` interface allows another growth source).
- **War and blight rates are per day, wars are rare on purpose.** About one war per 31 days and one blight per 70, so each is an event, not background. We lose about 1.5 times as often as we win because sisters are larger; kept, since it matches the mod and makes aid and refugees matter. Rejected: soldiers saved mid-war (a war in progress is not saved; the away soldiers are saved as home instead). Revisit if defeat feels too punishing (the 30% revolution after a defeat is the harshest part).
- **A festival is put off, not cancelled.** Rain postpones it up to two days so the celebration still happens (a built-in reward for waiting); a festival already under way ends early. Rejected: cancelling outright (the festival is a reward for building, losing it to a shower felt unfair). Rallies ignore weather because the rule orders them.
- **The window is the program, not a desktop with programs on it.** The teal desktop made the app look like a Win98 screenshot sitting inside Windows; the owner wanted just the panels. The window fits itself to the panels and snaps back after a resize, so there is never bare space. Rejected: a uniform zoom of everything, fonts included (a square town on a wide screen still leaves bars at the sides), and free resizing with silver padding (looked empty). Costs: Windows half-screen snapping is undone, and a maximised 16:9 window gives a very wide chronicle. Revisit if either annoys in use.
- **News is a chat, not a log.** 0.52: the Chronicle panel became an MSN-style Messenger (the owner's idea): sisters, notables and group chats talk to our leader, the crier became toasts, and the Chronicle is one contact so major events never drown in chatter. Read only (the owner's choice); rejected: reply buttons (kept as an idea: they would make the chat a decision point) and free text (hollow without a language model). Talk uses its own random generator so it never changes the town. Built in the Win98 look first against the advice to restyle first (0.53 moves the whole app to XP); revisit the Messenger's chrome then. Done in 0.53: the whole app is Windows XP Luna with Tahoma (the owner's choice over Trebuchet MS titles), drawn by one set of helpers that replaced the Win98 ones rather than sitting beside them.
- **Leaders have bodies, not a death roll.** 0.54: the owner wanted each death to have a clear cause, as in RimWorld. Leaders and former leaders in town get a body; age complaints replace the flat death roll and assassination became an attempt (about 40% kill at once, 15% later, 45% survived). The owner chose leaders only (not notables or sister rulers), attempts that can fail, and lives about twice as long as before. Ages run a year per town day so age matters within a reign (the 16-day seasonal year is cosmetic); rejected: real-time years (age would never change in a reign). Former leaders' deaths go to the crier, not the chronicle, because a commune's rotating speakers made them too many. Revisit if lives feel too long or too short (`OldK`), or if the chart is too grim.
