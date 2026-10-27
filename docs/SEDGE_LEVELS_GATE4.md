# Sedge Levels — Gate 4 review

Gate 4 adds a working Tollpiles, mixed residents, three outdoor encounters and **The Kept Crossing** regional survey. Stop for Claude's review. The 12 GB / stripped-cache / 30 GB shipping proof belongs to Gate 5.

## Canon and proposed authoring

Retain the accepted terrain, loop, real tides, Causeway Arch, Tollpiles, Pellow Cuts and Orr Winter Camp, the peoples' existing identities and the Other's violet colour. Mor remain living custodians, not undead. The Drowned Oath-Keeper is a separate hostile creature. Existing Arch crest / low-water span facts are observations, not a newly invented explanation of the Other.

The six named residents, their dialogue, eight generic residents, two maintenance workers, two ambient working-boat crew, all encounter tuning, outer-hall placement, new survey quest and report flag are **PROPOSED**. The 128 m Arch and its new suspended broken deck are art tuning, not a builder attribution. No new people, anatomy or homeland is invented.

| Person | People | Work |
|---|---|---|
| Nessa Reed | Veth | Covered food counter and paid berth |
| Oren Pin | Veth | Covered trade, outfitting and repair |
| Sella Tally | Siann | Survey clerk and quest giver |
| Iren Orr | Orenth | Orr ferry keeper |
| Keeper of Names | Mor | Living custodian |
| Tess Ferry | Veth | Tollpiles ferry keeper |

All figures use existing approved HumGen bodies, garments and native motion. Stature uses uniform whole-body scaling. No human mesh, rest skeleton, anatomy or outfit was edited. Work/home anchors and ordinary supported navigation place residents on the civic landings rather than underneath them. Creature spawns now use the same authoritative supported height as movement.

## Playable loop

Nessa sells food and rents a five-coin berth through existing systems. Rest restores the character without skipping world time. Oren quotes and repairs actual worn inventory through `MerchantRepairCommand`; the inventory now exposes that existing service with an affordable/disabled repair button. Both platforms have collision, supports, covered workspaces and lamps. Neither blocks the causeway.

Tess and Iren offer two-coin local passage at high water. Low water, an obstructed landing, insufficient coin or active combat refuses the service. Travel reuses the movement and party owners, carries the party, and changes neither time nor Waystone knowledge/return point. The moored punts ground on the flats at ebb and rise with the tide. This is a local passage service; ambient boats are not player-driven vehicles.

The quest can be declined and offered again. Walk Pellow Cuts, ask Oren about the crew, find/buy two timber (the nearby tide-washed spar yields normal timber), deliver it, reach the Arch, and inspect the outer record hall. Return to Sella and choose a public chart or ferry-family account. Both choices record the actual branch and award 25 coin / 120 XP. Neither claims an unlicensed cargo run or an arrival in Otherhome. The main **Kept Water / Salt Steps** connection remains an integration dependency: Otherhome is absent from this branch.

The bounded outer Oath-House hall reuses native masonry and tarred timber: raised floor, normal collision/nav, hanging tally-staves, a dry retreat and a barred descending stair. The warning explicitly marks the deep pocket level 30+ and outside the survey. No deep dungeon or false portal is included.

## Encounters

Three separated, level-12 encounter banks lie outside the service posts. Each has a quick strike, committed heavy strike and rushing attack, with two playback variants per class. Windup, active time, recovery, reach and damage are gameplay values. Humanoids can react to a visible player windup by guarding or stepping clear; two linked close cuts lead to a heavy finisher. Semantic RNG selects reactions and variants. Existing creatures without these new definitions retain their prior behaviour.

