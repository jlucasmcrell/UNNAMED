# Human base mesh, hands, hair, clothing and facial-animation pipeline research

Research pass, 2026-09-26. Research only: nothing downloaded, nothing installed, nothing imported.
This answers the brief to evaluate a clean-topology production humanoid pipeline as the replacement
for the rejected image-to-3D reconstruction + repair path (bad head/neck graft, neck seams, fused
"mitten" fingers, lumpy voxel hair, inconsistent faces — see `docs/phase_b/CHARACTER_FIDELITY.md`).

Format follows the existing depot convention (`F:\Otherreach_External_Assets\EXTERNAL_ASSET_CATALOG.md`):
a `commercial / worldwide / public-repo` yes/no/no triplet per item, license quoted with its source
URL, and an ADOPT-CANDIDATE / BENCHMARK / REJECT verdict. Anything not independently confirmed is
marked **UNCLEAR**.

## 0. What Otherreach already commits to

Read directly from the repo before evaluating anything external, so recommendations target the real
contract instead of inventing a new one:

- **Canonical skeleton**: every body family (`humanoid_standard`, `compact_broad`, `tall_narrow`,
  `irregular_heavy`) and the player rig are **52 bones**: 24 core, 20 finger (4 fingers x 2 joints +
  thumb x2, x2 hands), 8 IK helpers, 5 socket attachments — declared per-family in
  `assets/rigs/<family>/<family>_body_skeleton.json` and checked by `_verify_character_skeleton.py`
  (`docs/CANONICAL_BODY_AND_SKELETON.md`, `docs/SKELETON_CONTRACT_RECONCILIATION.md`).
- **What it replaces**: the rejected reconstruction pipeline produces a *different*, simplified
  20-bone rig with no fingers, no eye bones, two spine bones and fused sleeve/collar geometry
  (`docs/phase_b/CHARACTER_FIDELITY.md`, item 7 "Rig" and the finger/neck-seam notes). Any new base
  mesh must target the 52-bone contract, not this 20-bone one.
- **Body-region vocabulary**: a 16-region body-region naming convention already exists for armour
  coverage, tied to bone origins (`docs/WAVE_0_MODULAR_ASSET_STANDARD.md`, "Body-region naming").
  This is the natural mechanism for hiding skin under clothing (section 4 below) — no new convention
  is needed.
- **Godot-side tooling already confirmed present** (per `F:\Otherreach_External_Assets\FINAL_REPORT.md`,
  independently checked against the installed Godot 4.7.2 ClassDB): `BoneMap`, `SkeletonProfileHumanoid`,
  `RetargetModifier3D`, `TwoBoneIK3D`, `LookAtModifier3D`, `SpringBoneSimulator3D` and 15 other IK/retarget
  classes. All the rigging and eye/jaw work recommended below can use engine-native tools; nothing new
  needs to be installed on the runtime side.

## 1. Local asset depot audit (`F:\Otherreach_External_Assets`)

Grepped `EXTERNAL_ASSET_CATALOG.md`/`.csv` for human/character/body/hand/hair/face/head/base-mesh/
MakeHuman/MPFB/MetaHuman/Quaternius/ActorCore/Reallusion/Daz/Anny/SMPL/avatar terms, and read
`FINAL_REPORT.md` in full.

**Result: the depot holds zero realistic human base-mesh, hand, or hair assets.** Everything
character-adjacent already in the depot is explicitly logged as stylized reference, not production
material:

| Item | License | Note |
|---|---|---|
| `kaykit__adventurers` | CC0 1.0 | catalogued as "silhouette and stylistic reference only" |
| `kaykit__skeletons` | CC0 1.0 | catalogued as "silhouette/reference only" |
| `quaternius__bestiary_dungeon_monsters_kit` | **Quaternius Asset License (QAL) v1.0**, not CC0 | 7 low-poly monsters on a Humanoid rig; commercial use fine, but the raw assets "may never be committed" to the public repo per `FINAL_REPORT.md` section 7 ("Avoid") |
| `quaternius__universal_animation_library` (+2) | CC0 1.0 | animation clips only, no character mesh; this is the skeleton/animation set any new base would need to retarget onto if the Quaternius rig convention is ever used |

No MakeHuman/MPFB, Blender Studio, ActorCore, MetaHuman, Daz, SMPL or Meta MHR material is present.
The Quaternius QAL-vs-CC0 split the user flagged from memory is real and already bit this project once
(the Bestiary pack) — confirms the instruction to read each Quaternius product's own license badge
individually rather than assuming CC0 project-wide (see section 1.3 below).

No conflicts: this research introduces no new files into the depot or the game repos.

## 2. Clean human base meshes

### 2.1 Blender Studio "Human Base Meshes" bundle v1.0.0

- **URL**: distribution mirror `https://www.blender.org/download/demo-files/#assets`; coverage and
  license confirmation at `https://www.cgchannel.com/2023/06/download-blender-studios-free-human-base-meshes/`
  and `https://archive.org/details/human-base-meshes-bundle-v1.0.0`.
