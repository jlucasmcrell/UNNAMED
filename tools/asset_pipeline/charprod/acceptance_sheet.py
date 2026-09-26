"""A character's visual acceptance sheet: the source concept, the raw reconstruction (Phase A's shipped mesh), the
production mesh (Blender, same light rig) and the game (Godot close-up, third person, dialogue) for Phase A and for
the production character side by side - one JPEG a reviewer reads left to right, top to bottom.

    python acceptance_sheet.py --name "Kera Voss" --concept concept.png --raw-renders DIR/phasea --prod-renders DIR/final
        --godot-a shots/chars_default/raw/c_kera --godot-b shots/chars_prod/raw/c_kera --out sheet.jpg
(--raw-renders / --prod-renders: a render_views.py stem - <stem>_face.png, <stem>_body.png; --godot-*: a shot stem -
<stem>_close.png, <stem>_third.png, <stem>_dialogue.png)
"""
import argparse
import os

from PIL import Image, ImageDraw, ImageFont

CELL = 420


def font(size):
    for f in ("C:/Windows/Fonts/segoeuib.ttf", "C:/Windows/Fonts/arialbd.ttf"):
        if os.path.exists(f):
            return ImageFont.truetype(f, size)
    return ImageFont.load_default()


def cell(img, label, crop=None):
    im = Image.open(img).convert("RGB") if isinstance(img, str) else img
    if crop:
        im = im.crop(crop)
    w, h = im.size
    s = min(CELL / w, (CELL - 34) / h)
    im = im.resize((max(1, int(w * s)), max(1, int(h * s))), Image.LANCZOS)
    out = Image.new("RGB", (CELL, CELL), (24, 24, 26))
    out.paste(im, ((CELL - im.width) // 2, 34 + (CELL - 34 - im.height) // 2))
    ImageDraw.Draw(out).text((8, 7), label, fill=(235, 220, 170), font=font(17))
    return out


def godot_crop(path, kind):
    im = Image.open(path).convert("RGB")
    w, h = im.size
    # the harness frames the figure in the middle: close = the face, dialogue/third = the figure
    box = {"close": (int(w * 0.36), int(h * 0.22), int(w * 0.64), int(h * 0.78)),
           "dialogue": (int(w * 0.28), int(h * 0.05), int(w * 0.72), int(h * 0.95)),
           "third": (int(w * 0.3), int(h * 0.08), int(w * 0.7), int(h * 0.92))}[kind]
    return im.crop(box)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--name", required=True)
    ap.add_argument("--concept", required=True)
    ap.add_argument("--raw-renders", required=True)
    ap.add_argument("--prod-renders", required=True)
    ap.add_argument("--godot-a", required=True)
    ap.add_argument("--godot-b", required=True)
    ap.add_argument("--out", required=True)
    a = ap.parse_args()
    concept = Image.open(a.concept).convert("RGB")
    cw, chh = concept.size
    rows = [
        [cell(concept, "1 source concept (Z-Image)"),
         cell(a.raw_renders + "_body.png", "2 raw reconstruction (Phase A)"),
         cell(a.prod_renders + "_body.png", "3 production mesh"),
         cell(a.raw_renders + "_face.png", "2 Phase A face"),
         cell(a.prod_renders + "_face.png", "3 production face")],
        [cell(godot_crop(a.godot_a + "_close.png", "close"), "Godot close - Phase A"),
         cell(godot_crop(a.godot_b + "_close.png", "close"), "Godot close - production"),
         cell(godot_crop(a.godot_a + "_dialogue.png", "dialogue"), "Godot dialogue - Phase A"),
         cell(godot_crop(a.godot_b + "_dialogue.png", "dialogue"), "Godot dialogue - production"),
         cell(godot_crop(a.godot_b + "_third.png", "third"), "Godot third person - production")],
    ]
    title_h = 46
    sheet = Image.new("RGB", (CELL * 5, title_h + CELL * len(rows)), (14, 14, 16))
    ImageDraw.Draw(sheet).text((12, 10), f"{a.name} - character fidelity acceptance", fill=(255, 255, 255), font=font(24))
    for r, row in enumerate(rows):
        for c, im in enumerate(row):
            sheet.paste(im, (c * CELL, title_h + r * CELL))
    sheet.save(a.out, quality=86)
    print(f"ACCEPTANCE_SHEET {a.out} {sheet.size}")


if __name__ == "__main__":
    main()
