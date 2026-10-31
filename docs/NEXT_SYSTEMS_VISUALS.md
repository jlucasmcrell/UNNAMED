# CMB-16 visual work — IN PROGRESS

Branch: visual/cmb16-animations, based on systems/shove-trip-disarm ef0eaeb8. Next package only.

The control view now carries its committed move and phase timing independently of the held weapon. Player clips follow those phases; player/creature prone clips follow the domain's one-second footing clock. Event cues cover off-balance stumble, prone hit, disarm drop and player ground-weapon pickup. A missing exact clip returns false; no standing hit is relabelled as a prone hit. Gameplay and persistence are unchanged.

Native source selection: tools/animation/cmb16_sources.json plus the owned CMU source receipts under D:/Otherreach_Scratch/codex_systems_visuals_20261031/cmb16. Epic IK/GLTFExporter preserves the original Ada/Mateo native rigs. The original six-state/46-state merge was an older-target experiment, not a current delivery. Farm_Harvest was rejected for pickup. The current native Ada candidate uses the actual staged body hash 0ec0339d and the actual 49-state baseline hash 5e15b8a4; readback preserves all 98,429 original keys and metadata and adds seven states (56 total). An adjacent older 50-state sidecar does not describe those actual baseline bytes. No accepted release payload was overwritten.

The public CPU tools preserve the CMU archive bytes, normalize the source scene's centimetre units, calibrate the source reference hip height through Epic's native SOURCE pose, and reject kilometre-scale root errors. The first uncalibrated CMU exports are rejected. Native pickup uses the owned 69_70 squat/pickup take, cropped to2.1–4.6s, with whole-Root vertical fitting against the actual native boots. Native source captures01/02 disclosed a mismatched prone-reaction pose, loose tunic penetration, and a shader/readback stall that invalidated the second shove recording. They are not final acceptance. A candidate03 offline edit layers the recorded ground-recoil rotations over the native facedown prone base, preserves root/lower-body motion, and starts/ends on the base. Its native review remains pending; no standing hit was renamed. The new edit's input hash and selected joints are embedded in its resource metadata.

Current native candidate/proof scratch: D:/Otherreach_Scratch/codex_systems_visuals_20261031/cmb16/current_candidate01. Source-only fixtures retain actual wall-time frames, native clothes, bindings and assembly hashes. Neutral-floor source review does not prove ordinary control timing, prop continuity or a creature-family rig. Contact minima alone do not prove body support: a hanging tunic can touch the floor while the body floats. No cloth-minimum audit is treated as that visual verdict.

The ground-item presentation now uses each weapon's native held-art model when it has no dedicated ground binding, laid flat along the terrain slope. The committed item-move event transfers the actual held mesh out of the hand, animates its fall to the native ground model's exact transform, then reveals the settled model. It does not change item ownership, commands, simulation positions or persistence. Dropping a spare of a still-equipped definition leaves the held weapon attached. Ordinary refreshes retain an active fall; collecting the item cancels that visual. Pickup hand contact and the dedicated release pose remain unfinished.

Native headless gate at D:/Otherreach_Scratch/codex_systems_visuals_20261031/cmb16/drop_continuity01/cpu_native01/CPU_NATIVE_DROP.json exits0 after59.85s: actual GameSession inventory commands, actual production DrawGroundItemMove, same native held-node instance transferred into the fall, native weapon settled on the ground, no art problems. Its neutral-floor render fixture is prepared; this numerical/setup gate does not prove a landed disarm, rendered prop continuity or pickup.

## Still required before FINISHED

- Source and judge actual trip, disarm, hit-while-prone and weapon-release motion; the six existing candidates are not substitutes for those missing takes.
- Complete native source/rig work for creature families. No humanoid clip is bound onto a quadruped or spider.
- Review source timing/contact marks (Push_Loop and Farm_Harvest are candidates), and the one-second fall/idle/rise continuity in real time.
- Pack/audit Mateo and current approved body variants; confirm the latest accepted target hashes before shipping any library.
- Ground-weapon hand/prop continuity, rendered control outcomes and prone damage, full clothing/weapon stress review.
- Contact sheet or real-time native in-game evidence to Claude, then a precise FINISHED receipt. None has been claimed.

Validation: ControlMotionTests 1/1; TheDrawnControlMoveCarriesItsOwnTiming_AndRetainsTheHeldWeapon 1/1; Release presentation build 0 errors. Source migration retains acquired source hashes; Epic may serialize destination uassets, which have separate hashes. No source artwork was sent to an image-generation service.
