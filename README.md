# UNNAMED

An experimental single-player, first-person open-world fantasy RPG being developed with heavy AI-assisted engineering and asset production.

The project draws inspiration from the exploration, progression, danger, crafting, long-form quests, and persistent-world feel of classic MMORPGs such as Asheron's Call and EverQuest II, while being designed as an entirely original single-player RPG.

## Vision

The core fantasy:

> Start with almost nothing. Explore a world that does not revolve around you. Learn, fight, craft, build, recruit allies, uncover ancient secrets, acquire legendary equipment, establish a home or settlement, and eventually become someone the world recognizes.

Key design principles include:

- Solo-first gameplay with meaningful NPC companions and hirelings
- First-person exploration and combat
- No universal enemy level scaling
- Large authored regions with procedurally assisted environmental detail
- Meaningful crafting, gathering, professions, and equipment progression
- Long-form quests with memorable artifact rewards
- Skills, attributes, weapon mastery, magic mastery, and hybrid character builds
- Persistent player construction and eventual settlement development
- Exploration driven by curiosity rather than constant quest markers
- Seamless or near-seamless traversal wherever technically practical
- Architecture that remains single-player-first while preserving reasonable future LAN/WAN expansion possibilities

## Technology

- Engine: Godot 4.x
- Primary language: C#
- Content: Human-readable YAML
- Testing: xUnit / headless .NET tests
- Architecture: Engine-independent authoritative domain layer with command/event-based state mutation
- Persistence: Versioned persistent world state designed around deterministic baselines and sparse deltas

Godot handles presentation, rendering, physics integration, and scene composition.

Authoritative gameplay state lives outside the engine-facing presentation layer so that core systems can be tested headlessly.

## Current Status

Development is currently in Phase 1 - Playable Prototype.

Completed:

- Phase 0 game/system architecture and adversarial review
- M0 - Repository and architecture bootstrap
- M1 - Domain skeleton, command/event flow, and headless testing
- M1b - Data-driven content loading and validation tooling
- M2 - Entity registry and instance identity, deterministic world baseline, and crash-safe sparse-delta saves

The next foundation work covers save migration and progression before full gameplay implementation begins.

The project is not yet a finished or generally playable game.

## World Design

The intended world combines hand-authored macro geography with deterministic procedural environmental dressing and handcrafted settlements, dungeons, landmarks, and major quest spaces.

The goal is a world whose geography can be learned and remembered rather than one that feels randomly generated.

Player housing and construction are intended to be consequential parts of the world. Long term, players should be able to build across much of the wilderness, purchase or lease protected property inside settlements, or potentially grow a remote homestead into a settlement of its own.

## AI-Assisted Asset Pipeline

The project is experimenting with a heavily AI-assisted game-asset workflow using tools and models including:

- ComfyUI
- TRELLIS.2
- Pixal3D
- Hunyuan3D
- Generative image workflows
- Procedural and generative PBR material workflows
- Generative SFX and ambience
- Blender for cleanup, optimization, rigging, and export

Generated intermediate assets are intentionally kept outside normal Git history. Production-ready assets will use a deliberate versioning strategy rather than committing every generation and intermediate file.

## Development Philosophy

The project is being built vertically and incrementally.

> One small system that actually works is more valuable than a large system that merely exists on paper.

Major milestones must build, test, and demonstrate their exit criteria before development proceeds into dependent systems.

AI coding agents are treated as implementation collaborators, not as substitutes for architecture, automated testing, source control, or human playtesting.

## Repository Structure

- docs/ - Design, architecture, decisions, roadmap, and risk documentation
- src/ - C# runtime implementation
- tests/ - Headless automated tests
- content/ - Data-driven game definitions
- tools/ - Development and asset-pipeline tooling
- assets/ - Local/generated asset pipeline and manifests

The documentation under docs/ is intentionally extensive so development can continue coherently across multiple AI-assisted engineering sessions.

## Contributions

This is currently an experimental personal development project and the foundational systems are still under active construction.

Issues, discussion, technical observations, and constructive feedback are welcome.

Large unsolicited architectural rewrites or major feature pull requests may not be accepted while the foundational milestones are still being completed.

## License

Source code is licensed under the MIT License. See LICENSE.

Game artwork, models, audio, characters, world designs, and other non-code creative assets are not automatically licensed under the MIT License unless explicitly stated otherwise.
