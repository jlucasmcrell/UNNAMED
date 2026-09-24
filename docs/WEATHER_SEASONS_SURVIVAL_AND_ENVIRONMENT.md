# OTHERREACH — Weather, Seasons, Survival & Environment

**Status:** Owner-approved design direction  
**Implementation stage:** Mostly Phase 2+; only seams needed earlier  
**Core principle:**

> **Survival is preparation and adaptation, not maintenance chores.**

The environment should matter because it changes what is physically happening, not because the HUD forces the player to babysit six constantly draining bars.

---

## 1. Environmental Simulation Philosophy

Otherreach should simulate enough environmental state to create meaningful decisions:

- weather arrives, persists, moves, and changes;
- seasons alter geography and behavior;
- exposure matters when circumstances justify it;
- shelter, clothing, tools, knowledge, animals, companions, magic, and technology provide solutions;
- ordinary travel in ordinary conditions should not become survival busywork.

The simulation may contain precise numbers internally while the default UI communicates **conditions and consequences**, not spreadsheets.

---

## 2. Weather State

A region's weather can include:

- temperature;
- precipitation type/intensity;
- wind direction/speed;
- cloud cover;
- humidity;
- visibility;
- storm intensity;
- ground wetness/snow where relevant;
- extraordinary magical/Other conditions.

Weather should not be a purely random graphical toggle.

It should have enough continuity that the player can see a storm moving across a valley and make a decision about whether to travel, shelter, wait, or take another route.

---

## 3. Weather Changes Gameplay

### Rain

Can affect:

- mud and road speed;
- river level;
- tracks;
- scent;
- fire;
- visibility;
- equipment condition where materially justified;
- NPC travel;
- animal behavior.

Rain can both destroy old tracks and make fresh tracks easier to see in soft ground.

### Snow

Can affect:

- movement;
- temperature/exposure;
- tracks;
- camouflage;
- forage/resource availability;
- wagons and roads;
- animal migration/behavior.

### Wind

Can affect:

- ranged projectiles;
- smoke;
- fire spread;
- scent propagation;
- sailing;
- flight/gliding;
- Kal wing-assisted movement.

### Fog

Can affect:

- vision;
- navigation;
- ambush potential;
- ranged engagement distance.

### Severe storms

Can create:

- lightning;
- flash flooding;
- falling branches/trees;
- dangerous flying conditions;
- cancelled/delayed caravans;
- infrastructure damage;
- magical/cosmic interactions.

---

## 4. Seasons

Seasons should be systemic rather than texture swaps.

Potential effects:

- day length;
- temperature;
- snowfall/frost;
- flowering and fruiting;
- crop/forage availability;
- migration;
- mating/young;
- hibernation;
- roads and mud;
- flood/drought/ice;
- trade and prices;
- NPC work schedules;
- festivals/religious dates;
- magical/celestial conditions.

A trail can be trivial in summer, muddy in spring, and nearly impassable in winter.

---

## 5. Climate Is Regional

Do not make the entire world experience identical seasons simultaneously.

Climate can depend on:

- latitude;
- altitude;
- coastlines;
- terrain;
- wind/rain shadows;
- local Pacha rules;
- magical/cosmic conditions.

One region may be in deep winter while another experiences a warm rainy season.

---

## 6. Survival State

The simulation may internally track:

- thermal condition;
- wetness;
- fatigue;
- hydration;
- nutrition;
- exposure.

These should normally remain unobtrusive.

Default player-facing communication should use states such as:

```text
Comfortable
Chilled
Cold
Hypothermic
```

rather than forcing a permanent body-temperature gauge onto the HUD.

Detailed values can be available through optional diagnostics/accessibility UI.

---

## 7. Hunger and Nutrition

Use believable timescales.

Missing one meal should not immediately reduce combat ability or cause damage.

Nutrition matters through:

- recovery;
- long-term stamina;
- healing;
- strenuous work;
- cold resistance;
- extended expeditions.

Actual starvation is a long-horizon condition.

Food should primarily provide:

- sustenance;
- warmth;
- recovery;
- morale/comfort.

Avoid turning every normal meal into a stack of temporary combat buffs.

Exceptional/alchemical food can have special effects when fiction supports them.

---

## 8. Hydration

Hydration can become serious faster than nutrition, but should still operate on believable timescales.

