# CMB-16 visual work — IN PROGRESS

Branch: visual/cmb16-animations, based on systems/shove-trip-disarm ef0eaeb8. Next package only.

The control view now carries its committed move and phase timing independently of the held weapon. Player clips follow those phases; player/creature prone clips follow the domain's one-second footing clock. Event cues cover off-balance stumble, prone hit, disarm drop and player ground-weapon pickup. A missing exact clip returns false; no standing hit is relabelled as a prone hit. Gameplay and persistence are unchanged.

Native source selection: tools/animation/cmb16_sources.json. Six candidates were retargeted with Epic IK/GLTFExporter onto the original Ada and Mateo rigs in D:/Otherreach_Scratch/codex_systems_visuals_20261031/cmb16. Ada's numerical audit and six-state Godot supplement pass. The candidate merge retains all 46 baseline states and all 96,209 original keys byte-for-value, adding six states. Both targets' source bodies are unchanged. This is an isolated *older exact-target* source review, not installation into the current release payload.

## Still required before FINISHED

- Source and judge actual trip, disarm, hit-while-prone and weapon-release motion; the six existing candidates are not substitutes for those missing takes.
- Complete native source/rig work for creature families. No humanoid clip is bound onto a quadruped or spider.
- Review source timing/contact marks (Push_Loop and Farm_Harvest are candidates), and the one-second fall/idle/rise continuity in real time.
- Pack/audit Mateo and current approved body variants; confirm the latest accepted target hashes before shipping any library.
- Ground-weapon hand/prop continuity, rendered control outcomes and prone damage, full clothing/weapon stress review.
- Contact sheet or real-time native in-game evidence to Claude, then a precise FINISHED receipt. None has been claimed.

Validation: ControlMotionTests 1/1; TheDrawnControlMoveCarriesItsOwnTiming_AndRetainsTheHeldWeapon 1/1; Release presentation build 0 errors. Source migration retains acquired source hashes; Epic may serialize destination uassets, which have separate hashes. No source artwork was sent to an image-generation service.
