# Asset Pipeline (BEAST / RTX 3090)

Concept image to game-ready asset with no manual steps. Z-Image Turbo renders the
concept, ComfyUI's native Trellis.2 + Pixal3D generates the mesh and PBR set, and
Blender normalises scale and origin, forces single-sided, and builds LODs and
collision.

```
asset request (JSON)
  -> Z-Image Turbo concept render            _make_concepts.py
  -> background removal (BiRefNet) -> camera FOV (MoGe)
  -> Pixal3D or Trellis.2 sparse structure -> shape -> texture
  -> voxel -> mesh -> remesh -> decimate -> UV unwrap
  -> bake baseColor/metallic/roughness + normal + AO
  -> GLB with embedded PBR                   _run_3d_asset.py
  -> Blender: normalise, single-sided, LOD1-3, collision hull + box
                                             _blender_cleanup.py
  -> Godot-ready asset folder                _make_assets.py
```

## Step 1 - concepts

```
python W:\UNNAMED\tools\asset_pipeline\_make_concepts.py requests.json
```

Reads a JSON list of asset requests and renders one concept per entry at ~25 s each
into `assets\concepts\<id>.png`. `--skip-existing` resumes a partial run.

Request format:

```json
[
  {"id": "weapon_iron_war_axe", "prompt": "game asset concept art of ..."},
  {"id": "icon_heal", "prompt": "...", "width": 768, "height": 768, "seed": 42}
]
```

A standard suffix is appended to every prompt so the 3D pass gets what it needs:
one object, centred, whole object in frame, plain flat background, no text or border.
Individual prompts therefore only describe the asset itself.

Prompt enhancement is deliberately **off**. It defaults to on, which would call an
LLM once per image and make results unreproducible from the request file.

## Step 2 - concepts to 3D

```
python W:\UNNAMED\tools\asset_pipeline\_make_assets.py
```

With no arguments it reads `assets\concepts`, writes raw GLBs to `assets\raw`, and
cleaned assets to `assets\ready`, deriving `manifests\` alongside. Override with
`--out`, or pass a concepts folder positionally.

For each concept this generates the raw GLB, moves it into `raw/`, cleans it,
verifies the result, and writes a manifest to `manifests/run_<timestamp>.json`.

Flags: `--trellis2` (switch model), `--faces 25000` (pre-cleanup decimate),
`--texture-size 2048`, `--target-size 1.2` (force metres for every asset),
`--lod-faces 8000,2500,600`, `--only NAME`, `--limit N`, `--skip-generate`.

`--category` defaults to `auto`, which infers target size per asset from its
filename (character, creature, building, weapon, shield, tool, icon, prop) so one
run does not force every asset to a single size. Pass an explicit category to force
it, or `--target-size` to override the size itself.

```
$ python W:\UNNAMED\tools\asset_pipeline\_make_assets.py
=== 3 asset(s) ===
concepts : W:\UNNAMED\assets\concepts
raw      : W:\UNNAMED\assets\raw
ready    : W:\UNNAMED\assets\ready
[1/3] weapon_viking_rune_axe
    cleanup     2.9s  [weapon] 24558 faces, [1.2, 0.2665, 1.1682] m, 3 LODs
[2/3] char_alana_front
    cleanup     2.9s  [character] 23179 faces, [0.6109, 0.6455, 1.8] m, 3 LODs
[3/3] creature_mecha_dragon
    cleanup     2.8s  [creature] 24932 faces, [1.8, 0.8249, 1.4059] m, 3 LODs
```

### Re-cleaning is free

`--skip-generate` reuses raw GLBs already in `raw/` and re-runs only the Blender
pass. That takes **~3 s per asset and uses no GPU**, so tune LOD budgets, target
sizes, and collision settings without ever re-running generation.

```
python W:\UNNAMED\tools\asset_pipeline\_make_assets.py --skip-generate --lod-faces 4000,1200,300
```

Per-asset output, under `assets\ready\<name>\`:

```
  <name>.glb                    cleaned base mesh, textured, origin at base centre
  <name>_lod1.glb               geometry only, no textures
  <name>_lod2.glb
  <name>_lod3.glb
  <name>_collision_hull.glb     convex hull
  <name>_collision_box.glb      axis-aligned box
  <name>_meta.json              dimensions, face counts, LOD ratios
