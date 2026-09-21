# UNNAMED - Companion Access Matrix

Which peoples will **travel with** which, and what that blocks.

Companion to `ENDGAME_QUESTLINES.md`, which defines the access tiers. This document
fills in the constraint that makes those tiers bite: a player can only adopt a
companion who will actually join them, and not every people will travel with every
other.

This is the mechanism that limits how many secrets one character can reach. It is
deliberate, and it is the primary driver of replay.

---

## The rule this encodes

> A player character can pursue a racial secret if they can **field a companion of that
> race**, or reach the secret through a record that race left behind.

Two consequences follow, and both are intended:

1. **Your race chooses your routes.** Not by fiat, but by who will walk with you.
2. **What a people refuses is as informative as what it permits.** The Mor will not
   travel with the living. The Kal will not travel with anyone who has worked an Ondrek
   quarry. These refusals are characterisation, not obstacles.

---

## Willingness to travel

Read as: *will a member of the row people join a party led by a member of the column
people?*

| ↓ joins a party led by → | Veth | Kal | Siann | Orenth | Mor | Constr. | Vaskaal | Ondrek |
|---|---|---|---|---|---|---|---|---|
| **Veth** | yes | yes | yes | yes | **never** | yes | yes | **no** |
| **Kal** | yes | yes | yes | no | **never** | **no** | yes | **never** |
| **Siann** | yes | yes | yes | yes | **no** | yes | no | maybe |
| **Orenth** | yes | no | yes | yes | **no** | no | yes | maybe |
| **Mor** | **never** | **never** | **never** | **never** | **never** | **never** | maybe | **never** |
| **Constructed** | yes | yes | yes | yes | yes | yes | yes | yes |
| **Vaskaal** | yes | yes | no | yes | no | yes | yes | maybe |
| **Ondrek** | yes | **never** | yes | yes | no | yes | yes | yes |

**never** = will not travel under any circumstances.
**no** = will not travel, but a specific individual can be persuaded by the chain.
**maybe** = depends on the individual; the game supplies at least one who will.
**yes** = ordinary recruitment.

Three things this produces that are worth noting:

**The Constructed row is all yes.** They are the only people who will travel with
anyone, including the Mor, including the Ondrek. They are also the only people who
understand what everyone else is. Their openness is not warmth — it is that they regard
all eight as the same kind of thing, and have documentation.

**The Mor row is almost entirely never.** A Mor cannot recruit. It can only be
recruited, by a Constructed, or in specific cases by a Vaskaal or an Ondrek. This is
why the Mor are the hardest chain in the game and why encountering one at all should
feel significant.

**The Kal/Ondrek cell is a war, in both directions.** Neither will travel with the
other, which means the only route to either secret for an outsider is by committing to
one side of the Stone Question. The chain's faction choice is therefore also an access
choice, and it is permanent.

---

## What each race can reach

Given the matrix, a character can field companions from certain peoples and not others.
Combined with the chains in `ENDGAME_QUESTLINES.md`, this is the reachable set.

"Direct" means a companion of that race can be recruited. "Via record" means the
account is held by someone the character can recruit, so the chain is still reachable
in partial form.

| Playing as | Direct | Via record | Total reachable | Shut out of |
|---|---|---|---|---|
| **Veth** | 6 | +2 | **8 of 8** | — |
| **Constructed** | 8 | +0 | **8 of 8** | — |
| **Ondrek** | 6 | +2 | **8 of 8** | — |
| **Orenth** | 4 | +3 | 7 | Mor |
| **Kal** | 4 | +2 | 6 | Mor, Orenth |
| **Siann** | 5 | +1 | 6 | Ondrek, Vaskaal |
| **Vaskaal** | 5 | +1 | 6 | Ondrek, Siann |
| **Mor** | 0 | 0 | **1** | everything but their own |

Three things this produces that are worth noting:

**The Constructed row is all yes.** They are the only people who will travel with
anyone, including the Mor, including the Ondrek. They are also the only people who
understand what everyone else is. Their openness is not warmth — it is that they regard
all eight as the same kind of thing, and have documentation.

