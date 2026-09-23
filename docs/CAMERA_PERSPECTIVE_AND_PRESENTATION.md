# OTHERREACH — Camera, Perspective & Player Presentation

**Status:** Owner-approved direction  
**Implementation relevance:** M3  
**Core decision:**

> **Otherreach is a full-body third-person / over-the-shoulder RPG with a continuous player-controlled camera that can zoom seamlessly into first person. Neither perspective changes authoritative gameplay rules.**

The full-body character is the primary world representation.

First-person-specific presentation is added only where necessary for visual quality, comfort, or precision.

---

## 1. Perspective Is a Spectrum

Conceptually:

```text
FIRST PERSON
    ↓
CLOSE SHOULDER
    ↓
NORMAL THIRD PERSON
    ↓
DISTANT THIRD PERSON
```

The mouse wheel/controller equivalent adjusts distance continuously.

The exact distance ranges are tuning values, not architecture.

---

## 2. Default Camera

Recommended default:

- medium third-person;
- slightly elevated;
- modest over-the-shoulder offset.

The player can configure:

- distance;
- left/right/center shoulder;
- horizontal offset;
- vertical offset;
- FOV;
- sensitivity.

---

## 3. Shoulder Swap

Provide a quick shoulder-swap control.

Useful for:

- ranged aiming;
- corners;
- environmental visibility;
- player preference.

Potential modes:

- left;
- right;
- center;
- optional dynamic.

Dynamic camera movement should default conservatively because automatic camera shifts can be uncomfortable.

---

## 4. Indoor Camera

Camera collision should compress distance naturally indoors.

Possible player option:

- compress distance only;
- allow automatic transition to first person;
- never automatically enter first person.

Do not force a perspective change on players who dislike it.

---

## 5. Camera Collision

The third-person camera must not become a surveillance drone.

It cannot:

- pass through walls;
- orbit arbitrarily far around corners;
- reveal spaces the camera physically cannot reach.

Handle obstruction through:

- collision;
- gentle reposition;
- selective foliage fade;
- near-object fading.

Avoid constant camera snapping.

---

## 6. Authoritative Gameplay Is Perspective-Neutral

Both perspectives use the exact same:

- combat;
- hit resolution;
- projectiles;
- weapon reach;
- armor;
- weak points;
- interaction rules;
- spell effects.

Perspective affects presentation/input mapping only.

---

## 7. Full-Body Authority

Animation architecture assumes:

```text
AUTHORITATIVE FULL BODY
        ├── third-person presentation
        └── first-person presentation
```

Do not build an FPS-only character and bolt a world body onto it later.

---

## 8. First Person

Early implementation should reuse the actual body where possible.

Potential local presentation:

- hide head/hair;
- prevent camera clipping;
- retain visible torso/legs when looking down;
- use the actual equipped item.

Do not begin with a completely separate FPS animation library unless tests prove it necessary.

---

## 9. Hybrid First-Person Viewmodel

Some actions may later require a dedicated first-person presentation clip/viewmodel.

Examples:

- bows;
- close weapon manipulation;
- fine inspection;
- scopes/lenses.

The authoritative world action remains the same.

A separate view animation never changes gameplay timing/rules independently.

---

## 10. Equipment Synchronization

First and third person must show the same fictional equipment.

A custom weapon remains the same item:

- materials;
- components;
- damage state;
- attachments;
- enchantments.

First person may adjust camera-relative placement/presentation but never substitute a generic unrelated object.

---

## 11. Ranged Aiming

Holding aim can:

- move camera closer;
- use shoulder offset;
- narrow FOV slightly;
- show reticle;
- engage aim IK.

Release returns toward prior camera distance.

---

## 12. Melee

Third person is especially valuable for:

- weapon reach;
- multiple enemies;
- shields;
- polearms;
- grappling;
- positioning.

First person remains fully viable for players who prefer immersion.

Do not balance melee differently by perspective.

---

## 13. Race Identity

Third person allows racial morphology to remain visible:

- Kal wings;
- Orenth phase displacement;
- Mor body approximation;
- Ondrek mass/geology;
- Vaskaal gait/proportions;
- Constructed reconfiguration.

This is one reason third person is the primary presentation.

---

## 14. Armor / Clothing / Crafting Value

Visible character presentation makes:

- armor;
- clothing;
- custom equipment;
- damage;
- transformations;

more valuable to the player.

The camera decision supports the game's unusually deep equipment system.

---

## 15. Mounts / Traversal

Third person is recommended for situational awareness during:

- mounts;
- carts;
- gliding;
- unusual traversal;
- boats.

Players can still zoom inward.

---

## 16. Building

Building may permit a somewhat farther camera than ordinary exploration.

Do not turn it into an unrestricted RTS camera.

Camera distance must remain bounded to avoid scouting abuse.

---

## 17. Dialogue

Default dialogue should occur in-world without forcing a cinematic camera mode.

Important authored scenes may use directed cameras where justified.

The player's current perspective can otherwise remain intact.

---

## 18. Targeting

Do not make combat dependent on MMO tab-targeting.

Optional soft focus/target assistance can support:

- controllers;
- accessibility;
- ranged aim;
- spell selection.

This is assistance, not an authoritative “selected mob” requirement.

---

## 19. UI

Both perspectives share core HUD.

First person may emphasize:

- reticle;
- weapon state;
- interaction.

Third person may use:

- soft focus indicators;
- broader spatial cues.

Do not create two independent game UIs.

---

## 20. Contextual Camera Memory

Optional settings may remember preferred distance by activity:

- on foot;
- ranged aim;
- mount;
- building;
- indoor.

Manual player zoom always overrides.

---

## 21. Accessibility

Supporting both perspectives can help players with:

- first-person motion sickness;
- third-person camera discomfort;
- spatial-awareness needs.

Required options should include:

- FOV;
- camera distance;
- shoulder;
- camera shake;
- head bob;
- motion blur;
- sensitivity;
- inversion;
- auto-camera behavior.

---

## 22. M3 Minimum Implementation

M3 should prove:

- full-body third-person controller;
- seamless zoom to first person;
- shoulder swap;
- camera collision;
- interaction targeting at multiple distances;
- same authoritative movement/interaction commands in both views;
- no presentation state writes;
- basic animation/body visibility.

Polish is not required.

---

## 23. M3 Frame-Budget Spike

RK-02/D-01 wording must be updated from “first-person frame budget” to a **representative player-camera frame budget**.

The isolated 2×2 km greybox spike should measure:

- primary third-person view;
- first-person view;
- camera obstruction path;
- representative animated actor load where available.

The engine decision should not be validated only from the cheapest camera path.

---

## 24. Animation Integration

Animation Wave 0 should assume full-body authority.

Required proof eventually includes:

- locomotion;
- combat action;
- shared full-body rig;
- first-person camera on the same character;
- dedicated FPS override only if required.

IK and additive layers remain central.

---

## 25. Foundational Rule

> **Perspective is a player-controlled way of inhabiting the same authoritative body and world, not a choice between two different combat games.**