- **License**: **CC0 1.0 Universal** (public domain dedication) — CG Channel and the Internet Archive
  listing both state this plainly, and it is treated as CC0 by the community's own `awesome-cc0` list.
  Caveat: `https://developer.blender.org/docs/features/asset_system/asset_bundles/human_base_meshes/`
  carries a page footer reading "CC-BY-SA 4.0 unless otherwise noted" — that is the Blender
  *documentation wiki's own* site-wide license for its text, not the mesh bundle's license; do not
  confuse the two. Re-confirm by reading the license file bundled inside the actual download before
  use (**UNCLEAR** only in the sense that the two sources present it differently at a glance; the
  weight of independent evidence is CC0).
- **Commercial / worldwide / public-repo**: yes / yes / yes (CC0 permits committing to the public repo
  outright; Otherreach's own convention of gitignoring `assets/` still applies regardless).
- **Contents**: 17 meshes — complete male and female figures in both a stylized and a photorealistic
  version, plus separate Foot, Hand, Head, Jaw and Eyeball meshes. Clean quad topology, closed volumes
  (multiresolution/sculpt-ready), UV-mapped with UDIM support, Blender 3.2+. Exportable to Alembic,
  FBX, OBJ, USD.
- **Eyes/teeth**: eyeballs are a separate mesh; teeth/tongue presence is **UNCLEAR** (not mentioned in
  any source read).
- **Rig/shape keys**: **none shipped**. These are sculpting-base blanks in bind pose, not rigged
  characters — a rig and shape keys must be authored afterward (see the MPFB2 retarget path below).
- **Topology / face / hand / eye quality**: high — this is Blender Studio's own production sculpting
  base for its films, explicitly built for quad-topology subdivision and detail sculpting.
- **Realistic-look suitability**: high — the photorealistic variant is the single best-quality CC0
  realistic sculpting base found in this pass.
- **Effort to adopt**: medium-high — no rig, no shape keys, no clothing/hair system; needs full rig +
  weight paint + shape-key authoring, or fusion with MPFB2's system (both are CC0, so mixing is legally
  clean; MPFB2 documents a shrinkwrap/data-transfer technique for exactly this kind of base-mesh swap).
- **Verdict: ADOPT-CANDIDATE** as the sculpt-quality target for identity work; at minimum **BENCHMARK**
  as the realism bar every other candidate is measured against.

### 2.2 MakeHuman 1.x/2.x and MPFB2 (Blender)

