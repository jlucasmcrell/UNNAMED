"""Build one prompt per cell: a place, a time band, and the lines that fit it.

The plan is deterministic - the same batch id builds the same cells, the same trade rotation and the same weather mix -
so a batch can be regenerated and its prompt hash checked. Run from the worktree root:

    python tools/chatter_pipeline/make_prompts.py b0001

Prompts are written to D:\\Otherreach_Scratch\\chatter\\<batch>\\cell-<place>-<band>.md, and the sha256 of the exact
prompt bundle (the three versioned templates plus every rendered cell prompt, in plan order) is printed and saved, to
be recorded in _provenance.yaml as the batch's prompt_hash.
"""

import hashlib
import json
import sys
from pathlib import Path

from chatter_rules import load_meta

HERE = Path(__file__).resolve().parent
PROMPTS = HERE / "prompts"
SCRATCH = Path("D:/Otherreach_Scratch/chatter")

# The trades the schema names, in the order the rotation walks them.
SPEAKERS = ["villager", "farmer", "herder", "hunter", "guard", "carter", "traveller", "labourer", "crafter", "adventurer"]

# How often each weather is drawn in content/config/weather.yaml: fair 4, overcast 3, rain 2, wind 2, fog 1 over twelve.
WEATHER_MIX = ["fair", "overcast", "rain", "wind", "fog", "fair", "overcast", "wind", "fair", "overcast", "rain", "fair"]

# Trades that are plausible in a place, so a cell does not tag a herder inside a quarry. Every trade still appears in
# the rotation; these lists only order it, and the order is rotated per cell.
PLACE_NOTES = {
    "ashen_hollow": "the waystation in its small valley - forge shed, lodge, market stall, the road out",
    "cairnwold": "the moorland village on the dry rise - long lodge, seven houses, well, staddle granary, smithy",
    "old_east_road": "the road between the two towns - shrine, the wain on its side, the barrow, the drop at the Rim",
    "wilds": "off the settlements and off the road - moor, woods, bog, crags",
}

TIME_NOTES = {
    "dawn": "first light, the cold hour, before the day's work is chosen",
    "morning": "work in hand, light up, the day decided",
    "afternoon": "the long part of the day, work going stale, thoughts turning to the evening",
    "dusk": "light going, work stopping, the fires being made up",
    "evening": "indoors or by a fire, talk, aches, the day being turned over",
    "night": "dark, a watch kept or sleep refused, the small hours",
}

LINES_PER_CELL = 12          # 4 places x 6 time bands x 12 = 288, plus the event block below = 300
UNTAGGED_SPEAKERS = 3        # of the twelve, this many carry no trade, so any speaker can use them
WEATHER_TAGGED = 6           # of the twelve, this many carry a weather tag; the rest can be said in any weather
CANON_MIN_CELL = 5           # of the twelve, at least this many carry a fact specific to this world
EVENT_LINES = 3              # per event, in the batch's event block
EVENT_PLAN = ["weather_turned", "creature_slain", "market_day", "bounty_claimed"]

EVENT_FACTS = {
    "weather_turned": "A new front has just reached the valley. The speaker has noticed the change and says so, "
                      "in passing. Do not name the weather that came before it.",
    "creature_slain": "Something was killed in the region a short while ago. The slot {creature} is its common name "
                      "and must appear in the line.",
    "market_day": "The markets have just settled a day's trade. The speaker remarks on what that means for stock, "
                  "coin, or what will be short. Line may not use slots.",
    "bounty_claimed": "Someone other than the player has just finished a bounty. {claimant} is their name and "
                      "{bounty} is the bounty's title; use both, without inventing anything about either.",
}


def trade_order(index):
    """The ten trades, rotated, to say which trades a cell should hear from - not which line gets which trade."""
    offset = index * 3 % len(SPEAKERS)
    return SPEAKERS[offset:] + SPEAKERS[:offset]


