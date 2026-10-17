# Cell prompt

Version 2 (2026-10-18). One call writes one cell: a place, a time band, and the lines that fit it. The writer is a
model; the lines it returns are checked by `tools/chatter_pipeline/check_batch.py` and then by the game's own WP16 lint.

Read `voice.md`, `facts.md` and `canon.md` first. They are part of this prompt.

---

You are writing {{count}} ambient chatter lines for an in-game world chat channel in a frontier valley.

## The cell

- Place: **{{place}}** - {{place_note}}
- Time band: **{{time_band}}** - {{time_note}}
- Lines wanted: **{{count}}**

This cell is at **{{time_band}}**, so write what is true then: what is being done, what the light is like, what the
air is doing, what is coming up next in the day.

## This is not generic medieval

This world has its own things in it - a calendar that counts in tallies, a sky that is not quiet, waystones, the
Foldscar, peoples with names, Calls that decide what a person does with their life, a survey and a watch that ordinary
people have opinions about. **At least {{canon_min}} of the {{count}} lines must carry one such fact**, drawn from
`canon.md` and said the way a person who has lived among it would say it, in passing. Mark those lines `canon: true`.

Read `canon.md` before you begin and spread the canon across cells: the calendar, the sky, waystones, the Foldscar
seen from a distance, the peoples, Calls, the factions, the road, the iron, leaks and records, the creatures. Do not
put all of it in one or two lines.

A canon line is not an explanation. It is a fact used as a fact: "Third tally this Reckoning the sky's been wrong
over the crags." Never teach the reader anything, and never resolve the Foldscar.

## Who says them

Each line is said by one individual, of one of these trades, standing in that place at that hour:

{{speakers}}

- **Tag every line with the trade that could actually say it.** The tag is a filter: a line tagged `guard` will only
  ever be said by a guard. A remark about mending a roof is not a guard's line; a remark about a herd is not a
  crafter's. If a line could come from any trade, give it no `speakers` field at all.
- **About {{untagged_speakers}} of the {{count}} lines should carry no trade**, because anyone could say them. Those
  keep the cell playable, so write them as genuinely universal remarks, not as filler.
- One or two trades per line, never three. Do not tag a trade merely to give it something.

## The mix

- {{weather_plan}}
- {{channel_plan}}
- No line may wait on a world event in this cell; events are written separately.

## Rules, in short

One sentence, at most 140 characters, dry, concrete, present or immediate past, nobody addressing the player, no
proper nouns outside `canon.md` and `facts.md`, no named person quoted or placed anywhere or doing anything, no
invented lore, no numbers that state a world fact. Use `{place}`, `{weather}` or `{time_band}` only if the sentence
stays correct for every value the slot can take, and prefer carrying the place, hour and weather in the tags.

## What to avoid

- Six lines in a row opening with "The".
- Weather in every line. Most talk is about work, tools, animals, bodies, small mistakes, food, sleep, the road, and
  mild complaint - with the world's own facts salted through it.
- Wisdom, proverbs, aphorisms summarising the frontier. Nobody in a valley talks like an epigraph.
- Anything that reads like text written to advertise the world or to explain it to a stranger.
- Repeating the shapes in the examples. Vary the sentence shape: fragments, a question to no one, a half-sentence,
  one thing done after another.

## Shape

Return only YAML, no prose before or after, one mapping per line, like this:

```yaml
- { text: "Rain's set in over the moss. The ditches will run by dusk.", weather: [rain] }
- { text: "Third tally of this Reckoning and the sky's still wrong over the crags.", speakers: [villager], canon: true }
- { text: "That wall's been down since before I came. Nobody's in a hurry.", speakers: [villager, labourer] }
```

Fields: `text` always; `speakers`, `weather`, `events`, `channel`, `canon` only when they apply (`channel: world` for
the one broadcast line; `canon: true` when the line uses a fact from `canon.md`). Do not add a key, a place or a time
to a line: those come from the cell.

## Examples of the register (do not copy)

{{examples}}
