# Canon: the world this valley is in

Version 1 (2026-10-18). The facts a chatter line may stand on, each with where it comes from. `facts.md` covers the two
areas and their trades; this covers the world underneath them - the calendar, the peoples, the Other, the Foldscar, the
stones, Calls, the factions.

A line may use these facts. A line may **not** use a fact that is not here, and may not settle anything in the last
section. Everything here was checked against the source line given with it.

## The calendar

- A **tally** is 14 days. `content/config/time.yaml:14`
- A **Reckoning** is 13 numbered tallies of 14 days, and then the **Hollow Day**. `content/config/time.yaml:14-15`
- The **Hollow Day** is the day at a Reckoning's end, after the thirteenth tally, and it belongs to no tally.
  `content/config/time.yaml:16`
- So a Reckoning is 183 days. `content/config/time.yaml:12` (`offline_cap_game_days: 183`, "one Reckoning")
- The world's clock starts at 7.5h, early morning, and the sky and the creatures' hours both read it.
  `content/config/time.yaml:11`

Folk say: the tally turning over, the ninth tally, Hollow Day coming, "a Reckoning's worth of" something. Nobody says
a month or a weekday, because there are none.

## The Other, and what is unsettled about it

- The **Other** is the centre of the setting and is deliberately not one simple alternate dimension: it may be a layer
  outside ordinary reality, the medium between worlds, the space between places or times, or several phenomena
  civilisations wrongly group under one name. `direction/extensions/OTHERREACH_COSMOLOGY.md:32-42`
- **A major setting principle: no culture possesses the complete explanation.** A scholar describes dimensional
  topology, a priest a divine boundary, a druid hidden roots, and "an ordinary traveller may simply know that some
  roads are wrong" - and these interpretations coexist. `OTHERREACH_COSMOLOGY.md:44-52`
- The **Veil** is the perceived boundary between ordinary reality and the Other. It need not be a literal membrane; it
  may be thin in places, weakened by ancient events, affected by magic, or deliberately pierced or stabilised.
  `OTHERREACH_COSMOLOGY.md:56-69`
- An **Otherfold** is a distortion in normal space-time: it may connect distant places or eras, shorten distance,
  produce impossible interior geometry, or collapse. `OTHERREACH_COSMOLOGY.md:73-88`
- An **Othermarked** person was changed by later exposure to the Other rather than born to it; the marks may be
  visible, and may carry social or religious stigma. `OTHERREACH_COSMOLOGY.md:236-253`
- **There is no settled canon for lights or objects in the sky.** The cosmology vocabulary (Othertide as a rising and
  falling of influence, Otherwake as a disturbance left behind, Otherstorm as a violent local outbreak) is marked in
  canon as candidate/proposed vocabulary, not settled. `OTHERREACH_COSMOLOGY.md:360-410` Use it sparingly, always as
  what people *call* something, and never as a mechanism. Do not invent a moon, a comet, a constellation or an aurora.

## The eight peoples

Say them the way an ordinary person names a neighbour's people, not the way a book names a category.

- **Veth** - the touched lineage: their ancestors survived a Pachakuti, so reality-adjacent blood makes them learn
  faster than they should, then stall at a ceiling. The cultural majority, the least examined. Physical tells are
  subtle: long fingers and toes, unusually mobile joints, reflective irises. `RACES.md:114-150`
- **Kal** - the calcified: broad, dense, built for cold and work, thinking in materials and structure, work that
  outlasts them by centuries, and a pair of large folding dorsal wings worn under cloak or pack.
  `RACES.md:157-173` Their tragedy is Pacha transitions: where a Veth is *changed* by a Pachakuti, a Kal becomes
  stone. `RACES.md:199-201`
- **Siann** - the long view: they stop aging in their late teens, so a Siann of twenty and one of four hundred look
  the same, which makes them easy to underestimate and hard to trust; their minds work at the pace of careers.
  `RACES.md:213-225`
- **Orenth** - the walkers: the only people who cross between Pachas deliberately, not travelling through the space
  between realities but stepping sideways into an adjacent one. It is lethal work and each crossing costs them
  something - memory, years. `RACES.md:266-278`