Sources differ in safety.

Potential solutions:

- clean water;
- boiling;
- filtration;
- purification;
- magical treatment;
- technology.

Do not create constant drink-button busywork during normal town/road play.

---

## 9. Fatigue and Sleep

Fatigue can affect:

- Focus;
- reaction;
- precision;
- recovery;
- Resonance/Strain recovery;
- judgment/perception.

Players can push through fatigue when necessary.

Sleep deprivation should matter after realistic periods, not after a few minutes.

---

## 10. Rest Quality

Resting conditions matter:

- exposed ground;
- bedroll;
- tent;
- sheltered camp;
- inn;
- private home;
- high-comfort estate.

Better rest improves:

- recovery;
- warmth;
- safety;
- fatigue removal.

Homes therefore provide real utility beyond decoration.

---

## 11. Camping

Camping should be a useful lightweight system rather than a tent-peg simulator.

Potential camp elements:

- bedroll;
- tent/shelter;
- fire;
- cooking;
- supplies;
- animal tethering;
- watch;
- alarm/traps.

Companions can contribute.

A lone traveler may rely on:

- guard animals;
- alarms;
- concealment;
- safer campsites.

---

## 12. Watches

Companions may take watches.

A watch system can provide:

- earlier warning;
- reduced ambush risk;
- companion utility;
- roleplaying opportunities.

It should not require manually playing every hour of every night.

---

## 13. Shelter Properties

Shelters/buildings can expose properties such as:

- weather protection;
- insulation;
- ventilation;
- comfort;
- defensibility.

Construction quality and materials should influence actual living conditions.

A beautiful building with a leaking roof remains a bad shelter.

---

## 14. Clothing and Environmental Equipment

Clothing may have:

- insulation;
- breathability;
- wind resistance;
- water resistance;
- drying time;
- weight.

Examples:

- cloak;
- rain layer;
- insulated lining;
- desert clothing;
- snow footwear;
- face covering.

Armor and clothing layers should interact rather than exist as unrelated systems.

---

## 15. Armor and Climate

Armor should have environmental consequences based on real properties.

Examples:

- heavy insulation in heat;
- soaked padding in cold;
- metal temperature/conductivity;
- restricted ventilation.

Avoid generic class penalties.

A prepared battle mage or armored traveler can solve these problems with proper design and equipment.

---

## 16. Weather Forecasting

Forecasting can be learned rather than magically granted by UI.

A knowledgeable character may read:

- cloud structure;
- pressure signs;
- wind;
- animal behavior;
- local terrain.

Possible progression:

```text
Novice: "Looks like rain."
Expert: "Severe storm before dusk, probably moving northeast."
```

Other solutions:

- local farmer knowledge;
- Vaskaal instruments;
- magical divination.

Multiple methods can answer the same physical problem.

---

## 17. Ordinary Environmental Hazards

Potential hazards include:

- cold;
- heat;
- lightning;
- flood;
- ice;
- mud;
- drowning;
- cliffs;
- avalanche;
- rockfall;
- wildfire;
- smoke;
- toxic gas;
- unstable structures;
- disease;
- venom;
- altitude where appropriate.

The hazard should generally communicate evidence the character can perceive.

---

## 18. Hazard Knowledge

Knowledge changes what clues mean.

Example:

Dead birds near a cave entrance.

- novice: strange detail;
- experienced miner: possible bad air;
- Vaskaal sensor: composition reading;
- magic practitioner: supernatural contamination check.

The environment becomes another knowledge system.

---

## 19. Darkness

Darkness should actually matter.

Possible light solutions:

- torch;
- lantern;
- magical light;
- biological vision;
- Vaskaal optics;
- glowing organisms;
- Otherlight.

Light has a stealth cost:

> **If you can see by emitting light, someone else may be able to see the light.**

---

## 20. Fire

Fire can provide:

- warmth;
- light;
- cooking;
- comfort.

It can also:

- reveal location;
- produce smoke;
- consume fuel;
- spread.

Fire magic should acknowledge obviously flammable environments within practical simulation limits.

Do not attempt to simulate every blade of grass.

---

## 21. Disease

Disease should be:

- understandable;
- diagnosable;
- preventable where appropriate;
- treatable.

Potential causes:

