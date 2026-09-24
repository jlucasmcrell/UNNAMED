# OTHERREACH — Crafting & Itemization

**Status:** Signature-system candidate  
**M2 impact:** None  
**Future schema impact:** Significant; reconcile after M2.

## 1. Core principle

> **Otherreach crafting is based on techniques, components, materials, and physical/magical compatibility rather than a closed list of finished-item recipes.**

Second rule:

> **If a combination is logically possible within the world's rules, prefer meaningful costs and tradeoffs over simply forbidding it.**

## 2. Item grammar

Conceptually:

**Item = chassis/form + materials + structural components + functional components + mechanisms + magical/technical channels + modes + enchantments + tuning + appearance**

Familiar item categories remain useful UI labels, not absolute capability prisons.

## 3. Hybrid examples

Allowed in principle:

- wand/staff that deploys into dagger, spear or pike;
- recall enchantment on a thrown/deployed weapon;
- warhammer with a one-shot concussive/fire discharge;
- shield with spell focus;
- crossbow with Otherglass sight/channel;
- staff with retractable blade.

The system asks whether the components can coexist and what they cost.

## 4. Tradeoffs

Hybrid gear usually sacrifices some combination of:

- weight;
- balance;
- durability;
- repairability;
- cost;
- complexity;
- specialist performance;
- resource consumption;
- reliability.

A dedicated spear can outperform a transforming staff-spear at pure spear work while the hybrid wins on flexibility.

## 5. Materials have personality

Avoid simple tier ladders.

Material properties may include:

- density;
- hardness;
- toughness;
- flexibility;
- edge retention;
- impact resistance;
- heat tolerance;
- magical conductivity;
- enchantability;
- corrosion;
- repair difficulty.

A strange material combination can be viable for a specialized reason.

## 6. Craft quality dimensions

Avoid one universal “quality” number.

Possible dimensions:

- structural integrity;
- balance;
- edge/impact quality;
- durability;
- fit;
- focus stability;
- enchantment stability;
- recoil/pressure tolerance;
- repairability.

## 7. Crafting disciplines

Candidate traditions include:

- smithing;
- weaponsmithing;
- armorsmithing;
- woodworking;
- bowyery/fletching;
- leatherworking;
- tailoring;
- jewelcrafting;
- alchemy;
- enchanting;
- runecraft;
- artificing;
- mechanisms;
- Otherwrought craft.

Exact progression structure must be reconciled with current `PROGRESSION.md`.

## 8. Cross-profession construction

Complex gear can require multiple disciplines.

The character may:

- learn them;
- commission components;
- hire another crafter;
- buy subassemblies;
- collaborate with NPCs.

This strengthens the economy.

## 9. Technique vs recipe

Knowledge should include techniques:

- forging method;
- folding mechanism;
- channel construction;
- enchantment anchoring;
- blade geometry;
- pressure chamber;
- rune routing.

A finished item can emerge from known techniques rather than requiring an authored recipe for every combination.

Recipes remain appropriate for:

- standardized goods;
- alchemical formulae;
- cultural designs;
- legendary processes.

## 10. Complexity

Every additional system increases integration difficulty.

Complexity can gate:

- failure risk;
- quality ceiling;
- required tools/stations;
- skill;
- specialist assistance.

This is a stronger limiter than arbitrary class restrictions.

## 11. Progression philosophy

Crafting skill should reward:

- new techniques;
- meaningful difficulty;
- experimentation;
- complexity;
- first successful outputs;
- quality improvement;
- commissions;
- disassembly/research.

Repeated identical low-value crafting should rapidly diminish as a training method.

Do not require thousands of identical daggers to become a master.

## 12. Failure

Avoid casino-style destruction of rare materials as the default.

Normal failure should more often produce:

- lower quality;
- waste;
- defects;
- longer work;
- corrective work.

High-risk experimental overreach may genuinely threaten rare components when the player knowingly chooses it.

## 13. Reforging and rebalance

Beloved gear should remain viable through:

- refitting;
- reforging;
- replacing components;
- new grips;
- balance changes;
- upgraded materials;
- enchantment replacement;
- mechanism additions;
- repair/restoration.

Old equipment need not become trash simply because the player gained levels.

## 14. Salvage and research

Disassembly can yield:

- materials;
- components;
- technique knowledge;
- clues about unknown construction;
- partial reverse engineering.

Unknown artifacts may require appropriate science/magic/cultural knowledge to fully understand.

## 15. Loot integration

Found gear should use the same underlying component/material grammar where practical.

Generated loot should not live in a completely separate rules universe from crafted items.

## 16. Provenance

Items can record:

- original crafter;
- later modifiers;
- famous owner;
- significant repair/reforge;
- historical event.

This supports famous craftsmen and heirloom equipment.

## 17. Armor construction

Armor is layered and modular:

- underlayer;
- padding;
- mail/mesh;
- plates/scales;
- joint protection;
- enchantments;
- fit adjustments.

Hybrid protection is normal.

## 18. Visual/animation constraints

Freedom needs production boundaries.

Use compatibility families for:

- grip;
- attack mode;
- deployment;
- hand count;
- sockets;
- transform states.

This preserves wide combinations without requiring arbitrary runtime geometry.

## 19. Resource channels

Do not hard-code every magical mechanism to `ManaCost`.

Items may use:

- stored charges;
- ammunition;
- heat;
- reagents;
- batteries;
- personal magical reserve;
- blood;
- environmental power;
- Other-derived energy.

## 20. Risk

Primary risk: **combinatorial item explosion**.

Mitigate with:

- standardized interfaces/sockets;
- capability tags;
- compatibility validation;
- deterministic stat evaluation;
- automated combination tests;
- visual/animation families.
