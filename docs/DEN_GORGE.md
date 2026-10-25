# The gorge behind the wolf den

Owner playtest finding 28 (2026-10-20), spec `C:\Otherreach_External_Assets\specs\28_gorge_and_rope_cave.md`. The owner reported on
2026-10-24 that the area over the edge behind the den was still not fixed: the RC d4 pass had only moved the ravine floor's depth,
and the drop still read as a dark void and a map edge.

## What was wrong

Captured in the RC d8 art at noon and dusk (`C:\Otherreach_External_Assets\gorge\stills_before01`):

- The ravine behind the den was the generic north ravine: a 25 m ditch with a far face 26-40 m out that rose only to about the
  lip's height. From the lip there was no far side to see.
- The near face under the lip was smooth terrain with no rock on it. Its rock only stood on the far face, and only along the
  generic profile.
- The river was one flat 15 m strip along the whole north edge, near-black in its deep colour, with foam only in a 12 cm fringe.
- The ravine's only mist emitter stood at the middle of the 700 m north strip, x 750, far from the den.
- The river's sound was a flat loop on within 28 m of any edge, not louder at the lip.

Why the drop read as a void even where there was rock (measured, `stills_after04`, five variants of one frame):
- The gorge runs north-south, so its far wall faces away from the sun and was shaded all day. At noon it averaged about 0.08
  luminance, with its rock barely lit.
- The air's exponential fog takes its colour from the bright noon sky. Even the ten per cent or so it lays over 50 m was enough to
  flatten that dark wall to an even navy (0.18), erasing what texture it had.
- Volumetric fog, Sky3D's fog and the river mist were each switched off in turn and changed nothing. The atmosphere is the owner's
  tuned one and is not changed here: the gorge itself was too dark.

## What it is now (presentation only)

`src/Presentation/Greybox/DenGorge.cs` owns the gorge: its profile, its water and its air. `DenGorgeDressing.cs` owns its rock,
roots, trees and the rope. Both apply only to the frontier (`DenGorge.On(region)`).

- **Profile** (blended into `Terrain3DView.Outside` between x 425 and 700). The lip drops sheer about nine metres to a ledge,
  which is six metres wide at the rope (x 527-561), then sheer again to a river 31 m below the lip. The far wall stands 45-55 m out
  and rises 54 m to a wooded shelf at +30. The land behind climbs to ridges of 100-150 m about 450 m out.
- **The far wall** stands in three sheer steps with ledges between them. The steps wander along the gorge, and scrub and small trees
  grow on the ledges, so the wall reads in lit layers even while its faces are shaded.
- **Light.** `DenGorgeAir` places three reflection probes along the gorge. They add the fill a real gorge gets from its sunlit rock
  and its river (ambient, colour mode), and give the river its own reflections. They scale with the daylight, so the gorge is dark at
  night.
- **Ground.** From the lip to the top of the far wall the ground is stone, and it is wet towards the river.
- **Water.** The gorge has two runs of its own (`DenGorge.Runs`). They are sampled every two metres and their surface never rises
  downstream. They are white where they fall and drain into the swallow at x 640, where the river goes under the ground. The
  generic north strips now stop either side of the gorge.
  - A 54 m fall (`FallMesh`, `FallWater`) comes off the far wall opposite the den, fed by a stream in a cut over the far shelf.
    It is glossy, and it scatters a little skylight with the daylight, so it stays the palest thing in the gorge at dusk.
  - The river is brighter and faster than the ravine's.
- **Air.** River mist hangs every 30 m along the gorge, with spray at the fall's plunge and over the swallow.
- **Sound.** The `gorge_fall` and `gorge_rapids` hooks (`audio_hooks.json` `near_m`) grow from nothing at their radius to full at
  the place. Each is the same river recording at its own pitch.
- **Rock.**
  - Mountainside shells stand on the ledge against the sheer face and on the bank under the ledge.
  - Sea-cliff runs stand on the far wall's steps. Each run stands where the ground behind it reaches the run's foot, so the rock is
    in front of the terrain and never buried in it.
  - Roots hang down the face from the lip. The broken stone and moss along the lip are knee-high at most, so nothing stands between
    a character and the drop.
  - Boulders stand in the river and shelves of rock along its banks.
  - A pine wood covers the far shelf and thickens up to the skyline.
- **The way down, seen.** Halda's rope is tied to a stake on the lip beside the den's east rock. It goes over the edge, frayed
  where it crosses it, down to the ledge, where a cave mouth opens in the face. Terrain3D's hole bit cuts the ground away there.

## Art

All new art is in `D:\Otherreach_Depot\gorge\ready`. It is LOD0 only at 4K, kept at full resolution through the BC7 texture
cache.

| Piece | What it is | Source |
|---|---|---|
| `gorge_cliff_04` | A sea-cliff scan, 87 m long | Poly Haven `coastal_cliff_04`, CC0 |
| `gorge_cliff_02` | A sea-cliff scan, 41 m long | Poly Haven `coastal_cliff_02`, CC0 |
| `gorge_root_cluster` | Roots | Poly Haven `root_cluster_01`, CC0 |
| `gorge_pine_roots` | Roots | Poly Haven `pine_roots`, CC0 |
| `gorge_rock_face` | A rock face | Poly Haven `rock_face_01`, CC0 |
| `gorge_rope` | Its material dresses the procedural rope | Cosmos CS1 Climbing Ropes, owned |

They were converted with `tools/asset_pipeline/convert_asset.py` (`C:\Otherreach_Recovery\asset_work\gorge\convert_gorge.sh`). A
package needs them copied into its `fallback\ready`, or the gorge draws greybox where they stand.

## Gate

The stills are run by `OTHERREACH_GORGE_REVIEW=stills` (`Main.GorgeReview.cs`): views from the den, the lip, the ledge and the far
side, at noon and at dusk.

- `OTHERREACH_GORGE_VARIANTS` re-shoots with the mist hidden or one fog layer off, as a labelled diagnostic.
- Accepted stills: `stills_after08` (Ultra) and `stills_low09` (low).
- The gorge's art is about 0.4 GB of BC7 at 4K. On a 24 GB card the median VRAM rose about 0.3 GB at Ultra. The harness is `D:\Otherreach_Scratch\claude_gorge_runs\gorge_run.ps1` and the output is in
`C:\Otherreach_External_Assets\gorge`.