```

## Catalogue

`_make_assets.py` refreshes the catalogue at the end of every run, and it can be run
standalone:

```
python _catalog_assets.py
```

It reads each asset's `_meta.json` plus the run manifests and writes:

- `assets\catalog.json` - machine-readable index keyed by asset id, with category,
  triangle count, dimensions, LOD face counts, collision stats, and provenance
  (which concept, which model, which manifest produced it)
- `assets\CATALOG.md` - the same grouped by category, for reading

This is the metadata plane the game side consumes, so a content list can be built
without walking the asset folders.

## ComfyUI workflows

Openable graphs in `C:\Users\jluca\ComfyUI\user\default\workflows\`:

| Workflow | Purpose |
|---|---|
| `CONCEPT_weapons_props_3D.json` | weapon/prop concept, tuned for the 3D pass |
| `CONCEPT_creature.json` | creature concept, neutral pose, legs separated |
| `CONCEPT_icon_ui.json` | inventory/spell icon, bold readable silhouette |
| `CONCEPT_pbr_material.json` | seamless tileable material source |
| `3d_pixal3d_trellis2_image_to_model.json` | concept to textured GLB (Pixal3D/Trellis.2) |

Each concept workflow is a minimal 6-node graph: loader, CLIP, VAE, LLM config,
sampler, save. Edit the prompt in the sampler node and run.

Validate any workflow before opening it, which catches widget misalignment and bad
links without a GUI round trip:

```
python _validate_workflow.py path\to\workflow.json
```

## Where things live

This pipeline belongs to the game project, not to ComfyUI. ComfyUI's workflows
folder holds only workflow JSON.

```
W:\UNNAMED\
  tools\asset_pipeline\          these scripts and this document
  assets\
    requests\                    asset request lists
    concepts\                    rendered concept images
    raw\                         raw GLBs straight from ComfyUI
    ready\                       cleaned, game-ready assets
    rejected\                    assets that failed verification
    manifests\                   one run_<timestamp>.json per run

C:\Users\jluca\ComfyUI\
  user\default\workflows\        workflow JSON only
  output\                        ComfyUI's scratch space; outputs are moved out
