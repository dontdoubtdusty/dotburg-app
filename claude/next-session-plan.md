# Dotburg: Plan

**One home per fact.** `vision.md` holds the design, `systems.md` how each system works, `CLAUDE.md` the rules. This doc holds, for open steps only, the build sketch and the checks. When a step is built, cut it down to what later steps need.

---

## 1. Next steps, in order

1. **How a village chooses its rule** (found 2026-11-14). See "Village rule choice" below.
2. **Wanderers (a hero could beat back fires), feuds, market day, floods, comets** from the mod's Pass 3. Floods share the weather and seasons links and can rise from the land's ponds and marshes (Land, 0.82, `systems.md`); wanderers can use war and blight (a hero marches with soldiers, a prophet in a plague). Feuds are the other half of the bonds-to-births idea: weddings between friends and family names are built (0.78), so a feud is a grudge inherited between two family names (`Walker.Last`). Every one must pass the two questions under "The player" in `vision.md`.
3. Fix `--pals`: it no longer seats a leader since Settlers (0.73), so it shows no friends.
4. Optional: cap the hovered frame rate at 30 if 7.8% of a core while hovering matters.
5. **Hearsay tooltips** (vision, The player): mood, sister and people tips in words and reported reasons instead of exact numbers. Not designed yet.

### Village rule choice (found 2026-11-14)
- **The problem:** when a hamlet becomes a village, `ChooseGov` (`Politics.cs`) rolls the new rule from base lean x scheme lean x hamlet `Grows` only. It ignores the townsfolk and the sitting leader. `SetGov(k, line, keep: false)` then retires the leader and `Appoint` picks whoever fits the new rule's harshness. Example: Stonemere (snow, founding family) had a loyal, honest, calm founder; the roll gave a party state (about 5%), and a cruel, greedy, brave dot-man was about 12x likelier to lead it than someone like her. To the player it read as the founder handing power to a cruel friend (the friendship was chance: `Appoint` ignores bonds).
- **Fix:** make it like `FormRule` (the settlers' choice): weight each rule by who lives there and who leads (a calm, honest leader makes council, mayor, monarchy likelier; harsh rule needs cruel or ambitious townsfolk), keep the sitting leader under the new title when the rule suits them (`keep: true`; a founder can be crowned), and give the chronicle line a reason.
- **Keep crisis flips as they are:** coups (mood under 0.3, to junta), junta to party, strongman elections, army takeovers after a murder, revolutions and the Party's collapse all live in `Politics()` and do not use `ChooseGov`. Traits may steer which way a crisis flips (junta, party, commune), never whether it happens, so a gentle town can still go harsh after plague or defeat.
- **Optional with it:** a cause in the coup and strongman lines (plague, hunger, defeat); the strongman drawn from the angry, ambitious or cruel, ideally one with a grudge against the old leader; warning signs before a coup (fits the bonds idea of a threat before an attempt).

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
