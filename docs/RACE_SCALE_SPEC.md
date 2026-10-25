# Race scale: spec (RC d8 follow-up, 2026-10-23)

Statures are canon in `docs/RACES.md` ("Adult stature", owner-approved 2026-10-23): Siann 1.40-1.55 m, Veth and Orenth 1.60-1.90 m,
Kal 1.50-1.75 m (very broad), Constructed 2.10-2.30 m, Vaskaal 2.20-2.50 m, Ondrek 2.00-2.60 m, Mor varies. Today the game assumes one
human: a fixed eye height (`Avatar.EyeHeight` 1.62 m, crouched 1.05 m), one capsule (`base_speeds.yaml` `body_radius_m` 0.35), one reach
(`interact_reach_m` 1.6), one navigation agent (`navigation.yaml` `agent_height_m` 1.8), one jump (`jump_apex_m` 1.15) and one speed
(`base_m_s` 3.2). This spec says what changes so the whole canon range plays correctly. Not in RC d8.

## 1. One number drives everything: the body's stature

- A character's stature `H` (metres, the creator's height) is saved on the character record and is the only scale input. Every value
  below derives from `H` and the race's build, never from a per-race table of hand-tuned numbers.
- Race content (`content/races` or the `people` files) gains `stature_m: [min, max]` from RACES.md and `build: slight | standard | broad |
  heavy | lean_long | nonphysical`. A content lint rejects a body preset or NPC whose height is outside its race's range.
- Mor: `stature_m` is the remembered form's range, defaulting to the Veth range; the fold-sickness look never changes collision.

## 2. World clearances (the 2.6 m Ondrek is the design body)

| Space | Minimum clear height | Minimum clear width |
|---|---|---|
| Public building doors (inn, smithy, lodge, waystation, granary) | 2.9 m | 1.3 m |
| Public building ceilings and beams | 3.0 m | - |
| Private houses | 2.4 m door / 2.6 m ceiling (tall races stoop: see 4) | 1.0 m |
| Ruins, Barrowworks corridors | 2.8 m (authored exceptions marked `low_passage`) | 1.2 m |
| Waystone rings, bridges, cave mouths on any quest route | 3.0 m | 1.6 m |

- A quest or main-route path must be walkable by every playable race. A `low_passage` is allowed only off the critical path, and the
  navigation check (NavEditCheck) gains a per-agent-height pass: 2.6 m for routes, the race's max for optional spaces.
- Existing assets: audit doors and beams with a capsule sweep at 2.6 m; raise lintels by rescaling the door kit, not the building.

## 3. Collision capsule

- Radius `r = 0.35 * (H / 1.75) * buildWidth`, buildWidth slight 0.9, standard 1.0, broad 1.25 (Kal), heavy 1.3 (Ondrek), lean_long 0.85
  (Vaskaal). Height = `H`. Clamp r to 0.28-0.55 m.
- Creatures and NPCs use the same rule from their own stature.
- Navigation keeps one baked graph; the follower checks clearance with the agent's own radius and height (corner reach scales with r).

## 4. Camera and first person

- Eye height = `0.925 * H` (1.62 m at 1.75 m); crouched = `0.6 * H`.
- Third-person orbit distance and shoulder offset scale by `H / 1.75`, clamped 0.85-1.35, so a Siann is not swallowed by grass and an
  Ondrek's head is not cropped. FOV is unchanged (player setting).
- In a space whose ceiling is below `H + 0.1`, tall bodies stoop: eye height lerps to `ceiling - 0.25` over 0.3 s, walk speed x0.8.
  This is a presentation pose plus the speed factor; collision height becomes `ceiling - 0.05` while stooped.

## 5. Reach and interaction

- `interact_reach = 1.6 * (H / 1.75)`, clamped 1.3-2.2 m, measured from the body as today.
- Melee reach adds the same scale to the weapon's own length. Vaskaal long limbs: +10%.
- High shelves and wall levers are authored at 1.5 m so a Siann (1.40 m) still reaches them (reach is horizontal plus vertical).

## 6. Stride, speed and jump (no foot slide)

- Locomotion clips are authored for a 1.75 m body. Playback rate = `(worldSpeed / clipPace) * (1.75 / H)`, so stride length scales with
  `H` and the feet stay planted; `motion_paces` in the HumGen libraries already give clipPace in m/s.
- Base speed = `3.2 * (H / 1.75)^0.5` (taller is a little faster, not proportionally); walk/sprint percentages unchanged. Kal broad build
  -5%, Ondrek heavy -10%, Vaskaal +5%. These are content numbers per build, tuned in playtest.
- Jump apex = `1.15 * (H / 1.75)`, clamped 0.95-1.4 m. Mantle/step height = `0.45 * (H / 1.75)`.

## 7. Character creator

- Height slider per race over its `stature_m` range, default at the range midpoint; the HumGen height/build sliders map to it directly
  (HumGen bodies are scaled at export, not at runtime, for the named NPCs).
- The slider shows the height in metres and a silhouette against a 1.75 m reference.

## 8. Gear fit families

| Family | Races | Notes |
|---|---|---|
| standard | Veth, Orenth, Mor (default) | today's garments |
| compact_broad | Kal, Siann (slight variant via scale) | wider torso, shorter limbs; Kal wing slits on torso pieces |
| tall_narrow | Vaskaal, Constructed | longer sleeves/legs, narrow shoulders; extra joint gaps |
| heavy | Ondrek | plate and wraps only; no fitted cloth |
| non_physical | Mor (remembered form) | wears what the form remembers; uses standard meshes |
| modular | Constructed | segmented plates; refits by swapping segments |

- Each item carries `fit: [families]`. Equipping outside them is refused with a short reason ("This was made for a broader frame").
- Crafters refit between adjacent families (standard <-> compact_broad, standard <-> tall_narrow) for a cost in time and material; heavy
  and modular need a specialist.
- Within a family, garments scale with `H` (HumGen fitting per body for NPCs; runtime bone scale for the player).

## 9. NPCs and batch generation

- HumGen batch generation produces per-race bodies at statures drawn from the race range (DeepSeek's tools/humgen), with the existing
  race shaders (people_stone / people_phase / people_reflective). Named NPCs keep authored heights inside their race range.

## 10. Order of work and tests

1. Stature on the record + race ranges + lint (save schema bump; migration gives existing characters 1.75 m).
2. Capsule, eye height, reach, jump from `H` (pure functions in Domain/Spatial with unit tests at 1.40 / 1.75 / 2.60 m).
3. Clearance audit and NavEditCheck per-height pass; fix doors on routes.
4. Stride playback scaling (video gate: no foot slide at 1.40 and 2.60 m, real time).
5. Creator slider; fit families and refit recipes.

Gate: real-time video of a 1.40 m Siann and a 2.60 m Ondrek walking the Ashen Hollow -> Cairnwold route, entering the inn and the
lodge, interacting with a chest and a door, with no clipping, cropping or sliding.
