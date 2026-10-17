# Chatter pipeline (WP16 corpus)

The tooling that writes and checks the AI-generated ambient chat corpus in `content/chatter/`. Everything runs offline:
no model is called at runtime, and nothing here is loaded by the game. The game reads only the YAML these scripts
produce, and the WP16 lint in `src/Content/ChatterContent.cs` decides whether it loads.

## What is here

| Path | What it does |
|---|---|
| `prompts/voice.md` | The register every line is written to. Versioned; a change to it is a new corpus generation. |
| `prompts/facts.md` | The facts a line may stand on, taken from `content/`. The world is not allowed to grow from the corpus. |
| `prompts/canon.md` | The world's own facts - calendar, peoples, waystones, the Foldscar, Calls, factions, fold bleed - each with its citation, plus what no line may ever settle. |
| `prompts/canon_source.md` | The extraction the canon sheet was distilled from: 195 quoted facts with `file:line`, the protected-mystery statements, and the glossary candidates. The audit trail, not a prompt. |
| `prompts/examples.md` | Register examples used in the cell prompts. |
| `prompts/cell_prompt.md` | The per-cell prompt template: a place, a time band, the trades, the canon quota, the weather mix, the channel. |
| `make_prompts.py` | Builds the plan (cells, trades, canon quota, weather mix, event block) and writes one prompt per cell, plus the batch's `prompt_hash`. |
| `check_batch.py` | Checks one batch's source file without writing anything: rules, near-duplicates, keys, coverage, canon share. |
| `check_all.py` | The same across every batch at once, including near-duplicates **between** batches - the collisions a batch checked alone cannot see. |
| `assemble.py` | Validates a batch's source file and writes `content/chatter/<batch>.yaml` and its `_provenance.yaml` entry. |
| `lint.py` | Lints the corpus as it stands in `content/chatter`: the same rules, on what the game will actually load. |
| `sample.py` | Draws the review sample (5% of every batch) into `content/chatter/_review_sample.md`. |
| `report.py` | Line counts by cell and by tag, for a batch or for everything. |
| `chatter_rules.py` | The rules the checkers share: a deliberate copy of the C# lint's content checks. |
| `batches/<batch>.yaml` | The authored lines of a batch, with the keys assigned once and never moved. |

## The loop

```
python tools/chatter_pipeline/make_prompts.py b0002     # plan + one prompt per cell + prompt_hash
                                                        # (write lines into batches/b0002.yaml against those prompts)
python tools/chatter_pipeline/check_batch.py b0002      # rules, near-duplicates, coverage, canon share
python tools/chatter_pipeline/assemble.py b0002         # validate, write content/chatter/b0002.yaml + provenance
python tools/chatter_pipeline/check_all.py              # every batch together, duplicates across batches included
dotnet test tests/Content.Tests/Content.Tests.csproj --filter "FullyQualifiedName~ChatterContent"   # the gate
python tools/chatter_pipeline/sample.py                 # the review sample
```

`make_prompts.py` writes its prompts and plan to `D:\Otherreach_Scratch\chatter\<batch>\` (`D:` is the scratch drive
for this project; nothing large belongs in the worktree).

## Keys never move

A line's key is assigned once, in `batches/<batch>.yaml`, and is written from there. A line whose text is replaced
keeps its key. A line withdrawn gets `retired: true` and its slot is not reused. `chat.<batch>.<nnnn>` therefore names
one line for good, which is what makes a review note ("regenerate chat.b0001.0142") actionable.

## The rules the lint applies

`chatter_rules.py` mirrors the C# lint's content checks (CHT003-CHT011) so a line can be checked while it is written.
The C# lint is the gate: it also checks the provenance records, the glossary references (CHT012), the template uses and
the schema's own consistency, none of which this copy reproduces. When the two disagree, the C# lint is right.

Coverage is a report, not an error: for each speaker x place x time-band cell, how many lines that ask for no event can
be said there. A cell under `limits.coverage_min_lines` (3) is thin. The C# test asserts the corpus leaves no cell thin,
so every batch is written with three lines per cell that carry no trade tag.
