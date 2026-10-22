# RC d6 companion inspector — source candidate

Companions use their existing PartySheet gear and belongings. Click a party-frame name or open a selected companion from Party to see both packs, the companion equipment, level, health, armour, actual attack damage, attributes, orders and skills. Whole-stack buttons transfer items; equipment swaps put displaced pieces into the companion pack. Commands enforce reach and capacity before moving an item. The world keeps running.

Explicit dismissal returns no items automatically. Its confirmation lists retained gear and packed items, with Confirm, Cancel, and Take items back. The warning stays until acted on. Named companions retain their existing saved sheet; procedural adventurers take complete personal item records back into their life, separately from abstract trade goods, and restore those instances when rehired.

Schema 37 adds complete kept-item snapshots to adventurers (identity, count, quality, notable history, wear, flags and worn-loan ownership). Frozen schema 36 entity/adventurer types and a new writer-generated v37 fixture accompany the migration. Previous fixture save payloads are unchanged.

The same panel also exposes current pet health, level, bond, role, training and feeding through the existing pet commands. Pet collar/saddlebag APIs are not present in the pinned DS source and remain pending with DS. The feeding command needs DS's consumption/reach/cadence fix before release; the inspector must not be taken as mechanical pet acceptance.

CPU evidence: full Persistence 274 passed; companion suite plus real dismiss/save/reload/rehire 15 passed; final capacity/gear/save route recheck 6 passed; adventurer domain 14 passed; away/party words 30 passed. Full Presentation 399 passed; Architecture 23 passed. Presentation build: zero errors. Logs under D:/Otherreach_Scratch/rcd6_20261021.

#37 follow-up: a successful hunt now carries its actual drawn loot into a transient returned event before market selling. An injury reports a wounded return. AwayWords names the actor and ground, combines repeated loot draws, and keeps old records honest when details are unavailable. This adds no persisted away-log fields.

Visual acceptance remains pending: all GPU gates paused by Claude until DESKTOP BACK. Still required: 720/1080 companion and pet panels, reach/full-pack refusals, equipped swaps, dismissal warning, and normal-play rehire. No owner package or shortcut promoted.
