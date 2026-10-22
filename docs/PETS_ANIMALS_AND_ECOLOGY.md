# OTHERREACH — Pets, Animals, Hunting & Ecology

**Status:** Strong working direction  
**M2 impact:** None

## 1. Pet principle

> **Pets should do something.**

Avoid pets that exist only as inert cosmetics.

Their contribution can be small and situational rather than a mandatory combat stat bonus.

## 2. Pet roles

Potential functions:

- combat;
- guard;
- warning;
- tracking;
- scent detection;
- retrieval;
- scouting;
- carrying;
- hunting;
- herding;
- pest control;
- gathering assistance;
- warmth/comfort/morale.

## 3. Examples

### Dog/hound
- track scent;
- alert hidden threats;
- fight;
- guard camp.

### Pig
- locate roots/fungi/truffles;
- forage;
- limited defensive combat.

### Cat
- pest control;
- subtle movement detection;
- minor home/rest benefit.

### Bird
- scout;
- locate movement;
- carry limited messages where appropriate.

### Pack animal
- logistics;
- travel;
- cargo.

## 4. Combat

Most real animals can defend themselves, but pets should not become universal tanks.

Combat capability depends on:

- species;
- training;
- size;
- temperament;
- equipment;
- opponent.

## 5. Training

Possible learned behaviors:

- stay;
- follow;
- guard;
- track target;
- search resource;
- retrieve;
- avoid combat;
- attack on command.

## 6. Detection

Animals can make stealth richer.

Guard dogs may detect scent despite visual concealment.

Wind, rain, terrain and masking scents can matter.

## 7. Hunting

Hunting can draw on:

- tracks;
- droppings;
- feeding signs;
- blood;
- calls;
- habitat;
- wind;
- migration.

Skill should increase interpretation, not simply spawn arrows over animals.

## 8. Ecology

Creatures should occupy broad ecological roles:

- predator;
- prey;
- scavenger;
- herd;
- solitary;
- territorial;
- migratory.

Do not attempt a perfect ecosystem simulation before these roles create gameplay value.

## 9. Repopulation

Avoid obvious “mob respawn timer” presentation where possible.

Repopulation can represent:

- migration;
- reproduction;
- new pack arrival;
- faction reinforcement;
- nest/hive production;
- Other-related incursion.

## 10. Overhunting

Potentially allow local pressure to affect:

- encounter frequency;
- resource availability;
- predator/prey balance;
- settlement contracts.

Use bounds so the player cannot permanently destroy normal gameplay accidentally unless explicitly intended.

## 11. Pets and relationships

Animals can develop:

- trust;
- training;
- injury;
- fear;
- attachment.

Do not make every pet immortal by default, but consider accessibility/settings because permanent pet death can be unusually upsetting.

## 12. Design rule

> Animals should feel like organisms with useful behavior, not floating buff icons.

## 13. What is built (spec 43, first slice)

Spec 43's first slice is in the game: the grey wolf is tameable and the hound pup is bought. What exists, and where:

- **Taming** (`src/World/Runtime/Pets.cs`, rules in `src/Domain/Pets/Pets.cs`): one wolf can be taken with food. The offer names the food, spends it, and holds a channel (`content/config/pets.yaml`; 2.5 s) whose odds are fixed when the offer begins - the approach the character made (crouched/behind adds `unnoticed_percent`), the handling skill, the meat's own worth, and any wound the animal carries. The channel **breaks** if the animal is hurt, if it sees the character, or if the character steps away, and then that animal will not be offered food again for a day. An animal whose level is above the character's is refused ("beyond you"). The tamed slot stays empty in the world across saves.
- **The pup at Ashen Hollow**: `item.misc.hound_pup` is on Kera Voss's shelf beside four raw meat, and `item.pet_creature` on the item is what `AdoptPetCommand` reads. A pup is `creature.beast.hound_pup`, grows into `creature.beast.wolf_grey` in `grow_days` (two days away, or while the character sleeps through them), and adoption is the same record a tame makes without the offer.
- **The party cap**: one number, `config/party.yaml` `max_members` (now 4), shared by companions and animals (`PetParty.Refusal`). A badly wounded animal does not occupy a slot.
- **Orders** (owner ruling 4): wait here, guard here, scout ahead, pull one, lure to here, follow, attack my target. The key is `Y`, which raises the list and lays a ground marker where the ground-named orders would point; a number gives the order. **This is a deliberate exception to charter §3's "no markers in normal play"**, and it is the ruling that makes it: a tactical order has to name a place, and a place has to be shown.
- **A pet's own skills** (owner ruling 3): `bite` and `lure`, each with its own level and uses, levelling at content's `level_uses` (3, 8, 15) and stopping at 4. A bite hits harder per level; a lure reaches further and is resisted less per level.
- **A pet never dies** (owner ruling 1): at no health it is not killed - its condition becomes `Wounded`, it is at its home location (`location.outpost` for the hound), and it mends in content's `retire.days` (1-2 days, deterministic per animal). The HUD says how long, and the record is still the character's throughout.
- **Encounters scale to the party** (owner ruling 5): `PetSystem.Encounter` counts the party's strength (1 point for the first follower, a quarter for each level past its first) and `Encounters.Of` turns it into a percent at `15 % a point`, capped at 165, plus the levels that buys (`0.035` a percent). `CreatureSystem` applies it where a creature is placed, so health and the force of its blows are scaled and its level shown - authored counts are unchanged, so a full party meets a harder pack, not a longer one.
- **A lure is a real drawn creature**: `lure` sets the creature's mind to searching at the named point and stops it perceiving the party until it arrives or immunity runs out (content's `lure.immunity_ticks`). The lure's immunity is transient: it is not in the save.
- **The save**: schema 36 keeps the animals in the player document (`src/Persistence/SectionCodec.cs`, `SchemaV35.cs`, `Fixtures/v36/quick`). Version 35 and older migrate to no animals.

Not built yet, and named so nobody assumes it: the scout's **report** (scouting gathers the creatures ahead, and the HUD does not yet summarise them), the support and control roles' passive presence, the "focus the weakest"/flank/call-for-help encounter behaviours beyond lure resistance, and this document's §11 accessibility setting for pet loss. The animals are rendered as the world's own creatures (`PetsView` draws a pet from the creature's mesh), not as bespoke pet models.