def build_plan(schema, batch):
    plan = []
    cells = [(place, band) for place in schema.places for band in schema.times]
    # Every cell takes WEATHER_TAGGED colours from one long weighted sequence, so the batch's totals land on the
    # weather config's weights while each cell still gets a mix instead of a single weather.
    weighted = (WEATHER_MIX * ((len(cells) * WEATHER_TAGGED + len(WEATHER_MIX) - 1) // len(WEATHER_MIX))
                )[:(len(cells) * WEATHER_TAGGED)]
    for index, (place, band) in enumerate(cells):
        plan.append({
            "id": f"{place}-{band}",
            "place": place,
            "time": band,
            "count": LINES_PER_CELL,
            "untagged": UNTAGGED_SPEAKERS,
            "canon": CANON_MIN_CELL,
            "trades": trade_order(index),
            "weather": weighted[index * WEATHER_TAGGED:(index + 1) * WEATHER_TAGGED],
            "world": True,
        })
    events = []
    for order, event in enumerate(EVENT_PLAN):
        for line in range(EVENT_LINES):
            cell = plan[(order * 5 + line * 8) % len(plan)]
            events.append({"event": event, "cell": cell["id"], "place": cell["place"], "time": cell["time"]})
    return {"batch": batch, "cells": plan, "events": events,
            "lines": len(plan) * LINES_PER_CELL + len(events)}


def render(plan, schema):
    template = (PROMPTS / "cell_prompt.md").read_text(encoding="utf-8")
    examples = (PROMPTS / "examples.md").read_text(encoding="utf-8")
    rendered = []
    for cell in plan["cells"]:
        speakers = "\n".join(f"  - `{trade}`" for trade in cell["trades"])
        weather_counts = {}
        for weather in cell["weather"]:
            weather_counts[weather] = weather_counts.get(weather, 0) + 1
        weather_plan = (f"Tag {WEATHER_TAGGED} of the lines by weather: " + ", ".join(
            f"{count} {weather}" for weather, count in sorted(weather_counts.items()))
            + ". The rest carry no weather tag, so they can be said in any weather.")
        channel_plan = ("Exactly one line is spoken as if to the whole valley (`channel: world`), and it must be worth "
                        "broadcasting: a road, trade, a price, weather coming in, work or company wanted, or news of a "
                        "creature or a bounty. The other eleven are local and need no channel field.")
        prompt = template
        for name, value in (
            ("count", str(cell["count"])),
            ("place", schema.doc["places"][cell["place"]]),
            ("place_note", PLACE_NOTES[cell["place"]]),
            ("time_band", cell["time"]),
            ("time_note", TIME_NOTES[cell["time"]]),
            ("speakers", speakers),
            ("canon_min", str(cell["canon"])),
            ("weather_plan", weather_plan),
            ("channel_plan", channel_plan),
            ("untagged_speakers", str(cell["untagged"])),
            ("examples", examples),
        ):
            prompt = prompt.replace("{{" + name + "}}", value)
        rendered.append((cell["id"], prompt))
    for event in plan["events"]:
        prompt = (f"# Event lines: {event['event']}\n\nRead `voice.md`, `facts.md` and `canon.md` first.\n\n"
                  f"Write {EVENT_LINES} ambient chatter lines said by ordinary people in **{event['place']}** at "
                  f"**{event['time']}**, each waiting on the event `{event['event']}`.\n\n"
                  f"{EVENT_FACTS[event['event']]}\n\n"
                  f"Each line carries `events: [{event['event']}]` and the trade or trades it fits in `speakers`, and "
                  f"`canon: true` if it uses a fact from `canon.md`. One sentence, at most 140 characters, dry, "
                  f"concrete, nobody addressing the player.\n\n"
                  f"Return only YAML, one mapping per line:\n"
                  f"- {{ text: \"...\", speakers: [villager], events: [{event['event']}], canon: true }}\n")
        rendered.append((f"event-{event['event']}-{event['cell']}-{len(rendered)}", prompt))
    return rendered


def main():
    batch = sys.argv[1] if len(sys.argv) > 1 else "b0001"
    schema, _ = load_meta(Path("content"))
    plan = build_plan(schema, batch)
    out = SCRATCH / batch
    (out / "cells").mkdir(parents=True, exist_ok=True)
    rendered = render(plan, schema)
    digest = hashlib.sha256()
    for name in ("voice.md", "facts.md", "canon.md", "examples.md", "cell_prompt.md"):
        digest.update((PROMPTS / name).read_bytes())
    for name, prompt in rendered:
        (out / "cells" / (name + ".md")).write_text(prompt, encoding="utf-8")
        digest.update(prompt.encode("utf-8"))
    prompt_hash = "sha256:" + digest.hexdigest()
    (out / "plan.json").write_text(json.dumps(plan, indent=2), encoding="utf-8")
    (HERE / "batches").mkdir(exist_ok=True)
    (HERE / "batches" / f"{batch}.prompts.sha256").write_text(prompt_hash + "\n", encoding="utf-8")
    print(f"{batch}: {plan['lines']} lines over {len(plan['cells'])} cells and {len(plan['events'])} event lines")
    print(f"prompts: {out / 'cells'} ({len(rendered)} files)")
    print(f"prompt_hash: {prompt_hash}")
    for cell in plan["cells"]:
        counts = {}
        for weather in cell["weather"]:
            counts[weather] = counts.get(weather, 0) + 1
        print(f"  {cell['id']:<24} {cell['count']} lines  canon>={cell['canon']}  weather {counts}")


if __name__ == "__main__":
    main()
