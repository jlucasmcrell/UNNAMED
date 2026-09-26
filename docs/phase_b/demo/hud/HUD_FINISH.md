# Phase B — HUD finish (production HUD)

Branch `claude/pb-hud` (from `a080596`). Code commits `4db31e9` and `44af21b`. Every quality tier selects `hud=production`; `hud=classic`
remains the untouched baseline for comparison.

## What changed

The production HUD is now `src/Presentation/Ui/HudView.cs`, styled by `Ui/HudStyle.cs`. It shows the same information as the
classic HUD, at the same moments. Only the presentation changes. The style follows the Player Journey design, doc 09 §9:
dark translucent soot panels, a hairline iron-grey rule and one warm ember focus colour, with no parchment, neon or ornament.

| Piece | Classic | Production |
|---|---|---|
| Status (top left) | three lines of text | A compact plate. Row 1: name, level, `Debt N`, `[K] A point to spend`, `Crouched` and the `[Tab] Inventory` key cap. Row 2: the XP bar with `XP a / b`, or `(the highest)` at the cap. Row 3: the wielded item with its icon, `Guarding`, then coin, armour and carried weight as small-capital label/value pairs. |
| Pools | four bars, numbers in the status text | A gauge per pool with its name and `value / max`, plus a Resonance row. Once Strained, the Strain gauge turns red, gets a diagonal hatch and its label reads `Strained`, so the state is shown by pattern and word as well as colour (doc 09 §4). |
| Formulas | icons plus `[4] Impulse Bolt 10F …` text | One row of framed icons, each with its key cap on the corner and its name and `N Focus` beside it. The formula being cast is framed in ember, and a `Casting <name>` line appears. |
| Effects | icons plus text | Chips with icon, name and time left, at the top of the same panel |
| Target | name and bar | A panel with the name in small capitals and a gauge |
| Prompt | `[E] Search …` | A key cap plus the action, on a panel |
| Combat log | 7 floating lines | A quiet panel sized to its lines. Lines longer than 452 px wrap rather than run under the conversation panel. Damage to the character is warm red; the newest line is brighter. |
| Tracker | right-aligned text | A panel with the title in ember capitals and a diamond bullet per objective |
| Notices | centre-top text column | Panels in the **right column under the tracker**, with their words unchanged. A leading kind (`Quest complete`, `New quest`, `Done`, `Learned`, `Discovered`) is set as a small-capital label, and XP is set in brass. Same de-duplication and timing. |
| Death | one centred text block | `YOU DIED`, then the cause, `THE LAST BLOWS` with the blows, and the return line. Same words. |
| Compass | painted dial | The same dial art inside an iron bezel with ticks. N and the view mark are ember. The bearing sits on a small plate. |
| Conversation | grey panel, `1. reply` buttons | A soot panel with the speaker in ember capitals over a rule. Each reply is a row with its number on a key cap and an ember edge on hover. Same replies and keys. |
| Crosshair | `+` glyph | Four drawn strokes and a dot (first person only) |

Three layout decisions were made inside the visual scope, and all are flagged for the lead:

- **Notices moved from the top centre to the right column.** In classic, a run of notices (nine at quest completion) reaches
  down under the inventory, trade and journal panels (x 60–1190) and over the character. In the right column they meet none of
  those panels, the conversation or the death recap.
- **The plate is kept compact (y 16–106).** The journal and character sheet open at y 140 and the Field Guide at y 110, over this
  corner. A taller plate overlapped the journal in iteration 2 and was redesigned.
- **The bottom-left panel is kept short.** With one row of effects it runs from about y 843 down; without effects, from y 870. A
  full 24-stack pack's inventory list reaches down to y 838 over this corner. The first finished layout, with formulas stacked over
  the pools, would have sat under a full pack once formulas are known, so `44af21b` put the formulas in one row.

`Main.cs` gets one added call beside the existing status composition: `_hud.SetSheet(new HudSheet(...))`. It passes the status as
values, so the production HUD lays out widgets and never parses the sentence. `HudIcons.Binds` avoids asking for a weapon icon that
is not bound, which would otherwise record a coverage fallback. Classic `Hud.cs` is the pre-candidate file (`80dea8c`) with an
early hand-off to `HudView` when production is on. The previous candidate style (`Production()`, `Panel()`, `Band()`, `Frame()`)
was removed.

## Typography and provenance

- **Barlow** (Medium, SemiBold) for reading, and **Barlow Condensed** (SemiBold, +1 px letter spacing, capitals) for names, labels
  and keys. Designer: Jeremy Tribby (The Barlow Project Authors). Licence: SIL Open Font License 1.1, no Reserved Font Name.
