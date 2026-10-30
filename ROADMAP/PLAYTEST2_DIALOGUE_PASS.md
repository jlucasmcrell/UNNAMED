# Playtest 2 — dialogue and story pass

**Status:** review document for Claude. Nothing in `content/` was edited; every rewrite below is a proposal with its file and node named.
**Scope judged:** every spoken line and every player choice a character can meet in the first hour — `content/dialogue/ashen_hollow/` (Kera Voss, Renn Vale, Sel Arien, Tavar Orr) and `content/dialogue/cairnwold/` (Berrin Garth, Durn Skell, Mara Ling, Tam Crowden). **325 entries: 104 spoken lines, 221 choice labels.**
**Bar applied:** engaging, understandable, funny, and carrying each people's canon view of the others — including, as the owner's own example, Kal flight jokes.

---

## 1. Verdict in one line

**The prose is already good; the *choices* are the weak surface.** Spoken lines carry voice and detail that a first-hour player can feel (`"That? That's not a sword, that's a warning."`), but the labels the player actually clicks — 221 of them — are mostly flat (`Thanks.`, `Nothing today.`, `Another time.`), two are literally duplicated, and almost nothing outside one Kera node jokes about a *people* rather than a person.

| NPC | area | entries | voice | worst weakness |
|---|---|---|---|---|
| Kera Voss (Kal, smith) | Ashen Hollow | 53 | **strong** | duplicated choice; wings node explains rather than jokes |
| Renn Vale (Veth, steward) | Ashen Hollow | 50 | **strong** | a few flat acks; the sign-the-book beat is buried at the end |
| Sel Arien (Siann, archivist) | Ashen Hollow | 53 | **strong** | `strain` and `foldscar` are the longest explanations in the game |
| Tavar Orr (Orenth, guide) | Ashen Hollow | 27 | **good** | his gallows humour is present but never aimed at anyone else |
| Berrin Garth (Veth, lodge) | Cairnwold | 36 | **strong** | place-lore runs long; the Veth joke is told, not made |
| Durn Skell (Kal, smith) | Cairnwold | 43 | **strong** | duplicated choice; his terseness is used up before the humour lands |
| Mara Ling (Veth, well) | Cairnwold | 19 | **good** | thinnest set in the game — 19 entries for the quest that opens the bog |
| Tam Crowden (Siann, granary) | Cairnwold | 44 | **good** | counting-jokes repeat; no fear of the moor in her voice |

---

## 2. What is working, and should not be touched

Quoted from the files as they stand:

- `kera_voss.greet` — *"Ha - mind the anvil, it bites. Kera. Come here, let me look at you." She takes your hand and turns it over like a horseshoe she's thinking of buying.* — Establishes people, trade and character in one stage direction.
- `kera_voss.sword` — *"That? That's not a sword, that's a warning. Somebody let it sleep wet for a year."* — Funny, and it *teaches* what a bad weapon is.
- `kera_voss.praised` — *"Ha! Straight. Point holds. Socket's snug. That's made well."* — The reward for crafting is a person, not a number.
- `durn_skell.kera` — *"Kera talks. I hit things."* Something happens at the corner of his mouth, and is gone. — The single best cross-character joke in the build.
- `berrin_garth.granary` — *A mouse runs up the leg, meets the cap, hangs there upside down...* — Understandable engineering as humour; the staddle explains itself.
- `berrin_garth.personal` — *"Nothing keeps me - I keep it. That's a Veth joke; it's funnier if you're Veth."* — Exactly the register the owner's bar asks for, and it names its own people.
- `sel_arien.again` — *"You again. Good. I've been talking to the stones, and they make poor witnesses."*
- `sel_arien.boneleaf_paid` — *"I'll write it down, and nobody will read it, and it will still be true."* — Her whole people in a sentence.
- `mara_ling.greet` — *"Mind the rope, it's wet through - which, if you're wondering, is the only thing round here that is."*
- `tavar_orr.caught` — *"Three days, Sel will say. For me it was one long afternoon where everything arrived a breath late."* — The Foldscar explained as experience, not as lore.

---

## 3. Weak lines and rewrites

