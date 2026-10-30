"""Put the dark frame and the bright frame side by side, with the mean brightness of each, so the difference is measurable and visible."""
from pathlib import Path

from PIL import Image, ImageDraw

PAIRS = [
    (r'F:\otherreach_captures\fishing_ingame\01_cast.png', 'before: worktree never imported'),
    (r'F:\otherreach_captures\fishing_fixed\01_cast.png', 'after: --import built the cache'),
]
OUT = Path(r'F:\otherreach_captures\side_by_side_brightness.png')


def mean_brightness(image):
    small = image.convert('L').resize((160, 90))
    pixels = list(small.getdata())
    return sum(pixels) / len(pixels)


tiles = []
for path, label in PAIRS:
    if not Path(path).exists():
        raise SystemExit(f'missing {path}')
    image = Image.open(path).convert('RGB')
    tile = image.resize((900, 506))
    draw = ImageDraw.Draw(tile)
    draw.rectangle([0, 470, 900, 506], fill=(0, 0, 0))
    draw.text((12, 480), f'{label}  -  mean brightness {mean_brightness(image):0.1f}/255', fill=(235, 235, 235))
    tiles.append(tile)

sheet = Image.new('RGB', (900, 506 * len(tiles) + 8 * (len(tiles) - 1)), (18, 18, 18))
y = 0
for tile in tiles:
    sheet.paste(tile, (0, y))
    y += tile.height + 8
sheet.save(OUT)
print(f'wrote {OUT}')
for path, _ in PAIRS:
    print(path, f'{mean_brightness(Image.open(path)):0.1f}/255')