```

Paths are overridable by environment variable, so this can move to another box:
`UNNAMED_COMFY_OUTPUT`, `UNNAMED_COMFY_MODELS`, `UNNAMED_COMFY_SERVER`,
`UNNAMED_3D_TEMPLATE`, `UNNAMED_BLENDER`, `UNNAMED_CONCEPT_MODEL`,
`UNNAMED_CONCEPT_CLIP`, `UNNAMED_CONCEPT_VAE`, `UNNAMED_LLM_ENDPOINT`,
`UNNAMED_LLM_MODEL`.

## Requirements

ComfyUI 0.34.0 or newer, and Blender 5.2. This integration is core, so no custom
nodes are needed and there is no PyTorch downgrade or compiled CUDA extension to
install. `_make_assets.py` expects Blender at
`C:\Program Files\Blender Foundation\Blender 5.2\blender.exe`; override with the
`UNNAMED_BLENDER` environment variable if yours differs.

Concept rendering additionally needs Z-Image Turbo, `thmUNCZImageTE_v10` as a
`lumina2` text encoder, and the `z-image-utilities` custom node (which provides the
sampler and the LLM config node).

## Measured throughput on BEAST

| Stage | Time |
|---|---|
| Concept render (Z-Image Turbo, 1024^2, 8 steps) | ~20-25 s |
| Generate (Pixal3D, 25k faces, 2048 texture, quiet GPU) | ~120-160 s |
| Generate (same, when sharing the GPU with another job) | 270 s - 17 min |
| Blender cleanup (normalise + 3 LODs + 2 collision proxies) | ~3 s |
| Re-clean only (`--skip-generate`) | ~3 s, no GPU |

**Do not run two GPU jobs at once.** A measured 21-asset run took 116 min
(332 s/asset) because concept renders were interleaved with it; the same work on a
quiet GPU runs at 122-160 s per asset. One asset's texture bake went from a 12 s
baseline to 4 min 52 s under contention, and the slowest single asset hit 17 min
against a 2 min baseline. Blender's pass is CPU-only and can overlap freely.

Asset generation time varies a lot with subject complexity: simple weapons settle
near 122 s, while dense props (crate, firewood, barrel) took 10-17 min each.

Utilisation on a quiet GPU: GPU 70-76% average, peaking 100%; VRAM 10-15 GB of 24 GB;
CPU only 12.8% average. Blender work costs no GPU time. Batching is therefore
sequential by design: two concurrent runs would need ~29 GB VRAM on a 24 GB card.

Within a run, sampled diffusion dominates: ~37 s for the 1536 upsample pass and
~19 s for each 1024 pass, against ~1 s of UV unwrap and ~12 s of texture baking.
If you need to cut wall time, the sampler cascade is where the time is.

### Texture resolution

The template carries a "Texture Resolution" primitive that feeds **both** the UV
unwrap resolution and the texture bake size. `--texture-size` drives that primitive,
so the unwrap atlas and the bake always agree. Verified baked output is 2048x2048
per map at the default.

Note that base GLB size varies a lot between assets even at a fixed resolution
(measured 8.3 MB to 15.5 MB across the first 21), because PNG compression depends on
how much detail the baked texture carries, not on its dimensions. A wolf's fur costs
far more bytes than an unadorned sword blade. Do not read GLB size as a texture
resolution signal; check it with `_verify_glb.py`, which now prints the dimensions of
each embedded image.

## Models

Run `python _setup_3d_models.py` to fetch these. They total ~14.5 GB and land flat
in the standard ComfyUI model folders, which is where the nodes look for them.

| Folder | File | Size |
|---|---|---|
| `models/diffusion_models` | `pixal3d_int8_convrot.safetensors` | 5.20 GB |
| `models/diffusion_models` | `trellis_2_int8_convrot.safetensors` | 4.89 GB |
| `models/vae` | `trellis_2_shape_vae_bf16.safetensors` | 1.02 GB |
| `models/vae` | `trellis_2_texture_vae_bf16.safetensors` | 0.88 GB |
| `models/clip_vision` | `dino_v3_L_naf_fp32.safetensors` | 1.13 GB |
| `models/background_removal` | `birefnet.safetensors` | 0.41 GB |
| `models/geometry_estimation` | `moge_2_vitl_normal_fp16.safetensors` | 0.62 GB |

## Driving the 3D stage directly

`_make_assets.py` wraps this; run it directly to iterate on one asset.

`3d_pixal3d_trellis2_image_to_model.json` is the official template, wired for both
models with a boolean switch (`false` = Pixal3D, `true` = Trellis.2). Load it in the
ComfyUI UI, or drive it headlessly:

```
python _run_3d_asset.py path\to\concept.png
python _run_3d_asset.py path\to\concept.png --trellis2
python _run_3d_asset.py path\to\concept.png --faces 20000 --texture-size 2048 --prefix 3d/greatsword
```

Flags: `--trellis2` switches model, `--seed` sets the sampler seed, `--faces` sets
the decimate budget, `--texture-size` sets bake resolution, `--prefix` sets the
ComfyUI output path, `--dump-only` writes `_api_prompt.json` without running.

`--faces 30000` is a realistic game budget; the template default of 700,000 is
tuned for hero detail and is heavy for a prop library.

Measured on this box: **210-226 s** per asset end-to-end.

## Which model to use

- **Pixal3D** (default) — pixel-aligned, so geometry sticks closely to the input
  view. Best for weapons, props, hard-surface, anything you have a clean reference for.
- **Trellis.2** — more tolerant of awkward topology. Use when Pixal3D's strict
  view-faithfulness produces a bad silhouette, or for organic and creature shapes.

Both produce the same outputs, so switching is just the boolean.

## Individual stages

`_make_assets.py` chains these; run them directly when debugging a single stage.

```
# generate only
python _run_3d_asset.py concept.png --faces 25000 --prefix 3d/mine