### 3.1 The two duplicated choices (defect, not taste)

A player sees two identical options. Both files have two choices with the same label, gated on different conditions, so the doubling is invisible in the file and glaring in the game.

| file:node | current (appears twice) | why | rewrite |
|---|---|---|---|
| `kera_voss.again` | `I made a spear. Look.` | identical text, two conditions | keep one as `I made a spear. Look.` and relabel the other by state: `Something of mine to show you.` |
| `durn_skell.again` | `I made a spear. Look at it.` | identical text, two conditions | keep one, and make the second `A second one. Still straight.` |

### 3.2 The flat acknowledgements — the most-read lines in the game

`Thanks.`, `Thank you.`, `I will.`, `It will.`, `Nothing today.`, `Another time.`, `Later.`, `Not today.`, `Not now.`, `Practical.` These appear at nearly every node close. They are the player's *own* voice and currently do nothing with it. Rewrite per NPC so even a goodbye is somebody's:

| node | current | rewrite |
|---|---|---|
| `kera_voss.praised` | `Thank you.` | `Don't thank me. Bring me the next one.` |
| `kera_voss.ore_paid` | `Thanks, Kera.` | `Coin's on the bench. I'll not count it.` (already her line — keep the *voice* in the option) |
| `renn_vale.place` | `Thanks.` | `Bed, meal, roof. I'll take the second one now.` |
| `renn_vale.signed` | `Thank you, Renn.` | `Sign me in before I change my mind.` |
| `sel_arien.notes` | `Thank you. I'll be careful.` | `I'll be careful. And I'll read it twice.` |
| `sel_arien.strain` | `I'll remember.` | `So it spends me instead of refusing. I'll remember.` |
| `durn_skell.praised` | `Thank you.` | `Hn.` (one letter of a word — his voice exactly) |
| `durn_skell.where` | `I will find it.` | `West end. Black in the face. I'll find it.` |
| `berrin_garth.walls` | `I had not thought of it that way.` | `A kiln, a lime-man and a free summer. No wonder it's stone.` |
| `berrin_garth.older` | `Thank you.` | `A line, not a scatter. I'll look at the crags again.` |
| `mara_ling.told` | `I will.` | `Grey root, grey stem, grey leaf. I'll not wash it.` |
| `mara_ling.greet` | `Not today.` | `Not today. The rope can drip a while longer.` |
| `tavar_orr.again` | (no close) | add `Still here. Still late.` — mirroring his own joke back at him |

### 3.3 The wings node: a joke that is currently an explanation

`kera_voss.wings` is the only place in the first hour where the canon fact *Kal have large dorsal wings but are badly built for sustained flight* is allowed near a player. It explains, defensively:

> *"They fold. They're not for flying - not in this air, and not with me in them."*

The canon (`RACES.md` §2) is much funnier than that: wings *absurdly large for so compact a body*, useful for **braking, jump assistance, short gaps**, and "unrestricted flight is not the baseline racial capability." A Kal smith would not apologise for that; she would take pride in exactly the parts that work.

**Proposed replacement for `kera_voss.wings`:**

> Something behind her shoulders shifts under the harness, like a sail deciding against it. *"Careful. They're load-bearing."* She flexes one, and the anvil dust lifts off the floor and settles again. *"Not for flying. For landing - there's a difference, and the difference is who walks away."* A beat. *"I could fly. I'd arrive. Not in the shape you'd want to be seen in."*

### 3.4 The three longest explanations

Understandable is on the bar, and these three pass *meaning* but fail *pace*: each is four or more sentences of system detail before the player gets a word.

| node | problem | rewrite move |
|---|---|---|
| `kera_voss.lesson` | ore → ingot → spear in one breath | cut to the one concrete image: the seam *"black, like somebody spilled the night on it"*, then one line: *"Break me some. Ore in that pot, spear out of this one."* |
| `sel_arien.foldscar` | three stones, absence, guesses, Record | lead with the sensation (*"cold on the wrong side"*), keep the surveying justification for the `personal` node where it answers "why do it at all" |
| `berrin_garth.walls` | lime → kiln → man → summer, four links | keep the chain but land it in one clause: *"Mortar wants a man with a free summer, and Cairnwold has never had one."* |

