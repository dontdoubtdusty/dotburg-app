# Voices: the dialogue generator (vision)

Status: [Partly built]. Designed with the owner on Nov 13, 2026 (four rounds of questions). Step 1 built in 0.79 (engine, phrase file, `--voices`, sister aid); how it works is in `systems.md` under Messenger, Voices.

### Decided while building step 1 (0.79)
- **Only trait tags lean; every other tag must hold.** The design said all tags raise the score. The first samples showed why not: a guild sister wrote "We prayed for you", a guild leader "The Party thanks you", a rain aside came on a sunny day. So rule (`g:`), role, bond, moment and weight tags are conditions; traits still lean (people do not always talk as they act), and a trait phrase from someone with none of its traits scores lowest, so it is rare. Revisit if lines feel too predictable per rule.
- **`a/b` tags** mean either, so one header can cover two bonds or seasons.
- **The leader's reply is the event plus `.re`** (`sister.aid.re`), not a slot, so replies get the full open/news/feel/close build.
- **Why tags.** A caller can say why an event happened (`AskAid` passes fire, war or hungry), so aid after a fire talks about timber.
- **A second message in a row is feel or aside**, never a lone sign-off ("Write soon." on its own read as a glitch).
- **The repeat memory is not saved** (last 8 parts per speaker): a reload may repeat a part once. Callbacks are still undecided (step 5).

### Decided while building notables and friends (0.80)
- **A header can name several events** (`[npc.steward.harvest,npc.steward.bumper feel]`). The first notable samples tied generic feelings to the wrong news ("A good day for the ledgers" after grain rotted, "Smells wonderful" after the ovens went cold), so feelings and asides are written per group of events, not per role.
- **Never the same thing twice in a conversation.** Only news may repeat (the event must be told); an extra with nothing fresh is left out.
- **The best few are those scoring at least half the best**, at most 4. Without this a trait line from someone without the trait came up about 1 in 5 times.
- **Fear is a tag.** Under a rule of 0.6 harshness or more, every notable but the ringleader is `fearful`: fearful parts flatter ("Victory, {me}. Your victory.", "The fault is mine, not yours"), and lines that complain or demand carry `!fearful`.
- **Enemies write only once.** A grudge contact says its piece and is Blocked, as in MSN, where a blocked contact cannot message you. Rejected: enemies needling now and then (it would break the Blocked metaphor). Revisit if enemies feel too quiet; a hint before an attempt on the leader's life is a separate idea on the mechanics page.
- **Notables get no reply yet**; the leader's side is step 5.

## What it is
Voices builds every Messenger line from hand-written parts, chosen by who is speaking, who they are speaking to, and what is going on. The leader we watch uses it too, for replies and for messages they start themselves. It replaces the fixed tables in `Messenger.cs` (`TheySay`, `WeSay`, `GossipSay`, `NpcSay`, `RoutineSay`), one event at a time.

Name: **Voices** (`Voices.cs`). It pairs with Hands: Voices decides *what* someone says, Hands (`Hands.cs`) decides *how they type it*. "Dialogue generator" is its description, not its name. Rejected: "Dialogue Generator" as the code name (reads like an outside tool), "Talk" (too vague).

## Where it sits
```
sim event or idle roll
  -> Voices: who speaks, how many turns, which parts      (new)
  -> Fill: names, towns, numbers into {slots}             (exists)
  -> Hands: the writer's typing quirks                    (exists, 0.76)
  -> Post: into the conversation, toast, sound            (exists)
```
The chronicle is not part of Voices: it stays plain, in the chronicler's own voice (`Chronicler.cs`).

## Rules it must keep
- **Talk stays cosmetic.** Lines reflect the sim but never change it. A cold reply does not cool a bond; flattery does not warm one. Voices draws only from `chatRng`, never `Rng` or `RelOf` (the existing Messenger rule in `CLAUDE.md`). Rejected: replies that nudge bonds (it would turn talk into a mechanic and give it a weight it should not have).
- **Facts stay true; tone can bend.** A speaker may flatter, hedge, play news up or down, or sound braver than they are, but never states a wrong number or an invented event. Hearsay, late news and lies are a later layer of their own (see Later).
- **The player.** No lever: the player never chooses a line or a reply. Everything the leader says comes from the leader's character and situation.
- **Tone.** A harsh leader is cold and menacing by implication ("Handle it." "Names, by morning." "They were warned."), never cruel in detail, never gloating over the dead. Grave news gets fewer parts and no jokes (Hands already types grave lines more plainly).
- **No emojis.** MSN emoticon codes such as `:)` and `(L)` are fine, as now.

## The factors
What shapes a line, in the order the owner ranked them:
1. **Who they are.** Role (sister ruler, captain, physician, warden, ringleader, steward, baker, grocer, a friend, an enemy, the old leader), their traits (the 32 in `Gov.cs`, through `VoiceOf`), the rule they live under, their age.
2. **Their relation to the leader.** Bond (friend, grudge), sister relations, and fear: under a harsh rule, people below the leader flatter more and complain less openly.
3. **Memory and moment.** No repeating the same part soon; now and then a callback to a named past event ("since the fire", "after the grain you sent in spring"); time of day, season, weather, the town's mood.

Left out on purpose: *what they know* (who heard what, and when). See Later.

## How a message is built
A message is 1 to 3 parts joined together. Every part is a whole phrase written by a person, so there are never generated words inside a sentence. Rejected: Tracery-style grammars that fill words into sentences (the most variety, but they read like a machine after a day of watching); whole lines only (the most natural, but it needs far more writing for the same variety).

