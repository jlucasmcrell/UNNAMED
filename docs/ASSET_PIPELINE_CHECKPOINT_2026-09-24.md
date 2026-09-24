# Asset Pipeline Checkpoint — 2026-09-24

**Purpose:** record the exact state of the asset worktree before and after the post-M6 maintenance
pass, so the overnight work is recoverable and its provenance is not a matter of memory.

## 1. State before this pass

```
branch            main
HEAD              909c00c4b157da88776067d510f242dfa39c0343
                  "Install the Otherreach README; camera direction wording in the docs"
modified tracked  13
untracked         127
tracked under assets/   0
```

`assets/` is gitignored, by design — commit `ffb9d9c "Stop tracking generated assets"` removed the
library from the index. The consequence is that **the entire Phase-1 asset library existed only in
the working tree on one machine**, and every asset-pipeline tool that produced it was untracked.
A failed bulk build, a bad rescale or a disk fault would have taken all of it with no recovery point.

The 13 modified tracked files were all DeepSeek-owned asset-pipeline files and two Wave-0 documents,
each uncommitted.

### Local `main` was already ahead of `origin/main`

```
origin/main  27993f6  Merge pull request #2 from jlucasmcrell/readme-banner
local main   14 commits ahead of origin/main before this pass
```

Pre-existing, not created by this pass, and left alone. It means "unpushed" below is not the same as
"unpushed by me".

## 2. Branch created

```
deepseek/asset-maintenance-2026-09-24     from 909c00c, working tree preserved
```

Created with `git checkout -b`, which carries uncommitted changes onto the new branch and touches
neither `main` nor `claude/phase1`. **`claude/phase1` was not merged, rebased, cherry-picked or
checked out.** No `git clean`, no reset, no mass revert, no destructive operation of any kind.

### Git identity

There was **no git identity configured anywhere on this machine** — no `user.name`, no `user.email`,
and no global `.gitconfig` at all. Committing is impossible without one, so a **repository-local**
identity was set (not global):

```
user.name   DeepSeek Asset Agent
user.email  deepseek-asset-agent@localhost
```

This is scoped to this repository only and is trivially changed. Flagged rather than done silently,
because commits are now attributed to it.

## 3. Commits on the branch

```
9a59dfd  Make validation truth single-sourced and record asset limitations
4558736  Add Phase-1 asset-pipeline documentation
29732f8  Extend the Godot asset validators
1222f25  Add Phase-1 audio pipeline tooling
1351a13  Add Phase-1 asset pipeline tooling
```

275 files in the four tooling and documentation commits, plus 13 in the maintenance commit. All text.
**No generated geometry, no archive, no staged Godot import cache, no Claude gameplay file was
staged.**

Deliberately **not** committed, and still untracked:

- `.zip` archives (`GPT.zip`, `OTHERREACH_*BUNDLE*.zip`, `OTHERREACH_DESIGN_*.zip`)
- screenshots and loose images (`Screenshot 2026-09-23 171517.png`, `image-*.png`)
- `docs/CLAUDE_PHASE1_EXECUTION_PROMPT.md` — Claude-owned
- owner handoff and design documents, `PROJECT_CHARTER.md`, `RIGHTS`/design bundles
- `REVIEW_GIT_*.txt` scratch files and `RACES.md.bak-before-biology-update`
- `tools/godot_validate/.godot/` (1.5 GB) and `tools/godot_validate/assets/` (2.5 GB) — ignored by
  `.gitignore`, confirmed not staged

**Branch pushed: no.** The brief permits a push only with working credentials and no generated bulk;
no credentials were used and no push was attempted. 18 commits exist locally that are not on
`origin/main` — 14 of them pre-existing.

## 4. Snapshot

```
location    W:\_asset_snapshots\phase1_20260924_031724
created     2026-09-24T03:17:45
files       976
size        1,302,245,169 bytes (1241.9 MB)
failures    0
readback    24/24 sampled files re-read with matching SHA-256
```

