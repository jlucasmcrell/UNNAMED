# VFX finish: formulas, bolt, Quiet Stones and the Tavar rescue (Phase B VFX lane)

Branch `claude/pb-vfx` from `a080596`. Presentation only: no simulation, quest, combat, timing or content-data change. Everything below is
drawn when the run draws with the Phase-B effects (`--visual vfx=recipes`, the default of every tier); `vfx=flipbooks` (and `phase_a`)
keeps the a080596 look, so each change is reversible from the command line.

## What changed

| Effect | Before (a080596) | Now |
|---|---|---|
| Brace Ward | a flat camera-facing ring (flipbook quad) | a faceted lattice shell (icosahedron split to 80 facets, stretched to the body) drawn as fine edges and nodes, brightest at its silhouette, the far side fainter; depth-tested, so the body occludes its back and it occludes the body's front. It assembles from the feet up (0.45 s), turns slowly, a light climbs its edges; a thin inscribed circle at its foot. A blow on the braced body rings across the facets from the side it came from; in its last 1.5 s it wavers; when it ends the facets break outward and fade (0.6 s). |
| Mending Thread | faint pale strands at the feet (camera-facing strips) | seven warm threads (gold head, green tail) spiralling in from about a metre out, each drawn along its length again and again, finishing round the chest as a row of slanted stitches; soft warm light on the body while it works; warm motes. Threads are ribbons of geometry turned to the eye along their own length - nothing to cut off at a frame edge - with a minimum on-screen width, and they fade out between 1.3 and 0.6 m from the camera (a collapsed or first-person camera); the ward's facets likewise between 0.8 and 0.25 m. |
| Cast charge (all three formulas) | a small flipbook quad at the hand | an inscribed glyph before the casting hand, drawn round as the formula gathers: rings, ticks and a turning polygon (Force 3 sides, Warding 4, Vital 6, each in its formula's ink); on release it flashes once and widens. |
| Impulse Bolt in flight | the recipe's flipbook particles, which left a dotted string of blue blobs | a hard cold core stretched along its path inside a ring of displaced air (a screen-space lens with a fine ticked edge), and a continuous streak (white core in a deeper-blue wash) along its path. |
| Impulse Bolt impact (hit or miss) | cold flipbook puffs | a 0.11 s white-blue flash, a pressure-front shell of displaced air that swells to about a metre, an inscribed circle thrown square to the bolt's path, and a dozen hard points stopped short; a quick light. The same on a creature, a rock or the end of its reach. |
| Quiet Stones | set: a pale rim appears | while out of line each stone stands about fourteen degrees off square ("a hand's width round") with a faint cool double of it a hand's breadth along the ring. Turned: it grinds round into square (1.3 s) while its double slides back into it, and the set light comes up with a brief settle. The stone's own resonance is heard at the stone, arriving 0.45 s late while the fold stands (the done text: "a sound that arrives a moment late"). |
| Heart / rescue transition | the barrier pops off; Tavar's double fades over 2 s | the heart's double registers and its light comes up with the same settle; one faint pulse runs out across the ground (a wall of light with a line where it meets the ground, depth-tested); the fold's doubled view jolts once and closes into register, then thins away (2.4 s); Tavar's double snaps back into him and his displaced shadow slides home under him (1.1-1.3 s), then his own shadow takes over; the resonance is heard on time. |
| Tavar in the fold | a violet double and a delayed shadow (invisible while he stands still) | the same double, and a shadow that stands 0.3 m aside across the sun even when he is still, so it visibly does not meet his feet. |
| Aftermath | - | the stones that rested on nothing round the heart come down as the pulse passes them; the stones and heart hold their set light; Tavar is an ordinary NPC: spoken to, recruited, following. |

Code: `src/Presentation/Art/FormulaForms.cs` (new: the ward, weave, glyph, bolt core and shock), `Greybox/FoldscarRegister.cs` (new: stones,
heart, fold, pulse, pebbles), `Greybox/FoldEcho.cs`, `Art/MagicEffects.cs`, `Greybox/ProjectilesView.cs`, `Greybox/HollowView.cs`,
`Greybox/FoldscarDressing.cs`, `Greybox/Palette.cs` (two uniforms on the fold shaders, defaults unchanged), `Audio/SoundEvents.cs` +
`Art/ArtBindings.cs` + `art_bindings.json` (`audio.switches`), `Art/vfx_recipes.json` (`form:` entries and per-formula ink), `Main.cs`
(effect time left, blows on the character, flag changes in play). Harness: `Showcase.cs` scenes `formulas`, `bolts`, `mending`, `rescue`.

## Rules, quests and telegraphs untouched

The before runs (a080596 presentation + the same showcase script) and the after runs issue the same commands at the same frames; their
simulation event logs are identical line for line - 33 events in `formulas` (every hit and its damage, the second bolt's fizzle, the
ward, the boar's charge and gores, the kill) and 17 in `rescue` (each stone's flag, the heart, every objective, quest complete at 170.10 s,
Tavar joins at 175.68 s). The boar's alert marker stays visible through the ward; the bolt's impact flash lasts 0.11 s.

## Cost

Per ward: one 240-vertex mesh and a quad. Per mending: one 5,000-vertex ribbon mesh (built once, animated in its shader), one shadowless
omni light, 20 motes (was 22 strands + 30 motes). Per bolt: a sphere and a quad in flight (was 36-48 flipbook particles), at impact a
sphere, a quad and 12 points (was 16 flipbook particles). Stones: one faint copy of each stone's full-detail mesh while unset. The
release pulse: one open cylinder for 2 s. Screen-texture reads (bolt lens, shock shell, pulse) share the frame's single screen copy that
the fold already uses. Nothing was measured on the GPU in isolation (the display was shared); no frame-rate change was visible in the
60 fps Movie Maker runs.

## Captures

Showcase scenes driven by the player's own commands (`--showcase <dir> --showcase-scene formulas|bolts|mending|rescue`), recorded with
Godot's Movie Maker at 1920x1080, 60 fps, tier high, by `G:\UNNAMED_HISTORY\phaseB\vfx_lane\record_showcase_lane.ps1` inside a ledger slot
(`capture_slot.ps1`). Full recordings and state logs: `G:\UNNAMED_HISTORY\phaseB\vfx_lane\showcase\` (`before2_*` = a080596 presentation,
`after4_*`/`after6_*` = this branch). Clips here (H.264):

| Clip / still | What it shows |
|---|---|
| `ward_after.mp4` / `ward_before_a080596.mp4` | Brace Ward worked, held (behind, beside, close at 2 m), walked through its expiry and break. Same script, same frames. |
| `ward_hold_side_break.jpg` | the lattice at 4 m, beside at 4.8 m, and breaking apart at its expiry. |
| `mending_after.mp4`, `mending_4_5m_6m_3m.jpg` | Mending Thread in the open field at 4.5 m, 6 m and 3 m, then walked on (scene `mending`). |
| `mending_formulas_window_after.mp4` / `..._before_a080596.mp4` | the same mending in the formulas scene, including the collapsed camera against the longhouse post. |
| `bolt_and_charge_after.mp4` / `..._before_a080596.mp4` | cast glyph, Impulse Bolt at the boar from 42 degrees (hit), a second bolt that fizzles (the rule's own fizzle), then the Brace Ward as the boar charges: its blows ring the ward. |
| `bolt_hit_and_miss_after.mp4` | from behind: a bolt into the boar, then one wide that bursts at the end of its 20 m reach (scene `bolts`). |
| `bolt_impact_before_after_15m_crop.jpg`, `ward_under_the_charge_before_after.jpg` | 2x crops: the impact at 15 m; the charge landing on the ward, the boar's alert marker still visible. |
| `rescue_stones_after.mp4`, `stone_out_of_line_then_set.jpg` | the three Quiet Stones turned one by one (north, south-west, south-east), 4.4 m. |
| `tavar_in_the_fold_4m.jpg` | Tavar held: the violet double and the shadow standing off his feet. |
| `rescue_transition_after.mp4` / `rescue_transition_before_a080596.mp4` | continuous: up to the fold (Tavar out of true at 4 m), back to the heart, the heart steadied, the release. |
| `release_sequence.jpg` | 0.1 s, 0.3 s and 0.9 s after the heart: the pulse line on the ground, Tavar's double snapping in, the fold gone and the heart lit. |
| `rescue_aftermath_after.mp4` | Tavar spoken to (quest complete), asked along, walking with the character past the settled stones. |

Final code for every clip: the ward/mending/bolt windows are from `after6_formulas` (the committed code). `after4_rescue`,
`after4_bolts` and `after4_mending` were recorded before the last change, a fade of the ward and threads within 1.3 m of the camera; no
effect in those clips comes within 1.3 m of the camera, so they are what the committed code draws.

## Blind external review (advisory)

`G:\OtherreachTools\visual-qa` (`qa.py blind --seed 20260926 --rubric vfx`, `review --provider gemini --model gemini-3.8-flash`,
`reveal --expected gemini`), before (a080596) against after on the same windows at 854x480. Gemini only: Pegasus was unavailable for
this remediation and was not called. Files: `reviews/<window>/`. In every run A was this branch and B the baseline.

| Window | This branch (A) | Baseline a080596 (B) | Notes, checked against the footage |
|---|---|---|---|
| ward (after4, and again on the final code: `ward_final`) | framing pass, camera pass | framing **defect** (a planar ring that intersects the character flatly), camera **defect** (a billboard that turns with the camera) | Both runs agree. The reviewer's "tavar" pass on both is a misreading: the ward window has no Tavar state; the cage it calls trapped/freed is the player's own ward. |
| mending (after4) | framing **defect**: "ribbon renders over the foreground wooden pillar" | framing pass | Checked: 31.5-33.6 s the camera is jammed at 0.20 m against the longhouse post; the thread loops (1.25 m out) pass between the eye and the post - drawn over it, as geometry nearer than the post. Fixed by fading ward and threads within 0.6-1.3 m of the camera. |
| mending (final code: `mending_final`) | framing pass, camera pass | framing pass, camera pass | "equivalent Mending framing". The reviewer does not prefer either. |
| bolt + charge (after4) | framing, camera, telegraph pass | framing, camera, telegraph pass | "readable telegraphing" for both; it describes only the ward (faceted mesh vs soft planar glow) and says nothing about the bolt or its impact in either. |
| stone (after4 rescue) | camera pass, rest not_observable | same | The reviewer did not register the stone's turn, double or set light in either clip. |
| release (after4 rescue) | camera pass, telegraph "defect", tavar not_observable | same | The "telegraph defect" is about the player's sword idle, not combat. The reviewer did not identify Tavar, the fold, or the release in either clip. |

So the external review supports the Brace Ward change clearly, is neutral on the Mending Thread and the bolt, and gives no evidence either
way on the Quiet Stones or the rescue transition (it did not see them). Owner review is still required.

## Remaining defects (stated plainly)

- **The rescue reads only up close.** The release is restrained by design, but at the normal 10 m from Tavar in daylight the pulse is a
  faint line, Tavar's double snapping back and his shadow sliding home are a few pixels, and the fold's last jolt is barely visible. The
  blind reviewer did not identify the release at all. It reads at 4 m (`tavar_in_the_fold_4m.jpg`) and in the 2x crops; not convincingly
  at 10 m.
- **The stones' misregistration is subtle.** At 4.4 m the off-square turn reads as a grinding rotation; the faint double is visible in
  crops but easy to miss in motion. The reviewer did not register it.
- **The bolt is quick.** At 38 m/s it crosses 15 m in 0.4 s; from behind the player the core and the air ring are small and the streak does
  most of the work. The impact (flash 0.11 s, pressure front and circle 0.38 s) reads at 15 m in crops and is modest at full frame.
- **The mending at a collapsed camera.** With the camera jammed against a wall (0.2 m), the outer thread loops still pass between the eye
  and the scenery beyond 1.3 m: correct depth, but it can look drawn over a nearby post. First person was not captured.
- **The heart's set light** (the existing a080596 glow shell) coats the whole heart in a pale sheen once steadied; I only added its settle
  pulse. Too strong for "the ring goes quiet", in my view; left as it was.
- **The area is only visually calmer.** After the release the floating stones come down and the stones hold their light; the Foldscar's
  ambience bed and details are unchanged (V3 has no calmer bed; the `anomaly_fold` hum hook is still awaiting audio). The stones' sound on
  setting is the V3 `stone_resonance` detail, which also plays at random in the cell, so it may not read as the stone's own. Audio was
  verified only by the audio coverage counts (resonance requested 9 times against 5 before), not by listening.
- **The ward's last-1.5 s waver** (a per-facet flicker) was not reviewed separately and may read as a glitch rather than a warning.
- **Cost not measured in isolation.** No separate GPU measurement was taken (shared display and GPU).
- **Pegasus** was not used (unavailable for this remediation); Gemini is a single reviewer.
