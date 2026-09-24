# OTHERREACH — Asset Generation Plan

Derived from the design extension pack on 2026-09-22. Ordered so that each wave makes the
next cheaper, and so the highest-reuse assets come first.

**Current library: 250 built 3D assets, 445 concepts.** This plan covers what the design
pack implies that does not exist yet.

Running totals assume ~2.4 min per concept and ~5 min per 3D build at the current mid tier
(40k faces, 2048 PBR, 30 steps). Concept rendering is cheap; 3D is the real cost.

---

## The organising insight

`CRAFTING_AND_ITEMIZATION.md` is explicit that items are a **grammar**, not a list:

> Item = chassis/form + materials + structural components + functional components +
> mechanisms + magical/technical channels + modes + enchantments + tuning + appearance

and:

> This preserves wide combinations without requiring arbitrary runtime geometry.

That is a direct instruction to build **modular components** rather than finished items.
A single "flanged mace head" serves every haft length. A single "gorget" serves every
armour set. So wave 1 is components, and finished items later are largely assemblies of
things that already exist.

`COMBAT_DAMAGE_ARMOR_AND_DEATH.md` reinforces it. Armour is **coverage by body region**
(16 regions listed) across **11 construction families** plus **13 specialised gap pieces**.
Generating pieces per region across a few families is tractable; generating complete
armour sets per culture is not.

---

## Wave 1 — Armour components (highest reuse, ~90 assets)

Armour is layered and modular, so the components are shared across every culture and then
re-skinned. This is the single biggest reuse win in the pack.

### Body-region base pieces — 16 regions
head, face, neck, shoulders, chest, back, upper arms, elbows, forearms, hands, abdomen,
groin, thighs, knees, shins, feet.

Build each as a **neutral, cultureless plate/scale/mail shape** first. Culture variants
come later as re-skins.

### Gap and articulation pieces — 13 pieces
gorget, bevor, aventail, mail standard, voiders/gussets, besagew/rondel, couter, poleyn,
fauld, tasset, groin protection, sabaton, gauntlet.

### Construction families — sample pieces in each of 11
cloth, gambeson, hardened leather, mail, scale, lamellar, brigandine, coat of plates,
splint, plate, exotic (racial/magical/technical).

**Deliverable:** ~90 modular armour pieces that assemble into any set.

---

## Wave 2 — Weapon components (~70 assets)

Same grammar logic. Components combine into the historical forms the doc lists, and into
hybrids.

### Heads and blades
dagger, rondel dagger, stiletto, short sword, arming sword, longsword, greatsword, sabre,
scimitar, falchion, estoc, falx, khopesh, katar, pata, urumi, hand axe, bearded axe,
battle axe, great axe, war pick, bec de corbin, Lucerne hammer, goedendag, club, mace,
flanged mace, warhammer, maul, morningstar, spear, pike, glaive, halberd, poleaxe, ranseur,
partisan, spetum, bill hook, mancatcher, cestus, garrote.

### Hafts, grips and mechanisms
hafts in three lengths, crossguards, pommels, grips by hand-count, sockets, ferrules,
counterweights, folding joints, retraction mechanisms, channels.

### Ranged components
bow limbs (short/long/composite/recurve), bow risers, strings, crossbow prods, crossbow
stocks, triggers, sling pouches, atlatl, blowgun, throwing knives and axes.

### Ammunition — 8 types
broadhead, bodkin, blunt, barbed, alchemical, incendiary, enchanted, monster-specific.

### Shields — 7 types
buckler, round, heater, kite, large infantry, pavise, magical barrier focus.

---

## Wave 3 — Magic implements (~40 assets)

`COMBAT_DAMAGE_ARMOR_AND_DEATH.md` lists 13 implement forms that **change expression, not
just numbers** — so each needs a distinct silhouette.

wand, staff, rod, scepter, orb, grimoire, talisman, amulet, ring, athame, runestone,
censer, bell, instrument, lens, gauntlet, body inscription.

Plus **hybrid implements** the doc explicitly encourages: transforming focus-staff/spear,
recall dagger, blast-hammer, spell-focus shield, rune-channel crossbow.

