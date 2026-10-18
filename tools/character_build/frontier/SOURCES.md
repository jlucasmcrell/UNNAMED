# Frontier wardrobe sources

The external source library keeps the original downloads read-only. Source
authors save editable Blender files, exported skins and `source.json` beside
each feminine Average garment. Approved MetaHuman bodies remain unchanged.

| Use | Source and author | Licence |
| --- | --- | --- |
| Sewn jerkin, belt, closures, trousers and fly lacing | [Leather Armor — Giovani França](https://www.blenderkit.com/asset-gallery-detail/eb56ba5c-92b0-47db-ad3e-5fa00a5eb077/) | BlenderKit royalty-free; downloaded through the owner's installed add-on |
| Boots, including sole, heel, laces and cuff | [Medieval Leather Boots — Polary Studios](https://www.blenderkit.com/asset-gallery-detail/59193bc5-5c46-4c4d-a961-559cc83f24fa/) | BlenderKit royalty-free; downloaded through the owner's installed add-on |
| Linen shirt and sleeves | Elvaerwyn, `Elvs_Male_shirt_untucked_bd1`, MakeHuman community library | CC BY, as stated in the original OBJ and MHClO; no version inferred |
| Open-face hood | Donitz, `Monk_s_robe_hood`, MakeHuman community library | CC0, as stated in both source headers |
| Leather surface | ambientCG `Leather014`, albedo, roughness and OpenGL normal | CC0 |
| Boot leather surface | Poly Haven `brown_leather`, albedo, ARM and OpenGL normal | CC0 |
| Oiled/blackened iron surface | ambientCG `Metal013`, albedo, roughness, metalness and OpenGL normal | CC0 |
| Linen and wool surfaces | Poly Haven `rough_linen` and `poly_wool_herringbone` | CC0 |
| Glove source, short mantle panels and three sets of metal lames | Rift Studios | First-party geometry; scanned surfaces credited above |

Changes to the licensed garments: fit once to feminine Average; sewn sleeve
join and waist/cuff finishing; scanned PBR surfaces with the source leather
pack's registered seam normals. The other three fits use the same Surface
Deform and Data Transfer command. The mantle is a short original panel pattern
draped once with native Blender cloth over the body and vest, then thickened
with Solidify and bound to primary torso/shoulder joints after native
Data Transfer. Skin corrective/twist helper weights are excluded from the
mantle. The linen armhole uses a native smooth Mask and a concealed seam. The hood has
thickness and covers the scalp. No body deformation is part of garment fitting.

The `rough_linen` photograph is blue; its dye is neutralised in the baked
albedo to produce oatmeal linen while preserving the photographed weave.
Iron is desaturated and roughness is clamped to 0.56–0.60. Its horizontal
lames remain geometry; vertical seams are narrowed to remove the checker-like
read. The hood's rear peak falls towards the shoulders. Review 8's accepted
fit remains the source for these changes.

The lamework jack, coif and sabatons use new rigid metal lames over the new
jerkin, hood and boots. Each plate has one standard skeletal attachment; the
backing uses ordinary skin binding. Whole-region hiding follows the fixed
slot table; layered clothing is baked into combined meshes.

Copy these attributions into the merged `content/credits.yaml` and packaged
notices. Do not distribute the editable third-party downloads as a standalone
asset library. No generated meshes or generated textures are used here.