Slots, in order (most messages use two or three):
- `open`: a greeting or lead-in ("Hey.", "{us}, listen.", "Sorry to bother you so late")
- `news`: what the message is about (always present for an event)
- `feel`: how the speaker takes it ("We're shaken.", "Could be worse.")
- `aside`: an extra remark, often a callback or the weather
- `close`: a sign-off ("Write soon.", "That is all.")

Each part has tags. Choosing a part:
- The event key must match (`sister.aid`, `npc.captain.riot`, `idle`, ...).
- Tags that fit the speaker and the moment raise the score; a `!tag` rules the part out.
- Pick at random, weighted by score, from the best few, so a fitting part is likely but not certain. This is the same idea as Hands: traits lean, chance decides.

Tag families (first draft):
- role: `sister`, `captain`, `friend`, `enemy`, `leader`, ...
- trait: `t:cheerful`, `t:cruel`, `t:craven`, ...
- rule: `g:junta`, `g:commune`, ... and `harsh` / `kind`
- bond: `warm`, `cool`, `hostile`, `wary`, `fearful`
- state: `ill`, `wounded`, `grieving`, `new` (in office), `old`, `shaken` (after an attempt)
- moment: `morning`, `night`, `winter`, `rain`, `festival`, `hungry`
- weight: `grave`, `light`

## Conversation shapes
Per event, 1 to 4 turns, chosen by the event's weight and the bond:
- one message, no reply
- message and reply (the common case)
- message, reply, follow-up ("Also, the roads are bad.")
- two messages in a row from the speaker, then a reply

Rejected: long back-and-forth (floods the Messenger, and the important lines drown).

## The leader's voice
The leader answers, and sometimes starts conversations. What shapes it:
- **Traits and rule:** a paranoid junta leader and a cheerful council leader answer the same news differently.
- **Bond with the speaker:** warm to friends, curt to enemies and rival sisters.
- **Their state:** age, wounds, illness, grief, days in office, a recent assassination attempt (from `Health.cs`): tired, frightened, distracted or sure of themselves.
- **Silence and delay:** sometimes no reply, a late reply, or the message is read and nothing comes back. More likely when ill, busy, grieving or annoyed with the speaker. Silence says something too.

The leader starts conversations now and then: asking after a sick friend, boasting to a sister after a charter, demanding a report from a notable under a harsh rule, checking on a sister at war. The player chooses none of it.

## Idle talk
People write without a triggering event, but rarely, and more often the closer or more hostile they are: friends check in, enemies needle, sisters remark on the season, notables grumble. Rejected: frequent small talk (it buries the lines that matter); events only (the Messenger goes silent between events and contacts feel like event triggers, not people).

## Speakers
Only the current contacts: sisters, notables, the leader's friends and enemies, the old leader, gossip groups. Make these richer first. Rejected for now: ordinary dot-men writing to the seat as petitioners (more traffic, a harder list to read). Revisit once Voices is proven.

## The phrase file
`resources/voices.txt`, read at start. Plain text so prose is easy to write: no quotes or escapes.
```
# A comment.
[sister.aid news | warm]
Wagons are on the way.
Grain and timber, on us.

[sister.aid news | cool, g:guild]
Wagons leave at dawn. We'll settle up later.

[* close | warm, t:cheerful]
Chin up :)
```
Header: `[event slot | tags]`; `*` means any event. One part per line below it. `{us}`, `{them}` and the other existing `Fill` slots work as now.

Rejected: C# tables (awkward to write a lot of prose in), JSON (quotes and escapes everywhere).

## The tool: `--voices`
A test flag like `--hands` and `--names`: loads the phrase file, checks it (unknown tags, empty slots, events with no `news` part, slots no event uses), then prints sample conversations for a spread of speakers, bonds, rules and states into a text file next to the exe. This is how the lines get read and tuned without waiting for events in the app. Rejected for now: an HTML workbench page (it would need its own copy of the picking logic, which would drift from the app).

## Memory
- Each speaker remembers the last few parts they used and avoids them.
- Callbacks: a small list of memorable shared events per contact (aid given or received, a war, a fire, a wedding, a death) that `aside` parts can name. To decide when building: whether this list is saved (callbacks survive a reload) or rebuilt from the chronicle.
- Rejected: a full history per relationship with a mood that drifts (more saved state and design than it is worth at this stage).

## Moving over
1. [Built 0.79] Engine, phrase file, `--voices`, one event (sister aid) moved over; the old tables stay as a fallback for everything not yet moved.
2. [Built 0.80] Notables and routine voices: every notable event (captain, physician, warden, steward, ringleader, baker, grocer).
3. [Partly built 0.80] The leader's friends and enemies: hello, small talk, troubles, and the leader's replies to friends (by traits, rule and bond only). Still to do: idle talk across all contacts, the leader starting conversations.
4. The remaining sister events and gossip.
5. The leader's replies (traits, bond, state, silence and delay). Moved to last by the owner (Nov 13): the speakers first, then the leader's side.
6. Remove the old tables.

Each finished step is a minor version bump.

## Later
- **Hearsay:** a layer on top of Voices where distance and motive make news late, vague or wrong. Needs a model of who knows what. Fits the vision's "what reaches the seat"; left out of the first build to keep the engine simple.
- **Tongues:** a town's naming tradition (`Names.cs`, five tongues) could flavour greetings and sign-offs.
- **Petitioners:** ordinary dot-men as contacts.
