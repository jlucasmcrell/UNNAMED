"""First-hour content audit: every reference a quest, dialogue line, vendor or spawn makes must resolve.

Reports dangling refs, spelling-adjacent near misses, vendors with no stock or no purse, and quest
rewards that a level 1-5 character cannot use. Read-only.
"""
import glob
import json
import os
import re
import sys

import yaml

ROOT = r"D:\Otherreach_Scratch\ds_firsthour_wt\content"


def load_all(folder):
    docs = {}
    for path in glob.glob(os.path.join(ROOT, folder, "**", "*.yaml"), recursive=True):
        try:
            doc = yaml.safe_load(open(path, encoding="utf-8-sig").read())
        except Exception as exc:
            print(f"YAML FAIL {os.path.relpath(path, ROOT)}: {exc}")
            continue
        if isinstance(doc, dict) and "id" in doc:
            docs[doc["id"]] = (doc, os.path.relpath(path, ROOT))
    return docs


def walk(node, path=()):
    if isinstance(node, dict):
        for key, value in node.items():
            yield from walk(value, path + (str(key),))
    elif isinstance(node, list):
        for i, value in enumerate(node):
            yield from walk(value, path + (str(i),))
    else:
        yield path, node


spawns = load_all("spawns")
flags = load_all("world_flags")
quests = load_all("quests")
npcs = load_all("npcs")
items = load_all("items")
merchants = load_all("merchants")
regions = load_all("regions")
dialogues = load_all("dialogue")
creatures = load_all("creatures")
quest_ids = set(quests)
npc_ids = set(npcs)
item_ids = set(items)
region_ids = set(regions)
dialogue_ids = set(dialogues)
creature_ids = set(creatures)
everything = quest_ids | npc_ids | item_ids | region_ids | dialogue_ids | creature_ids

problems = []


def check(name, kind, ref, where):
    table = {"item": item_ids, "npc": npc_ids, "region": region_ids, "dialogue": dialogue_ids,
             "creature": creature_ids, "quest": quest_ids}.get(kind)
    if table is None:
        return
    if ref not in table:
        close = [t for t in table if t.split(".")[-1] == ref.split(".")[-1]]
        hint = f" (did you mean {close[0]}?)" if close else ""
        problems.append(f"MISSING {kind}: {ref}{hint} <- {name} [{where}]")


# 1. Every reference in every quest, dialogue, vendor row and spawn.
for doc_id, (doc, rel) in list(quests.items()) + list(dialogues.items()) + list(merchants.items()) + list(spawns.items()):
    for path, value in walk(doc):
        if not isinstance(value, str):
            continue
        # A display_key is a localisation key (quest.x.name), not a reference; notes are prose.
        if any(part in ("display_key",) for part in path):
            continue
        if value.startswith("quest.") or value.startswith("dialogue.") or value.startswith("npc."):
            if "." in value and value.split(".")[-1] in ("name", "desc", "text", "title"):
                continue
        for kind, pattern in (("item", r"(?<![\w.])item\.[a-z0-9_.]+"), ("npc", r"(?<![\w.])npc\.[a-z0-9_.]+"),
                              ("region", r"(?<![\w.])region\.[a-z0-9_.]+"), ("dialogue", r"(?<![\w.])dialogue\.[a-z0-9_.]+"),
                              ("creature", r"(?<![\w.])creature\.[a-z0-9_.]+"), ("quest", r"(?<![\w.])quest\.[a-z0-9_.]+")):
            for ref in re.findall(pattern, value):
                check(doc_id, kind, ref, ".".join(path))

# 2. Vendors: stock and purse.
for mid, (doc, rel) in merchants.items():
    stock = doc.get("stock") or doc.get("inventory") or []
    purse = doc.get("purse") or doc.get("coin") or doc.get("currency") or doc.get("gold_reserve") or 0
    if not stock:
        problems.append(f"EMPTY STOCK: {mid} [{rel}]")
    if not purse:
        problems.append(f"NO PURSE: {mid} [{rel}]")

# 3. Quest givers must exist and must be a person, and a first-hour quest must be reachable.
for qid, (doc, rel) in quests.items():
    giver = doc.get("giver_ref")
    if giver and giver not in npc_ids:
        problems.append(f"MISSING GIVER: {qid} -> {giver} [{rel}]")
    for obj in doc.get("objectives") or []:
        params = obj.get("params") or {}
        for key, value in params.items():
            if isinstance(value, str) and value.startswith("item.") and value not in item_ids:
                problems.append(f"MISSING OBJECTIVE ITEM: {qid}.{obj.get('id')} -> {value} [{rel}]")

# 4. Item rewards a low-level character cannot use.
for qid, (doc, rel) in quests.items():
    for reward in doc.get("rewards") or []:
        if reward.get("kind") != "item":
            continue
        ref = reward.get("item_ref") or reward.get("item") or reward.get("id")
        if ref not in item_ids:
            problems.append(f"MISSING REWARD ITEM: {qid} -> {ref} [{rel}]")
            continue
        item = items[ref][0]
        level = item.get("level") or item.get("required_level") or 0
        if isinstance(level, int) and level > 5:
            problems.append(f"REWARD ABOVE LEVEL 5: {qid} -> {ref} (level {level}) [{rel}]")

print(f"quests {len(quests)}, npcs {len(npcs)}, items {len(items)}, merchants {len(merchants)}, "
      f"dialogue {len(dialogues)}, regions {len(regions)}")
print(f"{len(problems)} findings")
for line in sorted(set(problems)):
    print("  " + line)
