# Gate D part 2 — clip sheet (visual proof) status

The clip sheet harness (`--anim-sheet`) was wired and run four times against the shipped game:

1. Two runs booted the front-end because Godot needs a bare `--` before the game's own options. Fixed in `work/anim_sheet.ps1`: the
   game's options now follow `--path … --windowed -- …`, the same form the NPC review runner uses.
2. The third run reached the sheet and threw
   `System.IO.FileNotFoundException: HumGen exact-target motion payload is missing` for
   `humgen_player_mateo_m_motion.res`, looked up under `src/Presentation/humgen_characters/mocap_motion`.
   Cause: the sheet builds the player figure, whose exact-target motion payload lives in
   `work/creator/game/humgen_characters/mocap_motion`, and the game only finds it through `OTHERREACH_CHARACTER_ROOT`
   (`SkinnedModel.ComparisonClips`). The runner now sets that variable.
3. The fourth run (`work/gatec_logs/anim_clips5`, states `idle,run,sword_attack_1`) holds a GPU lease request behind two other lanes
   and had not rendered when this note was written.

The audit itself does not depend on that sheet: every claim in `gate_d_part2_animation_audit.md` comes from the two creator motion
audits (42 clips each, 59 bones), the 43 motion libraries on disk, and the game's own references to each clip name.