---

## Wave 4 — Race and culture identity kit (~60 assets)

Section 6 of the combat doc gives each race a weapon and armour identity. None of this
exists yet, and it is what stops the library looking generic.

| Race | Needs |
|---|---|
| **Kal** | compact picks, counterweighted hammers, structural tools, **wing-assisted momentum designs**, dorsal-channel armour |
| **Vaskaal** | recoil-compensated projectiles, flechettes, smart sights, **tools shaped for 3+2 digit hands** |
| **Siann** | conductor blades, focus staves, elegant multi-mode forms |
| **Orenth** | anchoring, phase and displacement weapons |
| **Ondrek** | resonance and fracture tools, material-reading instruments |
| **Constructed** | reconfigurable modular implements, arm-mount sockets |
| **Mor** | no carried weapons — spectral technique visuals instead |

Also the five exotic defences named in the doc: **whispergorget, jointward mesh, veil
collar, sightguard, pulseplate, phase laminate**.

---

## Wave 5 — Animals, pets and mounts (~55 assets)

`PETS_ANIMALS_AND_ECOLOGY.md` requires pets that **do something**, so each needs a
functional identity rather than a cosmetic one.

### Working pets
tracking hound, guard dog, truffle pig, foraging pig, mouser cat, scouting hawk,
messenger pigeon, pack goat, herding dog.

### Mounts and transport
riding horse, warhorse, pack mule, draft ox, camel, sled dog team, wagon, cart, handcart,
ferry boat, river barge, coastal ship.

### Ecology roles
predator, prey, scavenger, herd, solitary, territorial, migratory — a few representatives
of each, which the existing 59 creatures partly cover.

---

## Wave 6 — Travel and traversal infrastructure (~35 assets)

`TRAVEL_AND_TRAVERSAL.md` tiers 3-5 are almost entirely unbuilt.

climbing equipment, pitons, rope, grappling hook, glider, Kal wing-harness, flying mount
tack, **Othergate forms** (ancient arch, standing stone, mirrored door, well, root arch,
mechanical structure, invisible threshold), recall anchor, waystone, gate-lens, transit
platform, lift, ferry dock, bridge types, ford markers.

---

## Wave 7 — Containers and logistics (~30 assets)

Directly specified in `INVENTORY_STORAGE_AND_LOGISTICS.md`.

belt pouch, potion bandolier, quiver, backpack, expedition pack, bag of holding, chest,
shelf, armory rack, warehouse pallet, crafting store, food store, secure vault,
ammunition case, refrigerated alchemical store, Otherwrought containment box, courier
satchel, mail pigeon cage, bank strongbox, trade crate, barrel, amphora, sack.

---

## Wave 8 — Buildings and settlement (~70 assets)

`WORLD_BUILDING_AND_PROPERTY_DESIGN.md` gives an explicit progression, and a settlement is
recognised by capability rather than a button, so each capability needs a building.

**Progression:** campfire, camp, shelter, cabin, homestead, workshop, estate, fortified
homestead, hamlet, village, settlement.

**Per-race architecture:** the pack states biology influenced ancestral building. Kal
vertical foundries, landing shelves, cliff structures; Ondrek structures of organised
geology; Constructed modular architecture; Vaskaal technical structures; Mor have none.

**Settlement services** (each attracts different NPCs): forge, farm, mill, bakery, mine
head, lumber yard, tannery, storehouse, market stall, inn, temple, apothecary, library,
barracks, wall segments, gatehouse, watchtower, well, dock.

---

## Wave 9 — Magic, ritual and cosmology VFX sources (~40 assets)

Concepts that 3D can't do, but art direction needs them.

Othergate activation, fold distortion, Otherwake residue, Otherstorm, Veil thinning,
Pachakuti transition, Otherfire, soul contact, phase displacement, resonance fracture,
rune routing patterns, enchantment anchoring, ritual circles, ward placement, corpse
interaction, body inscription patterns.

---

## Wave 10 — UI, HUD and map products (~50 assets)