- **Mor** - the stolen: natives of a Pacha that no longer exists, persisting as something like a person without a
  body, no fixed form, no weight, no reflection they can rely on. Every Mor is a guest here. `RACES.md:316-330`
- **Constructed** - the written: people from a Pacha that was **built rather than born**, engineered by a
  civilisation and still running after its authors were gone. Not machine intelligences. `RACES.md:354-368`
- **Vaskaal** - the void-farers: they came **around** Pachas, through the space that contains them, so to this world
  they are simply aliens with unmatched technology. `RACES.md:408-420`
- **Ondrek** - the read-stone: thinking stone, the only people who read raw material as memory, defined too by the
  fact that they are quietly ending. `RACES.md:469-478`

### The quarry argument (a real disagreement, unsettled in-world)

- The Ondrek say they are what the Kal become; the Kal say that is a lie told to justify a quarry. Both positions are
  genuinely contested in-world and scholars are found on both sides. `direction/extensions/STONE_QUESTION.md:16-20`
- The Kal regard the quarries as graves and their study as desecration; a Kal who conceded that some stone was never
  Kal would concede that some of the dead are not there, which is a religious crisis. `STONE_QUESTION.md:51-55`
- The Ondrek are courteous, patient and profoundly sad; they quarry because they are ending - they do not reproduce,
  each generation of new read-stone is rarer, and stone is the only medium that has carried memory.
  `STONE_QUESTION.md:22-39`
- Everyone else buys the result: stone-crafted goods are the finest made, the Ondrek trade generously, and most
  settlements would rather not examine where the stone came from. `STONE_QUESTION.md:59-63`

Folk say: don't ask what the quarry stone was before; the Kal won't have it in the house; the good stone comes from
people who don't answer letters. **Nothing here resolves whether the Ondrek's reading is true.**

## Fold bleed: the strangeness that has grown into the ground

The survey's word for it is **fold bleed** - things near the Foldscar that are not from here. Ordinary people use the
word without being able to define it, and that is the correct level of understanding for a chat line.

- The two great grazers, **Barrowback** and **Fellhorn**, are fold bleed: met near the Foldscar and nowhere else.
  `content/creatures/grazer/barrowback.yaml:7` (owner ruling 2026-10-06), `content/creatures/grazer/fellhorn.yaml:7`
- **Boneleaf** grows only round the Foldscar: "A pale, stiff leaf ... Its veins run the wrong way."
  `content/items/material/boneleaf.yaml:7`
- **Listening grass** is moss that "bends toward sound rather than light".
  `direction/extensions/WORLD_MATERIALS.md:240`
- A crossing leaves an **Otherwake**, "the residue a crossing leaves, like a ship's wake".
  `direction/extensions/WORLD_MATERIALS.md:201`
- The **record** is ordinary here: the Veth keep births, burials and who owes who a ewe; the Siann keep testimony, and
  their Record "begins too late - something is missing from the front of it". `direction/extensions/MYTHOLOGY.md:166-170`

Folk say: nothing grows but boneleaf on that side; the grazers don't come down off the fold ground; the book has it,
ask the book. **No line may say what the bleed is, where it came from, or why it started.**

## The Foldscar, as people meet it

- The Foldscar is where this place visibly stops being ordinary frontier country. The weirdness is deliberately
  restrained: slight image doubling, a delayed shadow, displaced audio, inconsistent reflections.
  `docs/PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md:230-237`
- Three **Quiet Stones** stand at it, with a central fold-shaft between them; the stones are aligned to settle it.
  `PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md:448-469`
- Canon's own rule of restraint: "If everything is strange, the Other stops feeling Other."
  `PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md:807`
- **Its cause is not settled and no line may explain it.** People may wonder, guess, disagree, and be wrong.

## Waystones

- The **Ashen Waystone** is where a newcomer begins, and where a downed person comes back: "You return at the Ashen
  Waystone." `PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md:134`, `:500-527`
- The **Waystones** are "an old network of travellers' stones", and the waystation customs go with them.
  `content/calls/wayfarer.yaml:8`
