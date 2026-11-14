# Dotburg Vision

Status tags: [Built] [In progress] [Planned] [Undecided]. This doc describes the app, not the builds: when something is built, change its tag and keep the description current. How each system works in detail is in `systems.md`.

## The player
The player is a witness, not a ruler. They see the intimate side of a town or city's leadership from the leader's own seat: messages from friends and enemies, reports from notables, letters from sister towns, the slow turns of power. The player holds the seat, not a person: when a leader dies, falls or loses a vote, the view passes to whoever leads next, with that leader's own contacts and blind spots. The town goes on without the player, and so does the world.

The game is an exercise in having no control and incomplete information. The player gains some insight into the wider world, but should feel like one person in a much larger town, which is in a much larger world. The feeling to aim for is smallness and quiet acceptance: things happen, people pass, the town is one of many and the world barely notices it.

Rules that follow from this:
- **No steering, ever.** No input changes what happens in the town. Window, panel, sound and display controls are fine; choices that alter the sim are not. Rejected for this reason: reply buttons in the Messenger (an idea until 0.77).
- **The picture is the view from the seat.** The town is in plain sight, but only its outside: the player sees people walk, not why. The picture stays a full live view of the town; the gaps are in the words and numbers, not the image.
- **What the player learns is what reaches the seat.** The Messenger, the crier, the chronicle and the ledger are all the leader's knowledge, not the truth: people tell what they know or want to say, they flatter, lie, stay silent or arrive late; the chronicle is slanted by its keeper; the ledger's figures are the steward's estimates. [Planned] Exact numbers in the tooltips (mood and sister reasons, people's traits and grudges) become hearsay: words instead of numbers ("restless", "cool toward us"), reasons as the leader has been told them, some wrong or missing, better with spies, friends or a loyal chronicler. The current tooltips stay until reworked.
- **A larger world beyond the region.** [Planned] A distant realm with its own events that press on the region (taxes, levies, a far-off war), known only as it reaches the seat. The region is not the whole world.
- **Every new feature passes two questions.** Could the person in the seat know this, and how? Does it give the player a lever? A feature that gives a lever is out; one the leader could not know about must arrive as hearsay, a rumour or a late report, or not at all.

## The concept
A quaint top-down pixel town of dot-men that grows on its own while the app is open, with a day and night, seasons, weather, governments and a region of sister towns. Something to glance at, not to play: no buttons that steer the town (see The player). It began as the `/town` mod for Claude Code (`../dotburg`, frozen at v0.6.0), which is the spec for the port.

