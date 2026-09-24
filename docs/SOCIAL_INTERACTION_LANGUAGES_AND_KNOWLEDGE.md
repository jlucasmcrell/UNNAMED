# OTHERREACH — Social Interaction, Languages, Teaching & Knowledge

**Status:** Owner-approved design direction  
**Implementation stage:** Phase 1 is structured dialogue (M4) and minimal NPC continuity only. All deeper social systems are future (owner ruling, 2026-09-23).  
**Core principle:**

> **Social skill cannot create a reason for someone to agree where none exists. It helps the character discover, communicate, strengthen, disguise, bargain with, or exploit reasons that do exist.**

---

## 1. Social Interaction Is Motivation-Based

A social attempt should consider:

- proposition — what the player wants;
- approach — persuasion, bargain, deception, threat, status, evidence;
- leverage — why the target might comply;
- relationship;
- target values/goals;
- current circumstances;
- character skill.

The system should not be:

```text
Speech 75 > Difficulty 60
SUCCESS
```

---

## 2. Possible Outcomes

Social outcomes may include:

- enthusiastic agreement;
- reluctant agreement;
- counteroffer;
- conditional agreement;
- partial agreement;
- delay;
- polite refusal;
- angry refusal;
- suspicion;
- false agreement;
- reporting the attempt.

Binary success/failure should be used only when the situation genuinely is binary.

---

## 3. Persuasion

Persuasion does not rewrite deeply held values.

A skilled character can:

- understand the obstacle;
- frame an argument appropriately;
- provide evidence;
- appeal to values;
- discover acceptable compromise.

They cannot make an NPC abandon core beliefs without believable reasons.

---

## 4. Reading People

Social skill can improve **diagnosis**, not just outgoing dialogue.

Example:

```text
Novice:
The guard refuses.

Skilled:
He seems worried about disobeying orders.

Expert:
His fear is disciplinary, not ideological; authorization from the captain would probably resolve this.
```

This gives the player information with which to reason.

---

## 5. Difficulty Feedback

Default UI should prefer qualitative communication:

- very unlikely;
- difficult;
- plausible;
- strong case.

Where character skill permits, explain why:

> **He trusts you, but your request directly violates his orders.**

Exact percentages may be an optional accessibility/debug setting.

---

## 6. Deception Creates Beliefs

A lie proposes a fact.

Example:

> “Captain Vale sent me.”

The target evaluates:

- plausibility;
- prior knowledge;
- source trust;
- evidence;
- verification;
- behavior.

If believed, the NPC gains a belief record with:

- proposition/fact;
- source;
- confidence;
- timestamp.

The belief can later be disproven.

---

## 7. Lies Have Continuity

A deception can become a multi-scene history.

If the player claims to be Inspector Harrow, later conversations may test:

- district;
- supervisor;
- procedure;
- prior story.

Knowledge, disguise, forged evidence, and consistency can support the deception.

---

## 8. Forgery and Infiltration

Deception can combine with:

- forged documents;
- seals;
- stolen uniforms;
- disguise;
- procedural knowledge;
- language;
- etiquette.

This allows infiltration builds without making one Speech stat solve everything.

---

## 9. NPCs Can Lie

NPC deception must use the same information principles.

NPCs may:

- lie;
- omit;
- exaggerate;
- bluff;
- manipulate;
- make false promises.

High player insight can reveal contradictions/clues rather than displaying a universal `LIE DETECTED` banner.

---

## 10. Intimidation

Intimidation creates fear/coercion, not affection.

A threat requires credibility:

- capacity;
- willingness;
- leverage;
- target fear;
- target alternatives.

A terrified NPC may comply now and seek revenge later.

---

## 11. Bribery

Bribery is negotiation, not a boolean `bribable` flag.

Evaluate:

- amount/value;
- risk;
- NPC need;
- greed/honor;
- consequences;
- alternatives.

An NPC may prefer:

- money;
- favor;
- information;
- debt relief;
- promotion;
- contraband.

---

## 12. Negotiation

Trade negotiation should respect actual economics.

Relevant factors:

- acquisition/replacement cost;
- stock;
- demand;
- urgency;
- merchant wealth;
- relationship;
- negotiation skill.

