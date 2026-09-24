"""Turn a rendered material concept into a tileable, engine-ready PBR material.

The 60 material concepts are single rendered images with baked lighting, a visible frame edge and
no guarantee of periodicity. None of that is usable as a texture, so this does the four things
that turn a concept into a material:

  1. **De-lighting.** A concept render carries its own light. Dividing out a heavily blurred
     luminance removes the large-scale light gradient while keeping surface detail, so the result
     re-lights correctly under the game's own lighting instead of double-lighting.
  2. **Tiling.** The image is rolled by half in both axes and cross-faded across the new seam with
     a cosine ramp. The result repeats without a visible edge, which a raw concept does not.
  3. **Normal and occlusion from height.** A tileable Sobel gradient over luminance gives a normal
     map whose gradients wrap, so the normal is seamless too. Occlusion is the cavity difference
     between the height and a blurred height.
  4. **Roughness and metallic per class.** These are not recoverable from a photograph at all, so
     they are declared per material class and modulated by luminance rather than invented.

Every map is derived from the same de-lit, tileable base, so base colour, normal, roughness and
occlusion agree with one another instead of describing three different surfaces.

Usage:
    python _make_pbr_materials.py --audit
    python _make_pbr_materials.py --apply --set prototype
    python _make_pbr_materials.py --apply --material material_oak_plank_floor
"""
import argparse
import io
import json
import os
import sys

import numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter

ASSETS = r"W:\UNNAMED\assets"
CONCEPTS = os.path.join(ASSETS, "concepts")
OUT_ROOT = os.path.join(ASSETS, "materials")

# The prototype set from sprint section 12. `tile_m` is the real-world size one repeat covers:
# a texture with no declared scale cannot be placed correctly in a world, and getting it wrong is
# the flat-texture equivalent of the scale bug that hit the meshes.
#
# roughness / metallic are declared per material because they are not recoverable from a render.
# `bump` drives how strong the derived normal is, which follows how much relief the surface has.
PROTOTYPE_SET = {
    "material_packed_dirt_ground": {
        "class": "ground", "tile_m": 4.0, "roughness": 0.95, "metallic": 0.0, "bump": 1.4},
    "material_loose_gravel": {
        "class": "ground", "tile_m": 2.0, "roughness": 0.90, "metallic": 0.0, "bump": 2.2},
    "material_churned_wet_mud": {
        "class": "ground", "tile_m": 3.0, "roughness": 0.55, "metallic": 0.0, "bump": 1.8},
    "material_marsh_grass_turf": {
        "class": "ground", "tile_m": 3.0, "roughness": 0.92, "metallic": 0.0, "bump": 1.6},
    "material_oak_plank_floor": {
        "class": "timber", "tile_m": 2.0, "roughness": 0.72, "metallic": 0.0, "bump": 1.2},
    "material_plaster_lath_wall": {
        "class": "plaster", "tile_m": 2.5, "roughness": 0.88, "metallic": 0.0, "bump": 1.0},
    "material_rubble_stone_wall": {
        "class": "stone", "tile_m": 3.0, "roughness": 0.85, "metallic": 0.0, "bump": 2.0},
    "material_limestone_ashlar": {
        "class": "stone", "tile_m": 2.5, "roughness": 0.78, "metallic": 0.0, "bump": 1.1},
    "material_slate_roof_scale": {
        "class": "roof", "tile_m": 2.0, "roughness": 0.70, "metallic": 0.0, "bump": 1.5},
    "material_cast_iron_surface": {
        "class": "metal", "tile_m": 1.5, "roughness": 0.45, "metallic": 0.9, "bump": 1.3},
}

SIZE = 1024          # prototype tier; the standard allows 1024 and it keeps 40 maps tractable
DELIGHT_SIGMA = 0.12  # as a fraction of the image, so it is resolution independent
BLEND_FRACTION = 0.25


def load_concept(path):
    image = Image.open(path).convert("RGB")
    # Centre-crop to a square first: concepts are square already, but a future one might not be,
    # and stretching to square would silently distort the material's real-world scale.
    side = min(image.size)
    left = (image.width - side) // 2
    top = (image.height - side) // 2
    image = image.crop((left, top, left + side, top + side))
    image = image.resize((SIZE, SIZE), Image.LANCZOS)
    return np.asarray(image, dtype=np.float32) / 255.0


def luminance(rgb):
    return rgb[..., 0] * 0.2126 + rgb[..., 1] * 0.7152 + rgb[..., 2] * 0.0722


def delight(rgb):
    """Divide out large-scale lighting so the material re-lights under its own scene."""
    lum = luminance(rgb)
    background = gaussian_filter(lum, sigma=SIZE * DELIGHT_SIGMA, mode="wrap")
    background = np.maximum(background, 1e-3)
    gain = float(np.mean(background)) / background
    # Clamp the correction: an unclamped divide turns shadowed corners into blown highlights.
    gain = np.clip(gain, 0.55, 1.8)
    return np.clip(rgb * gain[..., None], 0.0, 1.0)


