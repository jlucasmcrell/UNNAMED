# RESTORE — not an original development branch

This branch was reconstructed on BEAST on 2026-10-14 while ASTRAL (the development machine) was offline.
It is a **best-available restore of the Oct 2 state**, not the latest work. When ASTRAL is back, its
branches are authoritative; reconcile this branch against them rather than building on it blindly.

## Sources

| Part | Restored from |
|---|---|
| Git history, core projects, tests | `claude/phase-b-visual-overhaul` (`df5362f`, Sep 26) on GitHub |
| `src/Presentation` | BEAST render-job snapshot `C:\Otherreach_Worker\jobs\humanoid_pace_source_qualification_20261002_r1\project` (Oct 2) |
| `GameSession` additions | Decompiled from that snapshot's Oct 2 `UNNAMED.Application.dll` |
| `content/` | The same job's `packet/content` and `connected_inputs/content` (Oct 2) |

`src/Presentation/addons/` (third-party Godot plugins, 606 MB) is not in git; copy it from the snapshot above.

## Known gaps

- 7 Content.Tests and 29 Application.Tests still assert the Sep 26 single-region content. Their Oct 2 updates were not recoverable.
- Missing entirely: all work from Oct 3 onward on ASTRAL, including the Oct 4 starting-region build (`023e77ce`),
  `G:\UNNAMED_OPUS_SECOND_PLAY_FIXES` commits `c266c34`..`c927832` (save schema 19, character creation),
  `G:\UNNAMED_CLAUDE_UI_POLISH`, and asset production on ASTRAL's F:/G: drives.
- Session transcripts covering Oct 2–14 are saved separately as `RESTORE_transcript_*.json`; they hold the direct
  file edits from those sessions but not subagent edits.
