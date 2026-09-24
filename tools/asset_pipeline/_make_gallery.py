"""Build a browsable HTML gallery of the concepts and finished assets.

Contact sheets are good for scanning a category, but there was no single place to look at
everything, and no way to see which concepts are still at the older 1024 render. This
writes a self-contained page that groups by category and marks the render quality.

Usage:
    python _make_gallery.py
"""
import base64
import glob
import html
import io
import json
import os
import sys

from PIL import Image

ASSETS = r"W:\UNNAMED\assets"
CONCEPTS = os.path.join(ASSETS, "concepts")
READY = os.path.join(ASSETS, "ready")
RIGGED = os.path.join(ASSETS, "rigged")
OUT = os.path.join(ASSETS, "review", "gallery.html")

CATEGORIES = [
    ("race_", "Races"),
    ("npc_", "NPCs"),
    ("creature_", "Creatures"),
    ("weapon_", "Weapons"),
    ("tool_", "Tools"),
    ("prop_", "Props"),
    ("item_", "Items and materials"),
    ("resource_", "Resources"),
    ("herb_", "Herbs"),
    ("reagent_", "Reagents"),
    ("flora_", "Flora"),
    ("icon_", "Icons"),
]

THUMB = 260


def thumbnail(path):
    """Return a data URI so the gallery is one file with no external references."""
    image = Image.open(path).convert("RGB")
    image.thumbnail((THUMB, THUMB), Image.LANCZOS)
    buffer = io.BytesIO()
    image.save(buffer, format="JPEG", quality=82)
    return "data:image/jpeg;base64," + base64.b64encode(buffer.getvalue()).decode()


def main():
    concepts = {os.path.basename(p)[:-4]: p
                for p in glob.glob(os.path.join(CONCEPTS, "*.png"))}
    ready = set(os.listdir(READY)) if os.path.isdir(READY) else set()
    rigged = set(os.listdir(RIGGED)) if os.path.isdir(RIGGED) else set()

    total_hq = sum(1 for p in concepts.values() if Image.open(p).size[0] >= 1536)

    parts = [
        "<!doctype html><meta charset='utf-8'>",
        "<title>UNNAMED asset gallery</title>",
        "<style>",
        "body{background:#14161a;color:#d8dae0;font:13px/1.5 system-ui,sans-serif;"
        "margin:0;padding:24px}",
        "h1{font-size:19px;font-weight:600;margin:0 0 4px}",
        "h2{font-size:15px;font-weight:600;margin:28px 0 10px;color:#9fb4d8}",
        ".sub{color:#7d828c;margin:0 0 20px}",
        ".grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(260px,1fr));gap:14px}",
        ".card{background:#1c1f25;border:1px solid #2a2e36;border-radius:6px;"
        "overflow:hidden;position:relative}",
        ".card img{width:100%;display:block;background:#262a32}",
        ".name{padding:6px 8px;font-size:11px;color:#b9bdc6;word-break:break-all}",
        ".badges{position:absolute;top:6px;right:6px;display:flex;gap:4px}",
        ".b{font-size:10px;padding:2px 6px;border-radius:3px;font-weight:600}",
        ".hq{background:#1f4d33;color:#8fe0b0}",
        ".low{background:#4d3f1f;color:#e0cb8f}",
        ".built{background:#26374d;color:#9fc4e0}",
        ".rigged{background:#3d2a4d;color:#c9a0e0}",
        "</style>",
        "<h1>UNNAMED - asset gallery</h1>",
        f"<p class='sub'>{len(concepts)} concepts ({total_hq} at 1536, "
        f"{len(concepts)-total_hq} at the older 1024) &middot; {len(ready)} built "
        f"&middot; {len(rigged)} rigged</p>",
    ]

    for prefix, label in CATEGORIES:
        ids = sorted(i for i in concepts if i.startswith(prefix))
        if not ids:
            continue
        parts.append(f"<h2>{html.escape(label)} <span class='sub'>({len(ids)})</span></h2>")
        parts.append("<div class='grid'>")
        for asset_id in ids:
            path = concepts[asset_id]
            wide = Image.open(path).size[0] >= 1536
            badges = [f"<span class='b {'hq' if wide else 'low'}'>"
                      f"{'1536' if wide else '1024'}</span>"]
            if asset_id in ready:
                badges.append("<span class='b built'>3D</span>")
            if asset_id in rigged:
                badges.append("<span class='b rigged'>rig</span>")
            parts.append(
                "<div class='card'>"
                f"<div class='badges'>{''.join(badges)}</div>"
                f"<img src='{thumbnail(path)}' alt='{html.escape(asset_id)}'>"
                f"<div class='name'>{html.escape(asset_id)}</div></div>")
        parts.append("</div>")

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as handle:
        handle.write("\n".join(parts))
    print(f"gallery -> {OUT}  ({os.path.getsize(OUT)/1e6:.1f} MB, "
          f"{len(concepts)} concepts)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
