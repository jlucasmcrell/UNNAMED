"""Build one face sheet per town for F1 Gate C: every NPC's face at 2 m, labelled, in a grid the owner can judge at a glance.

Input is the folder the review mode writes: `npc_looks.json` plus one PNG per NPC. Rows are the people, so a reader can check a
people's ruled look down a column (Siann chalk-white with white hair, Kal stone with growths and no hair, Veth and Orenth natural).
The sheet is deliberately 3 across: beyond that the faces are too small to judge for gloss, staring eyes or duplicate faces.
"""
import json
import sys
from pathlib import Path

from PIL import Image, ImageDraw

TILE = 512
LABEL = 26
COLUMNS = 3


def build(folder):
    folder = Path(folder)
    manifest = json.loads((folder / "npc_looks.json").read_text(encoding="utf-8"))
    rows = [row for row in manifest["npcs"] if (folder / (row["id"] + ".png")).exists()]
    if not rows:
        raise SystemExit(f"{folder}: no NPC frames to build a sheet from")
    height = (TILE + LABEL) * ((len(rows) + COLUMNS - 1) // COLUMNS)
    sheet = Image.new("RGB", (TILE * COLUMNS, height), (18, 18, 20))
    draw = ImageDraw.Draw(sheet)
    for index, row in enumerate(rows):
        tile = Image.open(folder / (row["id"] + ".png")).convert("RGB")
        # The review frames a head and shoulders; the square crop keeps every face the same size on the sheet.
        if manifest.get('full_body'):
            from PIL import ImageOps
            # Remove spare horizontal background, retaining the entire frame height and its feet.
            if tile.width>=tile.height:
                left=(tile.width-tile.height)//2
                tile=tile.crop((left,0,left+tile.height,tile.height))
            tile=ImageOps.pad(tile,(TILE,TILE),method=Image.Resampling.LANCZOS,color=(18,18,20))
        else:
            side = min(tile.size)
            left = (tile.width - side) // 2
            top = max(0, int((tile.height - side) * .35))
            tile = tile.crop((left, top, left + side, top + side)).resize((TILE, TILE), Image.LANCZOS)
        column, line = index % COLUMNS, index // COLUMNS
        x, y = column * TILE, line * (TILE + LABEL)
        sheet.paste(tile, (x, y))
        caption = f'{row["name"]}  -  {row["people"].split(".")[-1]} {row["sex"]}  -  {row["role"]}'
        draw.text((x + 6, y + TILE + 5), caption[:64], fill=(232, 232, 236))
    out = folder / "NPC_LOOKS.png"
    sheet.save(out)
    print(f"{folder.name}: {len(rows)} faces -> {out.name} {sheet.size[0]}x{sheet.size[1]}")
    return out


if __name__ == "__main__":
    for argument in sys.argv[1:]:
        build(argument)