`W:\_asset_snapshots\` is a **sibling of the repository**, not inside it — verified by the tool, which
refuses to treat a destination inside `W:\UNNAMED` as safe.

Manifest: `W:\_asset_snapshots\phase1_20260924_031724\SNAPSHOT_MANIFEST.json`, listing every file
with its path, byte size and SHA-256, plus the source root, creation time and the Phase-1 id list.

Re-verify at any time:

```powershell
python tools\asset_pipeline\_snapshot_phase1.py --verify W:\_asset_snapshots\phase1_20260924_031724
```

### Scope, and what was deliberately left out

Included: the Phase-1 asset ids and everything hanging off them — `ready/`, `rigged/`, `raw/`,
corresponding `blender_src/`, `animation/`, all `concepts/` for those ids, `manifests/`, `requests/`,
`sockets/`, `vfx/`, `ui/`, the `review/` judgement images, the Phase-1 `_superseded/` records, the
catalog, and the governing documents.

Excluded: the ~750 non-Phase-1 concepts, the 5 GB `raw/` backlog, the concept-churn superseded
directories, and the 4 GB staged Godot import tree. The full tree is ~9 GB; copying all of it would
have taken an hour and protected nothing further, because everything excluded is either regenerable
from included concepts or unrelated to Phase 1.

A full-library snapshot is a reasonable future step and is **not** done here.

## 5. Validation truth reconciliation

### The defect

`godot_validated` was stored in three places and had drifted:

- `ashen_hollow_landmarks.json` — 24 placements carried a copied boolean, **8 of them saying `false`
  for assets whose metadata said `true`**
- `playable_prototype_assets.json` — 29 entries carried `godot_validated`, written as
  `asset_id in proofset`. That is *proofset membership*, not validation, under a misleading name — and
  `in_proofset` already recorded it one line below.
- the metadata backfill wrote `godot_validated: False` unconditionally, so `--force` **erased a
  recorded validation result**.

### The fix

Per-asset metadata owns validation truth outright:

```
ready/<id>/<id>_meta.json : godot_validated
only writer               : _godot_validate_assets.py
```

- Both derived manifests **no longer store a boolean**. They resolve by reference.
- `playable_prototype_assets.json` reads validation into a **summary count at generation time**,
  which cannot go stale because it is recomputed every freeze.
- `_backfill_metadata.py` no longer writes the field at all.

### Enforced, not remembered

`tools/asset_pipeline/_audit_validation_truth.py` fails if any derived manifest stores its own
validation boolean, and distinguishes a stored boolean from a consumed count by value type.

```
derived manifests scanned : 6
stored validation booleans : 0
consumed by reference      : playable_prototype_assets.json summary reads 7
asset metadata records read : 500
recording validated         : 27
RESULT: ok
```

### Proof the backfill cannot erase it

```
before  landmark_ashen_waystone godot_validated=True
        forge_shed              godot_validated=True
        creature_bristleback_boar godot_validated=True
        resource_ash_haft       godot_validated=True
run     _backfill_metadata.py --force
after   all four still True
```

That is the exact operation that destroyed the field before. It is now a regression test.

## 6. Skeleton conclusion

See `docs/SKELETON_CONTRACT_RECONCILIATION.md`. In short:

| contract | bones | where |
|---|---|---|
| canonical full humanoid (`humanoid_standard_v2`) | **52** | `assets/rigs/<family>/body.glb` |
| core contract | **24** | the `role: "core"` subset — not a separate file |
| simplified fixed NPC rig | **20** | `assets/rigged/<id>/` |

All three numbers are correct; they describe different things. `CANONICAL_BODY_AND_SKELETON.md` said
24 for the bodies, which is what made this look like a contradiction — the bodies are 52 and the
figure was stale. Corrected in place.

The 20-bone NPC rig is **not a subset** of the canonical skeleton: `hips` vs `pelvis`, `upper_arm.L`
vs `upperarm_l`, one `spine` vs `spine_01/02/03`. NPCs therefore cannot play canonical animation.
**Mass re-rigging is not required for M6** and is a stop-and-ask item; it was not started.

## 7. Prompt risk

`docs/ASSET_PROMPT_RISK_AUDIT.md`. 16 of 27 Phase-1 prompts tagged; **2 confirmed failures**.

- `FINE_GEOMETRY_RECONSTRUCTION_RISK` — `creature_bristleback_boar`, confirmed (two failed builds).
- `NEGATION_PROMPT_RISK` — `resource_ash_haft`, confirmed (asked for a bare stave, got a spear).

Tags were written back into the request entries. Request library re-validated afterwards: **45 files,
1205 entries, 0 invalid**.

## 8. Phase-1 assets regenerated in this pass

**Two**, both because they were broken, both verified by looking at the render:

1. `creature_bristleback_boar` — reconstruction failed reproducibly; rebuilt from a fur-free concept,
   re-rigged (18 bones, 0 unweighted vertices, exact clip match).
2. `resource_ash_haft` — the concept was a finished spear rather than a raw stave; new prompt, rebuilt
   at 1.900 m, rendered and confirmed as a bare pale stave.

Nothing else was regenerated. Audio was marked but **not** regenerated, by the brief's instruction.

## 9. Exact state at time of writing

```
branch            deepseek/asset-maintenance-2026-09-24
HEAD              9a59dfd
modified tracked  0
untracked         32   (archives, screenshots, Claude/owner docs, and the new maintenance tools
                        and documents before their commit)
tracked under assets/   0   (unchanged, by design)
```

## 10. How to reverse any of this

Nothing here is destructive:

```powershell
git checkout main                          # leave the branch; the work stays on it
git branch -D deepseek/asset-maintenance-2026-09-24   # discard the commits, keep the tree
git config --local --unset user.name; git config --local --unset user.email   # drop the identity
```

The snapshot is a copy and can simply be deleted. No asset, concept, manifest or archive was
overwritten or removed by this pass — superseded material went to `assets/_superseded/`, as before.
