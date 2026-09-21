# UNNAMED Content Fixtures

Valid content fixtures for testing the content validation tool.

## Directory Structure

Per DATA_MODEL.md section 1.1, these directories map one-to-one with content kinds:

```
content/
  items/          - item.weapon, item.armor, item etc.
  creatures/      - creature
  npcs/           - npc
  spells/         - spell
  abilities/      - ability, skill, class, mastery
  effects/        - status_effect
  recipes/        - recipe
  resources/      - resource
  quests/         - quest
  dialogue/       - dialogue
  dialogue_branches/ - dialogue_branch
  factions/       - faction
  loot_tables/    - loot_table
  config/         - _aliases.yaml, _tags.yaml (meta files)
```

## Content Files

Each file defines ONE content definition. YAML anchors and multi-document files are disallowed.

### Example Files (to be added)
- items/weapon/iron_sword.yaml
- creatures/beast/wolf_grey.yaml
- spells/elf.Fireball.yaml
- ... etc

## Invalid Fixtures (for testing errors)

See `tests/fixture-invalid/` for deliberately broken files:

1. **malformed-schema.yaml** - YAML syntax error or missing required fields
2. **duplicate-id.yaml** - Same definition ID as another file
3. **dangling-reference.yaml** - References non-existent definition ID