A great negotiator cannot normally buy a 1,000-silver object for 8 silver without extraordinary circumstances.

---

## 13. Barter and Favors

Deals can involve:

- money;
- goods;
- services;
- favors;
- information;
- future obligations.

This integrates social play with economy and contracts.

---

## 14. Status and Credentials

Potential social leverage:

- citizenship;
- guild membership;
- title;
- religious office;
- professional license;
- academic credential;
- faction rank;
- settlement founder status.

Standing is contextual.

A scholarly title may mean nothing to bandits.

---

## 15. Clothing and Equipment Communicate

Appearance can convey:

- authority;
- occupation;
- wealth;
- affiliation;
- military membership;
- danger;
- restricted technology.

Do not reduce this to `formal clothes +12 speech`.

It changes how characters interpret the person.

---

## 16. Etiquette

Cultures may have rules around:

- greetings;
- address;
- hospitality;
- bargaining;
- gifts;
- funerals;
- religious spaces;
- taboos.

Character knowledge can automatically handle trivial etiquette to avoid busywork.

Important scenes may expose choices.

Knowing etiquette also lets the player insult someone deliberately and precisely.

---

## 17. Languages

Language should support graded proficiency rather than a binary checkbox.

Conceptual bands:

- recognition;
- basic;
- conversational;
- fluent;
- scholarly.

Speaking and literacy can differ.

---

## 18. Partial Comprehension

Low proficiency can reveal fragments:

> “…merchant… western road… three… danger…”

Higher proficiency fills in meaning.

This makes language useful without making unfamiliar dialogue completely blank.

---

## 19. Translation Technology / Magic

Translation can exist, but may struggle with:

- idiom;
- poetry;
- legal language;
- technical vocabulary;
- archaic dialect;
- cultural implication.

Language mastery retains value.

---

## 20. Secret Communication

Possible nonstandard communication systems:

- thieves' cant;
- guild notation;
- military signs;
- hunter signs;
- ritual languages;
- scientific notation.

Some knowledge is not simply spoken language.

---

## 21. Teaching

Teaching effectiveness can depend on:

- subject mastery;
- teaching capability;
- shared language;
- relationship;
- student foundation;
- time/resources.

A brilliant master may be a poor teacher.

A moderate practitioner can be excellent at teaching beginners.

---

## 22. Teaching Does Not Upload Mastery

Teachers provide:

- concepts;
- correction;
- exercises;
- technique;
- access.

The student still practices.

Do not use:

> pay trainer → instant mastery.

---

## 23. Cross-Cultural Teaching

Advanced knowledge may require prerequisites.

A Vaskaal engineer teaching an outsider may first need to teach:

- vocabulary;
- mathematics;
- models;
- tool use.

This reinforces the distinction between biology and culture.

---

## 24. NPCs Teach NPCs

Knowledge can spread through the world.

Over decades:

- students become teachers;
- techniques spread;
- hybrid schools appear;
- cultural monopolies weaken.

Knowledge need not remain permanently locked to the culture that invented it.

---

## 25. Durable Belief Change

Characters can genuinely change beliefs through:

- evidence;
- experience;
- trusted relationships;
- repeated discussion.

Deep beliefs should not flip because of one critical-success roll.

---

## 26. NPC-to-NPC Influence

NPCs can:

- persuade;
- recruit;
- proselytize;
- argue;
- spread rumors;
- change affiliations.

Most offscreen interaction can be abstracted.

Tier-A presentation materializes it when the player is present.

---

## 27. Rumors

Rumors should carry:

- source;
- confidence;
- distortion;
- spread;
- age.

Example:

Actual:
> three travelers killed by a wyvern.

Retelling:
> a dragon destroyed a caravan.

Further retelling:
> something from the Other ate thirty people.

---

## 28. Fame vs Reputation

**Fame** answers:

> How many people know who you are?

**Reputation** answers:

> What do they think about you?

A character can be:

- famous and loved;
- famous and hated;
- obscure but respected within a profession.

---

## 29. Professional Reputation

Separate professional communities can know the player differently:

- smiths;
- healers;
- bounty hunters;
- cartographers;
- scholars;
- thieves.

Professional fame can unlock commissions, teachers, invitations, and trust without making the player universally persuasive.

---

## 30. Relationship Memory