| Creature | Moves | Art / motion |
|---|---|---|
| Drowned Oath-Keeper | Quick strike; raised-arm blow; rushing shove | DM913 native rigged soldier; three existing CC0 UAL1 takes reused across six variant labels |
| Eel-Serpent | Snap bite; coiling lash; channel lunge | KrazyKaijus native 27-bone rig; six distinct hand-keyed trajectories plus idle/run/hit/death |
| Back-Ledger Raider | Short cut; committed cut; running thrust | Exact approved pool body, native blade takes, held rusted sword, native guard/dodge/hit/death |

Six playback labels do not mean six independently captured mocap takes. The eel preserves source anatomy, weights and rest skeleton. Its original blue/cyan pigment was rejected; the delivered material is olive/brown-grey with a pale underside, warm teeth and wet sheen. Its ordinary fins can enter soft mud. Source/artist/license URLs and modifications appear in the rendered credits and `Sedge_Gate4_Credits.txt`. Licensed binaries stay external.

## Art carry-overs

The Arch gains an upper broken deck, sagging bronze suspension chains, ordinary-size maintenance workers, boats and two crow flocks as scale cues. Existing native carved stone is matte; bronze stays metal. Day uses the existing structured Sky3D clouds with Sedge-only tuning, while accepted dusk keeps its original sky. Physical board modules gain per-board tone, wear and roughness variation. No generated imagery or new custom rain system is used; rain remains disabled pending shared D2.

## Proof and integration

Private evidence root: `D:/Otherreach_Scratch/sedge_gate4_20261027`. Review gallery: this chat's `outputs/Sedge_Levels_Gate4_Review/index.html`. Original native PNGs, raw recordings, gameplay Master WAV, 5 Hz encounter traces, binary hashes, cache receipts and diagnostics are retained. Review MP4s trim startup and attach the actual Master WAV; alignment is approximate (matched against the native five-second screenshot). They are visual evidence, not a frame-time benchmark. Sampled motion frames are explicitly labelled; source PNGs remain unedited.

All captures run on the 6900XT farm. Standard AMF recording failed before output, so the farm runner gained an opt-in `record_codec: libx264` CPU encoder; its default remains unchanged and its previous file is backed up in the evidence root. Combat recordings include original teardown Texture/Font/ShapedText RID and ObjectDB warnings; these are retained, not silently classified as harmless. No VRAM, soak or package-cap acceptance is claimed here.

CPU validation: Presentation Release build has zero errors (33 inherited warnings across projects, including 26 in Presentation). Final content lint loads 726 definitions with zero errors. Seven Sedge application tests pass: offer/re-offer, full gathering/delivery/outer-hall quest with both endings, real worn-gear repair/stock/rest, high/low-water ferry and insufficient purse, supported spawn height, humanoid defensive reactions, and linked finisher. Two world tests pass encounter-bank reachability and dry outer-hall navigation/sealed stair. Earlier accepted-loop two-tide tests pass, as do architecture 23, dialogue menu 5 and new attribution 2. The content regression pass initially had five expected catalogue/scope failures; all five were corrected and passed on focused rerun (52 other cases passed initially). Final cache preflight checks 8,838 image references, including 2,568 images at least 2K, with zero failures. This is a cache-integrity check, not Gate-5 VRAM acceptance.

Use pathspec-only source commits on `art/sedge-levels`; no push/merge. The base predates current `claude/pp11-fixes`: reconcile overlapping C1 combat, D3 quest UI, C2 services, F1 appearance and idle/talk changes when integrating. In particular preserve current accepted body/face/clothing libraries and merge binding entries instead of overwriting the shared binding file. Rebuild on the current integration branch for Gate 5.

Private delivery contains changed primary assets and their SHA-256 manifest against accepted Gate-3b primary04, plus the required existing `humgen_characters` payload, credits and source receipts. Full-resolution BC7 cache entries accompany new models. CPU compression pads a constant 1-pixel image to one 4x4 BC block; original source art is unchanged. Never copy `.godot`, unrelated `.import`/`.uid` noise or the rejected procedural/blue eel drafts into the handoff.
