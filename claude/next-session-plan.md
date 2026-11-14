# Dotburg: Plan

**One home per fact.** `vision.md` holds the design, `systems.md` how each system works, `CLAUDE.md` the rules. This doc holds, for open steps only, the build sketch and the checks. When a step is built, cut it down to what later steps need.

---

## 1. Next steps, in order

1. **Wanderers (a hero could beat back fires), feuds, market day, floods, comets** from the mod's Pass 3. Floods share the weather and seasons links; wanderers can use war and blight (a hero marches with soldiers, a prophet in a plague). Feuds are the other half of the bonds-to-births idea: weddings between friends and family names are built (0.78), so a feud is a grudge inherited between two family names (`Walker.Last`). Every one must pass the two questions under "The player" in `vision.md`.
2. Fix `--pals`: it no longer seats a leader since Settlers (0.73), so it shows no friends.
3. Optional: cap the hovered frame rate at 30 if 7.8% of a core while hovering matters.
4. **Land** (designed 2026-11-13, not built; order against step 1 not decided). See below.
5. **Hearsay tooltips** (vision, The player): mood, sister and people tips in words and reported reasons instead of exact numbers. Not designed yet.

### Land: agreed design (brainstormed with the owner, 2026-11-13)
- **Why:** every town has the same 7x7 grid. Instead, every town loses exactly **7 of the 49 tiles** to land, so every town has **42 lots** (same for all, fair, one tuning) and the land gives the town its shape.
- **Arrangement:** one big feature per town plus small ones (single rocks, copses) for the rest of the 7. Big features: **river with bridges, lake or pond, rocky ridge, forest edge**. Kind **leans by scheme** (any can appear). Generated from a saved seed at founding; re-roll if any lot cannot reach the gate.
- **Roads:** a road between two blocked tiles disappears, so features are solid organic shapes (rough painted edges, not squares); rivers keep crossing roads as bridges. `Route` (L-shaped) must become a path search over road pixels; check queues (`PlaceInLine`), lamps (crossings), `Entrance`, `StrollSomewhere`.
- **Growth:** same middle-out order as today, skipping blocked tiles (rank 0 stays central).
- **House size:** each house rolls its own room from a hash of its lot (cottage 2, house 3, family house 4; upgrades add on top), shown in the hover tip, so 24 homes fill like 29 did. Tier caps (`PopCaps` 40/60/80/100) stay. Soak-check time per tier and food.
- **Effects, existing systems only:** water: ducks live there, bucket chain for fires next door (like a well), marsh fever likelier, pond ice (the existing accident), freezes in winter. Woods: seasonal colour, fireflies, birds, fire can spread from them in a dry summer. Rocks: never burn, firebreaks. Not now (ideas for the mechanics page): floods, fishing, quarry, wanderers from the woods, sisters named by their land.
- **Old saves:** no land means a new town on load, done like the restart button (backup, archive chronicle, toast). Old-grid code is not kept.
- **River:** cuts a corner (about 4 to 5 tiles, e.g. west edge to south edge), framing the town rather than splitting it; the rest of the 7 goes to small features.
- **Gate follows the land:** the road comes in where the land suggests (along the river, through a gap in the ridge, past the woods), so newcomers, caravans, soldiers and exiles use that side. `Entrance()` and everything that assumes "the west gate" (walkers, war, `Leave`, visitors, sacks for the barn in `DrawFoodSigns`, crier lines saying "west gate") must read it.
- **Land stays fixed** for the life of the town: no clearing, no shrinking ponds. Keeps "same for everyone" true.
- **Not decided yet:** art per scheme and season (decide at build time, show screenshots).

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