- **URLs**: `http://www.makehumancommunity.org/`; license `https://static.makehumancommunity.org/about/license.html`;
  MPFB2 `https://github.com/makehumancommunity/mpfb2`; MPFB license
  `https://github.com/makehumancommunity/mpfb2/blob/master/LICENSE.md`; release notes
  `https://static.makehumancommunity.org/mpfb/releases/release_2015.html`; face ops docs
  `https://github.com/makehumancommunity/mpfb2/blob/master/docs/ui/operations/faceops.md`; closed-source
  FAQ `https://static.makehumancommunity.org/mpfb/faq/use_in_closed_source.html` (title/summary
  corroborated via search index, not independently re-fetched — **UNCLEAR** beyond the consistent
  messaging repeated across MPFB's own docs).
- **License — code vs assets, kept separate as requested**:
  - Standalone MakeHuman program source: **AGPL**.
  - MPFB2 Blender addon source (python/scripts): **GPL 3.0 or later** ("GPLv3", per the LICENSE.md
    text: *"The MPFB source code is defined as files that contain program logic... released under
    GPLv3"*).
  - All bundled system assets — base mesh, body-shape targets, skins, eyes, eyebrows, eyelashes,
    teeth, tongue, proxies, poses, rigs, expression/viseme shape-key packs — **CC0 1.0 Universal**
    (*"These assets have been released under CC0 1.0 Universal... anyone can do whatever they want
    with it"*).
  - Output: MPFB explicitly disclaims any restriction on exports/renders/saved files — user data.
- **Commercial / worldwide / public-repo**: yes / yes / yes for exported character data (CC0). The
  GPLv3/AGPL only covers the Blender addon and the standalone MakeHuman program themselves, which are
  development-time tools never shipped in the compiled game — copyleft never attaches to the Godot/C#
  build. Keep the addon off the public repo as a matter of hygiene (it's third-party tooling, not
  Otherreach IP), but this is a repo-cleanliness choice, not a license requirement.
- **Rigs**: multiple options exist, including a **"default" rig with face bones** and a dedicated,
  lighter **"game engine rig"**, plus `rigifyhelpers`/`righelpers` to convert either into a Rigify
  control rig. Confirmed from the `RigService`/`Rig` entity code and release docs; exact bone-name
  parity with Otherreach's own 52-bone contract was **not** independently verified in this pass (no
  hands-on export/diff was done) — **UNCLEAR**, first proof step should diff MPFB2's exported bone
  names against `humanoid_standard_body_skeleton.json`.
- **Face / expressions / visemes** — a direct hit on section 5 of the brief, shipped free as of MPFB
  2.0.15: `faceunits01` = **52 shapes on the Apple ARKit blend-shape standard** (same standard used by
  iPhone FaceID and widely adopted by game engines); `visemes01` = **22 shapes, Microsoft/SSML viseme
  standard**; `visemes02` = **15 shapes, Meta/Oculus (OVR) viseme standard**. The original feature
  request (GitHub issue #302, "Blend shapes (ARKit + Oculus visemes) and dynamic bones") predates this
  and should be read as superseded by the 2.0.15 release, not as the current state — dynamic
  (jiggle/physics) bones for hair/cloth are still **not** natively supported, per that same issue.
- **Hands**: base mesh and proxies ship full five-finger hands — a direct fix for the "fused mitten
  fingers" defect (see section 3 below for fitting to Otherreach's 20-finger-bone contract).
- **Clothing**: clothes/proxies fit onto the base via a shrinkwrap-style system, CC0; the dedicated
  `MakeClothes` authoring tool is, per the community's own release notes, outdated and mid-rebuild
  inside MPFB2 — fitting existing garments works today, authoring brand-new garment meshes from scratch
  is the immature part (real gap, not solved).
- **Topology/realism**: medium-high as a parametric generator — recognizable "MakeHuman-ish" without
  further sculpt/texture work, but its real strength is the *system* (identity morphs + asset library +
  rig + face/viseme packs), not raw sculpt fidelity; pair with Blender Studio's meshes (2.1) for the
  sculpt-quality ceiling.
- **Effort to adopt**: medium — Blender 4.2+ addon install (dev machines only), generate/sculpt
  identity, export FBX/glTF, re-rig to the 52-bone contract.
- **Verdict: ADOPT-CANDIDATE.** Best overall system match for the whole brief (base + identity morph +
  hands + face/viseme shape keys + rig scaffolding, all CC0, all commercial-safe) and the natural
  backbone of the recommended pipeline in section 6.

### 2.3 Quaternius Universal Base Characters + Modular Character Outfits

- **URLs**: `https://quaternius.itch.io/universal-base-characters`,
  `https://quaternius.com/packs/universalbasecharacters.html`,
  `https://quaternius.itch.io/modular-character-outfits-fantasy`.
- **License**: **CC0 1.0 Universal** on both product pages checked (*"Free to use in personal,
  educational and commercial projects (CC0 License)"*). This is **not** universal across the
  Quaternius catalogue — as the local depot already proves (section 1), the Bestiary Dungeon Monsters
  Kit is QAL v1.0, not CC0. Read each product's own license badge; never assume CC0 publisher-wide.
- **Commercial / worldwide / public-repo**: yes / yes / yes for these two specific packs.
- **Rig**: a "Humanoid rig" stated by the publisher to be compatible with retargeting "in any engine"
  and explicitly compatible with Quaternius's own Universal Animation Library — the same UAL already
  staged CC0 in the local depot and already used for prototyping. Exact bone-name parity beyond the
  publisher's own compatibility claim was not independently re-verified (**UNCLEAR**, low-risk given
  it is the same publisher's own animation library).
- **Style / realism**: stylized low-poly toon characters ("Quaternius style"), simplified hands and
  faces — 20 mix-and-match hairstyles and adjustable eye/skin colours exist but the whole line is
  stylized, not photoreal.
- **Topology / face / hand quality**: clean and game-ready for its own style; not a source of
  realistic head or hand detail.
- **Effort to adopt**: low (CC0, drop-in, matches animation clips already in the depot).
- **Verdict: REJECT** for the realistic-look goal (wrong style category entirely).
  **BENCHMARK only** as a free rig/skeleton-compatibility reference against the UAL clips already
  staged, useful only if a stylized fallback tier is ever wanted — not indicated by the brief.

### 2.4 Other candidates surveyed

- **Poly Haven**: catalogue is props/plants/materials/HDRIs; no realistic playable human base-mesh
  line found (`https://polyhaven.com/models`, license `https://polyhaven.com/license`, CC0 for
  whatever it does host). **Verdict: REJECT** — not applicable today; nothing to adopt.
- **Sketchfab "CC0" human base meshes**: many individually-uploaded items exist (e.g. "Human Base Mesh
  01" by 3D Sculpt Daily, "Human Male/Female Basemesh Rigged" by Niclas, "Human Head Base Mesh" by
  ferrumiron6). These are one-off community uploads, not a curated production line: license is
  whatever the individual uploader marked and must be verified per-download on Sketchfab's own listing
  page, quality is inconsistent hobbyist-to-professional, and there is no shared rig/shape-key/UV
  standard across them. **Verdict: UNCLEAR / case-by-case.** Usable only as occasional supplementary
  reference, never as the production base without a per-asset license check at download time.
- **Human Generator** (Blender addon, `https://humgen3d.com`, license FAQ `https://help.humgen3d.com/license`):
  commercial, tiered Personal/Portfolio/Educational vs Commercial license (pay the price difference to
  upgrade). Under the commercial tier, shipping generated characters baked into a closed game build is
  allowed; **redistributing the raw exported model/texture files, even after customization, is
  explicitly barred**, and sharing/reselling to anyone without their own license is barred outright.
  Addon code is GPL (Blender Extensions requirement); the actual human meshes/textures are under Human
  Generator's own "Digital Asset License," not CC0. Realism is high (marketed for photoreal humans);
  export topology/rig quality against Otherreach's 52-bone contract is **UNCLEAR** (not independently
  verified). **Commercial / worldwide / public-repo: yes / yes / no** — never commit raw content to the
  public repo under this license, keep entirely in a private asset workspace, ship only compiled/baked
  into the build. **Verdict: BENCHMARK** — fast likeness/reference generator, not recommended as the
  shipped production base given the redistribution ban and unverified topology.
- **Character Creator / ActorCore** (Reallusion): commercial. Content license tiers (Standard vs
  Extended) per `https://www.reallusion.com/license/content.html`: Standard covers unlimited
  commercial characters for games, royalty-free and perpetual, one-time purchase, unlimited projects,
  no per-seat cap within a workgroup account. Restriction: content/derivatives may not be redistributed
  through "any... online market for 3D models or similar services" — fine to ship compiled into the
  game, **not** fine to commit the raw purchased mesh/texture/rig to a **public** GitHub repo (that
  would functionally republish it). A direct fetch of the ActorCore-specific EULA
  (`https://actorcore.reallusion.com/eula`) returned no retrievable content from this tool (blocked/JS
  page) — its stricter "no redistribution through ActorCore to any third party" wording is corroborated
  only via search-index snippets, so treat the exact current text as **UNCLEAR**, and have the owner
  re-check it directly in a logged-in browser before committing budget. Realism is high; CC Avatar
  topology/rig are stated to remain Reallusion's own IP even when generated from user input.
  **Commercial / worldwide / public-repo: yes / yes / no.** **Verdict: ADOPT-CANDIDATE for a paid-tier
  evaluation** (strongest out-of-box realistic face+body+rig combination surveyed) if the Reallusion
  license spend is acceptable and raw content is kept disciplined out of the public repo; otherwise
  **BENCHMARK** as the quality bar.
- **MetaHuman** (Epic/Unreal, EULA `https://www.unrealengine.com/eula/mhc`): as of the mid-2026
  licensing change (covered by CG Channel, Digital Production, RenderU — the EULA page itself returned
  HTTP 403 to this tool's automated fetch, so exact current wording is **UNCLEAR** pending a human
  visit), MetaHuman characters and clothing may now be exported and used, and sold, in other engines
  including Godot under the ordinary Unreal Engine EULA: free under $1M/yr studio revenue, Unreal seat
  licenses (~$1,850/seat/yr) required above that threshold even though the shipped product never uses
  Unreal at runtime, and no separate revenue cut applies once exported. Practical catch: tooling and
  rig conventions are Unreal-native; getting a MetaHuman into Godot is a manual, artist-driven
  export/re-rig (skeletal mesh → FBX/glTF → re-rig to the 52-bone contract), not a plugin-button
  operation. Realism is best-in-class (full ARKit facial rig, LOD system). **Commercial / worldwide /
  public-repo: yes (below $1M/yr) / yes / no** (own IP, keep private, ship compiled only).
  **Verdict: BENCHMARK.** Now technically licensable, but requires standing up and owning a whole
  secondary Unreal-based authoring toolchain solely to feed a Godot project — not recommended as the
  primary Phase-B pipeline; worth flagging the $1M/yr threshold to the owner as a business fact.
- **Daz3D** (`https://www.daz3d.com/daz-licenses`): the general/Standard license does **not** cover
  game-engine (interactive) distribution; a separate Interactive/Game-Dev license is required and only
  covers Daz Originals — third-party vendor content on the Daz store carries its own, often
  incompatible, per-vendor license that must be checked individually. **Verdict: REJECT** as a default
  source — licensing overhead and per-vendor fragmentation are too high relative to the CC0
  alternatives already meeting the bar (2.1/2.2). Could be revisited per-asset only if a specific Daz
  Original with a purchased Interactive License is found indispensable.
- **SMPL / SMPL-X** (`https://smpl.is.tue.mpg.de/modellicense.html`,
  `https://smpl-x.is.tue.mpg.de/modellicense.html`): the modeling software/model is licensed for
  **non-commercial research, education, or art only** — *"Any other use, in particular any use for
  commercial purposes, is prohibited,"* including training ML on top of it; a commercial sublicense is
  available only through Meshcapade. Separately, **"SMPL-Body"** (the bare neutral topology, distinct
  from the shape-modeling software) is **CC BY 4.0**, which does permit commercial use with attribution
  — but that only yields a bare research retopology with no shape-blending system, clothing, or rig.
  **Verdict: REJECT** the modeling system (non-commercial, unbudgeted paid sublicense required);
  **BENCHMARK at most** for SMPL-Body's CC-BY topology, since Blender Studio's and MPFB2's CC0 meshes
  are equally free and already game-adjacent.
- **Meta MHR (Momentum Human Rig)** (`https://github.com/facebookresearch/MHR`): **Apache 2.0**,
  fully open and commercially usable — rare for a body model this sophisticated (45 shape params, 204
  pose params, 72 facial-expression params, LOD 0-6). Very recent research release (arXiv 2511.15586).
  It is a Python/PyTorch research package with no confirmed Blender/Godot import path and no
  clothing/hair system. **Verdict: BENCHMARK / watch** — license-clear but too immature and code-facing
  for a Phase-B proof; revisit if a DCC bridge appears.
- **"Anny" parametric body** (`https://github.com/naver/anny`): **Apache 2.0** code, explicitly built
  on MakeHuman/MPFB2's CC0 assets plus its own SOMA/anny topology variants; ships a compact 104-bone
  "anny" rig or a full 163-bone "makehuman" rig; targets differentiable (PyTorch) research, not
  real-time games. **Verdict: BENCHMARK only** — its main value here is independent confirmation that
  MPFB2's CC0 assets are already being reused successfully elsewhere as a legally clean base.
- **MB-Lab / ManuelbastioniLAB**: historical MakeHuman-alternative Blender addon; per its own
  community framing it is now largely superseded by MPFB2 and its current maintenance status is
  **UNCLEAR** (not independently confirmed in this pass). Not recommended over the actively maintained
  MPFB2.

## 3. Hands

Both leading candidates already ship production-quality, separate-finger hand topology under CC0:

- Blender Studio's bundle includes a standalone photorealistic **Hand** mesh matched to its body
  figures.
- MPFB2's base mesh and proxies ship full five-finger hands with posable finger proxies.

Otherreach's own canonical contract budgets exactly **20 finger deform bones** (4 fingers x 2 joints +
thumb x 2, x2 hands) per `<family>_body_skeleton.json`. Both candidate hand meshes have *more* joints
per finger by default (typical Blender-quality hand rigs use 3 phalanges per finger) — so fitting means
**reducing/re-weighting joints down** to the existing contract, not authoring fingers from nothing.
This is a strictly easier problem than the one being fixed (the reconstruction pipeline's fused "mitt"
with only a separate thumb, no per-finger bones at all).

**Verdict: ADOPT-CANDIDATE** for both (MPFB2 hands, being already proxy/rig-aware inside the same
system as the body, are the lower-effort of the two; Blender Studio's hand is the sculpt-quality
option if a same-artist match to a Blender-Studio-sculpted body is wanted).

## 4. Hair for real-time

Card-based hair (strand curves converted to flat textured polygon strips) is the standard real-time
technique, and directly targets the "lumpy reconstructed hair helmet" defect (voxel resynthesis has no
concept of separate strands or clumps; authored cards do).

- **MPFB2 hair**: ships as fitted hair **proxies** (whole-mesh hairstyles), CC0, not native card
  generation — useful as a base shape/silhouette, not as a strand-authoring system.
- **Blender's native Curves/hair system** (Blender 4.x) is the standard way to *groom* hair per
  character; it does not itself export game-ready cards.
- **Curve-to-card conversion tools found**:
  - "Hair Compiler" (`https://github.com/megumumpkin/Hair-Compiler`) — converts hair-card curves to a
    rigged real-time mesh. License **UNCLEAR** — not independently confirmed by reading its repo's own
    LICENSE file in this pass; check before use.
  - A free Blender add-on by Daniel Bystedt (covered by CG Channel,
    `https://www.cgchannel.com/2024/03/daniel-bystedts-free-blender-add-on-creates-hair-cards-from-curves/`)
    turns curve-based hair into card geometry. License **UNCLEAR** — check the addon's own page.
  - "Hair Tool 4" (`https://joseconseco.github.io/HairTool_3_Documentation/`) — paid, well-established,
    converts hair curves/particles into UV-preserving card-mesh ribbons with texture-atlas support.
    Standard Blender-Extensions-style addon license (code GPL per platform requirement); the card
    geometry it helps the artist build from the artist's own curves is the artist's own output, not a
    restricted digital asset — same category as any modeling tool's output.
- **Hair card textures (CC0)**: OpenGameArt's "Hair Alphas For Days"
  (`https://opengameart.org/content/hair-alphas-for-days`) — 85 CC0 PNG hair-strand alpha/mask
  textures, straight/wavy/curly, safe to commit to the public repo and use commercially. A paid
  alternative, "8x Hair Card Alphas" on ArtStation, permits unlimited commercial use but explicitly
  forbids reselling the alpha files as-is — fine to bake into the shipped game, not to redistribute raw.
- **Verdict: ADOPT-CANDIDATE workflow** — groom hair as Blender curves per character (matches the
  project's existing practice of per-character, per-scene authored variation rather than one fixed
  asset) → convert to cards with a card-conversion tool (verify Hair Compiler's/Bystedt's license first,
  or use the paid Hair Tool 4 if either is unclear) → shade with the CC0 OpenGameArt alpha atlas or a
  custom one. This retires the lumpy-hair defect directly.

## 5. Modular clothing and facial motion

### 5.1 Modular clothing workflow

- **MPFB2 clothes/proxy system**: garments are built/fitted as proxies to the base mesh via a
  shrinkwrap-style fit; system content is CC0. The dedicated `MakeClothes` authoring tool is, per the
  community's own release notes, outdated and mid-rebuild inside MPFB2 — **fitting** existing garments
  to a custom body works today; **authoring** brand-new garment meshes from scratch is the immature
  part of the toolchain (a real gap, not a solved problem — flag this honestly rather than assuming it
  "just works").
- **Generic Blender fitting techniques** usable regardless of source system: the **Shrinkwrap**
  modifier (project a garment mesh onto the body surface) and **Data Transfer** modifier (carry
  weights/UVs from body to garment), plus community add-ons such as "Elastic Clothing Fit"
  (`https://github.com/VRC-Staples/Elastic-Clothing-Fit`, proxy-based fitting with a proximity-falloff
  parameter so tight areas deform fully while loose areas float free). That add-on's license was not
  independently fetched in this pass — **UNCLEAR**, check its repo before adoption.
- **Marvelous Designer** (commercial cloth-simulation authoring tool):
  `https://support.marvelousdesigner.com/hc/en-us/articles/47358258006425-License-Plan` and Steam/forum
  discussion both show real ambiguity about whether the "Personal" tier permits any commercial or
  freelance output, with the vendor steering commercial/studio use toward Enterprise/subscription
  plans. **UNCLEAR / CONFIRM-BEFORE-BUY** — get current terms directly from Marvelous Designer sales
  before budgeting; assume any commercial game-studio use requires at least the paid
  subscription/Enterprise tier, never the free Personal/education tier.
- **Body-region hiding under clothing**: not a licensing question. Otherreach already has a 16-region
  body-region naming convention tied to bone origins for armour coverage
  (`docs/WAVE_0_MODULAR_ASSET_STANDARD.md`). Recommend driving skin-mesh visibility from the same
  vocabulary — split the body mesh's vertex groups/material slots per named region and toggle
  visibility per equipped garment — rather than inventing a second system.

### 5.2 Facial motion for games

- **ARKit 52 blendshapes**: an industry-standard, FACS-based set (roughly 12 eye, 4 brow, 26 mouth
  params plus jaw/cheek/nose/tongue) used by iPhone TrueDepth capture and widely adopted as a common
  facial-rig interchange target by game engines (`https://arkit-face-blendshapes.com`,
  `https://miniface.org`). MPFB2 already ships this exact 52-shape pack (`faceunits01`) free under CC0
  — the natural target format for hand-keyed or mocap-driven facial animation.
- **Visemes**: MPFB2 ships two CC0 options — 22 shapes (Microsoft/SSML standard) and 15 shapes
  (Meta/Oculus OVR standard) — either small enough to hand-key or drive procedurally per dialogue line.
- **Godot 4.7 blend shape support** (primary source: `docs.godotengine.org` `class_meshinstance3d.html`
  and the Animation track-types docs): `MeshInstance3D` exposes `get_blend_shape_value` /
  `set_blend_shape_value` / `get_blend_shape_count` / `find_blend_shape_by_name` for driving morph
  targets from script, and `Animation` resources support a dedicated `BLEND_SHAPE` track type for
  baking blend-shape keyframes into `AnimationPlayer` clips. Caveat found in the wild (GitHub issues
  #87400 "Cannot override bone rotation and blend shape when using AnimationTree", #95244 blend-shape
  track path/import quirks, and multiple 2024-era forum threads): blend-shape tracks have real rough
  edges when combined with bone-rotation overrides inside `AnimationTree` blend graphs.
  **Recommendation**: do not route facial blend shapes through the same `AnimationTree` blend graph as
  locomotion; drive them from a small, separate script layer (direct `set_blend_shape_value` calls,
  tweened) dedicated to dialogue/face — this sidesteps the known `AnimationTree` interaction bugs
  entirely and is also the cheapest thing to build.
- **Jaw and eye aim**: cheaper and more robust as **bone-based** rather than blend-shape-based, since
  transform/bone-pose tracks blend and layer through `AnimationTree` without the blend-shape-specific
  bugs above. Godot's already-confirmed-present modifier stack (`LookAtModifier3D` for eye aim) plus a
  simple jaw-open bone on the existing skeleton needs no addon at all.
- **Blink**: cheapest as one or two eyelid blend shapes driven by a timer/random-interval script,
  independent of dialogue — either MPFB2's face pack or a hand-authored blink shape on the custom base
  mesh works.
- **Rhubarb Lip Sync** (`https://github.com/DanielSWolf/rhubarb-lip-sync`): **MIT** for the tool itself
  (confirmed by reading `LICENSE.md`); bundles several permissively-licensed speech components
  (PocketSphinx/CMU Sphinx and libvorbis/libogg under 2/3-clause BSD, WebRTC under 3-clause BSD, Boost
  under the Boost license, Flite under a BSD-like license) — all commercial-safe. Generated lip-sync
  data belongs entirely to the user; the tool makes no claim on output. **This is the recommended free
  automatic audio-to-viseme tool**: feed a dialogue WAV, get phoneme/viseme timing on Rhubarb's own
  small mouth-shape set, map it onto whichever MPFB2 viseme pack is adopted (a one-time manual mapping
  table). Newer neural-TTS-centric alternatives (HeadTTS/HeadAudio) surfaced in search are less mature
  and less proven for this exact task — **BENCHMARK only**, Rhubarb remains the default.

**Recommended cheapest robust path to blink + eye aim + jaw + basic mouth movement in dialogue**:

1. Bone-based jaw-open bone and bone-based eye-aim (via `LookAtModifier3D`), driven by a lightweight
   script layer *outside* `AnimationTree`.
2. Blend-shape blink (1-2 shapes) on a timer/random-interval script.
3. Blend-shape mouth shapes — a small 6-10-shape subset of MPFB2's viseme pack, not the full 52-shape
   ARKit set, to keep authoring cost down — driven by Rhubarb Lip Sync output at dialogue time via
   direct `set_blend_shape_value` calls rather than baked `AnimationPlayer` clips.
4. Reserve the full ARKit 52-shape pack for later, higher-fidelity work (hero cutscenes / close-ups
   only), not the default per-line dialogue budget.

## 6. Recommended hybrid pipeline

1. **Base**: **MPFB2** (Blender addon, CC0 system assets, GPLv3 addon never shipped) as the
   identity-generation backbone — body-shape sliders, skin/eye/teeth/tongue/eyebrow/eyelash system
   assets, five-finger hands, and the ARKit-52 / 22-viseme / 15-viseme face packs, all free and
   commercial-safe.
2. **Sculpt-quality option**: swap in Blender Studio's CC0 photorealistic **Human Base Meshes** for the
   raw body/head sculpt if MPFB2's own base reads too generic once textured — both are CC0, so mixing
   is legally clean, and MPFB2 documents a shrinkwrap/data-transfer technique for retargeting its own
   shape-key/rig/asset system onto an external mesh.
3. **Identity fitting**: sculpt each character's identity on the chosen base under direct artist
   control — the direct structural fix for "inconsistent faces," replacing per-photo voxel
   reconstruction with an artist-controlled sculpt.
4. **Hands**: use the base mesh's own five-finger hands; reduce/re-weight to Otherreach's existing
   20-finger-bone, 2-joint-per-finger contract already declared in `<family>_body_skeleton.json` — a
   joint *reduction*, directly fixing the fused-mitten defect.
5. **Hair**: groom as Blender hair curves per character → convert to real-time cards with a
   license-checked card-conversion tool → shade with the CC0 OpenGameArt hair-alpha atlas or a custom
   one — retires the lumpy reconstructed-hair defect.
6. **Modular clothing**: build/fit garments as MPFB2 clothes-proxies (shrinkwrap + data transfer) on
   the shared base so one wardrobe fits every identity sculpted from the same topology; hide skin using
   the project's existing 16-region body-region naming convention rather than a new one.
7. **Texture projection**: keep Otherreach's existing "project the concept's own pixels" step — already
   proven and preferred over voxel resynthesis per `CHARACTER_FIDELITY.md`'s own findings — and bake the
   concept-art/target face and skin onto the new clean-topology UVs; same projection principle, clean
   mesh instead of a reconstruction mesh.
8. **Rig**: rig/retarget the finished mesh directly onto Otherreach's existing 52-bone canonical
   contract using the already-confirmed-present `BoneMap`/`SkeletonProfileHumanoid` +
   `RetargetModifier3D`, or Blender-side Rigify via MPFB2's `rigifyhelpers` — drop-in compatible with
   all existing player/NPC animation, no second skeleton standard invented.
9. **Face**: bone-based jaw + eye-aim, blend-shape blink, and a small viseme subset driven by Rhubarb
   Lip Sync output, scripted outside `AnimationTree`, per section 5.2; escalate to the full ARKit-52
   pack only for later hero-cutscene work.

## 7. First proof-of-concept scope (one player character)

1. Pick one existing player identity already fully speced under the old pipeline (e.g. Renn or Tavar —
   both are already documented rejects in `CHARACTER_FIDELITY.md` with concept art and camera-fit data
   available), so the proof has a known-good target and a documented reject baseline to diff against.
2. Install MPFB2 on a Blender 4.2+ dev workstation only (never shipped); generate a base identity
   matching the character's established proportions.
3. Sculpt to match the existing concept art / projection reference, reusing the same concept
   camera-fit data already computed in the old pipeline so texture projection stays comparable.
4. Fit hands (reduce to the 20-bone/2-joint finger contract), fit one wardrobe piece via MPFB2's
   clothes-proxy + shrinkwrap, groom a first hair pass as curves and convert to cards with the CC0
   alpha atlas.
5. Rig to the matching family's 52-bone contract; verify with the project's own
   `_verify_character_skeleton.py`, the same way `humanoid_standard_body_skeleton.json` is already
   verified (bone_count 52, core 24, fingers 20, ik 8, sockets 5).
6. Import into Godot 4.7, retarget existing player animations (walk/run/idle) via the confirmed
   `BoneMap`/`RetargetModifier3D` path, and add the minimal face layer (bone jaw/eye-aim + blink + a
   6-10-shape viseme subset scripted outside `AnimationTree`) driven by one Rhubarb-processed test
   dialogue line.
7. Judge against the same acceptance bar `CHARACTER_FIDELITY.md` already uses — a side-by-side
   comparison sheet (concept, old reconstruction, new clean-topology character; Godot close / dialogue
   / third-person framings), matching the project's own "canvas render is the release gate" / "review
   against the bible" conventions rather than inventing a new review format.

**Effort estimate**: a single character's sculpt + rig + hair + one outfit is a multi-day,
single-artist effort (days, not weeks), since every recommended tool is free and already licensed and
the target skeleton/region conventions already exist in the repo — the cost is artist sculpting and
animation re-verification time, not new licensing or tool acquisition.

## Sources

- Blender Studio Human Base Meshes: https://www.cgchannel.com/2023/06/download-blender-studios-free-human-base-meshes/ , https://archive.org/details/human-base-meshes-bundle-v1.0.0 , https://developer.blender.org/docs/features/asset_system/asset_bundles/human_base_meshes/
- MPFB2: https://github.com/makehumancommunity/mpfb2 , https://github.com/makehumancommunity/mpfb2/blob/master/LICENSE.md , https://static.makehumancommunity.org/mpfb/releases/release_2015.html , https://github.com/makehumancommunity/mpfb2/blob/master/docs/ui/operations/faceops.md , https://github.com/makehumancommunity/mpfb2/issues/302 , https://static.makehumancommunity.org/mpfb/developer/rigging_and_posing.html
- MakeHuman license: https://static.makehumancommunity.org/about/license.html
- Quaternius: https://quaternius.itch.io/universal-base-characters , https://quaternius.com/packs/universalbasecharacters.html , https://quaternius.itch.io/modular-character-outfits-fantasy
- Poly Haven: https://polyhaven.com/license , https://polyhaven.com/models
- Human Generator: https://help.humgen3d.com/license , https://humgen3d.com/pricing
- Reallusion / ActorCore: https://www.reallusion.com/license/content.html , https://actorcore.reallusion.com/eula (fetch blocked; corroborated via search index only)
- MetaHuman: https://www.unrealengine.com/eula/mhc (fetch blocked, HTTP 403; corroborated via https://www.cgchannel.com/2025/06/you-can-now-sell-metahumans-or-use-them-in-unity-or-godot/ and https://digitalproduction.com/2025/06/05/metahumans-graduate-ready-for-unity-godot-and-the-fab-cash-register/ )
- Daz3D: https://www.daz3d.com/daz-licenses
- SMPL / SMPL-X: https://smpl.is.tue.mpg.de/modellicense.html , https://smpl-x.is.tue.mpg.de/modellicense.html , https://smpl.is.tue.mpg.de/license.html
- Meta MHR: https://github.com/facebookresearch/MHR , https://arxiv.org/abs/2511.15586
- Anny: https://github.com/naver/anny
- Rhubarb Lip Sync: https://github.com/DanielSWolf/rhubarb-lip-sync/blob/master/LICENSE.md
- Hair cards: https://www.cgchannel.com/2024/03/daniel-bystedts-free-blender-add-on-creates-hair-cards-from-curves/ , https://github.com/megumumpkin/Hair-Compiler , https://joseconseco.github.io/HairTool_3_Documentation/ , https://opengameart.org/content/hair-alphas-for-days
- Clothing fitting: https://github.com/VRC-Staples/Elastic-Clothing-Fit , https://support.marvelousdesigner.com/hc/en-us/articles/47358258006425-License-Plan
- ARKit blendshapes: https://arkit-face-blendshapes.com/ , https://miniface.org/
- Godot: https://docs.godotengine.org/en/stable/classes/class_meshinstance3d.html , https://docs.godotengine.org/en/stable/tutorials/animation/animation_track_types.html
- Local depot: `F:\Otherreach_External_Assets\EXTERNAL_ASSET_CATALOG.md`, `F:\Otherreach_External_Assets\FINAL_REPORT.md`
- Project contract: `docs/CANONICAL_BODY_AND_SKELETON.md`, `docs/SKELETON_CONTRACT_RECONCILIATION.md`, `docs/phase_b/CHARACTER_FIDELITY.md`, `docs/WAVE_0_MODULAR_ASSET_STANDARD.md`
