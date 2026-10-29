# Gate D parts 2â€“3: animations and face

Branch `npc/gate-d`, base `78c2f3d3`. Own checkout: `D:\Otherreach_Scratch\npc_gate_d`.
Status: parts 2 and 3 implementation FINISHED for Claude review; owner acceptance is pending.
Gate C snapshot `e3fcae86` was merged as `0064d926`; subsequent Gate C work remains separate.

## Runtime

The player and NPC native rigs share the complete acquired motion library. Role and mood
choose quiet idles; actual schedule activity chooses smithing, cooking, sweeping, carrying,
harvest, mining, reading and writing. Routine roles resolve through `RoutineDefinition.Key`,
which can differ from the routine record ID. Sleeping and seated performances use furniture
support; entry/exit, ground pickup, bow release and weapon-family performances use the
existing gameplay clocks. Unknown roles do not get an unrelated work animation. The smith
holds the acquired hammer at the existing anvil. Motion preview exposes the full catalogue
with source names. Standing talk uses standing sources; Mixamo Talking_1 remains available
as an acquired seated take. Optional clips join the actor's live FootPlants library.

All native faces retain blink, saccades and look-at, with seven authored dialogue moods.
Voiced dialogue and barks use their own playback clocks, text articulation and per-recording
20 ms envelopes (all 75 recordings staged). Loud consonants can still close the mouth.
Six acquired facial takes retain their original 100 Hz / 52 ARKit channels. The creator
previews mood expressions and facial takes without changing identity. The same speech-clock
API is available for player story dialogue; the existing opening is a narrator film and is
not relabelled as player speech.

## Source quality and native contract

The full import has 3,221 groups / 4,202 selected states, plus native supplements and contextual
aliases. Packaging adds the established locomotion aliases. Sources include UAL1/2, FAL,
594 skeletal Mixamo/Rokoko files, 2,548 CMU takes, 38 mobility FBXs, native Fab libraries,
ActorCore, Hyperhuman and Clownhuman, plus retained Kenney/Humanizer takes. Credits stay in
`content/credits.yaml`. The six face-only files are facial performances. The MakeHuman BVHs
are chiefly static poses; Ryzom's topology-specific facial morphs have no native ARKit mapping.
KayKit, creature rigs and weapon-only rigs do not become humanoid performance sources.

The original HumGen bodies, all 59 core rests/parents, and full materials/textures are kept.
Epic IK retargeting exports transports for the exact frozen body; Godot does no rest-frame
conversion or manual posing. The existing finger/feet/eye/jaw retarget contract remains.
Only planar Root travel is extracted. Original source playback rate, including its sign, is
retained. Atomic publication waits for rest, hierarchy, finger and motion checks.

A rejected proof exposed an imported reference-axis mismatch: Mixamo/Rokoko reference rigs
lay sideways while their animation was upright. Epic's height normalization then amplified
pelvis motion to 55â€“77 metres. Corrected source-only transports bake constant ancestor units
and hierarchy while verifying unchanged world joint/skin motion. Fresh Epic imports preserve
the original sampling rate (the old automatic importer reduced one 100 Hz source to 25 Hz).
Fractional final samples extend to the next sample boundary rather than being trimmed.
Two-source native probes now have correct metre-scale motion and original sampling. The
632-source repair is complete. All 61 vendor Unreal variants use their original native FBX
rigs: the Blender intermediary had a different pelvis-reference fault. Both full libraries
pass 4,211 transport checks and package 4,218 runtime states each, including standard aliases.
Their combined payload is 2,990,882,410 bytes.
An absolute hip-height check now rejects the formerly accepted bad transports.

## Evidence and remaining work

Focused speech/performance checks passed (16), as did the named-routine regression. The new
absolute-motion check rejects the old smith export and accepts the corrected sources. Latest
Presentation build/export succeeds. No local GPU was used; farm jobs use the dedicated 6900XT.

The full-town proof repeatedly lost its GPU device before capture. A focused studio harness
runs normal simulation, NPC schedules, dialogue, voice and the actual creator, using acquired
art and a real work station. Earlier studio captures were visually rejected for the source
scale error. They are not acceptance evidence. Both proofs are ready for Claude review:
- `F:/Otherreach_Captures/gate_d/outputs/gate_d_part2_work.mp4`: 8.01 seconds, all 240 frames
  inspected; Kera's normal smith schedule and native strike at her existing anvil.
- `F:/Otherreach_Captures/gate_d/outputs/gate_d_part3_face.mp4`: 13.77 seconds, all 253 frames
  inspected; Sel's actual voiced greeting, then creator neutral/warm/worried/curious expressions.

PNG compression runs off the game thread; encoding
retains actual frame timestamps and the owned voice offset, with silence during creator poses.
Studio evidence will be labelled explicitly and cannot establish full-world GPU acceptance.

HumGen shader-sidecar maps, including every hair-LOD flow map, now participate in the normal
signed texture-cache preflight. No source texture, mesh or LOD quality was reduced. Offline
source/transport archives are preserved separately; their size is not a shipped-package total.

D15 uses existing Ashen Hollow/Cairnwold work routines, stations and conversations. No new
skill, technique or lore system is introduced. The F1 baseline hooks check reports
52 placed / 106 missing / 99 unmodelled across nine areas; this is an older branch baseline,
not a replacement for the accepted D18 totals. Canon hooks and per-part evidence are on `npc/gate-d-performance`; accepted D18 dossier totals
are preserved. This changes presentation at existing places, with no new skills or lore.

The work proof exposed a contextual issue in the full 34-second smith take. Its scheduled
activity now repeats the genuine 10.02–12.84 second strike phase, crossfading between two
playback slots sharing the complete original animation. No keys, rotations or source takes
are removed. Kera's existing anvil stance is aligned to that phase. Eye aim is relative to
the animated head, avoiding a doubled head/target angle. Native pitch is preserved outside
the existing UAL head-level correction. Both proofs are ready for Claude review. Her measured Kal-height station instance has a 0.718 m
steel surface, with a choked wooden-haft grip and 0.1 m work-anchor adjustment. The shared world
and studio use the same bindings. No source keys, rest frames, arm poses, meshes or textures
were altered. The older Gate C outfit masking visible in these frames belongs to the separate
outfit lane; these are performance proofs, not an acceptance claim for that wardrobe snapshot.

At capture the engine reported 1,793.75 MiB video memory in the work studio and 2,918.02 MiB
in the face/creator studio. These are current studio readings, not world peaks or a package
budget pass. No new memory savings were introduced.
