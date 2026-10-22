# RC d6 inn poses and animation-timed footsteps

Both character casts share these presentation changes. Native MetaHuman rest poses, hierarchy, binds and body buffers are untouched; body IK remains off. MPFB motion is exported onto its own 53-bone target through Epic IK Retargeter, never retargeted in Godot.

## Inn

Five existing free source clips are appended: UAL1 Sitting_Enter, Sitting_Idle_Loop, Sitting_Talking_Loop, Sitting_Exit, and UAL2 LayToIdle. The first frame of LayToIdle supplies the sleeping rest pose; it is deliberately held, not presented as a breathing loop. The two sitting idle/talking clips loop. Scheduled stationary NPCs use the nearest unoccupied bed/stool; render-only offsets align their posed pelvis/back to the furniture surface. Moving, downed and recruited companions do not receive these rest poses. Renn has a separate north-wall cot, leaving all three guest beds available.

Export with OTHERREACH_INN_CLIPS=1 using retarget_native_animations.py / retarget_mpfb_motion.py. Build native libraries in append mode. The MPFB builder supports OTHERREACH_MPFB_ANIM_MANIFEST, OTHERREACH_MPFB_LIBRARY_FOLDER and OTHERREACH_MPFB_LIBRARY_SOURCE for appending to a separate output. All 27 runtime libraries have 42 states; all 999 original animation key hashes are unchanged. Native 342 rests/parents match; the five MPFB base rigs and ten named variants match their original 53 rests (maximum position discrepancy 0.000557 mm).

Current external native runtime for packaging: D:\Otherreach_Scratch\rcd6_inn_20261021\runtime. Do not copy back into frozen RC d3 or RC d5 runtimes. MPFB libraries are versioned under Art/MPFBMotion. The append identity receipt is D:\Otherreach_Scratch\rcd6_inn_20261021\evidence\append_identity.json.

## Footsteps

Offline measured ankle-height minima are metadata on the existing motions. FootPlants creates per-instance method tracks from that metadata, preserving original source tracks. The AnimationTree reports the dominant gait so only its contacts produce sound; normal surface/gait sound selection is retained. Unsupported casts keep the existing distance-based fallback. Opposite-direction blend smoothing retains its unnormalised state so it can cross zero instead of remaining stuck in the previous direction. No world movement or simulation pacing changes.

Run verify_footplant_library_identity.gd and verify_foot_plants.gd against the bake manifest. The latter exercises the actual Godot AnimationPlayer/AnimationTree callbacks on four representative bodies. Real-time evidence uses OTHERREACH_FOOTSTEP_REVIEW=1; optional material audit is confined to this review path. Master bus recording contains no microphone. WGC now confirms its first positive captured frame before signalling recording.started, preventing a missing A/V synchronisation flash.

## Validation and review limits

Clean source build: zero errors. Presentation tests: 394/394. InnNight and NpcSpacing: 4/4. Original animation keys are unchanged by metadata/append operations. Live source gates for both casts contain three labelled 12-second phases (seated, sleeping, keeper). Clock advancement occurs only between phases; lighting is held at daylight to expose the pose. Engine.MaxFps=0. Footstep gates use normal-controller steering with capture-only shadowless scatter hidden to expose ankles. These are source-runtime videos, not packaged acceptance. Claude must judge the motion videos; no visual acceptance is inferred from a test pass. Unaccepted #44 grip experiments are excluded from this commit.
