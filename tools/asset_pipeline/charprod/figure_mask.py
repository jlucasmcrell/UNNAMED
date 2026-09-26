"""The concept's figure mask (BiRefNet, the reconstruction template's own background-removal model), saved as an 8-bit PNG.

    python figure_mask.py <concept.png> <mask.png>
"""
import io
import sys

import numpy as np
from PIL import Image

import comfy

concept, out = sys.argv[1], sys.argv[2]
name = comfy.upload(concept)
graph = {
    "1": {"class_type": "LoadImage", "inputs": {"image": name}},
    "2": {"class_type": "LoadBackgroundRemovalModel", "inputs": {"bg_removal_name": "birefnet.safetensors"}},
    "3": {"class_type": "RemoveBackground", "inputs": {"bg_removal_model": ["2", 0], "image": ["1", 0]}},
    "4": {"class_type": "MaskToImage", "inputs": {"mask": ["3", 0]}},
    "5": {"class_type": "SaveImage", "inputs": {"images": ["4", 0], "filename_prefix": "charprod/mask"}},
}
images = comfy.run(graph)
mask = np.asarray(Image.open(io.BytesIO(images["5"][0])).convert("L"))
Image.fromarray(mask).save(out)
print(f"FIGURE_MASK {out} {mask.shape} coverage {(mask > 127).mean():.3f}")
