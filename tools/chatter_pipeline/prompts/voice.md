# Voice: how every chatter line is written

Version 1 (2026-10-18). This is the register the corpus is held to. It is read with `facts.md` and `cell_prompt.md`.

## What these lines are

Ambient small talk overheard in a frontier valley. Somebody living or passing through says one sentence about the
day, the weather, their work, the road, or the news. They are not talking to the player. They are not giving quests,
directions or warnings. Nobody is explaining anything to anybody.

The game picks a line by the speaker's trade, the place they stand, the hour and the weather, so a line has to be
true for *any* individual of that trade in that place at that hour. A named person never says a corpus line.

## Register

- Grounded frontier science-fantasy: mud, iron, wet wool, cold hands, a tool that needs mending. The world is old and
  the people are practical. The strange is ordinary enough to mention in passing, not to marvel at.
- Dry and concise. One sentence, occasionally two short ones. Contractions are normal. No exclamations, no
  cheerleading, no warmth-for-its-own-sake.
- Concrete over abstract. "The ford's up to the axle" beats "the roads are bad". Name a thing, a part, a task, a
  number of days.
- Varied voices: some blunt, some tired, some wry, some grumbling, some matter-of-fact. Never twee, never whimsical,
  never a fantasy peasant accent ("aye, m'lord").
- A line may be a fragment, a complaint, a half-thought, an aside. It may start in the middle of something. It may
  end without resolution.
- Present tense for now, past for what happened, and the future only as intention or prediction.

## Hard rules (the lint enforces these)

1. At most 140 characters, counting the text exactly as written.
2. No proper noun outside the glossary in `facts.md`. Capitalised words mid-sentence are checked as proper nouns, so
   write common nouns in lower case: "wolves", not "Grey Wolves"; "the smith", not an invented name.
3. No named person's signature line. Their tics are banned outright (see the banned list in `facts.md`); in particular
   never open with a warning followed by a name, never say "now then", "in ink", "give or take", "exactly that".
4. No modern slang, no brands, no explicit content, no interface words (press, click, button, menu, cooldown, level up).
5. No raw ids, and no word containing an underscore.
6. No lore invention. Do not name a place, person, people or creature that the facts sheet does not list. Do not state
   a fact about the world that the facts sheet does not support: no distances, populations, prices, dates, gods,
   wars, kingdoms, currencies or seasons of your own.
7. Chat presents facts, it never creates them. No line may claim something happened that the game does not track.
8. **Never place a named person.** Residents keep daily routines - work, meals, sleep, errands - so no line may say
   where a named character is or what they are doing at a given hour. Speak of a trade ("the smith"), never of the
   tradesman's whereabouts.

## Slots

Three slots may appear, written `{place}`, `{weather}`, `{time_band}`. They are expanded at runtime:
`{place}` is the speaker's place ("Cairnwold", "Ashen Hollow", "the Old East Road", "the wilds"),
`{weather}` is one word ("fair", "overcast", "rain", "fog", "wind"),
`{time_band}` is one word ("dawn", "morning", "afternoon", "dusk", "evening", "night").

Use them rarely, and only where the sentence stays grammatical for **every** value. `{place}` gives "the Old East
Road" and "the wilds", so it cannot take a possessive or a singular verb. `{weather}` gives "fair", which is not a
noun, so "the {weather}" is wrong. Most lines should carry no slots at all: the tags already tie a line to its
place, hour and weather.

## What good looks like

- "Rain's set in over the moss. The ditches will run by dusk."
- "Third night the dog's slept by the door. He knows something."
- "That wain's still on its side. Nobody's come back for it and nobody will."
- "Iron out of the seam comes up soft this week. Takes twice the hammering."
- "My back's telling me it's going to rain. My back's usually right."

What to avoid: "Ah, the simple life of the frontier!" (warmth, exclamation), "As you know, the wolves grow bolder in
winter" (exposition), "Greetings, traveller!" (nobody says this), "Press the button to trade" (interface), anything
that describes a named person's day.

## Length and shape balance for a batch

Aim for a mix: roughly a third short (under 40 characters), a third medium, a third longer but under the limit.
Vary the opening word across the batch; do not let six lines in a row begin with "The".
