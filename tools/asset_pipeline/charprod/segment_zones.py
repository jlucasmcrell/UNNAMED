"""Material zones of a character concept: a label image (one index per zone) from text prompts - Grounding DINO boxes
(IDEA-Research, Apache-2.0) refined by SAM 2 masks (Meta, Apache-2.0), both run offline through transformers; nothing
of them ships. The zones and their prompts come from the character's spec, so the same stage serves skin, hair and
cloth and a science-fantasy character's crystal or chrome.

    (the ComfyUI rig's embedded Python has the CUDA torch, torchvision and transformers this needs)
    python segment_zones.py <image> <zones.json> <out_labels.png> [--preview preview.jpg] [--mask figure_mask.png]

zones.json: {"zones": [{"name": "skin", "prompts": ["face", "neck", "arm", "hand"]}, ...]} - later zones win where
masks overlap (list broad zones first, small parts - a buckle, a strap - last). Label 0 is "unassigned".
"""
import argparse
import json

import numpy as np
import torch
from PIL import Image
from transformers import AutoModelForZeroShotObjectDetection, AutoProcessor, Sam2Model, Sam2Processor

DINO = "IDEA-Research/grounding-dino-base"
SAM = "facebook/sam2.1-hiera-large"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("image")
    ap.add_argument("zones")
    ap.add_argument("out")
    ap.add_argument("--preview")
    ap.add_argument("--mask", help="the figure mask: labels outside it are cleared")
    ap.add_argument("--box-threshold", type=float, default=0.3)
    ap.add_argument("--text-threshold", type=float, default=0.25)
    a = ap.parse_args()
    spec = json.load(open(a.zones))["zones"]
    image = Image.open(a.image).convert("RGB")
    W, H = image.size

    dproc = AutoProcessor.from_pretrained(DINO)
    device = "cuda" if torch.cuda.is_available() else "cpu"
    dmodel = AutoModelForZeroShotObjectDetection.from_pretrained(DINO).eval().to(device)
    sproc = Sam2Processor.from_pretrained(SAM)
    smodel = Sam2Model.from_pretrained(SAM).eval().to(device)

    labels = np.zeros((H, W), np.uint8)
    report = []
    figure = np.asarray(Image.open(a.mask).convert("L")) > 127 if a.mask else np.ones((H, W), bool)
    ys, xs = np.nonzero(figure)
    fig_area = float((xs.max() - xs.min()) * (ys.max() - ys.min()))
    for zi, zone in enumerate(spec, start=1):
        text = ". ".join(p.lower() for p in zone["prompts"]) + "."
        inputs = dproc(images=image, text=text, return_tensors="pt").to(device)
        with torch.no_grad():
            out = dmodel(**inputs)
        det = dproc.post_process_grounded_object_detection(out, inputs.input_ids, threshold=a.box_threshold,
                                                           text_threshold=a.text_threshold, target_sizes=[(H, W)])[0]
        # A box around most of the figure is a whole-person detection, not the part asked for.
        keep = [i for i, b in enumerate(det["boxes"].tolist())
                if (b[2] - b[0]) * (b[3] - b[1]) <= zone.get("max_box", 0.4) * fig_area]
        boxes = [det["boxes"].tolist()[i] for i in keep]
        zone_mask = np.zeros((H, W), bool)
        if boxes:
            sin = sproc(images=image, input_boxes=[boxes], return_tensors="pt").to(device)
            with torch.no_grad():
                sout = smodel(**sin, multimask_output=False)
            masks = sproc.post_process_masks(sout.pred_masks.cpu(), sin["original_sizes"].cpu())[0]
            for mk, b in zip(masks, boxes):
                clip = np.zeros((H, W), bool)
                x0, y0, x1, y1 = [int(round(v)) for v in b]
                clip[max(0, y0 - 4):y1 + 4, max(0, x0 - 4):x1 + 4] = True
                zone_mask |= mk.squeeze(0).numpy().astype(bool) & clip
        labels[zone_mask] = zi
        report.append({"zone": zone["name"], "boxes": len(boxes), "scores": [round(float(det["scores"][i]), 2) for i in keep],
                       "phrases": det.get("text_labels", det.get("labels")), "pixels": int(zone_mask.sum())})
        print(f"ZONE {zone['name']}: {len(boxes)} boxes, {int(zone_mask.sum())} px", flush=True)
    if a.mask:
        labels[np.asarray(Image.open(a.mask).convert("L")) <= 127] = 0
    Image.fromarray(labels).save(a.out)
    if a.preview:
        rng = np.random.default_rng(3)
        palette = np.vstack([[0, 0, 0], rng.integers(40, 255, (len(spec), 3))]).astype(np.uint8)
        over = (np.asarray(image) * 0.45 + palette[labels] * 0.55).astype(np.uint8)
        Image.fromarray(over).save(a.preview, quality=88)
    json.dump({"zones": [z["name"] for z in spec], "report": report}, open(a.out + ".json", "w"), indent=1)
    print("SEGMENT_ZONES " + json.dumps({z["zone"]: z["pixels"] for z in report}))


if __name__ == "__main__":
    main()
