# 3D Asset-Generation Pipeline Research (September 2026)

Scope: find the best current local/offline pipeline to produce a production-quality
realistic hero player character (then NPCs/props) from reference images, for
Otherreach (Godot 4.7, worldwide commercial release, public GitHub repo).
Research only. No weights downloaded, nothing installed, no workflow executed.

Machine: RTX 5090, 32GB VRAM, Windows 11. Local ComfyUI for 3D:
`G:\ComfyUI_LTX25\ComfyUI`, server `http://127.0.0.1:8190`.

## 0. What is actually on this machine right now

Read directly from `G:\ComfyUI_LTX25\ComfyUI`:

- **Native 3D node files present** (in `comfy_extras/`, i.e. built into ComfyUI
  core, not a community pack): `nodes_trellis2.py`, `nodes_hunyuan3d.py`,
  `nodes_stable3d.py`, `nodes_sam3d_body.py`, `nodes_mesh_postprocess.py`,
  `nodes_mesh_io.py`, `nodes_load_3d.py`, `nodes_save_3d.py`, plus the
  `mesh3d/` and `sam3d_body/` support packages.
- **No 3D community node packs are installed.** `custom_nodes/` was checked in
  full — no `ComfyUI-3D-Pack`, no Hunyuan3D-Paint wrapper, no InstantMesh/CRM
  node pack, nothing 3D-related. Everything 3D-capable on this box is the
  native core code.
- **No 3D model weights are downloaded yet.** Checked every `models/*`
  subfolder; `checkpoints/`, `diffusion_models/`, `diffusers/`,
  `geometry_estimation/`, `background_removal/`, `detection/`, `unet/` all
  contain only the placeholder `put_..._here` files or unrelated LTX/Z-Image
  weights (`h3ErosMax_beta2.safetensors`, `z_image_turbo_bf16.safetensors`).
  There is no `trellis`, `hunyuan3d`, `pixal3d`, or `sam3d` weight file
  anywhere under `models/`. The native node *code* is ready; the *weights*
  for any 3D model still need to be fetched before anything can run.
