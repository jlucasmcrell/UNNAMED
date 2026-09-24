# OTHERREACH — AI Narrative Service

**Status:** Future optional capability  
**M2 impact:** None  
**Hard rule:** The game must remain fully playable with AI disabled.

## 1. Purpose

Use an LLM to add language, variation and interpretation to an already-authoritative simulation.

Good uses:

- NPC conversation;
- rumor phrasing;
- letters/books/journals;
- quest prose;
- NPC memory summarization;
- generated commission text;
- faction propaganda;
- historical retellings;
- companion banter;
- proposals for systemic side stories.

Bad use:

> Let the LLM directly decide authoritative world truth.

## 2. Authority boundary

The LLM may **propose** facts. It may never directly:

- add/remove inventory;
- award XP;
- change reputation;
- spawn/kill entities;
- create arbitrary definition IDs;
- set quest rewards;
- change authoritative world state;
- bypass validation.

Flow:

**World state → structured facts/constraints → AI Narrative Service → prose/proposal → deterministic validator → accepted/rejected game action**

## 3. Provider-independent interface

Future conceptual boundary:

`IAiNarrativeService`

Possible providers:

- disabled;
- template/fallback;
- local model;
- LAN-hosted model;
- OpenAI-compatible endpoint;
- pre-generated/cache-only.

No gameplay system should directly depend on Ollama, OpenAI, Qwen, or a specific vendor.

## 4. Dynamic dialogue

Give the model only facts the NPC is allowed to know:

- personality;
- relationships;
- witnessed events;
- cultural knowledge;
- local rumors;
- current objectives;
- permitted lore.

The model must not become an omniscient narrator.

## 5. NPC memory

Store structured durable memories such as:

- rescued family member;
- unpaid debt;
- witnessed necromancy;
- prior trade;
- personal betrayal;
- last meeting.

Periodically compress old memory into summaries while preserving high-value facts.

## 6. Generated grind/commission quests

The deterministic game decides:

- request type;
- target definition;
- quantity;
- source/location feasibility;
- reward;
- issuer;
- expiry/constraints.

The AI creates:

- title;
- motivation;
- dialogue;
- flavor;
- optional hint.

This makes systemic quests feel authored without allowing hallucinated rewards or nonexistent materials.

## 7. World storyteller proposals

A later, more ambitious layer may inspect real simulation facts and propose small arcs.

Example inputs:

- mine output increased;
- goblin population rose;
- settlement needs iron;
- caravan route is dangerous;
- player knows local faction.

The model can propose a narrative connection. Systems validate all actors, locations, objectives and rewards before creating anything.

## 8. Performance strategy

Do not require foreground inference for every interaction.

Use:

- background pre-generation;
- caching;
- small fast models for conversation;
- larger models only for rare planning;
- remote/LAN inference when available.

The game GPU should not be assumed to have spare VRAM.

## 9. LAN AI server

Developer/test setup can use a separate machine (for example a 3090-class GPU) as an OpenAI-compatible narrative service while the main GPU renders the game.

This must remain optional and not become a minimum player requirement.

## 10. Generated record lifecycle

Generated narrative should carry:

- source facts;
- NPC/context;
- creation world time;
- validity conditions;
- expiration;
- model/provider metadata where useful for debugging.

If the source fact becomes false, the generated content can expire.

## 11. Safety against lore drift

For major authored lore:

- retrieve from approved lore records;
- limit model claims;
- validate entity/definition references;
- never let generated prose silently establish new canon.

## 12. Design principle

> The simulation creates history. AI helps characters talk about it.