- contaminated food/water;
- wounds;
- animals;
- seasonal outbreaks;
- regional pathogens;
- magical phenomena.

Healing magic should not automatically erase every infection/pathogen.

Medicine remains meaningful.

---

## 22. Wound Infection

Infection should depend on relevant conditions:

- wound depth/type;
- contamination;
- treatment;
- time;
- species/biology.

Avoid arbitrary percentage rolls after every weapon hit.

---

## 23. Species/Race Environmental Biology

Each playable people can eventually have an environmental profile based on actual biology rather than flat `+25% cold resistance`.

Examples:

- Kal density and wings affect traversal and exposure differently;
- Vaskaal may tolerate different environmental ranges;
- Mor may interact with temperature/nutrition unusually;
- Constructed may require maintenance/resources rather than conventional food;
- Ondrek have fundamentally different material/environment concerns.

These details belong in race-specific design, not improvised per feature.

---

## 24. NPC Weather Response

Autonomous NPCs should react to environmental conditions.

Examples:

- merchant delays a caravan;
- farmer protects crops;
- sailor remains in harbor;
- animal seeks shelter;
- reckless adventurer travels anyway;
- caravan becomes stranded.

This can create organic systemic content such as an overdue caravan after a storm.

---

## 25. Infrastructure Damage

Significant events may:

- wash out roads;
- damage bridges;
- flood structures;
- destroy crops;
- knock down trees;
- damage poorly protected buildings.

Ordinary weather should not constantly punish the player's property.

Severity, construction quality, location, and maintenance matter.

---

## 26. Economy Integration

Weather/seasons can affect:

- food prices;
- fuel/firewood demand;
- regional transport;
- resource availability;
- harvests;
- caravan timing.

The economy can therefore communicate environmental history.

---

## 27. Environmental Magic

Environment can influence magic:

- storm assists lightning-related work;
- thin Veil changes Othercraft;
- celestial conditions affect rituals.

Magic can influence environment:

- local warming/cooling;
- fog;
- wind;
- rain ritual;
- storm warding.

Large-scale climate alteration requires correspondingly large power/infrastructure.

---

## 28. Othertide

**Othertide** is a strong candidate for cosmological weather.

It may vary with:

- celestial geometry;
- Pacha conditions;
- unknown cycles.

During unusual Othertides:

- gates become more/less active;
- Otherglass changes behavior;
- some entities appear;
- divination changes;
- Resonance becomes easier or less stable;
- Otherways shift.

Experienced travelers may eventually care about both:

```text
Weather forecast
Othertide forecast
```

---

## 29. Seasonal Content Windows

Seasonality can naturally create timing without arbitrary quest timers.

Examples:

- migration;
- bloom period;
- harvest;
- solstice ritual;
- pilgrimage;
- seasonal gate opening.

---

## 30. Survival Skill Philosophy

High survival capability should primarily reduce friction through knowledge:

- campsite selection;
- edible resources;
- water safety;
- fire;
- weather signs;
- terrain;
- animals;
- shelter.

Low-skill characters can still compensate through:

- equipment;
- guides;
- roads;
- inns;
- companions.

---

## 31. UI Principle

Do not create a survival HUD that dominates normal play.

Display environmental information when actionable.

Default:

- contextual statuses;
- visual/audio feedback;
- character behavior;
- journal/tool information.

Optional detailed displays can exist.

---

## 32. Persistence / Determinism

Weather and environmental systems must use explicit game time and deterministic state.

Do not depend on wall-clock time or unseeded randomness.

The exact weather simulation algorithm is future work and must comply with the existing worldgen/save compatibility rules.

---

## 33. Delivery Staging

### Prototype seam
- world time exists;
- environmental modifier interface exists;
- no full survival loop required.

### Vertical slice
- simple regional weather;
- temperature/wetness where relevant;
- one camping/rest loop;
- one significant hazard;
- NPC response to weather.

### Expansion
- seasons;
- regional climates;
- economy effects;
- disease;
- broader survival gear;
- Othertide.

### Late
- regional climate alteration;
- magical/cosmic weather;
- major infrastructure damage;
- Great Work environmental projects.

---

## 34. Foundational Rule

> **Ordinary travel should feel like travel. Harsh travel should require preparation. Survival matters most when the player deliberately enters conditions where survival should matter.**