## Tone
- Towns range from egalitarian to harsh, even fascist; not all friendly utopias. Harsh rule is shown, not dwelt on: curfews, guards, rallies, people fleeing, "someone is taken away". No purges or crackdowns that empty the streets.
- Real setbacks are allowed (plague, fire), but rare.
- Small life is never harmed (the owner's rule, 2026-11-14): ducks, the cat, birds, fireflies and the rest may hide from rain or winter, but no famine, fire, war or illness ever kills, eats or drives them off. Rejected: ducks vanishing in a famine (a grim hint, but the owner wants them safe). They may react and join in (fireflies over a festival), never suffer.
- No emojis anywhere; plain characters in the legend.

## The town
- [Built] 7x7 lots, roads, buildings filling from the middle; tiers Hamlet, Village, Town, City with upgrades in place.
- [Built] Day and night (6 minutes), four seasons of four days, weather by scheme and season.
- [Built] Dot-men with homes, visits, night owls, festivals; each has a stable id and habits from it.
- [Built] Scaffolding and builders; crier line; chronicle file of major events.

## Rule and people
- [Built] Nine rules chosen on becoming a Village, a leader dot-man, mood, politics every quarter day.
- [Built] Settlers (0.73): a new town has no rule until 5 grown dot-men settle on a hamlet rule, chosen by who they are.
- [Built] Character and bonds (0.68): every dot-man has three traits of 32 (and maybe a rare wildcard); roommates make grudges and friendships that feed assassinations, arrests, emigration, fires and hoarding. Leaders are chosen from the townsfolk by their traits and keep them; epithets after a day in office, heirs often share a trait.
- [Built] The chronicler (0.69): a dot-man keeps the chronicle in their own voice and slant; the pen passes when they die, leave or lead.
- [Built] The leader's own contacts (0.69): friends and enemies of the leader appear in the Messenger; a new leader signs in to their own list.
- [Built] Citizen health (0.67): everyone ages (a year every 4 town days) and dies of named causes, told in a burial summary every 4 days.
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
- [Built] Weddings between friends (0.78): friends marry and share a home, strict rules arrange matches, children carry a parent's family name. The first positive payoff for friendship; feuds between families are the next half of this idea.
- [Built] Sharing food (0.78): the hungry eat at a friend's table; in hard times the generous feed the street, and under a junta or party state the guards may break it up.
- [Built] Names (0.78): five naming traditions, each town leaning toward one; names are saved and unique in town.
- [Built] Fires and ruins (`Fires.cs`): RimWorld-style size and spread, rain and bucket lines, ruins rebuilt after a day or two.
- [Built] War with a sister, war between sisters, plague and famine across the region (`War.cs`): soldiers march out, raiders burn, struck sisters ask for and get aid, a beaten sister's people arrive as refugees.
- [Built] Weather and festivals: rain or snow puts a festival off until it clears and breaks one up; a harsh rule's rally goes on regardless.
- [Built] Harvest home and called-off festivals (0.81): every harvest that does not fail is celebrated; a real outbreak or hunger calls a festival off, a rally excepted.
- [Built] Food and hunger (0.61, `Food.cs`): fields, barn, mill, bakery, counters, carriers, stomachs; each rule's reserve, hoarding and granary policy.
- [Built] Panic and imports (0.63): panic follows bare shelves and bad news and outlasts the shortage; in a pressed region caravan grain gets scarce and dear, and harsh or guild sisters keep theirs. Chosen from a study of food-crisis and political-survival literature as the cheapest, most visible part. Rejected for now: a money economy with wages and prices (the biggest system in the app for something you only glance at). Next candidates, on the mechanics page: coalition-first feeding and coercion that fails when guards go unfed, counter queues, extraction growing with tenure, war ratcheting harshness. Revisit the panic curve if bread queues feel like the town's normal state (`ShelfNormal`, `PanicAt`).
- [Planned] Floods, wanderers (hero, prophet, hermit), feuds, market day, comets. Spec: `../dotburg/hooks/register.tsx` and `../dotburg/CLAUDE.md` Pass 3.
- [Undecided] Whether a sick migrant or caravan can carry illness to or from a sister town.

## The app
- [Built] Windows XP look (0.53, text drawn by GDI since 0.71): the town window with the government band, the ledger and the Messenger; hover tips on buildings, the mood bar, food and sisters.
- [Built] The window is the program (0.51): no desktop behind the panels; on start it is fitted to the panels at the largest town scale that fits the monitor, and after a resize it snaps to the nearest fitted size. Maximise and F11 stretch the same layout (spare width to the Messenger).
- [Built] Own clock: one growth unit per 20 s while open. Nothing happens while closed (0.55; the 2 h catch-up at half rate was removed so the owner can watch everything happen).
- [Built] Saves every 30 s and on exit; outbreaks and weather in progress are cosmetic or reset.
- [Built] Frame rate by attention (60 hovered, 30 focused, 15 background, 2 minimised).
- [Built] Messenger display pictures are mostly real photos, deliberately uneven in look (0.75): some flash-lit, blocky, tilted, sepia or date-stamped like 2005 pictures; a few cartoons kept; pixel portraits for one in three.
- [Built] Everyone types in their own 2006 hand in the Messenger (0.76): traits lean, chance decides, grave news reads plainer; the chronicle stays plain.
- [Partly built 0.79-0.80: engine, sister aid, notables, friends and enemies] Voices, the dialogue generator: every Messenger line, the leader's included, built from tagged hand-written parts chosen by who speaks, their bond with the leader and the moment. Full design in `voices-vision.md`.
- [Undecided] Any link back to Claude Code (the `IPowerSource` interface allows another growth source).

## Decisions and why
- **War and blight rates are per day, wars are rare on purpose.** About one war per 31 days and one blight per 70, so each is an event, not background. We lose about 1.5 times as often as we win because sisters are larger; kept, since it matches the mod and makes aid and refugees matter. Rejected: soldiers saved mid-war (a war in progress is not saved; the away soldiers are saved as home instead). Revisit if defeat feels too punishing (the 30% revolution after a defeat is the harshest part).
- **A festival is put off, not cancelled.** Rain postpones it up to two days so the celebration still happens (a built-in reward for waiting); a festival already under way ends early. Rejected: cancelling outright (the festival is a reward for building, losing it to a shower felt unfair). Rallies ignore weather because the rule orders them.
- **Sickness and hunger cancel a festival; rain only delays it (0.81).** A shower passes, but nobody dances through a plague or with the bread gone, and the cancelled festival is part of the story. Only what the town can see counts (an announced outbreak with 3 or more sick, the grey plague, hunger the steward would report), so a couple of coughs do not stop a dance. Rejected: calling off for any outbreak (about half of all festivals were lost) and for one weak dot-man. Revisit if festivals feel rare: about 3 are held per 60 days. Rallies still go ahead.
- **The window is the program, not a desktop with programs on it.** The teal desktop made the app look like a Win98 screenshot sitting inside Windows; the owner wanted just the panels. The window fits itself to the panels and snaps back after a resize, so there is never bare space. Rejected: a uniform zoom of everything, fonts included (a square town on a wide screen still leaves bars at the sides), and free resizing with silver padding (looked empty). Costs: Windows half-screen snapping is undone, and a maximised 16:9 window gives a very wide chronicle. Revisit if either annoys in use.
- **News is a chat, not a log.** 0.52: the Chronicle panel became an MSN-style Messenger (the owner's idea): sisters, notables and group chats talk to our leader, the crier became toasts, and the Chronicle is one contact so major events never drown in chatter. Read only (the owner's choice); rejected: reply buttons (kept as an idea until 0.77, then dropped for good by The player: they would make the chat a decision point) and free text (hollow without a language model). Talk uses its own random generator so it never changes the town. The whole app is Windows XP Luna with Tahoma (0.53, the owner's choice over Trebuchet MS titles).
- **Leaders have bodies, not a death roll.** 0.54: the owner wanted each death to have a clear cause, as in RimWorld. Leaders and former leaders in town get a body; age complaints replace the flat death roll and assassination became an attempt (about 40% kill at once, 15% later, 45% survived). The owner chose leaders only (not notables or sister rulers), attempts that can fail, and lives about twice as long as before. Ages first ran a year per town day so age mattered within a reign; since 0.67 everyone, leaders included, ages a year every 4 town days on one clock with the citizens (the owner's choice), so leaders live about 4 times as many days. Rejected: real-time years (age would never change in a reign). Former leaders' deaths go to the crier, not the chronicle, because a commune's rotating speakers made them too many. Revisit if lives feel too long or too short (`OldK`), or if the chart is too grim.
- **Friends marry; strict rules arrange (0.78).** Friendship had only ever been a shield against grudges; now it leads somewhere visible (a wedding line naming the day they became friends, a shared home, children with a family name). Strict rules (harshness 0.5+) arrange a share of matches, friends or not, so the rule shows in private life too. Rejected: weddings only between friends (a friendless town would stop having children; a neighbour's match covers it 40% of the time) and a sex field to pair couples (not needed for the story yet, and the Words rule has no sex field). Accepted: about 44% more births than 0.77, because the old wedding silently needed two dot-men outdoors. Revisit if towns crowd their cap with dotlets (`MaxExtraKids`).
- **Sharing is a fallback, not a second economy (0.78).** A friend's pantry is only tried when there is nowhere to buy food, and a street table only in hard times, so food sharing does not empty pantries in good times or replace the counters. Both pass The player: no lever, and the leader hears of them as the crier does. Breaking up tables under a junta or party state ties it to the rule and turns charity into a grudge.
- **Names are saved, not hashed (0.78).** A hash of the id cannot promise two living dot-men different names, and 40 first names in a town of 60 made namesakes common. Saved names cost a few bytes per dot and allow families (a child takes a parent's name). Old saves keep their names (bonds, the leader and the chronicle use them), and only duplicates are renamed. Names stay out of `Rng`: draws are seeded from the id, so naming never changes what happens.
- **Voices builds lines from hand-written parts (planned Nov 13).** A few fixed lines per event read the same from everyone, and the leader could only be harsh or kind. Each message will be 1 to 3 tagged parts written by a person, picked by speaker, bond and moment; talk stays cosmetic and facts stay true. Rejected: word-level grammars (read like a machine), replies that change bonds (talk would become a mechanic), hearsay in the first build (needs a model of who knows what). Details in `voices-vision.md`.
