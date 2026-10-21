"""Wire the lore entries into the world: a readable for every page that is read, a telling for every page that is told.

Spec 41 pass 2. The entries declare their sources; this places them. It is idempotent: a readable or a telling that is already there is
left alone, so it can be run after every batch of entries.
"""

import pathlib
import re
import sys

import json
import yaml

ROOT = pathlib.Path(r"C:\Otherreach_Build\ds_lore_wt")
LORE = ROOT / "content" / "lore"
REGION = ROOT / "content" / "regions" / "starting_frontier.yaml"
DIALOGUES = ROOT / "content" / "dialogue"


def entries():
    for path in sorted(LORE.glob("*.yaml")):
        if path.name.startswith("_"):
            continue
        doc = yaml.safe_load(path.read_text(encoding="utf-8"))
        yield path, doc


def flat(text):
    return " ".join(str(text).split())


def main():
    told = []      # (dialogue id, entry id, spoken text)
    books = {}     # readable key -> [entry id, page, title, how many pages it opens]
    for path, doc in entries():
        for page in doc.get("pages") or []:
            for source in page.get("sources") or []:
                if source.get("how") == "inspected":
                    key = source["ref"]
                    number = int(page.get("page", 1))
                    if key in books:
                        # The same thing written holds more than one page: it is a book, and it opens whole.
                        books[key][1] = 0
                    else:
                        books[key] = [doc["id"], number, doc["title"], 0]
                if source.get("how") == "told":
                    told.append((source["ref"], doc["id"], int(page.get("page", 1)), flat(page["text"]), doc["title"]))

    # 1. One readable per thing written, on the Survey's record shelf beside Sel Arien's table: a page of an entry, or the whole book.
    region_text = REGION.read_text(encoding="utf-8")
    missing = [(key, *row) for key, row in books.items() if key not in region_text]
    if missing:
        # The Survey's record shelf: what a reader can pick up beside Sel Arien's table, in a small block so every one is reachable.
        start = len(re.findall(r"^  - key: readable\.", region_text, re.M))
        lines = []
        for index, (key, entry, page, title, _) in enumerate(missing):
            x = 472.6 + 1.4 * (index // 6)
            z = 717.8 + 0.85 * (index % 6)
            lines.append(f"  - key: {key}")
            lines.append(f"    lore_ref: {entry}")
            lines.append(f"    page: {page}")
            lines.append(f"    name: the {title} on the Survey shelf")
            lines.append("    verb: Read")
            lines.append(f"    circle_m: [{x:.1f}, {z:.1f}, 0.6]")
            lines.append("    height_m: 0.9")
        anchor = "readables:\n"
        at = region_text.find(anchor)
        if at < 0:
            sys.exit("the region has no readables: block")
        insert = at + len(anchor)
        region_text = region_text[:insert] + "\n".join(lines) + "\n" + region_text[insert:]
        REGION.write_text(region_text, encoding="utf-8")
        print(f"placed {len(missing)} readables")

    # 2. One telling per told page: an 'ask' reply on the speaker's repeat node, and the telling itself.
    for dialogue_ref, entry, page, spoken, title in told:
        name = dialogue_ref.split(".")[-1]
        candidate = [p for p in DIALOGUES.rglob(f"{name}.yaml")]
        if not candidate:
            print("no dialogue file for", dialogue_ref)
            continue
        path = candidate[0]
        text = path.read_text(encoding="utf-8")
        choice_id = f"ask_{entry.split('.')[-1]}"
        if f"- id: {choice_id}" in text:
            continue
        node_id = f"tell_{entry.split('.')[-1]}"
        # The reply goes on the node the player returns to, before its leave.
        again = text.find("\n  again:")
        if again < 0:
            print("no again node in", path.name)
            continue
        leave = text.find("\n      - id: leave", again)
        if leave < 0:
            print("no leave reply in", path.name)
            continue
        choice = (f"\n      # Spec 41: told, not read. The page is the telling, so the two cannot drift.\n"
                  f"      - id: {choice_id}\n"
                  f"        text: {json.dumps('Tell me about ' + title.lower().rstrip('.'), ensure_ascii=False)}\n"
                  f"        next: {node_id}\n")
        text = text[:leave] + choice + text[leave:]
        node = (f"\n  {node_id}:\n"
                f"    text: {json.dumps(spoken, ensure_ascii=False)}\n"
                f"    choices:\n"
                f"      - id: back\n"
                f"        text: {json.dumps('I will remember it.')}\n"
                f"        consequences: [{{ command: tell_lore, lore_ref: {entry} }}]\n"
                f"        next: again\n")
        barks = text.find("\nbarks:")
        text = (text[:barks] + node + text[barks:]) if barks > 0 else (text.rstrip("\n") + "\n" + node)
        path.write_text(text, encoding="utf-8")
        print("wired", entry, "into", path.name)


if __name__ == "__main__":
    main()
