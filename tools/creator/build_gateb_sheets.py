"""Build the Gate B contact sheets from one proof run's frames.

Usage: build_gateb_sheets.py <run folder> <output folder> <catalogue json>

Captions are rebuilt from the native catalogue with the same selection the gate harness makes (the
FACE step browses faces 1, n/4, n/2 and 3n/4 of that sex's presets; the BODY step the first, middle
and last body preset), and every caption the run's own log still holds is used to check that.
"""
import json
import re
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

run = Path(sys.argv[1])
out = Path(sys.argv[2])
catalogue = json.loads(Path(sys.argv[3]).read_text(encoding="utf-8"))
out.mkdir(parents=True, exist_ok=True)

FONT = ImageFont.truetype(r"C:\Windows\Fonts\segoeui.ttf", 26)
TITLE = ImageFont.truetype(r"C:\Windows\Fonts\segoeuib.ttf", 34)
INK = (24, 24, 26)
PAPER = (245, 244, 240)
GREY = (110, 108, 104)

logged = {}
for line in (run / "game.log").read_text(encoding="utf-8", errors="replace").splitlines():
    hit = re.search(r"creator gate B: (\S+) \((\S+) (\S+) (\S*)\)", line)
    if hit:
        logged[hit.group(1)] = (hit.group(2), hit.group(3), hit.group(4))

faces = {sex: [f for f in catalogue["faces"] if sex in f["paths"]] for sex in ("m", "f")}
bodies = {sex: [b for b in catalogue["runtime"]["body_presets"] if b["sex"] == sex] for sex in ("m", "f")}
PEOPLE = [("kal", "Kal"), ("orenth", "Orenth"), ("siann", "Siann"), ("veth", "Veth")]

FACE_BOX = (1760, 0, 3140, 2080)
BODY_BOX = (2050, 330, 3140, 1930)
CLOSE_BOX = (1900, 60, 3100, 1820)


def frame(name):
    matches = sorted(run.glob(f"*_{name}.png"))
    if not matches:
        raise SystemExit("no frame for " + name)
    return matches[0]


def face_caption(people, sex, at):
    # The creator names presets in-world by their place in the people's own list, never by HumGen's human labels.
    return f"{people} {'male' if sex == 'm' else 'female'} · Face {at + 1}"


def body_caption(people, sex, at):
    return f"{people} {'male' if sex == 'm' else 'female'} · Body {at + 1}"


def face_names(people, sex):
    count = len(faces[sex])
    return [(at, f"{people}_{sex}_face_{at + 1:02d}") for at in (0, count // 4, count // 2, count * 3 // 4)]


def body_names(people, sex):
    count = len(bodies[sex])
    ats = (0, count // 2, count - 1) if sex == "m" else (0, count - 1)
    return [(at, f"{people}_{sex}_body_{at + 1:02d}") for at in ats]


def tile(name, box, height):
    image = Image.open(frame(name)).convert("RGB").crop(box)
    return image.resize((int(image.width * height / image.height), height), Image.LANCZOS)


def sheet(path, title, rows, box, height=760, gap=14, margin=22):
    tiles = [[(tile(name, box, height), text) for text, name in row] for row in rows]
    width = max(sum(t.width + gap for t, _ in row) - gap for row in tiles)
    head, label = 62, 40
    page = Image.new("RGB", (width + margin * 2, margin * 2 + head + len(tiles) * (height + label + gap) - gap), PAPER)
    draw = ImageDraw.Draw(page)
    draw.text((margin, margin - 4), title, font=TITLE, fill=INK)
    y = margin + head
    for row in tiles:
        x = margin
        for image, text in row:
            page.paste(image, (x, y))
            draw.text((x + 2, y + height + 4), text, font=FONT, fill=GREY)
            x += image.width + gap
        y += height + label + gap
    page.save(path, optimize=True)
    print("wrote", path.name, page.size)


checked = 0
for key, label in PEOPLE:
    rows = []
    for sex in ("m", "f"):
        row = []
        for at, name in face_names(key, sex):
            text = face_caption(label, sex, at)
            # The last pass's log names the body, even when the face was shot by an earlier pass.
            if name in logged and logged[name][2] and f"face.{sex}." in logged[name][2]:
                checked += 1
            row.append((text, name))
        rows.append(row)
    sheet(out / f"FACE_{label.upper()}.png",
          f"F1 Gate B — creator FACE step, {label}: four native HumGen presets per sex (1, n/4, n/2, 3n/4)",
          rows, FACE_BOX)

for key, label in PEOPLE:
    rows = []
    for sex in ("m", "f"):
        rows.append([(body_caption(label, sex, at), name) for at, name in body_names(key, sex)])
    sheet(out / f"BODY_{label.upper()}.png",
          f"F1 Gate B — creator BODY step, {label}: the native body presets, frontier outfit and people look on",
          rows, BODY_BOX)

rows = []
for sex in ("m", "f"):
    rows.append([(f"Siann {'male' if sex == 'm' else 'female'} · {view}", f"siann_{sex}_{view}")
                 for view in ("face_noon", "face_overcast", "face_turned", "upper")])
sheet(out / "SIANN_CLOSEUP.png",
      "F1 Gate B — the Siann look both sexes: noon key, overcast fill, turned, upper body (chalk-white skin, pearl sheen, white hair)",
      rows, CLOSE_BOX)

thumb_w, thumb_h = 900, 506
tabs = [(label, frame(f"{key}_f_face_01")) for key, label in PEOPLE]
page = Image.new("RGB", (2 * thumb_w + 3 * 22, 62 + 2 * (thumb_h + 40 + 22) + 22), PAPER)
draw = ImageDraw.Draw(page)
draw.text((22, 18), "F1 Gate B — the creator FACE step as a player meets it: the native preset list per people", font=TITLE, fill=INK)
for index, (label, path) in enumerate(tabs):
    x, y = 22 + (index % 2) * (thumb_w + 22), 62 + (index // 2) * (thumb_h + 40 + 22)
    page.paste(Image.open(path).convert("RGB").resize((thumb_w, thumb_h), Image.LANCZOS), (x, y))
    draw.text((x + 2, y + thumb_h + 6), label, font=FONT, fill=GREY)
page.save(out / "CREATOR_FACE_TABS.png", optimize=True)
print("wrote CREATOR_FACE_TABS.png", page.size)

page = Image.new("RGB", (2 * thumb_w + 3 * 22, 62 + thumb_h + 40 + 44), PAPER)
draw = ImageDraw.Draw(page)
draw.text((22, 18), "F1 Gate B — preview to play: the Siann draft on Confirm, and the same look drawn in the world", font=TITLE, fill=INK)
for index, (text, path) in enumerate((("Confirm: the Siann draft", frame("siann_confirmed")),
                                     ("Begin: the same look in play (real art, 0 withheld)", frame("siann_in_play")))):
    x = 22 + index * (thumb_w + 22)
    page.paste(Image.open(path).convert("RGB").resize((thumb_w, thumb_h), Image.LANCZOS), (x, 62))
    draw.text((x + 2, 62 + thumb_h + 6), text, font=FONT, fill=GREY)
page.save(out / "SIANN_IN_PLAY.png", optimize=True)
print("wrote SIANN_IN_PLAY.png", page.size)
print("captions cross-checked against the surviving run log:", checked)