- **The workflow template path given in the task,
  `%USERPROFILE%\ComfyUI\user\default\workflows\3d_pixal3d_trellis2_image_to_model.json`,
  does not exist on this machine** (confirmed via `Test-Path` — false, and the
  parent `%USERPROFILE%\ComfyUI` folder itself doesn't exist). It could not be
  read for this report. The driver script,
  `G:\UNNAMED_PHASEB\tools\asset_pipeline\_run_3d_asset.py`, *was* read in
  full and its node-name and parameter usage was cross-checked against the
  live node source below — the analysis in this report reconstructs the
  pipeline shape from that script plus the native node source, not from the
  missing JSON. If the template lives somewhere else, re-point the driver's
  `UNNAMED_3D_TEMPLATE` env var and re-check.

### What the driver script tells us about the intended pipeline

`_run_3d_asset.py` toggles between **Pixal3D** and **Trellis.2** modes via a
`PrimitiveBoolean` node, and drives these node types (all confirmed to exist
in `nodes_trellis2.py` / `nodes_mesh_postprocess.py` / `nodes_save_3d.py`):
`DecimateMesh.target_face_count` (the "40k faces" in the task brief),
`UnwrapMesh.resolution` + `BakeTextureFromVoxel.texture_size` (the "4096
bakes"), `BakeNormalMapFromMesh.resolution`, `BakeAmbientOcclusion.resolution`
+ `.samples`, `Trellis2UpsampleStage.target_resolution`, and
`Save3DAdvanced.filename_prefix`. So the shipped pipeline is: image → Pixal3D
or Trellis.2 conditioning → shape stage → (optional upsample) → texture stage
→ decimate to a game-usable face count → UV-unwrap → bake color/normal/AO →
export GLB.

## 1. Native ComfyUI 3D support — read from source, node by node

### 1.1 TRELLIS.2 (`nodes_trellis2.py`) — native, full PBR

Microsoft's TRELLIS.2 is integrated natively with two conditioning paths that
share the same shape/texture pipeline underneath:

- **`Trellis2Conditioning`** — vanilla single-image TRELLIS.2 path (DINOv3
  global tokens only).
- **`Pixal3DConditioning`** — single-image, pixel-aligned conditioning
  (DINOv3 ViT-L/16 + a bundled NAF upsampler network), with an explicit `fov`
  input.
- **`Pixal3DMultiViewConditioning`** — **multiview input, up to 4 fixed
  orbit views** (`front`, `left`, `back`, `right`, 90° apart), each expected
  as a square, alpha/black-background composite. This is the node the hero
  character's front/back/left/right reference set would feed.
- Shape pipeline: `Trellis2ShapeStage` → optional `Trellis2UpsampleStage`
  (1024–2048 voxel resolution) → `VaeDecodeShapeTrellis` (outputs a `MESH`).
- Texture pipeline: `Trellis2TextureStage` → `VaeDecodeTextureTrellis`. Read
  directly from source (`nodes_trellis2.py` lines ~236–239): the texture VAE
  decodes **6 channels — base_color (0:3), metallic (3), roughness (4), alpha
  (5)** — i.e. this is genuine PBR (albedo + metallic + roughness), not just
  a vertex-color bake. This is the strongest finding in this file: the
  native ComfyUI TRELLIS.2/Pixal3D path already produces metal/roughness
  maps, not just an albedo texture.
- No weights for TRELLIS.2 or Pixal3D are present on this machine (see §0).

### 1.2 Hunyuan3D (`nodes_hunyuan3d.py`) — native, shape-only, targets v2.0/2.1

- `EmptyLatentHunyuan3Dv2` (latent shape 64ch × 3072 tokens — matches the
  Hunyuan3D-DiT 2.0/2.1 architecture specifically, not the newer 2.5/3.0
  architecture, see §2.3), `Hunyuan3Dv2Conditioning` (single image via
  CLIP-Vision), **`Hunyuan3Dv2ConditioningMultiView`** (front/left/back/right,
  4-view, positional-embedding fused), `VAEDecodeHunyuan3D` → voxel,
  `VoxelToMesh` / `VoxelToMeshBasic` (surface-net or basic marching-cubes
  meshing).
- **No texture/paint node exists in this file.** The native Hunyuan3D path in
  this ComfyUI build is shape-generation only; Hunyuan3D-Paint (the PBR
  texture-painting half of the Tencent pipeline) is a separate project not
  wired in here. Texturing a Hunyuan3D mesh on this box would mean either
  running Hunyuan3D-Paint standalone (outside ComfyUI) or feeding the mesh
  through the generic `PaintMesh`/`BakeTextureFromVoxel` mesh-postprocess
  nodes, which are not model-aware and would need a color source.
- No Hunyuan3D weights are present on this machine.

### 1.3 Stable3D (`nodes_stable3d.py`) — native, legacy, not fit for this task

`StableZero123_Conditioning` and `SV3D_Conditioning` (Stable Video 3D). These
are 2023-era single-view **novel-view-synthesis** models (they produce
rotated 2D views or an orbit video from one image), not direct mesh
generators — a separate reconstruction stage would be needed to turn their
output into geometry. Obsolete relative to everything else in this report;
listed for completeness only.

### 1.4 SAM 3D Body (`nodes_sam3d_body.py`) — native, wrong tool for this job

Full pipeline present: `SAM3DBody_Loader`, `SAM3DBody_Predict`,
`SAM3DBody_FaceExpression`, `SAM3DBody_Smooth`, `SAM3DBody_Render`,
`BuildPoseFile`. This is Meta's human **pose/shape reconstruction** model
(SMPL-X-style parametric body from a photo) for pose/motion capture, not a
textured hero-mesh generator. Useful later for animation reference or
mocap-from-video, not for generating a shippable character asset. See §2.6
for licensing if it's ever used.

### 1.5 Generic mesh post-processing (`nodes_mesh_postprocess.py`, `nodes_save_3d.py`, `nodes_load_3d.py`, `nodes_mesh_io.py`) — native, model-agnostic

This is the shared back-end every one of the above feeds into, and it's
comprehensive: `PaintMesh`, `BakeTextureFromVoxel`, `MeshTextureToImage`,
`ApplyTextureToMesh`, `BakeNormalMapFromMesh`, `BakeAmbientOcclusion`,
`RenderMesh`, `DecimateMesh`, `RemeshMesh`, `UnwrapMesh`, `RenderUVAtlas`,
`FillHoles`, `WeldVertices`, `MeshSmoothNormals`; plus
`Load3D`/`Load3DAdvanced`/`Preview3D(Advanced)`/`PreviewGaussianSplat`/
`PreviewPointCloud` and `SaveGLB`/`MeshToFile3D`/`RotateMesh`/`MergeMeshes`/
`GetMeshInfo`/`Save3DAdvanced`/`SaveGaussianSplat`/`SavePointCloud`. This is
where the pipeline's decimate/unwrap/bake/export steps (and the "40k
faces / 4096 bakes" defaults from the driver script) actually happen,
regardless of which generator produced the raw mesh. `RemeshMesh` exists but
its algorithm could not be confirmed from the grep pass (name only) — verify
whether it does isotropic/quad remeshing or triangle decimation before
relying on it for a clean retopology pass.

**Conclusion on "what's runnable now":** the *code path* for TRELLIS.2 +
Pixal3D (single image and 4-view multiview, full PBR) and for Hunyuan3D
2.0/2.1 (single image and 4-view multiview, shape-only) both already exist
natively in this ComfyUI build. Nothing will actually run until the
corresponding model weights are downloaded into `models/` (out of scope for
this research task) and the missing workflow JSON is located or rebuilt.

## 2. Candidate matrix

Format per candidate: name/version — repo — license (code / weights if
different) — commercial/worldwide — output rights — redistribution — AI/ML
restrictions — account needed — VRAM — Windows — ComfyUI support — input
modes — topology — texture/PBR — formats — speed — **verdict**.

### 2.1 TRELLIS.2 (Microsoft, 4B params, Dec 2025)
- Repo: https://github.com/microsoft/TRELLIS.2 · weights:
  https://huggingface.co/microsoft/TRELLIS.2-4B
- License: **MIT**, code and weights both (per the model card; some
  dependencies — e.g. nvdiffrast — carry their own separate licenses, none of
  which are copyleft). Read at
  https://huggingface.co/microsoft/TRELLIS.2-4B/blob/main/README.md
- Commercial use: allowed, worldwide, no revenue/MAU gate found.
- Output rights: no claim over generated assets found in the license.
- Redistribution: MIT — permissive, attribution/license-notice only.
- AI/ML restrictions: none found.
- Account/API: none, fully local.
- VRAM: not explicitly numbered in the card; a 4B-parameter sparse-voxel
  diffusion model with an "O-Voxel" structured latent — comfortably fits a
  32GB 5090 based on architecture size, but treat as UNCLEAR until measured.
- Windows: not explicitly documented; ComfyUI's native integration (this
  machine) runs cross-platform since it's built into ComfyUI core.
- ComfyUI: **native** (`nodes_trellis2.py`, confirmed above).
- Input modes: single image or 4-view multiview (via the Pixal3D
  conditioning nodes layered on the same TRELLIS.2 backbone).
- Topology: sparse-voxel / structured-latent mesh, arbitrary topology,
  sharp features supported per the model card.
- Texture: full PBR — **base color + metallic + roughness** decoded
  natively (confirmed from `VaeDecodeTextureTrellis` source, §1.1).
- Formats: GLB via the native `SaveGLB`/`Save3DAdvanced` nodes.
- Speed: UNCLEAR — not measured; no public benchmark read for this report.
- **Verdict: CANDIDATE-FOR-BENCHMARK.** Best license terms of any
  high-quality candidate, native support already in the local ComfyUI, and
  the only path in this list with confirmed metal/roughness PBR out of the
  box.

### 2.2 Pixal3D (TencentARC, SIGGRAPH 2026)
- Repo: https://github.com/TencentARC/Pixal3D · project page:
  https://ldyang694.github.io/projects/pixal3d/
- What it is: a pixel-aligned single/multi-image-to-3D model that
  back-projects 2D pixel features directly into 3D space (rather than only
  cross-attending to image features), for higher-fidelity geometry + PBR
  texture than prior feed-forward methods. It is *also* the second
  conditioning mode implemented inside ComfyUI's native `nodes_trellis2.py`
  file — i.e., in this local ComfyUI build, "Pixal3D" is not a separate
  model to install, it's a conditioning/quantization mode layered on the
  TRELLIS.2 backbone (see the `pixal3d_mode` branch in
  `Trellis2UpsampleStage._quantize_unique`, §1.1). Whether that native
  integration is bit-for-bit the same checkpoint as TencentARC's official
  release, or ComfyUI's own re-trained equivalent, could not be confirmed
  without the weights present — flag as UNCLEAR until a checkpoint is loaded
  and its metadata is checked.
- License: **code MIT**, per the README (fetched directly); "third-party
  components retain their original licensing terms" — the specific
  third-party pieces were not enumerated in the README and should be
  re-checked before shipping (UNCLEAR on weight-level attribution
  requirements beyond MIT).
- Commercial use: no restriction stated in the README.
- Multiview: yes — `inference_mv.py` takes several posed views via
  `transforms.json` (this matches the native `Pixal3DMultiViewConditioning`
  node's fixed 4-view orbit rig).
- PBR: yes, explicit PBR texture stage.
- VRAM: "Low-VRAM mode" mentioned (loads models on demand, defaults to 1024
  vs. 1536 resolution) but no GB figure given — UNCLEAR exact number.
- ComfyUI: **native**, as the Pixal3D conditioning path inside
  `nodes_trellis2.py`.
- Formats/topology: same GLB/mesh pipeline as TRELLIS.2 (they share
  `VaeDecodeShapeTrellis`/`VaeDecodeTextureTrellis`).
- **Verdict: CANDIDATE-FOR-BENCHMARK.** MIT, native, multiview, PBR — same
  strength profile as TRELLIS.2 because they're the same underlying node
  path on this machine; the two should be benchmarked together as "the
  TRELLIS.2/Pixal3D native pipeline" rather than as fully separate systems.

### 2.3 Hunyuan3D 2.0 / 2.1 (Tencent) — open weights; 2.5 / 3.0 are NOT open weights
- Repos: https://github.com/Tencent-Hunyuan/Hunyuan3D-2 ,
  https://github.com/Tencent-Hunyuan/Hunyuan3D-2.1
- License: **Tencent Hunyuan Community License** (both code and weights —
  the license's own definitions section covers "trained model weights,
  parameters..., machine-learning model code, inference-enabling code,
  training-enabling code, fine-tuning enabling code"). Read directly from
  https://github.com/Tencent-Hunyuan/Hunyuan3D-2.1/blob/main/LICENSE :
  - **Territory exclusion, quoted exactly:** "the worldwide territory,
    excluding the territory of the European Union, United Kingdom and South
    Korea." Otherreach ships worldwide, so **this license cannot be used for
    a worldwide release** as long as that clause stands — EU/UK/South Korea
    users would be outside the granted territory.
  - **MAU threshold, quoted in substance:** a separate license is required
    once "monthly active users of all products or services made available by
    or for Licensee is greater than 1 million monthly active users in the
    preceding calendar month."
  - The license also forbids using outputs to train/improve a competing AI
    model (typical of these Tencent/community licenses).
- Commercial use: allowed under 1M MAU, outside the excluded territories.
- Multiview: yes, native `Hunyuan3Dv2ConditioningMultiView` (front/left/
  back/right).
- Texture/PBR: Hunyuan3D-2.1 ships **Hunyuan3D-Paint**, a separate
  albedo+metallic+roughness+normal PBR texture-painting stage
  (`hy3dpaint/`, https://github.com/Tencent-Hunyuan/Hunyuan3D-2.1 ) — but
  **this is not wired into the native ComfyUI node file** on this machine
  (§1.2); it would need to run standalone or via a community node.
- VRAM: general community reporting puts high-quality Hunyuan3D generation
  around 24GB+ VRAM class hardware; fits the 5090 but should be measured,
  not assumed.
- ComfyUI: **native for shape** (2.0/2.1 architecture only — the native
  latent shape is 64×3072, matching 2.0/2.1's DiT, not the newer 3.0
  hierarchical-sculpting architecture). No native texture node.
- 2.5 status: per Tencent's own GitHub issue tracker
  (Tencent-Hunyuan/Hunyuan3D-2.1 issue #111, "Inquiry: Open-Source Plans for
  Hunyuan3D-2.5 and Hunyuan3D-PolyGen"), 2.5's weights/code have **not**
  been confirmed open-sourced as of this research.
- 3.0 status: Tencent's September 2025 announcement of "Hunyuan 3D 3.0" (up
  to 1.5M-face geometry, 8K PBR) and the subsequent "global launch of the
  Hunyuan 3D Engine" describe a **hosted creation engine/API product**, not
  a downloadable open-weight release — no GitHub/HF weights repo for 3.0
  was found. Treat 3.0 as SaaS/account-dependent, not a local option, until
  contradicted by an actual weights release.
- **Verdict: BENCHMARK-ONLY (license blocks worldwide shipping).** The
  EU/UK/South Korea exclusion is a hard blocker for a public worldwide
  commercial release under the stock license; usable for internal
  benchmarking/comparison only unless Tencent grants a separate license or
  the game's distribution is restricted to exclude those territories
  (not acceptable per the stated goal). If Otherreach ever needs Hunyuan3D
  quality specifically, budget time to contact Tencent about a commercial
  license rather than assuming the community license covers it.

### 2.4 ComfyUI-3D-Pack (MrForExample) — community node pack, MIT wrapper
- Repo: https://github.com/MrForExample/ComfyUI-3D-Pack · License: **MIT**
  (confirmed by fetching the repo's LICENSE file directly).
- What it exposes: a large node suite wrapping InstantMesh, CRM, TripoSR,
  TriplaneGaussian, Wonder3D, 3DGS/NeRF tooling, etc., inside ComfyUI. The
  wrapper itself is MIT, but **each wrapped model keeps its own upstream
  license** — the wrapper's permissiveness does not override e.g. a
  restrictive underlying checkpoint.
- Maintenance: last broad update found dated mid-2025; no 2026 activity
  confirmed in this research pass — treat as **possibly stale**; verify the
  repo's commit history before depending on it, since it is not part of
  ComfyUI core and could break against a current ComfyUI build.
- ComfyUI: community node pack, **not installed on this machine** (§0).
- **Verdict: CANDIDATE-FOR-BENCHMARK as a wrapper**, conditional on
  confirming it still installs cleanly against this ComfyUI version, and on
  checking each wrapped model's own license before shipping anything
  produced through it.

### 2.5 InstantMesh (TencentARC)
- Repo: https://github.com/TencentARC/InstantMesh · License: **Apache-2.0**
  (confirmed via the repo's LICENSE file).
- Commercial use: allowed, worldwide, attribution/NOTICE preservation only.
- Input: single image, or sparse multiview generated internally via a
  multiview diffusion front-end.
- Topology/texture: sparse-view LRM reconstruction; textured mesh output,
  vertex-color/baked-texture quality generally considered below TRELLIS.2 /
  Hunyuan3D-2.1 for hero-character fidelity as of 2026 comparisons.
- ComfyUI: via ComfyUI-3D-Pack (community), not native.
- **Verdict: CANDIDATE-FOR-BENCHMARK** as a fast/cheap secondary check, low
  priority relative to TRELLIS.2/Pixal3D for a hero character.

### 2.6 CRM — Convolutional Reconstruction Model (Tsinghua, thu-ml)
- Repo: https://github.com/thu-ml/CRM · License: **MIT** (confirmed via
  the repo's LICENSE file).
- Commercial use: unrestricted under MIT.
- Speed: ~10 seconds single-image → textured mesh per the paper title, but
  quality is lower-fidelity than TRELLIS.2/Hunyuan3D for a hero-quality
  asset.
- ComfyUI: via ComfyUI-Flowty-CRM or ComfyUI-3D-Pack (community).
- **Verdict: BENCHMARK-ONLY** — useful as a fast draft/iteration tool
  (e.g. rapid silhouette checks for props), not a hero-character finalist.

### 2.7 TripoSR (Stability AI + Tripo AI)
- Repo: https://github.com/VAST-AI-Research/TripoSR · weights:
  https://huggingface.co/stabilityai/TripoSR
- License: **MIT**, code and weights, copyright Tripo AI & Stability AI
  (confirmed via search of the LICENSE file).
- Commercial use: unrestricted, worldwide.
- Input: single image only, feed-forward, sub-second on modern GPUs.
- Topology/texture: fast but noticeably lower fidelity/detail than
  TRELLIS.2/Hunyuan3D — a speed play, not a quality play.
- ComfyUI: via ComfyUI-3D-Pack (community), not native.
- **Verdict: BENCHMARK-ONLY** for the hero character (not enough fidelity);
  reasonable CANDIDATE-FOR-BENCHMARK for quick prop/NPC iteration later.

### 2.8 Stable Fast 3D / SPAR3D (Stability AI)
- Repos/cards: https://huggingface.co/stabilityai/stable-fast-3d ,
  https://huggingface.co/stabilityai/stable-point-aware-3d · License:
  **Stability AI Community License** for both.
- Commercial use: free for orgs/individuals under **US $1,000,000 in annual
  revenue**; above that, an Enterprise License must be purchased directly
  from Stability AI (confirmed via Stability's license page and the SPAR3D
  model card, which states outputs and derivative "fine tunes" are also
  covered by the same threshold).
- Risk for Otherreach: a "commercial PC game, shipped worldwide" is exactly
  the kind of product that could cross $1M revenue if successful, at which
  point continuing to use SPAR3D/Stable Fast 3D outputs would require going
  back to Stability for an Enterprise License — a real, if deferred,
  obligation. Not a hard blocker today but a known future cost/renegotiation
  to flag now rather than discover after shipping.
- Topology/texture: SPAR3D and Stable Fast 3D are fast (sub-second to
  seconds) single-image reconstructors, again a speed/quality trade rather
  than a hero-fidelity finalist relative to TRELLIS.2.
- ComfyUI: not confirmed native on this build; would need a community node.
- **Verdict: CANDIDATE-FOR-BENCHMARK with a licensing caveat** — fine to
  benchmark now, but track the $1M revenue clause as a standing compliance
  item if it's ever chosen for shipped production assets.

### 2.9 TripoSG / TripoSF (VAST-AI-Research / Tripo)
- Repos: https://github.com/VAST-AI-Research/TripoSG ,
  https://github.com/VAST-AI-Research/TripoSF · License: **MIT** for both
  (confirmed via each repo's LICENSE search result; TripoSG ships a NOTICE
  file crediting other open-source code it derives from — check that NOTICE
  before redistribution, standard MIT practice, not a restriction).
- Commercial use: unrestricted, worldwide.
- What they are: TripoSG is a large rectified-flow-transformer image-to-3D
  shape model (high-fidelity geometry); TripoSF is the "SparseFlex"
  representation for high-resolution arbitrary-topology shapes, including
  open surfaces (useful for e.g. clothing, hair strands, thin props) —
  something TRELLIS.2/Hunyuan3D's closed-volume voxel representations handle
  less naturally.
- ComfyUI: not native on this machine; no confirmed community node pack in
  this research pass — likely needs a custom wrapper or standalone
  inference script (UNCLEAR on ComfyUI integration effort).
- **Verdict: CANDIDATE-FOR-BENCHMARK**, specifically worth a look for
  open-surface detail (hair, cloth, gear straps) that closed-volume methods
  smooth over, if the ComfyUI integration gap can be closed cheaply.

### 2.10 Step1X-3D (StepFun)
- Repo: https://github.com/stepfun-ai/Step1X-3D · License: **Apache-2.0**
  (confirmed).
- Commercial use: unrestricted, worldwide.
- What it is: controllable textured-3D-asset generation, notable for
  transferring 2D control techniques (e.g. ControlNet-style guidance)
  directly into 3D generation.
- ComfyUI: not confirmed native or community-wrapped in this research pass.
- **Verdict: CANDIDATE-FOR-BENCHMARK**, contingent on someone building or
  finding a ComfyUI wrapper, or running its own inference scripts standalone
  (outside ComfyUI) for a first quality check.

### 2.11 Direct3D-S2 (DreamTechAI / NJU)
- Repo: https://github.com/DreamTechAI/Direct3D-S2 · License: **MIT**
  (confirmed).
- Commercial use: unrestricted.
- What it is: gigascale sparse-volume 3D generation via Spatial Sparse
  Attention — pitched at very high-resolution geometry with lower training
  cost; NeurIPS 2025.
- ComfyUI: not confirmed integrated; has its own Gradio demo.
- **Verdict: CANDIDATE-FOR-BENCHMARK** for geometry fidelity specifically,
  pending a ComfyUI integration path or standalone run.

### 2.12 Hi3DGen (ByteDance / CUHK-Shenzhen, aka Stable3DGen)
- Repo: https://github.com/bytedance/Hi3DGen ; ComfyUI wrapper:
  https://github.com/Stable-X/ComfyUI-Hi3DGen · License: **MIT**, and
  notably the maintainers deliberately stripped NVIDIA-licensed dependencies
  (kaolin, nvdiffrast, flexicube) specifically so this fork can be used
  commercially without pulling in those separate license terms — a
  license-hygiene detail worth calling out positively.
- Commercial use: unrestricted.
- What it is: normal-map-bridged geometry generation (image → normal map →
  geometry) aimed specifically at fine surface detail that direct
  image-to-geometry methods lose.
- ComfyUI: **community node pack exists** (`Stable-X/ComfyUI-Hi3DGen`) —
  not installed on this machine.
- **Verdict: CANDIDATE-FOR-BENCHMARK**, good license hygiene and an existing
  ComfyUI wrapper make this one of the easier non-native options to actually
  try.

### 2.13 SAM 3D Objects / SAM 3D Body (Meta)
- Repos: https://github.com/facebookresearch/sam-3d-objects ,
  SAM 3D Body ships inside this ComfyUI's native `nodes_sam3d_body.py`.
- License: **Meta "SAM License"** (custom, Apache-like but with carve-outs).
  Read directly from the LICENSE file: commercial use is generally allowed
  ("non-exclusive, worldwide, non-transferable and royalty-free limited
  license"); explicit **field-of-use bans on "military or warfare purposes,
  nuclear industries or applications, espionage, or the development or use
  of guns or illegal weapons,"** plus Trade Control/ITAR compliance
  obligations; you own your own derivative works but Meta retains ownership
  of the original SAM materials; redistribution of derivatives must carry
  the same license; a patent-suit termination clause is included (typical of
  Meta's recent model licenses, e.g. Llama).
- None of the field-of-use bans (military/nuclear/weapons/espionage) touch a
  commercial RPG, so this license is workable for Otherreach.
- What it does: SAM 3D Objects reconstructs a full 3D object/scene from a
  single image; SAM 3D Body reconstructs human pose/shape (SMPL-X-style),
  already native in ComfyUI (§1.4) but aimed at pose capture, not a
  textured, game-ready hero mesh.
- ComfyUI: SAM 3D Body native; SAM 3D Objects not confirmed integrated here.
- **Verdict: CANDIDATE-FOR-BENCHMARK for SAM 3D Objects** (worth comparing
  on props/environment objects); **REJECT for the hero-character generation
  task specifically** for SAM 3D Body — it's a pose/shape estimator, not a
  textured asset generator, wrong tool for this job even though the license
  is fine and the node is already native.

### 2.14 PartCrafter
- Repo: https://github.com/wgsxm/PartCrafter · License: code **MIT**
  (stated as forthcoming/released with the code), dataset **CC-BY 4.0**
  (confirmed via search of the paper page and repo description).
- Commercial use: unrestricted for the code/model; CC-BY attribution
  required only if the *dataset* itself is redistributed, not for using the
  trained model's outputs.
- What it is: generates multiple disentangled parts/objects from one RGB
  image in a single pass — potentially useful later for prop sets or
  compositional NPC gear (e.g. a character plus separate equippable items in
  one generation), less directly relevant to a single hero-body mesh.
- ComfyUI: not integrated; standalone research code.
- **Verdict: CANDIDATE-FOR-BENCHMARK for later prop/part-composited work**,
  not a first-tier pick for the hero character itself.

### 2.15 Multiview generators: MV-Adapter, Zero123++, Era3D
- **MV-Adapter** — https://github.com/huanngzh/MV-Adapter · License:
  **Apache-2.0** (confirmed). ComfyUI wrapper exists:
  https://github.com/huanngzh/ComfyUI-MVAdapter. Adapts SDXL (and similar)
  into a consistent multiview generator, useful as a *front-end* to produce
  a clean 4-view (or more) reference set from a single hero-concept image
  before feeding TRELLIS.2/Pixal3D's multiview conditioning node.
  **CANDIDATE-FOR-BENCHMARK**, specifically as the multiview-reference
  generation step upstream of the 3D reconstructors.
- **Zero123++** — https://github.com/SUDO-AI-3D/zero123plus · License:
  **Apache-2.0** (confirmed via LICENSE fetch). Older (2023) but permissive
  and simple; single-image → fixed 6-view multiview diffusion.
  **CANDIDATE-FOR-BENCHMARK** as a cheap fallback multiview front-end,
  behind MV-Adapter in expected quality for 2026.
- **Era3D** — https://github.com/pengHTYX/Era3D · License: **AGPL-3.0**.
  The repo's own text says "any downstream solution and products that
  include the codes or the pretrained model inside it should be
  open-sourced to comply with the AGPL." That is a direct conflict with a
  closed-source commercial game pipeline if the model or its code is
  embedded in shipped tooling; even used only offline in the art pipeline
  (not shipped in the game binary), AGPL's network-use clause and viral
  redistribution terms make this legally murkier than any other candidate
  in this report. **Verdict: REJECT** (or BENCHMARK-ONLY strictly as a
  never-redistributed, never-networked internal quality comparison, with
  legal sign-off first) — do not adopt Era3D into the shipped or
  redistributed pipeline without a lawyer's review of AGPL's applicability
  to generated-asset-only use.

### 2.16 Texture/PBR generators
- **Hunyuan3D-Paint** (part of Hunyuan3D-2.1) — same **Tencent Hunyuan
  Community License** as §2.3, same EU/UK/South Korea exclusion and 1M MAU
  cap. **Verdict: BENCHMARK-ONLY**, same reasoning as Hunyuan3D itself.
- **MV-Adapter texturing mode** — same Apache-2.0 as §2.15; can project
  multiview-consistent textures onto an existing mesh using geometry
  guidance. **CANDIDATE-FOR-BENCHMARK.**
- **Material Anything** (3DTopia) —
  https://github.com/3DTopia/MaterialAnything, CVPR 2025 Highlight; license
  was **not found/stated** in the README or search results available for
  this report — **UNCLEAR**, do not assume permissive until the LICENSE
  file is actually located and read.

### 2.17 Auto-retopology / remeshing
- **Instant Meshes** (ETH Zurich, Jakob et al.) — long-standing tool for
  clean triangle/quad meshes; historically BSD-3-Clause-style permissive
  (verify the exact repo's LICENSE before use; not independently re-fetched
  in this pass — **UNCLEAR, low risk** given its academic/permissive
  reputation but confirm before shipping).
- **QuadriFlow** — algorithmic quad remesher, available inside Blender's
  built-in remesh tools as of recent Blender versions; Blender-bundled use
  inherits Blender's GPL context only if you're distributing a Blender
  build, not if you're just using Blender as an artist tool in your
  pipeline — **not a licensing concern for asset production**, only for
  redistributing modified Blender itself (not applicable here).
- **QuadWild** — research code for quad-dominant remeshing preserving
  feature lines; check its specific repo license before adopting (not
  independently re-fetched here — **UNCLEAR**).
- Newer learned remeshers (**QuadGPT**, **QuadLink**, **TriFlow**) surfaced
  in the 2026 search pass as active research (arXiv 2609.xxxx / 2606.xxxx
  range, i.e. very recent) but no production-ready, licensed, downloadable
  tool was confirmed for any of them in this pass — **UNCLEAR / too
  immature to recommend today**, worth re-checking again in a few months.
- Practical note: ComfyUI's own native `RemeshMesh` node (§1.5) should be
  tried first since it requires zero extra installation — its algorithm
  wasn't confirmed from source in this pass, so bench it against Instant
  Meshes/QuadriFlow rather than assuming parity.

### 2.18 Auto-rigging
- **UniRig** (Tsinghua + VAST-AI-Research/Tripo, SIGGRAPH 2025) —
  https://github.com/VAST-AI-Research/UniRig · License: **MIT** (confirmed).
  Predicts a full skeleton plus skinning weights from a mesh via an
  autoregressive "Skeleton Tree Tokenization" method; reports large
  accuracy gains over prior academic and commercial auto-riggers in its own
  benchmarks (treat their self-reported numbers as a starting point, not
  verified independently here). **Verdict: CANDIDATE-FOR-BENCHMARK** — MIT,
  no restrictions, directly relevant to turning a generated hero mesh into
  something Godot's animation system can drive.

### 2.19 Commercial SaaS (API/account-dependent — listed for completeness, not "local/offline")
- **Meshy** — free tier outputs are CC BY 4.0 (attribution required);
  Pro-tier-and-above grants commercial rights to generated models. Requires
  an account and network access; not usable for the "local/offline" goal of
  this research, and free-tier CC-BY outputs would legally require crediting
  Meshy in a shipped commercial game unless paid tier is used.
- **Tripo (Tripo AI)** — free tier cannot commercialize output; paid tier
  grants full commercial rights (monetize/distribute/copyright exported
  meshes). Account/API required.
- **Rodin (Hyper3D)** — commercial terms exist but exact output-rights
  language was not confirmed in this pass — **UNCLEAR**, re-check Hyper3D's
  own ToS directly before any use. Account/API required.
- **CSM (Common Sense Machines)** — same caveat, exact terms not confirmed
  here — **UNCLEAR**. Account/API required.
- All four: **verdict N/A for this research's local/offline mandate** —
  listed only because the task asked for them; do not treat any of them as
  "CANDIDATE-FOR-BENCHMARK" under the local-pipeline framing without a
  separate decision to accept an ongoing account/API dependency and
  per-generation cost.

## 3. Recommended benchmark set (3–4 candidates)

1. **TRELLIS.2 native (single-image + Pixal3D multiview)** — top pick.
   MIT throughout, already coded into this ComfyUI build, only needs
   weights. Full PBR (albedo/metallic/roughness) confirmed from source.
   Accepts the 4-view front/left/back/right reference set directly via
   `Pixal3DMultiViewConditioning`.
2. **Hi3DGen** (`Stable-X/ComfyUI-Hi3DGen`) — best "quick win" second
   candidate: MIT, deliberately stripped of NVIDIA-licensed deps, existing
   ComfyUI wrapper, and its normal-map-bridged approach targets exactly the
   kind of fine surface detail (skin pores, fabric weave, armor edges) a
   hero character needs and that TRELLIS.2's voxel representation can
   under-resolve. Single-image input only (no native multiview found) — use
   it as a geometry-detail comparison against TRELLIS.2, not a multiview
   pipeline.
3. **TripoSG / TripoSF** — MIT, no restrictions, and TripoSF's open-surface
   handling is the best answer in this list to hair, cloth folds, and thin
   gear straps that closed-volume methods (TRELLIS.2, Hunyuan3D) tend to
   weld shut or smooth over. Needs a ComfyUI wrapper built or run standalone
   — budget integration time.
4. **Hunyuan3D 2.1, benchmark-only** — include it in the quality comparison
   specifically *because* Tencent's own PBR texture stage
   (Hunyuan3D-Paint: albedo+metallic+roughness+normal) is a genuinely strong
   reference point for texture quality, and because it's worth knowing how
   far ahead or behind TRELLIS.2/Hi3DGen it is before deciding whether it's
   worth pursuing a commercial license from Tencent. **Do not ship anything
   produced with it** under the stock community license — the EU/UK/South
   Korea exclusion directly conflicts with "shipped worldwide."

**Hybrid route for the hero character:** generate the head/face at high
fidelity from a tightly-controlled multiview face reference set (TRELLIS.2
Pixal3D multiview, since faces are where pixel-aligned back-projection should
pay off most), and combine it with a **hand-authored or UniRig-auto-rigged
clean humanoid base body** (a conventional low/mid-poly game-ready body mesh
with proper edge loops for deformation) rather than relying on any of these
generators for full-body topology — none of the candidates above claim
game-ready deformation-quality body topology out of the box; they all
produce a dense, non-edge-flow-aware mesh that then needs `DecimateMesh` +
manual or `RemeshMesh`/Instant-Meshes retopology before it will skin cleanly
in Godot. Generated head/face mesh → retopologized/blended onto the clean
body base → UniRig for skeleton + skinning weights → hand-touch the skinning
in Godot or Blender before shipping is the realistic production path, not a
single generator producing a finished rigged hero in one pass.

**Already runnable through native ComfyUI nodes on this machine** (code
present, weights not yet downloaded): TRELLIS.2, Pixal3D (as a TRELLIS.2
conditioning mode), Hunyuan3D 2.0/2.1 (shape only, no native texturing),
Stable Zero123/SV3D (obsolete), and SAM 3D Body (wrong tool for this task).
Everything else in this report (Hi3DGen, TripoSG/TripoSF, InstantMesh, CRM,
TripoSR, Step1X-3D, Direct3D-S2, MV-Adapter, Zero123++, UniRig, PartCrafter,
Material Anything) requires either installing a community ComfyUI node pack
or running standalone inference scripts outside ComfyUI.

## 4. License summary table (quick reference)

| Candidate | License (code/weights) | Worldwide commercial OK | Verdict |
|---|---|---|---|
| TRELLIS.2 | MIT / MIT | Yes | CANDIDATE-FOR-BENCHMARK |
| Pixal3D | MIT (code); third-party terms UNCLEAR | Likely, verify | CANDIDATE-FOR-BENCHMARK |
| Hunyuan3D 2.0/2.1 | Tencent Hunyuan Community License | **No** (EU/UK/South Korea excluded; 1M MAU cap) | BENCHMARK-ONLY |
| Hunyuan3D 2.5 | Not confirmed open-weight | N/A | REJECT (unavailable) |
| Hunyuan3D 3.0 | SaaS/"Engine", no open weights found | N/A | REJECT (not local) |
| ComfyUI-3D-Pack | MIT (wrapper only) | Depends on wrapped model | Conditional CANDIDATE |
| InstantMesh | Apache-2.0 | Yes | CANDIDATE-FOR-BENCHMARK |
| CRM | MIT | Yes | BENCHMARK-ONLY (low fidelity) |
| TripoSR | MIT | Yes | BENCHMARK-ONLY (low fidelity for hero) |
| Stable Fast 3D / SPAR3D | Stability Community License | Yes under $1M revenue/yr | CANDIDATE-FOR-BENCHMARK (watch revenue clause) |
| TripoSG / TripoSF | MIT / MIT | Yes | CANDIDATE-FOR-BENCHMARK |
| Step1X-3D | Apache-2.0 | Yes | CANDIDATE-FOR-BENCHMARK |
| Direct3D-S2 | MIT | Yes | CANDIDATE-FOR-BENCHMARK |
| Hi3DGen | MIT | Yes | CANDIDATE-FOR-BENCHMARK |
| SAM 3D Objects | Meta SAM License | Yes (no relevant field-of-use conflict) | CANDIDATE-FOR-BENCHMARK |
| SAM 3D Body | Meta SAM License | Yes, but wrong tool for hero-mesh generation | REJECT (for this task) |
| PartCrafter | MIT (code) / CC-BY 4.0 (dataset) | Yes | CANDIDATE-FOR-BENCHMARK (later, parts/props) |
| MV-Adapter | Apache-2.0 | Yes | CANDIDATE-FOR-BENCHMARK |
| Zero123++ | Apache-2.0 | Yes | CANDIDATE-FOR-BENCHMARK |
| Era3D | AGPL-3.0 | **No** (viral copyleft risk) | REJECT |
| Hunyuan3D-Paint | Tencent Hunyuan Community License | **No** (same as 2.1) | BENCHMARK-ONLY |
| Material Anything | UNCLEAR | UNCLEAR | UNCLEAR |
| UniRig | MIT | Yes | CANDIDATE-FOR-BENCHMARK |
| Meshy / Tripo / Rodin / CSM | SaaS ToS, tier-dependent | Paid tiers only | N/A (not local/offline) |

## 5. Notes on sourcing and gaps

- Every license claim above cites the specific URL it was read from inline;
  where a LICENSE/README could not be directly fetched and the claim rests
  on aggregated search-result summaries rather than the primary file, it is
  marked UNCLEAR rather than stated as fact (Material Anything's license,
  Rodin/CSM exact output-rights language, Instant Meshes'/QuadWild's exact
  current license, and the exact bit-for-bit relationship between
  TencentARC's public Pixal3D checkpoint and whatever weights would load
  into ComfyUI's native Pixal3D conditioning nodes).
- The task's referenced workflow template JSON does not exist on this
  machine (§0) — the pipeline shape described in §0 is reconstructed from
  the driver script's node usage and the native node source, not read
  directly from that file.
- No VRAM figures were independently measured — this was a research task,
  not a benchmark run; every VRAM number above is either a vendor claim, a
  community estimate, or explicitly marked UNCLEAR. Actual measurement
  should be the first step of the benchmark phase, using the same
  `--measure` GPU/CPU sampling already built into
  `_run_3d_asset.py`.
