# OTHERREACH — HUD, Input & Action Access

**Status:** Strong working direction  
**M2 impact:** None

## 1. Core rule

> **A hotbar is a convenience layer, not a capability limit.**

If a character knows a spell or carries a usable object, its existence must not depend on whether it occupies one of ten boxes on screen.

## 2. Contextual actions

Immediate controls should follow what is physically in hand.

Examples:

- sword → sword attacks/techniques;
- bow → draw/aim/fire/ammunition actions;
- wand → casting/focus actions;
- shield → block/bash;
- tool → tool actions.

## 3. Access methods

Support several simultaneous ways to reach abilities:

- direct keybinds;
- favorites/hotbar;
- radial menus;
- searchable spell/ability list;
- equipment sets;
- contextual actions;
- controller-friendly layers.

Players who like many keybinds should not be artificially capped. Controller users should retain the same underlying capability through context actions and direct bindings; a radial is only ever an optional mirror of actions reachable without it, and no core action requires one (M7 reconciliation (2026-09-24); owner ruling 5, `PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md` §20).

## 4. Fundamental resources

Current preferred baseline:

- **Health** — bodily condition;
- **Stamina** — exertion;
- **Focus** — concentration/precision/mental load where relevant.

Do not commit to universal “mana” yet.

## 5. Resource channels

Specific disciplines/items may add contextual resources:

- magical reserve;
- stored charges;
- heat;
- ammunition;
- blood;
- reagents;
- batteries;
- resonance;
- divine favor;
- Other energy.

The HUD displays channels when relevant instead of permanently showing every possible bar.

## 6. Gear switching

During combat:

Allowed/expected:
- drop weapon;
- draw sword;
- switch bow/wand;
- swap shield/offhand;
- use accessible consumable.

Restricted by time/accessibility:
- digging through deep storage;
- changing full armor sets;
- extensive refitting.

Special equipment can deliberately bypass ordinary switching limits.

## 7. HUD philosophy

Display enough information to support skilled play without omniscience.

Information can depend on character knowledge:

- known armor gaps;
- identified creature weakness;
- map certainty;
- tracked target;
- current wound.

## 8. Minimal persistent HUD

Prefer configurable display over mandatory clutter.

Possible persistent elements:

- health;
- stamina;
- current contextual resource;
- current weapon/ammunition;
- immediate status effects;
- target feedback where justified.

Everything else can be contextual or user-configurable.

## 9. Accessibility

Plan from the beginning for:

- scalable UI;
- remapping;
- hold/toggle options;
- color-independent indicators;
- subtitle controls;
- combat assists where appropriate;
- controller parity.

Accessibility options should not be confused with world difficulty.