**The Mor row is entirely never,** and the consequence is total. A Mor can recruit
nobody, so a Mor character's reach is **one secret: their own, which they reach as a
native.** They are the only race that gets a native ending and nothing else. Their
record would be held by the Constructed, whom they cannot recruit — so even the partial
route is closed.

That is the most extreme reach in the game by a wide margin, and it is thematically
exact: a Mor is a being that should not persist, hunting an answer that will indict
their own people, and cannot ask anyone for help. It is also the clearest case in the
game for the fourth open question below.

**The Kal and Ondrek exclusion is mutual and absolute,** so no character can hold both
secrets. Pursuing either means permanently closing the other, and since the Ondrek
record is held by the Siann, a Kal *can* read the Ondrek account — secondhand, through
the people who were polite enough to write it down. That is a good scene waiting to
happen and it should be written as one.

**The Constructed reaches everything.** Counterweight: they can never be a native of
any secret but their own, so they always get the outsider ending. Breadth traded for
depth. A Mor player is the exact mirror trade.

---

## Nothing is unreachable — records

The matrix would leave some combinations with no route to certain secrets. That is
acceptable only because every secret has a **record route**: an account the people left
behind, preserved by whoever kept it.

Records are held by the people who keep things, which follows from `MYTHOLOGY.md`:

| Race | Their record sits with | Because |
|---|---|---|
| Kal | **Siann** archives | the Siann keep accurate testimony, and the Kal trust them |
| Siann | **Vaskaal** ships' logs | the Vaskaal observed from outside and had no reason to lie |
| Mor | **Constructed** documentation | the Constructed copied everything, including things that no longer exist |
| Constructed | **Vaskaal** | the Vaskaal were outside when the Written Pacha was written |
| Orenth | **Mor** memory-carriers | only a Mor can carry a memory across a transition intact |
| Vaskaal | **Siann** | the Siann interviewed them first, and wrote it down |
| Ondrek | **Siann** | the Ondrek have no archives; the Siann kept what the Ondrek said |
| Veth | **Kal** stone | the Veth recorded their own history in whatever would outlast them |

This produces a routing rule with real texture: **the people who hold the record are
usually the people who get along with its subject.** A Kal chain is open to anyone who
can reach a Siann archive. A Mor chain requires a Constructed document collection,
which is why the Mor chain is hard even for a Constructed and near-impossible for most.

Records cost more than a companion: they are partial, they omit the gated finale, and
the native ending is unavailable through them. But they mean **no player is ever
permanently locked out of information**, which keeps the solo-first pillar intact.

---

## Replay, and why this is the right constraint

The charter asks the world not to revolve around the player and to allow meaningful
failure. This system delivers both by making **the player's reach a function of who
they are**, and their identity a choice made at character creation that they cannot
undo.

A Veth can finish six chains and is permanently shut out of two. To see the Mor's
accusation from the inside — to be the one who tells their people what they did — you
must roll a Mor, and then accept that you will reach almost nothing else.

That is exactly what you described: **if you want that questline, you need another
character.** The constraint is legible, it is justified by the setting rather than by
budget, and it means a second playthrough is not a replay of the same content but a
different position in the same world.

---

## Open questions

1. **Is one companion enough for every gated moment in a chain?** If a chain needs a
   Kal *and* a Mor to read its evidence, then only a Constructed can complete it — which
   may be intended, or may be too tight.
2. **Can a companion be earned across a refusal?** A Kal who would never travel with an
   Orenth might make an exception for one specific Orenth who did something. Worth
   having a handful of these, so the matrix is a default rather than a wall.
3. **Does the Mor player get a companion at all?** As written, a Mor leads no party and
   the Mor chain is played alone. That is thematically right and mechanically severe.
   The alternative is one Constructed who will follow a Mor, which softens it at the
   cost of the isolation.
4. **Should refusals be visible before character creation?** A player who picks Kal
   without knowing it costs them four chains may feel cheated. Showing the reach at
   creation turns a trap into a decision.