- Source: the official google/fonts repository over plain HTTPS, downloaded 2026-09-26, for example
  `https://raw.githubusercontent.com/google/fonts/main/ofl/barlow/Barlow-Medium.ttf` and
  `.../ofl/barlowcondensed/BarlowCondensed-SemiBold.ttf`. No click-through agreement was involved.
- Originals, OFL.txt and PROVENANCE.txt (URL and SHA-256 per file) are in
  `F:\Otherreach_External_Assets\fonts\barlow\` and `...\barlow_condensed\`. The files used, with `OFL.txt`, are in
  `src/Presentation/Ui/Fonts/`:
  - Barlow-Medium.ttf `f8906f762cb73dca441da034bc363b2d8e2e68bc10d5c05e58717646c20cc4b4`
  - Barlow-SemiBold.ttf `86577cb32f8abe3673db53ca0f4221e6856751a4f6730c867e00f720f8bb1fc5`
  - BarlowCondensed-SemiBold.ttf `7b619d14bc2327509a9ef32b0890f709626f7ecc9ff61191c2a4314c5499d2d9`
  - OFL.txt `186d750eb496a4c17a76385f82be6aea2ac1cf2de074a811d63786cf374ea73f`
- Loading: `HudStyle` reads the font file directly when the project folder has it, so a worktree without a fresh import still
  shows Barlow. Otherwise it loads the imported resource, as an exported build does. The `.import` files are committed.

## Resolutions checked

All 40 ui-shots moments were captured from this worktree with `hud=production` at 1920x1080, 1280x720, 1366x768, 2560x1440 and
3440x1440. Classic was captured at 1920x1080. The harness is `G:\UNNAMED_HISTORY\phaseB\hud_lane\hud_shots_lane.ps1` and the
output is in `G:\UNNAMED_HISTORY\phaseB\hud_lane\shots\final3_production_*` (code `44af21b`) and
`final_classic_1920x1080`. Every run has 40 distinct frames (md5), so no window was covered.
Each capture has START/END lines in the session ledger.

- **1920x1080:** all 40 moments of the final run viewed on contact sheets, plus full-size views of the evidence moments (combat,
  seam, corpse_prompt, kera, hearth, quest_done, learned, death) and, in earlier iterations, lesson, journal and trade. No overlapping panels, clipped text or missing information. The plate ends at y 106; journal,
  inventory, trade and station panels open clear of it and of the notices.
- **1280x720 and 1366x768:** same layout (canvas_items stretch of the 1920x1080 layout). All text is readable. The smallest text
  is the 14–15 px small-capital labels, with a cap height of about 7 px at 720p; that is the tightest case (see limitations).
- **2560x1440:** crisp, identical layout.
- **3440x1440:** the layout widens to 2580x1080 virtual (aspect expand), the corners stay anchored and nothing overlaps.
- **UI scale:** the game has no UI or text-scale setting (accessibility settings are out of this scope). The supported scaling
  is the canvas_items stretch, and the resolutions above cover it.

## External review (blind, Gemini only)

Tooling was Codex B's `G:\OtherreachTools\visual-qa`: `qa.py blind --rubric hud`, then
`qa.py review --provider gemini --model gemini-3.8-flash --allow-network`, then `qa.py reveal --expected gemini`. The key was set
in the process environment only and removed after. Per the owner, Pegasus is unavailable; the toolkit's Pegasus adapter recorded
`unavailable` without any network request (`reviews/pegasus_unavailable/`). The model recorded by the tool is
`gemini-3.8-flash` (provider_version `gemini-3.8-flash`).

The inputs are the final classic and finished captures at 1920x1080, from the same worktree and the same moments. The finished
captures are at code `44af21b`. The 2x2 full-resolution grid (3840x2160 per style) exceeds visual-qa's 19 MB request cap: two
PNGs of about 10 MB each. It was therefore sent as its two full-resolution rows (3840x1080, the same pixels). The dialogue plus
notification pair was a third run.

With seed `20260926` the finished HUD lands at position A every time. Each run was therefore repeated with seed `20260927`, which
puts it at B, as a position control. That makes six Gemini requests in all.

| Run (`reviews/…`) | Moments | Finished (A, then B) | Classic |
|---|---|---|---|
| `01_row1_exploration_combat` | exploration (seam prompt), combat | hierarchy, layout, context: **pass** in both positions | hierarchy and layout **defect** in both; context pass in both |
| `02_row2_interaction_objective` | interaction prompt after a kill (casting), dialogue + objective + notices | **pass** in both positions | hierarchy and layout defect; context defect with seed 20260926, pass with 20260927 |
| `03_dialogue_notifications` | dialogue (Kera), notices at quest completion | **pass** in both positions | hierarchy and layout defect; context defect with seed 20260926, pass with 20260927 |

Checked against the pictures:

- True: finished has backed, legible text, consistent margins and no overlap; classic has unbacked text over bright ground, an
  unframed target bar and a dense, unstructured status block.
- Not quite accurate: "target boss bar" (it is a Bristleback Boar, not a boss), and "hotbar slots" or "hotbar bindings" in run 03
  for both candidates (there is no hotbar; these are the formula keys 4-6).
- Missed by the reviewer: every item under "Remaining limitations" below. The reviews are short and uncritical. They show a
  consistent preference, not polish or commercial readiness.
- Superseded: an earlier round on `4db31e9`, before the compact bottom-left panel, gave the same verdicts. It is kept in
  `reviews/superseded_4db31e9/`.

Owner decision: pending.

## Remaining limitations (stated as they are)

These are design-owned; the FE-2 information architecture is deliberately **not** implemented here:

- Everything classic shows is still shown at all times: coin, armour, weight, level and XP included. Doc 09 moves coin, armour and
  weight to the inventory header, and level and XP to the character sheet. It also calls for HUD modes (Full, Dynamic, Minimal,
  Off), contextual stamina and focus, target condition words instead of a bare bar, and the combat log off in Dynamic. All of
  these need FE-2.
- Notices are not queued or capped. The quest-completion moment stacks nine, as classic does, and doc 09's three-line queue with
  categories is FE-2. `[K] A point to spend` is still an inline hint; FE-2 turns it into a badge.
- No hotbar, no subtitles, no save indicator: not built, and outside this scope.

These are visual and outside this scope:

- The icon art is the existing generated set. Its tiles are painted on backgrounds of every shade. A shader and iron frames pull
  them together, but the set stays mixed: the Strain crack icon is nearly invisible at 20 px, and the 18 px weapon icon in the
  plate is a small blur at 720p. Redrawing the set was not done.
- The aiming reticle is unchanged. Its colour-plus-shape change on creatures, proposed in doc 09, is not done.
- The other panels keep the engine's default font and grey panels: inventory, trade, station, journal, character, saves, Field
  Guide, F4 quest debugger, main menu and creator. When one opens over the HUD, two typefaces show. Their layout and behaviour were
  out of scope, and no global theme was applied.
- At 1280x720 the small-capital labels (COIN, ARMOR, CARRYING, pool names, Focus cost) are 6–7 px tall. They are readable but
  small, and there is no UI-scale setting to raise them.
- The F4 quest debugger (developer only) overlaps the bottom-left vitals panel, exactly as it overlaps the classic bars.
- With a full 24-stack pack open, the inventory's last row (to y 838) meets the top of the bottom-left panel only when that panel
  is at its tallest: a cast in progress, or effects wrapping to a second row. The cast case is transient, because no formula can
  be started while the inventory is open. With one row of effects the panel starts at y 839–843, and without effects at y 870.
  Classic's formula icons sit about 20 px under a full pack in the same state.
- World state differs between runs: for example the forged spear is "Crude March Spear" in one run and "March Spear" in another.
  This comes from craft-quality timing, not the HUD; each HUD shows its run's actual item.

## Verification

- Build: `dotnet build src/Presentation/UNNAMED.Presentation.sln` succeeds, with no new warnings.
- Tests: `dotnet test src/UNNAMED.sln` on `44af21b`: 825 passed, 0 failed. No test was changed.
- Build warnings: only the four pre-existing ones (`Greybox/Palette.cs` x3 and `Main.cs:307`); the HUD files add none.
- M-06 layout check with the production HUD over the full-pack content (`--layout-check`, content from
  `G:\UNNAMED_HISTORY\phaseA_verify\content_fullpack`, which differs from `content/` only in `config/inventory.yaml`): **PASS**
  at 1280x720 and 1366x768. It covers the 24-stack pack and its last row, the saves list, the controls, the character sheet and a
  conversation with Sel; every button is on screen. Its pictures were viewed: the Field Guide opens at y 110, just under the plate,
  and the character sheet and journal at y 140. Output: `G:\UNNAMED_HISTORY\phaseB\hud_lane\layout\`.

## Evidence in this folder

- `before_*.jpg` / `after_*.jpg` (1920x1080, classic and finished, same worktree): exploration (seam), combat, interaction
  (corpse prompt), dialogue (Kera), objective (hearth), notifications (quest completion), notifications_learned, death.
- `res_<resolution>_lesson.jpg` / `res_<resolution>_death.jpg`: the finished HUD at 1280x720, 1366x768, 2560x1440 and 3440x1440.
- `reviews/`: blind payloads and prompts, identity maps, Gemini raw and structured results, reveals, and the review inputs.
- Tools, outside the repository: `G:\UNNAMED_HISTORY\phaseB\hud_lane\hud_shots_lane.ps1` (ui-shots per style and resolution),
  `layout_check_lane.ps1` (M-06 check with the production HUD) and `hud_compose_lane.py` (before/after jpgs and review grids).
