# Chatter pipeline (WP16 corpus)

The tooling that writes and checks the AI-generated ambient chat corpus in `content/chatter/`. Everything runs offline:
no model is called at runtime, and nothing here is loaded by the game. The game reads only the YAML these scripts
produce, and the WP16 lint in `src/Content/ChatterContent.cs` decides whether it loads.

## What is here

| Path | What it does |
|---|---|
| `prompts/voice.md` | The register every line is written to. Versioned; a change to it is a new corpus generation. |
| `prompts/facts.md` | The facts a line may stand on, taken from `content/`. The world is not allowed to grow from the corpus. |
| `prompts/examples.md` | Register examples used in the cell prompts. |
| `prompts/cell_prompt.md` | The per-cell prompt template: a place, a time band, the trades, the weather mix, the channel. |
| `make_prompts.py` | Builds the plan (cells, trade rotation, weather mix, event block) and writes one prompt per cell, plus the batch's `prompt_hash`. |
| `assemble.py` | Validates a batch's source file and writes `content/chatter/<batch>.yaml` and its `_provenance.yaml` entry. |
| `lint.py` | The fast lint: key format, tags, slots, length, raw ids, proper nouns, banned terms, near-duplicates, coverage. |
| `sample.py` | Draws the review sample (5%, at least 100 lines) into `content/chatter/_review_sample.md`. |
| `report.py` | Line counts by cell and by tag, for a batch or for everything. |
| `chatter_rules.py` | The rules `lint.py` and `assemble.py` share: a deliberate copy of the C# lint's content checks. |
| `batches/<batch>.yaml` | The authored lines of a batch, with the keys assigned once and never moved. |

## The loop

```
python tools/chatter_pipeline/make_prompts.py b0002     # plan + one prompt per cell + prompt_hash
                                                        # (write lines into batches/b0002.yaml against those prompts)
python tools/chatter_pipeline/assemble.py b0002         # validate, write content/chatter/b0002.yaml + provenance
python tools/chatter_pipeline/lint.py b0002             # fast lint and coverage
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
