# OTHERREACH — spec 41 lore system: what is built

**Branch:** `playtest/ds-lore` · **Status:** live in the frontier build · **Evidence:** every line below is checkable from this repository or from
`C:\Otherreach_Build\ds_lore_frames`. Numbers are from the tree at the time of writing, not from memory.

## 1. The system

| Piece | Where | What it does |
|---|---|---|
| Entries, pages, sources | `content/lore/*.yaml` | 85 entries, 190 pages. Each page names how it is found: `visited` a place, `inspected` a thing written, `told` by a person, `quest` on finishing a job, `studied` a beast. |
| Domain | `src/Domain/Knowledge/Lore.cs` | The journal's headings, the page record, the word limits, the trade-word ban, and the two reading rules (`Plate`, `Latest`). |
| Content + lint | `src/Content/LoreContent.cs` | Builds the entries and refuses a page that is unreachable, mis-numbered, off-length, leaky, or names something the pack does not have. |
| Runtime | `src/World/Runtime/Lore.cs` | Files a page when its source fires; five sources are wired to the world's own events. |
| Journal tab | `src/Presentation/Ui/JournalPanel.cs` | Lore under the journal's headings, with page counts and unread marks; the tab is opened on the page a discovery offered. |
| Reading panel | `src/Presentation/Ui/ReadingPanel.cs` | A book or inscription read in place, page by page. |
| Target plate | `src/Presentation/Main.Hud.cs` | One tactic per beast, from its entry's authored `plate:` line, and nothing when no page of it is known. |
| Map notes | `src/Presentation/Ui/MapPanel.cs` | A place's entry and how much of it is known, under its pin. |
| Rest snippet | `src/Presentation/Main.Inn.cs` | On waking, the newest page the character found, in its own words. |
| Loading snippet | `src/Presentation/Main.cs`, `Ui/LoadingScreen.cs` | The newest page, under the bar, once the content pack exists. |
| Save | schema 35 | Pages survive a save and a load (`src/Persistence/Sections/SchemaV34.cs`, migration `SchemaV34ToV35`). |

## 2. Delivery surfaces, and where they were photographed

| Surface | Frame |
|---|---|
| Landmark banner on first arrival, with the *Read* prompt | `lore_landmark_banner.png` |
| A person's telling: Sel Arien on the ember hounds, page filed by the reply | `lore_npc_told.png` |
| A book read where it stands, page by page | `lore_book.png` |
| The journal's Lore tab: headings, page counts, not-yet-found pages | `lore_journal.png` |
| The map's note under a place's pin | `lore_map.png` |
| The target plate speaking a beast's tactic | `lore_bestiary.png` |

Books and inscriptions (78 placed), tellings (74 wired across six voices), quest epilogues (3, opening themselves), the rest and loading
snippets, and the bestiary are all live. Item flavour is carried by the material entries.

## 3. Canon discipline

- `content/lore/_canon_index.yaml` indexes **111 concepts** mined from MYTHOLOGY, COSMOLOGY, RACES, WORLD_ARCHITECTURE, LINEAGE and every
  content file's notes: **85 covered, 26 missing, 0 unclaimed**. `dotnet run --project src/Content -- lore-coverage -c content` prints it and
  fails on an index that lies (a "covered" concept must name an entry that exists; every entry must be claimed).
- `LORE_OWNER_QUESTIONS.md` holds the gaps canon leaves open (20 in the index, 6 remaining questions in the ledger) and, separately, the
  questions the writing has already answered.
- Pages are written only from canon. The three undecided names in COSMOLOGY (`otherhome`, `otherhome_marches`, `otherfall`) are **not**
  written, on purpose.
- Contradiction is authored where canon holds two tellings (13 contradicting third pages and 3 corrections), and a later page says plainly
  where it parts from an earlier one.

## 4. Tests

`dotnet test tests/Content.Tests --filter "FullyQualifiedName~Lore"` — **16 passing**:

| File | What it pins |
|---|---|
| `LoreCoverageTests` | Every lore reference resolves; every placed readable names an entry that exists and declares that readable as an inspected source at that key; the canon index agrees with the entries. |
| `LoreLeakTests` | The trade's words never reach a reader: refused in pages, in the lines of a telling, in a readable's name and verb — and the shipped lore reads clean. |
| `LoreReadingTests` | The plate and the night reading say the page's own words, and nothing when nothing is known. |
| `LorePlateTests` | Every beast that can be studied has a plate line of ninety characters or fewer, leak-free, and a beast nobody studied says nothing. |

## 5. What is not built, and why

| Item | State | Why |
|---|---|---|
| Lore unlocking **runes** (spec 47) | **Blocked** | Spec 47's enhancement system has no runes in this build. There is nothing for a page to unlock, and inventing a rune system inside the lore pass would invent a mechanic and world facts at once. |
| Lore unlocking **hidden paths** | **Blocked** | No reveal mechanic exists: a location appears on the map by being walked into. A page-driven reveal needs its own persisted state (a schema change) and a decision about whether a *told* place counts as visited for objectives. |
| 26 canon concepts | **Open** | Undecided in canon (`otherhome`, `otherhome_marches`, `otherfall`), already carried by an existing entry (`old_corra`, `the_measure`, `order_of_the_lantern`), or thin enough that two honest pages would require invention. |
| Purchased maps, cartography skill, tracking layers | **Not built** | See `docs/MAPS_CARTOGRAPHY_AND_WORLD_KNOWLEDGE.md` §13, which now records each principle's state. |
