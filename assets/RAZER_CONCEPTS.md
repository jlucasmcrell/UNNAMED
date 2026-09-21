# Running concept renders on RAZER

RAZER's ComfyUI is a separate install from BEAST's, at `\\RAZER\ComfyUI` (mapped as
`R:`), serving on port 8188. It is a 4070 Ti with 12.9 GB and is well suited to concept
renders, which need roughly 12 GB. The 3090 on BEAST remains the only machine that can
do the 3D stage, which peaks near 14.75 GB.

Use RAZER for all concept rendering so the BEAST GPU stays free for the 3D queue.

## Launch

On RAZER itself, run `R:\run_comfy_4070.bat`. Its install root is `D:\ComfyUI`, so
output lands in `\\RAZER\D\ComfyUI\output`. It cannot be started remotely from BEAST:
WinRM is not configured between the machines, so remote WMI process creation is refused.

Its argv for reference:

```
D:\ComfyUI\main.py --disable-auto-launch --listen 0.0.0.0 --port 8188 --max-upload-size 1024
```

## Rendering

Two environment variables switch the pipeline to RAZER:

```powershell
$env:UNNAMED_COMFY_SERVER = 'http://RAZER:8188'
$env:UNNAMED_COMFY_OUTPUT = '\\RAZER\D\ComfyUI\output'
python W:\UNNAMED\tools\asset_pipeline\_make_concepts.py <requests.json>
```

No sampler override is needed. RAZER now has the same flow samplers as BEAST.

## Do not render concepts on BEAST while the 3D queue runs

The 3D stage holds roughly 12.5 GB of the 3090 for the duration of a build. A concept
render submitted at the same time does not fail, it simply waits, and a combined run
will stall for many minutes with no visible progress. Concept work goes to RAZER and
BEAST stays on 3D. This is a hard rule, not a preference.

## Sharing euler_flow between the machines

BEAST's sampler list includes `euler_flow` because it has
`custom_nodes\ComfyUI-ZImageTurbo-FlowSampler` installed. RAZER did not, so it offered
only the 44 core samplers and rejected `euler_flow` with `value_not_in_list`. The same
node has since been copied to RAZER:

```
\\RAZER\D\ComfyUI\custom_nodes\ComfyUI-ZImageTurbo-FlowSampler\
```

It is worth having beyond parity. Its own manifest states it exists to fix instability
in ComfyUI's built-in `euler` for Z-Image-Turbo, so `euler_flow` is the correct sampler
for this model rather than merely the matching one.

Note this node exports **no node types** - it appends its samplers to
`comfy.samplers.KSampler.SAMPLERS` at import time and its `NODE_CLASS_MAPPINGS` is
`None`. So it will not appear in the node list, and comparing `/object_info` node names
between machines will not detect it. Check the sampler dropdown instead:

```powershell
# expect 49 samplers with euler_flow present, not 44
python -c "import json,urllib.request as u;print([s for s in json.load(u.urlopen('http://RAZER:8188/object_info/Z_ImageIntegratedKSampler'))['Z_ImageIntegratedKSampler']['input']['required']['sampler_name'][0] if 'flow' in s])"
```

`UNNAMED_CONCEPT_SAMPLER` still exists in `_make_concepts.py`. Its default remains
`euler_flow`, and it is only useful if a machine is ever added that lacks the node.

## Measured speed

| Settings | BEAST 3090 | RAZER 4070 Ti |
|---|---|---|
| 1024x1024, 8 steps (old) | ~25 s | - |
| 1536x1536, 24 steps (current) | - | ~125 s |
| 832x1216, 10 steps | - | ~47 s |

## Known differences between the installs

| | BEAST | RAZER |
|---|---|---|
| ComfyUI | 0.34.0 | 0.33.0 |
| Models | many | z_image_turbo_bf16, thmUNCZImageTE_v10, ultrafluxVAEImproved_v10 |
| Samplers | 127 | 49 |
| Output | `C:\Users\jluca\ComfyUI\output` | `\\RAZER\D\ComfyUI\output` |

Anything relying on a BEAST-only node will fail on RAZER with a validation error, so
check coverage before assuming a graph is portable.

## Share layout gotcha

`R:` (`\\RAZER\ComfyUI`) and `\\RAZER\D\ComfyUI` are **two share names onto the same
directory**, not two copies. Deleting a file through one path deletes it through both.
Its server runs from `D:\ComfyUI`, so `D:\ComfyUI\custom_nodes` is the tree that is
actually imported.
