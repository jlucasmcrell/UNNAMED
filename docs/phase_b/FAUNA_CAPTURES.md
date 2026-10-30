# Fauna captures

Every frame listed here was opened and judged. Where a judgement is partial, it says so.

Frames live in `C:\Otherreach_Farm\results\<job>\` (the farm's output folders) and the compiled proof clip is
`F:\otherreach_captures\fauna_areas\fauna_proof_v2.mp4`.

| species | job / frame | what it shows | verdict |
|---|---|---|---|
| mallard | `claude_fauna_sedge01\near.png`, `mid.png` | three mallards standing on the sedge pools; banner "MALLARD - Level 1: Even. Passive - can be hunted."; three target rows | good |
| mallard | `claude_area_millrace01\near.png` | banner and two Mallard rows by the millpond; "LANDMARK DISCOVERED: CAIRNWOLD" | good |
| red deer hind | `claude_motion_deer03\mid.png` (+ 2x crop `deer03_mid_right.png`) | a hind standing in grass at the Charwood tree line, hide and head reading clearly | good |
| red deer | `claude_area_oldlate01\near.png` | banner "TIMBER HIND - Level 1: Even. Neutral - can be hunted.", two Timber Hind rows | good |
| Barrowback | `claude_motion_deer04\near.png` | full body at ~3 m, pale tusk, stampede attack line, health 80/120 | good |
| frost hare | `claude_fauna_gorge01\near.png` | banner "FROST HARE - Level 1" and a Frost Hare target row; the frame is an unlit cave | partial: presence proven, no visible body |
| hare | `claude_area_moor02\near.png` (+ crop `moor02_left.png`) | a hare in moor grass, brown body and pale ears | good |
| Upland Elk | `claude_motion_elk04\near.png` (+ crop `elk04_near_animal.png`) | banner "UPLAND ELK - Level 3: Hard. Neutral - can be hunted."; body with pale antlers on the ground | partial: pose unresolved |
| Upland Elk | `claude_motion_elk03\near.png` (+ crop `elk03_near_bottom.png`) | an upright multi-tined antler and body at the camera | partial: cropped |
| crow | - | three attempts: inside Cairnwold village, then too distant, then a close stand at 1012,690 that framed a frost hare rather than the mob | **not captured** |
| trout | - | four shoals exist in content; no frame shows a fish under water | **not captured** |

## Things noticed in the frames that are not fauna

- **floating boulders**: `claude_motion_deer04\near.png` and `claude_area_oldlate01\near.png` both show several boulders hanging in the sky on the
  frontier. Two independent frames, so it reads as a scenery placement bug rather than a one-off.
- **Barowback on the Charwood deer ground**: the deer stand bought a Barrowback at three metres, so a hostile grazer ranges that close to a wildlife
  spawn.

## The `claude_cap_*` set (2026-10-30 15:10-15:20, `claude_min` on the farm)

Seven standpoints from `work\fauna_capture_all.ps1`, judged frame by frame:

| job | what the frame shows | verdict |
|---|---|---|
| `claude_cap_elk_band` | banner "UPLAND ELK - Level 3: Hard. Neutral - can be hunted." with the animal and its pale antlers on the ground at the left; no sky rock in view | **good** - the elk's best frame so far; the prone pose is B-025 |
| `claude_cap_oldlate_hares` | banner "TIMBER HIND - Level 1: Even."; **four deer standing in the grass**, confirmed on a 3x crop (`oldlate_deer_right.png`); a Barrowback at the left edge | **good** for deer, hare not in frame |
| `claude_cap_charwood_deer` | three Timber Hind rows in the target list, no deer in shot; a large boulder hangs in the sky at the upper right | partial - deer present by target rows only; the rock is B-023 |
| `claude_cap_moor_crows` | the camera stands inside the player's own head, so the flock cannot be seen; the frame is the player's scalp and a Frost Hare banner | **unusable standpoint** - crows still not captured |
| `claude_cap_sedge_ducks` | the sedge renders as a bare grey plane - no ground detail, no water, no ducks - on this build and asset root | **unusable** - the duck proof stands on `claude_fauna_sedge01` instead |
| `claude_cap_field_deer` | the stand lands at the gate itself: "Animated Armour", "Gate Warden", "Gate Tender" targets and the industrial architecture, no deer | **wrong standpoint** - the deer field is not at (852,768) |
| `claude_cap_millpond` | banner "MALLARD - Level 1: Even. Passive - can be hunted." over the millpond with "LANDMARK DISCOVERED: CAIRNWOLD"; the ducks are too small to make out at this range | **good for the banner**, ducks unproven here |

**What the set did establish**: the elk photographed with its banner and body (`claude_cap_elk_band`), and deer standing in numbers at Old Late (four animals on
the crop). **What it did not**: crows, the field deer, the sedge, and anything at the millpond.

## Elk motion proof (2026-10-30 17:05)

`claude_proof_elkvideo02` wrote 82 in-game screenshots (`--watch-shots`, every 15 frames at 60 fps, 18 s of walking the elk ground) and they are
assembled into `F:\otherreach_captures\fauna_areas\elk_motion_proof.mp4` (20.5 s, 1280x720, 12 MB).

What it shows: the "UPLAND ELK - Level 3: Hard. Neutral - can be hunted." banner over the elk ground, the camera walking the stand, and a small tan
animal far to the left at ~7 s. **The elk is too far from the camera to judge its gait**, so this is a motion *record* of the ground, not yet a proof
that the animal walks correctly. A closer pass is owed.

The farm cannot record with the runner's desktop grab: `record: true` came back with
`[Parsed_ddagrab] Failed to enumerate DXGI output 0 ... Error opening input file ddagrab=framerate=30`, so the frame-sequence route above is the only
video path on that host.
