# F1 Gate B — complete native creator (in progress)

Coordinator 17:20 reviewed Gate A as CLOSE: shape, native detail and eyes match;
no further A-review wait. Finish matte-satin/neutral skin, relaxed native eyelids
and brow density during B. Memory snapshot is report-only; no new savings.

Source checkpoint, not a completed gate:

- Existing catalogue includes all 83 native face records and their full settings,
  plus body-preset data, over two bases. No 83 exported bodies.
- Per-people allowed controls/ranges are data (72 Veth/Orenth/Kal, 67 Siann).
  Siann remain fine/slight with physical appearance age 18–30; Kal compact/broad.
  Bone-length controls stay hidden because they require body skeleton refitting
  and exact-target motion. Native eye-fitting controls are supported separately.
- Groups, per-group reset and semantic-channel randomize within people. Untouched
  Veth/Orenth presets keep all native face weights. Randomization starts with a
  native preset and varies locally rather than combining every extreme at once.
- Preview and gameplay share the kept-key/material path. Stature updates the
  figure's native uniform scale and stride scaling. Face and body selections
  are independent. Legacy saved identities stay on their existing path until
  explicitly mapped; no silent replacement of a named identity.
- Native bases initially exported with 202 body keys at each of four LODs.
  HumGen's keep selection needed both original and converted key names; otherwise
  its second pass silently baked the body controls away.
- Native body-vertex attachment data transfers retained static keys to grooms,
  brows and lashes, in addition to HumGen's animated/FACS keys. Refreshed exports
  with this hook are underway. Both source-prepared bases must remain unchanged
  during their native processing.
- Native 4K body / 2K eye / 2K hair material baking for all presets is underway
  on CPU. Full native source maps, usual cache/LOD only. The initial filename
  binding issue is repaired from existing native filenames without rebaking or
  touching source pixels. Native thumbnail originals accompany the choices.
- Both creator bases have fresh exact-target motion: 42 original source clips
  each, with the existing native builder's aliases, timing and contacts. No
  other body's exact-target library was borrowed. A subsequent material-only
  correction preserved native lower-LOD eye/hair atlases (the old garment-repair
  helper incorrectly replaced them with LOD0). The same actors' libraries were
  repackaged through the existing name/parent/rest checks for the final body pins.
- Modular native grooms and wardrobe are exporting on CPU. The first hairstyle
  for both sexes contains only its seven native LOD meshes, no body, and matches
  all base rest matrices exactly. Native shader graphs/atlases are retained as
  private build inputs for colour controls; runtime files contain only the parts.
  All registered head/beard/outfit/footwear variants are queued. Named skin binds
  attach these to the existing skeleton; no anatomy is substituted.
- Native shader carries the owner-requested satin/neutral correction; HumGen's
  own eyelid channels have a relaxed rest; native brow alpha coverage is modestly
  increased, retaining its atlas and colour.
- Native eye fitting retains the real eyeball-size morph on both original
  iris/cornea layers, and uses HumGen's own live-key/bone-tail landmarks and width
  formula. Native source geometry matched all 5,416 exported eye vertices per
  base within 0.2 micrometres. A focused comparison against HumGen's real API
  refit on three moved eye controls matched both bone tails within 0.11
  micrometres and size weights within 0.00000028. Body motion rest stays intact.
  This also fixes the older catalogue's omission of capitalized eye controls
  and lower-skull controls. These are CPU conversion checks, not visual acceptance.

Still required before Gate B review: finish/bind all native material sets; native
groom/wardrobe choices and independent skin/eye/brow/hair controls; people looks
over the full-quality base, including white/chalk/pearl Siann and Kal stone;
final exact-target motion for the two bases; save/play verification and one short
real-art creator proof. Existing named/resident rebuilds remain Gate C.

`creator_ready.json` is absent. It must only be installed when the full private
payload above is actually usable. The incomplete source checkpoint does not
enable unfinished choices or promote a partial owner build.

Minimal checks: one focused catalogue/weights/constraints/randomization/reset
case and a presentation build. No GPU campaign, broad test suite or new savings.
The existing sparse-buffer garment-repair case also checks that native per-LOD
atlases survive. The CPU guard now checks fatal stderr after process exit too,
so a short Blender Python failure cannot be misreported as completion.
Game commits pathspec-only, no push. Canon Siann appearance corrected to the
explicit owner rulings in otherreach-canon 3ec979c, pushed only on its working
branch codex/f1-humgen-creator. Production actor/wardrobe selections and credits
remain to be finalized; these base/preset fixtures assert no new NPC identity.
Owned source: mob_library/MOB_LIBRARY.md row148,
HUMGEN_GAME_20261021; installed native data D:/HumGen Content. Original licences
remain. No generative AI, reference-art copying or external downloads.