# clean only
blender --background --factory-startup --python _blender_cleanup.py -- \
    --input raw.glb --outdir out\name --name name --category weapon

# verify a GLB is engine-usable
python _verify_glb.py out\name\name.glb
```

## Output

A single GLB under `output/3d/` with one mesh, `POSITION`/`NORMAL`/`TANGENT`/
`TEXCOORD_0`, and one material carrying baseColor, metallicRoughness, normal and
occlusion maps embedded as PNGs.

`_verify_glb.py` checks a GLB is actually usable before you hand it to Godot:

```
python _verify_glb.py output\3d\your_asset.glb
```

It reports triangle count, which vertex attributes exist, and whether all four PBR
maps are bound, then prints `complete` or names what is missing. Run it in CI or as
a batch gate rather than eyeballing files.

It is mode-aware: a file that carries a material must have all four maps plus
`TEXCOORD_0`, while a geometry-only file (LOD or collision proxy) is only required
to have `POSITION` and `NORMAL`. It also reports `doubleSided` per material, so you
can confirm the Blender pass actually made the asset single-sided.

Note that Blender does not export `TANGENT`; Godot computes tangents from the normal
map on import, but confirm this matches your material setup if you rely on explicit
tangents.

Verified runs on this box:

| Asset | Faces | Size | Time |
|---|---|---|---|
| `beast_smoke_00001.glb` (Pixal3D, default 700k budget) | 695,741 | 46 MB | 225 s |
| `beast_trellis2_00001.glb` (Trellis.2) | 695,807 | 46 MB | 226 s |
| `beast_game_ready_00001.glb` (Pixal3D, `--faces 30000`) | 29,247 | 16 MB | 210 s |

## Why LODs carry no textures

Blender emits LODs and collision proxies with materials and UVs stripped. Emitting
them with textures made each derivative re-embed the full 2048^2 PBR set: a
294-triangle collision hull came out at 14.6 MB. After stripping, the same folder
went from ~70 MB to ~16 MB:

| File | With textures | Stripped |
|---|---|---|
| base `.glb` | 15.2 MB | 15.2 MB (keeps textures) |
| `_lod1.glb` | 14.3 MB | 277 KB |
| `_lod2.glb` | 14.1 MB | 106 KB |
| `_lod3.glb` | 14.0 MB | 38 KB |
| `_collision_hull.glb` | 13.9 MB | 16 KB |

LODs share the base mesh's material in engine, so nothing is lost.

## Licensing

Both models are MIT-licensed and this native path carries no non-commercial
dependencies, so output is usable commercially. This matters: the original Trellis.2
reference pipeline depended on NVIDIA `nvdiffrast`/`nvdiffrec` (non-commercial
research only), and those were removed in this native integration. Some third-party
community node packs for Pixal3D are labelled academic-only — this path avoids them.

One caveat: `TencentARC/Pixal3D` is flagged `extra_gated_eu_disallowed`, so confirm
the EU situation before shipping there.

## Gotcha for API callers

ComfyUI's dynamic combo inputs (`sign_mode` on RemeshMesh, `placement_mode` on
DecimateMesh) have a non-obvious API contract. The prompt carries the option as a
plain string and its sub-widgets as dotted keys alongside it:

```json
{
  "sign_mode": "udf",
  "sign_mode.qef": false,
  "sign_mode.drop_enclosed_components": false
}
```

The server re-nests these into `{"sign_mode": {"sign_mode": "udf", "qef": false}}`
before calling `execute()`. Wrapping the value as a dict yourself makes
`_expand_schema_for_dynamic` read the wrong shape and silently drop the input, which
surfaces as `execute() missing 1 required positional argument`. `_run_3d_asset.py`
handles this.