`HUD_INPUT_AND_ACTIONS.md` and `MAPS_CARTOGRAPHY_AND_WORLD_KNOWLEDGE.md` specify these.

action icons per weapon family, contextual action sets, status effect icons, wound
indicators, resource channel icons, coverage diagrams, **map products** (regional road map,
herbalist's map, mining survey, military map, monster-hunter notes, treasure map, ancient
ruin map, gate chart), map markers in five confidence states, and knowledge-gap
indicators.

---

## Wave 11 — Terrain, foliage and region dressing (~120 assets)

The largest wave and the least specified so far, because regions are not yet designed.
`WORLD_MATERIALS.md` already lists the texture groups and biome flora; this wave builds the
3D versions and fills region-specific gaps once the first region is chosen.

Priority inside this wave: the region actually being built first.

---

## Totals

| Wave | Assets | Concepts ~40 min, 3D ~hours |
|---|---|---|
| 1 Armour components | 90 | 1.5 h / 7.5 h |
| 2 Weapon components | 70 | 1.2 h / 5.8 h |
| 3 Magic implements | 40 | 0.7 h / 3.3 h |
| 4 Race identity | 60 | 1.0 h / 5.0 h |
| 5 Animals and mounts | 55 | 0.9 h / 4.6 h |
| 6 Travel infrastructure | 35 | 0.6 h / 2.9 h |
| 7 Containers | 30 | 0.5 h / 2.5 h |
| 8 Buildings | 70 | 1.2 h / 5.8 h |
| 9 VFX sources | 40 | 0.7 h / 3.3 h |
| 10 UI and maps | 50 | 0.8 h / 4.2 h |
| 11 Terrain and foliage | 120 | 2.0 h / 10.0 h |
| **Total** | **660** | **11 h concepts / 55 h 3D** |

Both machines running flat out, that is **roughly a week of continuous generation** for
wave content, before region-specific work. Which matches "we'll be doing this for a long
time."

---

## What to do about the 3D cost

55 hours of 3D assumes everything gets a mesh. It should not:

- **UI, maps and VFX sources are 2D deliverables.** ~90 of the 660 need no 3D at all,
  which takes the build cost down by about 8 hours.
- **Armour and weapon components are small objects.** They should build faster than the
  5-minute average, and their LOD budgets can be much lower.
- **Culture variants are re-skins**, not new meshes. One neutral gorget plus texture work
  covers every culture.

Realistic 3D requirement is closer to **35-40 hours**, and a large part of that is small
props.

---

## Recommended order

1. **Wave 1 and 2** — armour and weapon components. Highest reuse, and they make every
   later wave cheaper by letting finished items be assemblies.
2. **Wave 4** — race identity kit. This is what stops the library reading as generic
   fantasy, and the race biology is fresh.
3. **Wave 7, 6, 5** — containers, travel and animals. Small, self-contained, high value.
4. **Wave 3** — magic implements.
5. **Wave 8** — buildings, once a first region is chosen so effort goes somewhere real.
6. **Wave 11** — terrain, region by region, not all at once.
7. **Wave 9, 10** — VFX and UI, which depend on art direction decisions not yet made.

## Dependencies worth noting

- **Wave 8 depends on choosing a first region.** Building 70 generic structures before
  knowing the first region risks wasted work. The pack says regions are authored units.
- **Wave 11 depends on the same decision**, and is the largest wave.
- **Wave 9 and 10 depend on aesthetic choices** (colour language, iconography style) that
  no document has settled yet.
- **Wave 5 depends on the ecology roles being assigned per region**, since which animals
  exist where is a regional question.

## Open questions for the owner

1. **Which region is first?** Waves 8 and 11 are blocked on it and together are 190 assets.
2. **Does every culture need its own armour re-skin**, or do fit families share textures
   with only accents differing? This changes wave 1 from 90 assets to 90 plus skins.
3. **Are hybrid/transforming items a first-class artwork requirement** or a later luxury?
   The crafting doc encourages them, but each transformation state is separate geometry.
4. **Should the Vaskaal 3+2 digit hands get their own weapon grip family?** The doc implies
   tools shaped for unusual hands, which means a parallel set of every melee weapon.