def make_tileable(rgb, band_fraction=0.05, sigma_fraction=0.014):
    """Roll by half, then heal the seam that lands in the middle.

    The roll alone already tiles: after shifting by half, the wrap edges are two adjacent columns
    of the ORIGINAL image, so they are continuous by construction. What the roll creates is a
    cross-shaped discontinuity through the centre, where the concept's own frame edge went.

    An earlier version blended the rolled image with the original using an edge-weighted mask,
    which is precisely backwards: it put the original frame edges back on the wrap boundary and
    drove the seam ratio from ~1.3 up to ~9.

    The centre is healed by blending a blur in a narrow band. The blur is symmetric about the
    seam and never touches the outer edges, so tiling is preserved and only the cross softens.
    """
    rolled = np.roll(rgb, (SIZE // 2, SIZE // 2), axis=(0, 1))
    blurred = gaussian_filter(rolled, sigma=(SIZE * sigma_fraction, SIZE * sigma_fraction, 0),
                              mode="wrap")
    band = max(int(SIZE * band_fraction), 2)
    centre = SIZE // 2
    axis = np.arange(SIZE, dtype=np.float32)
    # Distance from the centre line, wrapped, so the band is symmetric across the seam.
    distance = np.minimum(np.abs(axis - centre), SIZE - np.abs(axis - centre))
    falloff = np.clip(1.0 - distance / band, 0.0, 1.0)
    falloff = 0.5 - 0.5 * np.cos(np.pi * falloff)     # smooth, no hard band edge
    mask = np.clip(falloff[:, None] + falloff[None, :], 0.0, 1.0)
    return np.clip(rolled * (1.0 - mask[..., None]) + blurred * mask[..., None], 0.0, 1.0)


def height_from(rgb):
    """Luminance as a height proxy, with the low frequencies removed.

    Raw luminance would make every large dark patch a crater. Subtracting a blurred copy leaves
    the surface relief, which is what a normal map should encode.
    """
    lum = luminance(rgb)
    return lum - gaussian_filter(lum, sigma=SIZE * 0.02, mode="wrap")


def normal_from_height(height, strength):
    """Tileable Sobel normal. Gradients use np.roll so they wrap with the texture."""
    gx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5
    gy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5
    nx = -gx * strength * 12.0
    ny = -gy * strength * 12.0
    nz = np.ones_like(height)
    length = np.sqrt(nx * nx + ny * ny + nz * nz)
    normal = np.stack([nx / length, ny / length, nz / length], axis=-1)
    return np.clip(normal * 0.5 + 0.5, 0.0, 1.0)


def ao_from_height(height):
    """Cavity occlusion: how far below the local average each texel sits."""
    broad = gaussian_filter(height, sigma=SIZE * 0.03, mode="wrap")
    cavity = np.clip((height - broad) * 4.0, -1.0, 1.0)
    return np.clip(0.65 + 0.35 * cavity, 0.0, 1.0)


def roughness_map(rgb, base):
    """Declared base roughness, modulated by local tone so it is not a flat constant."""
    lum = luminance(rgb)
    detail = np.clip((lum - gaussian_filter(lum, sigma=SIZE * 0.02, mode="wrap")) * 2.0, -0.3, 0.3)
    return np.clip(base + detail, 0.05, 1.0)


def save_gray(array, path):
    Image.fromarray((np.clip(array, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8), "L").save(path)


def save_rgb(array, path):
    Image.fromarray((np.clip(array, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8), "RGB").save(path)


def godot_material(asset_id, spec, meta):
    """Emit a Godot StandardMaterial3D resource beside the maps.

    Written as text rather than produced by hand in the editor so it is reproducible and so the
    ORM wiring is explicit: Godot reads occlusion, roughness and metallic from one texture and
    scales them by the material's own roughness/metallic multipliers, which is exactly the
    packing glTF uses.
    """
    base = f"res://assets/materials/{asset_id}"
    return f"""[gd_resource type="StandardMaterial3D" load_steps=4 format=3]

; {asset_id} - {spec['class']}, one repeat covers {spec['tile_m']} m.
; Generated by _make_pbr_materials.py; do not hand-edit.
; Roughness and metallic values are declared, not recovered from the source render.

[ext_resource type="Texture2D" path="{base}/{asset_id}_basecolor.png" id="1_albedo"]
[ext_resource type="Texture2D" path="{base}/{asset_id}_normal.png" id="2_normal"]
[ext_resource type="Texture2D" path="{base}/{asset_id}_orm.png" id="3_orm"]

[resource]
albedo_texture = ExtResource("1_albedo")
normal_enabled = true
normal_texture = ExtResource("2_normal")
normal_scale = {min(spec['bump'] * 0.5, 2.0)}
orm_texture = ExtResource("3_orm")
roughness = 1.0
metallic = 1.0
metallic_specular = 0.5
uv1_scale = Vector3(1, 1, 1)
texture_filter = 3
"""


def lighting_gradient(rgb):
    """Standard deviation of heavily blurred luminance: how much large-scale lighting is baked in.

    This is the measurement behind the de-lighting claim. A render lit from one side has a strong
    low-frequency brightness ramp; a material should not, because the game lights it again.
    """
    broad = gaussian_filter(luminance(rgb), sigma=SIZE * DELIGHT_SIGMA, mode="wrap")
    return float(np.std(broad))


def build(asset_id, spec, apply_changes):
    concept = os.path.join(CONCEPTS, f"{asset_id}.png")
    if not os.path.exists(concept):
        return False, "no concept"
    out_dir = os.path.join(OUT_ROOT, asset_id)
    os.makedirs(out_dir, exist_ok=True)

    raw = load_concept(concept)
    lit_gradient = lighting_gradient(raw)
    delit = delight(raw)
    rgb = make_tileable(delit)
    remaining_gradient = lighting_gradient(rgb)
    reduction = 1.0 - (remaining_gradient / lit_gradient) if lit_gradient > 1e-6 else 0.0
    height = height_from(rgb)
    normal = normal_from_height(height, spec["bump"])
    ao = ao_from_height(height)
    rough = roughness_map(rgb, spec["roughness"])
    metallic = np.full_like(rough, spec["metallic"])

    # glTF packs occlusion in R, roughness in G, metallic in B, so the engine needs one texture
    # rather than three and the channels cannot drift apart.
    packed = np.stack([ao, rough, metallic], axis=-1)

    if apply_changes:
        save_rgb(rgb, os.path.join(out_dir, f"{asset_id}_basecolor.png"))
        save_rgb(normal, os.path.join(out_dir, f"{asset_id}_normal.png"))
        save_gray(ao, os.path.join(out_dir, f"{asset_id}_ao.png"))
        save_gray(rough, os.path.join(out_dir, f"{asset_id}_roughness.png"))
        save_rgb(packed, os.path.join(out_dir, f"{asset_id}_orm.png"))
        meta = {
            "asset_id": asset_id,
            "material_class": spec["class"],
            "tile_size_m": spec["tile_m"],
            "resolution": SIZE,
            "maps": {
                "basecolor": f"{asset_id}_basecolor.png",
                "normal": f"{asset_id}_normal.png",
                "ao": f"{asset_id}_ao.png",
                "roughness": f"{asset_id}_roughness.png",
                "orm": f"{asset_id}_orm.png",
            },
            "pbr": {"roughness_base": spec["roughness"], "metallic": spec["metallic"],
                    "normal_strength": spec["bump"]},
            "source_concept": f"assets/concepts/{asset_id}.png",
            "tileable": True,
            "delit": True,
            "delight": {
                "baked_lighting_gradient": round(lit_gradient, 5),
                "residual_gradient": round(remaining_gradient, 5),
                "reduction": round(reduction, 4),
            },
            "note": ("Roughness and metallic are declared per material class, not recovered from the "
                     "render; they cannot be read from a photograph. Occlusion, roughness and "
                     "metallic are packed as glTF ORM in one texture."),
        }
        with io.open(os.path.join(out_dir, f"{asset_id}_material.json"), "w",
                     encoding="utf-8") as handle:
            json.dump(meta, handle, indent=2)
        with io.open(os.path.join(out_dir, f"MAT_{asset_id}.tres"), "w",
                     encoding="utf-8") as handle:
            handle.write(godot_material(asset_id, spec, meta))
    return True, (f"{spec['class']}, {spec['tile_m']} m tile, "
                  f"baked lighting -{reduction * 100:.0f}%")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--audit", action="store_true")
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--set", default="prototype", choices=("prototype", "all"))
    parser.add_argument("--material", nargs="*", default=None)
    args = parser.parse_args()

    if args.set == "all":
        specs = {name[:-4]: {"class": "generic", "tile_m": 2.0, "roughness": 0.8,
                             "metallic": 0.0, "bump": 1.4}
                 for name in os.listdir(CONCEPTS)
                 if name.startswith("material_") and name.endswith(".png")}
    else:
        specs = dict(PROTOTYPE_SET)

    if args.material:
        specs = {k: v for k, v in specs.items() if k in args.material}

    print(f"  {'material':<36} {'class':<9} {'tile':>6}  result")
    print("  " + "-" * 74)
    ok = failed = 0
    for asset_id in sorted(specs):
        built, detail = build(asset_id, specs[asset_id], args.apply)
        mark = "OK  " if built else "MISS"
        print(f"  {mark} {asset_id:<33} {specs[asset_id].get('class', '?'):<9} "
              f"{specs[asset_id].get('tile_m', 0):>5} m  {detail}")
        if built:
            ok += 1
        else:
            failed += 1

    print(f"\n  {ok} materials, {failed} missing concepts")
    if not args.apply:
        print("  (audit only; pass --apply to write the maps)")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
