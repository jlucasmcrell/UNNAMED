"""Build the body-dial-extremes sheet: the starter outfit on every body preset the creator offers, both sexes.

The creator's body step runs one preset list per people and sex. This sheet puts the extreme ends of that list side by side
for each people - the frame at the first preset, the middle one and the last - at the upper-body camera, which is where the
neck, shoulders and chest are largest in frame and where skin coming through a shirt shows first.
"""
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

run, destination = Path(sys.argv[1]), Path(sys.argv[2])
destination.mkdir(parents=True, exist_ok=True)
TILE = (760, 700)
CAPTION = 46
PEOPLES = ['kal', 'orenth', 'siann', 'veth']


def frame(number):
    return next(iter(sorted(run.glob(f'{number:02d}_*.png'))), None)


def load(path):
    image = Image.open(path).convert('RGB')
    # The capture is the whole 3840x2160 window: crop the 3D view at full height and keep the upper body centred.
    width, height = image.size
    left = int(width * .28)
    view = image.crop((left, 0, width, height))
    scale = min(TILE[0] / view.width, TILE[1] / view.height)
    return view.resize((int(view.width * scale), int(view.height * scale)), Image.LANCZOS)


font = ImageFont.load_default(size=30)
tiles = []
for people in PEOPLES:
    for number, label in (({'kal': 5, 'orenth': 18, 'siann': 31, 'veth': 44}[people], 'first'),
                          ({'kal': 6, 'orenth': 19, 'siann': 32, 'veth': 45}[people], 'middle'),
                          ({'kal': 7, 'orenth': 20, 'siann': 33, 'veth': 46}[people], 'last')):
        path = frame(number)
        if path is None:
            print('missing frame', number)
            continue
        tiles.append((f'{people} {label} body preset', load(path)))
for people in PEOPLES:
    for number, label in (({'kal': 12, 'orenth': 25, 'siann': 38, 'veth': 51}[people], 'first'),
                          ({'kal': 13, 'orenth': 26, 'siann': 39, 'veth': 52}[people], 'last')):
        path = frame(number)
        if path is None:
            continue
        tiles.append((f'{people} {label} body preset (female)', load(path)))

columns = 3
rows = (len(tiles) + columns - 1) // columns
sheet = Image.new('RGB', (columns * TILE[0], rows * (TILE[1] + CAPTION) + 60), (12, 12, 14))
draw = ImageDraw.Draw(sheet)
draw.text((18, 14), 'F1 Gate B — the starter outfit on the extreme body presets the creator offers (no skin through cloth)',
          font=font, fill=(240, 232, 218))
for index, (caption, image) in enumerate(tiles):
    x = (index % columns) * TILE[0]
    y = 60 + (index // columns) * (TILE[1] + CAPTION)
    sheet.paste(image, (x + (TILE[0] - image.width) // 2, y))
    draw.text((x + 12, y + TILE[1] + 6), caption, font=font, fill=(214, 205, 190))
out = destination / 'BODY_DIAL_EXTREMES.png'
sheet.save(out)
print('wrote', out.name, sheet.size)
