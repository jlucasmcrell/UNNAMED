# Portable native character cache

This caches the raw imported rigged scene before actor identity, materials,
outfits, bone modifiers, animation and facial drivers are applied. It retains
every mesh, LOD, morph, skin bind and texture mip. It is a loading artifact,
not visual acceptance evidence.

Use a private, complete assetbase. Keep the accepted source root unchanged.
With the matching Godot editor executable, build on CPU:

```text
Godot.exe --headless --path src/Presentation res://Diagnostics/NativeSceneCacheGate.tscn -- ASSETBASE MODEL_IDS_JSON RECEIPT_JSON --build
```

`MODEL_IDS_JSON` is a nonempty JSON array of canonical rigged asset IDs. Build
in bounded batches to avoid retaining the entire depot in one process. Verify
the same list in a fresh process by omitting `--build`; every model must report
a native cache hit. The exported EXE can run that verification scene too.

The artifact is `primary/character_native/<engine>/v2/<model>_rigged.glb.scn`
or the corresponding fallback path, beside its `.proof.json`. Bundled resources
have no external dependencies; immutable textures are shared by DDS identity
and mip policy after loading. Each proof pins the source GLB, compiled scene
and all used DDS bytes. Its stamp includes engine/import policy and only that
GLB's signed cache entries, so packaging's unrelated manifest pruning is safe.
Stale, changed, incomplete or externally dependent artifacts fail loudly.

The canonical package builder retains both cache folders; art staging treats
SCN files as immutable payloads. Cache rebuilds replace files atomically rather
than writing through staged hardlinks. Prefetch skips cached rigged GLBs.

Before using a new cache policy, compare fresh-process glTF and native results
with `CharacterCreationProfile`: complete geometry/morph/skin-bind fingerprints,
texture formats/mips/bytes, actor isolation, relocation and required native hits.
These are CPU controls. Measure the normal packaged launcher and populated world
separately before claiming a loading improvement or character acceptance.