- A Waystone on the Rim is a Rim Stone; the three stones of the Foldscar are the Quiet Stones.
  `content/chatter/_glossary.yaml:25-27`

## Magic, as ordinary people meet it

- Magic is "the ability to make a Pacha behave differently from the way it normally would".
  `direction/extensions/MAGIC_SUPERNATURAL_AND_COSMIC_SYSTEMS.md:8`
- Practitioners work with **resonance** and carry **strain**; high strain brings poor control, injury, collapse.
  `MAGIC_SUPERNATURAL_AND_COSMIC_SYSTEMS.md:12-25`
- The **Vaskaal** cannot cast at all: their biology lacks native resonance, though they can measure, contain and
  engineer around magic. `MAGIC_SUPERNATURAL_AND_COSMIC_SYSTEMS.md:28-32`
- One device may meet several traditions at once - Siann enchantment, Vaskaal control systems, Kal structural
  engineering, Ondrek material knowledge, Constructed patterning. `MAGIC_SUPERNATURAL_AND_COSMIC_SYSTEMS.md:186-196`

## Calls

Four Calls, and nothing else about them: `content/calls/`

- **Hedge healer** - "Someone always needs mending." You know the frontier herbs, wounds and fevers. `hedge_healer.yaml:6-8`
- **Scholar** - "You came to see the stone for yourself." The books disagree about the old stones, so you came to
  see one; you know "the Pacha account and the Other account" for what they are: "two vocabularies for one
  strangeness". `scholar.yaml:6-8`
- **Smith's hand** - "Your master's forge went cold." You know bog iron from grey iron. `smiths_hand.yaml:6-8`
- **Wayfarer** - "The road kept going, so you did." You know the waystation customs and what the Waystones are.
  `wayfarer.yaml:6-8`

Folk may remark on a Call as the shape of a life ("she took the road and never came back to the bench"). Nothing may
say a Call grants power, ranks people, or is chosen by anyone.

## The two factions, as people see them

- **the Wayhold** - the waystation's watch and stewardship; it keeps the shrine lantern filled.
  `content/factions/wayhold.yaml`, `content/locations/lantern_shrine.yaml`
- **the Long Survey** - surveyors and scholars working the old workings and the Foldscar; they want the Quiet Stones
  set right. `content/factions/long_survey.yaml`

An ordinary person's view: the watch is who you ask when the road is bad; the survey is who asks the questions nobody
here wants answered. Neither is villainous, neither is in charge.

## Work, goods and money

- Cairnwold's own iron is running out, so iron is dear there and the road's carters bring it in.
  `content/markets/cairnwold.yaml`
- Ashen Hollow's stall holds what travellers going out to the old workings need - arrows, salves - and the iron the
  smith works; it makes no hides of its own. `content/markets/ashen_hollow.yaml`
- Cairnwold's market is a moorland village's: hides, ash and moor gear. `content/markets/cairnwold.yaml`
- Sell what you make; no line may state a price, a wage or a rate.

## Creatures

- The valley's five bad ones: the **ash ember hound**, the **bone walker husk**, **animated armour**, the
  **bristleback boar**, and the **cave hunting spider**. `PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md:292-318`
- Husks keep the Sunk Barrow on the road. `content/locations/sunk_barrow.yaml`
- Grazers: **Barrowback** and **Fellhorn**; the hen is an **Ashcomb**. `content/creatures/`

## Never, under any circumstance

- Nothing may say or imply that the player's arrival, or any arrival, causes the Leak, the bleeds or the trouble at
  the Foldscar. (Claude's notes, §1; canon leaves the causes open, `OTHERREACH_COSMOLOGY.md:44`)
- Nothing explains what the Foldscar is or why it is, and nothing explains what the Other is.
- What the owner has kept open stays open: the Other's origin, the gate builders, chronology, and the protagonist's
  cosmic status. `owner/OWNER_DIRECTION_2026-10-06.md:27`
- Folk may wonder, guess, disagree and be wrong about all of it. Nobody in the valley is right, and no line is
  allowed to be.