Important relationship changes should preserve reasons.

Examples:

- saved my child;
- lied about my brother;
- paid fair wages;
- publicly humiliated me;
- kept a promise.

Internal numeric dimensions may exist, but the attributed memory matters.

---

# 31. Systemic Hostility

> **Hostility is a relationship/state outcome, not an NPC type.**

The same NPC can begin friendly, become suspicious, become an enemy, reconcile, or remain personally friendly while belonging to a hostile faction.

Do not collapse all hostility into one number or flag.

---

## 32. Distinct Hostility Layers

Keep separate concepts for:

- personal relationship;
- fear;
- trust;
- grudge;
- reputation/standing;
- legal status;
- faction relation;
- war state;
- known identity;
- current tactical threat;
- attack legality.

They interact but answer different questions.

---

## 33. Personal Enemies / Nemeses

A significant grievance may create autonomous goals.

Possible retaliation:

- ruin reputation;
- theft;
- sabotage;
- framing;
- bounty;
- attack;
- political opposition;
- joining enemies.

An enemy can later become an ally if events justify it.

---

## 34. Settlement Hostility

A settlement can be hostile in different ways.

### Legal hostility
Guards seek arrest.

### Political hostility
Government bans/expels/seizes rights.

### Social hostility
Population refuses service/help.

### Military hostility
The character is an enemy combatant.

These states are not interchangeable.

---

## 35. Faction Hostility

Hostile factions may:

- deny services;
- close territory;
- issue bounties;
- sabotage;
- raid;
- propagandize;
- form alliances.

Individual members can still hold different personal relationships.

---

## 36. Culture vs Race

Avoid `all members of biological race X hate the player`.

Race is biology.

Culture/community/polity is social.

Very large acts can affect broad cultural reputation, but individuals retain agency.

---

## 37. Information Propagation

Hostility/reputation should spread through information.

A remote faction cell does not instantly know about an act unless communication permits it.

Disguise and identity knowledge therefore remain relevant.

---

## 38. Companion Association

Companions may become:

- accomplices;
- suspected associates;
- separately innocent;
- wanted.

They can refuse to enter hostile jurisdictions.

---

## 39. Property / Settlement Consequences

Enemies may target:

- licenses;
- bank access;
- rented property;
- caravans;
- businesses;
- player settlements.

Hostility therefore has world consequences beyond combat.

---

## 40. Future PvP Bridge

The same conceptual separation will later support player-versus-player rules.

> **Relationship determines attitude. Law/rules determine permissibility. Combat authority determines whether the attempted attack is accepted.**

Do not assume hostility automatically makes killing legally permitted.

---

## 41. LLM Role

The deterministic social system decides facts such as:

> NPC refuses because the request risks their family.

Optional AI may phrase the response naturally.

The model does not decide authoritative persuasion success or relationship state.

Future free-form text can potentially be parsed into:

- proposition;
- argument;
- evidence;
- tone;

then evaluated deterministically.

---

## 42. Authored Dialogue

Major emotional/story scenes should retain an authored spine.

AI/systemic dialogue can provide connective tissue.

Do not hand pivotal authored scenes entirely to runtime generation.

---

## 43. Social Skill Families

Potential disciplines include:

- persuasion;
- negotiation;
- deception;
- intimidation;
- empathy/insight;
- etiquette;
- leadership;
- teaching;
- performance.

Do not use one universal Speech stat for every social activity.

Exact progression mapping must be reconciled with the progression-axis review before implementation.

---

## 44. Failure

Avoid hidden one-roll permanent catastrophes.

Social failure should often create a new state:

- refusal;
- suspicion;
- counteroffer;
- relationship damage.

This reduces save-scumming pressure.

---

## 45. Knowledge/Belief Architecture Opportunity

Maps, witnesses, rumors, deception, stealth, divination, and social systems all converge on a generic information concept.

A future model may include:

- proposition/fact;
- source;
- confidence;
- timestamp;
- subject;
- location;
- staleness/expiry.

The simulation can know truth while characters hold beliefs.

Do **not** build a universal framework until its milestone requires it, but avoid incompatible ad-hoc representations.

---

## 46. Foundational Rule

> **Social power comes from understanding people and circumstances, not from a stat that turns NPCs into puppets.**
