# Cell prompt

Version 1 (2026-10-18). One call writes one cell: a place, a time band, and a set of lines that fit it. The writer is
a model; the lines it returns are checked by `tools/chatter_pipeline/lint.py` and then by the game's own WP16 lint.

Read `voice.md` and `facts.md` first. They are part of this prompt.

---

You are writing {{count}} ambient chatter lines for an in-game world chat channel in a frontier valley.

## The cell

- Place: **{{place}}** - {{place_note}}
- Time band: **{{time_band}}** - {{time_note}}
- Lines wanted: **{{count}}**

## Who says them

Each line is said by one individual, of one of these trades, standing in that place at that hour. Tag every line with
the trade or trades it truly fits, and with no others:

{{speakers}}

Line by line, cover them this way: {{speaker_plan}}

## The mix

- {{weather_plan}}
- {{channel_plan}}
- About {{untagged_speakers}} of the lines carry no trade at all, because anyone could say them. Those keep the cell
  playable, so write them as genuinely universal remarks, not as filler.
- No line may wait on a world event in this cell; events are written separately.

## Rules, in short

One sentence, at most 140 characters, dry, concrete, present or immediate past, nobody addressing the player, no
proper nouns except the places and creatures listed in `facts.md`, no named person placed anywhere or doing anything,
no invented lore, no numbers that state a world fact. Use `{place}`, `{weather}` or `{time_band}` only if the sentence
stays correct for every value the slot can take, and prefer carrying the place, hour and weather in the tags instead.

This cell is at **{{time_band}}**, so write what is true then: what is being done, what the light is like, what the
air is doing, what is coming up next in the day.

## What to avoid

- Six lines in a row opening with "The".
- Weather in every line. Most talk is about work, tools, animals, people's bodies, small mistakes, food, sleep, the
  road, and mild complaints.
- Wisdom, proverbs, aphorisms summarising the frontier. Nobody in a valley talks like an epigraph.
- Anything that reads like text written to advertise the world or to explain it to a stranger.
- Exact repeats of the shapes in the examples below. Vary the sentence shape: some fragments, a question to no one,
  a half-sentence, a list of one thing done after another.

## Shape

Return only YAML, no prose before or after, one mapping per line, like this:

```yaml
- { text: "Rain's set in over the moss. The ditches will run by dusk.", weather: [rain], channel: local }
- { text: "Third morning the dog's slept by the door.", speakers: [herder] }
- { text: "That wall's been down since before I came. Nobody's in a hurry.", speakers: [villager, labourer] }
```

Fields: `text` always; `speakers`, `weather`, `events`, `channel` only when they apply (`channel: world` when the line
is spoken as if to the whole valley; the default is local). Do not add a key, a place or a time to a line: those come
from the cell.

## Examples of the register (do not copy)

{{examples}}