---

## 4. The gap the bar is really about: nobody jokes about a people

The owner's exemplar is **Kal flight jokes**. Canon gives us four peoples with usable edges:

- **Veth** — hospitality, belonging, "a bed, a meal and a roof", the guest book
- **Kal** — dense, deliberate, **winged and bad at flying**, think in materials, become stone in a transition
- **Siann** — record-keeping, precision, the Survey, "I'll write it down and nobody will read it"
- **Orenth** — the ledger of crossings, gallows humour, "everything arrived a breath late"

Today only two lines cross that gap: `durn_skell.kera` (*"Kera talks. I hit things."*) and `berrin_garth.personal` (*"That's a Veth joke; it's funnier if you're Veth."*). Proposed additions, each one line, placed at an existing node so no structure changes:

| speaker | node | proposed line | the view it carries |
|---|---|---|---|
| Kera (Kal) | `wings` | *"They ask me if I fly. I ask them if they walk everywhere on purpose."* | Kal on Veth assumptions |
| Durn (Kal) | `kera` | *"She'll tell you about the wings. Don't stand under her."* | Kal self-deprecation, deadpan |
| Berrin (Veth) | `personal` | *"A Kal came through with a broken wing and stayed for the winter. Best smith we ever had. Worst flier. He'd tell you that himself."* | Veth warmth + the joke |
| Sel (Siann) | `again` | *"A Kal's wings are a ratio problem, not a mystery. They have more weight than surface, and they've never once admitted it."* | Siann precision applied to feelings |
| Tavar (Orenth) | `caught` | *"A Kal landed on me once. He called it a landing. I called it the end of my afternoon."* | Orenth gallows |
| Tam (Siann) | `again` | *"I count what comes off the moor. Kal count what the moor is made of. We're both counting, and neither of us is getting anywhere."* | Two peoples, one joke |
| Renn (Veth) | `again` | *"Everyone who sits there thinks they're the first to be lost. You're not: there's a Kal in the book who asked me for directions back to a mountain that isn't there."* | Veth hospitality with a knife of kindness |
| Mara (Veth) | `again` | *"A Kal smith told me the water here was 'structurally honest'. I've been trying to work out whether that was a compliment for thirty years."* | Veth stubbornness vs Kal materials-speak |

---

## 5. Scope: the first hour is missing three of its eleven people, and the ford has nobody

This is the largest finding and it is not about prose.

- **Eight speaking NPCs exist** — four in Ashen Hollow, four in Cairnwold. The owner's target is **the Steps' eleven people**; three are absent, and I will not invent the missing names here.
- **The ford has no speaking NPC at all.** Cairnwold's jetty and the water crossing are geometry and a `SurfacePatch`; nothing there talks. Given that S2's water masking and the bog quest (`What The Bog Kept`) both live at the water, a ford voice is the cheapest new character in the build.
- **Mara Ling is the thin set** (19 entries) for the quest that opens the moor. Her complaint is excellent; her *responses* run out fast.

Recommendation for the story gate: name the three missing people before the pass is closed, because the three remaining gaps are all *between* existing characters (a Kal/ford/Kal triangle is what the humour table above is missing).

---

## 6. What I did not judge

- **Chatter, hints and calls** (`content/chatter/`, `content/hints/`, `content/calls/`) — they are the ambient first-hour text and were outside the brief's named places; they are the natural next pass, and `chatter/_banned.yaml` suggests a register already exists to enforce.
- **Quests' own text** (`content/quests/`) — journal copy was not in scope.
- **Delivery** — voice acting, pacing and node *order* are untouched; this pass judges text only.

---

## 7. The three changes I would make first

1. **De-duplicate the two choices** — it is a defect a tester will hit in the first ten minutes (both smiths, both at the "I made a spear" node).
2. **Rewrite the twelve acknowledgement choices** in each speaker's voice — highest read frequency in the game, lowest cost (twelve strings, no structure).
3. **Replace `kera_voss.wings`** with the landing-not-flying version — it is the one place the owner's own exemplar (Kal flight jokes) can be cashed, and it currently apologises for canon instead of enjoying it.
