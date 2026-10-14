# Art coverage (smoke)

UNNAMED art coverage: 884 requested, 883 resolved, 1 fallbacks, 0 unexpected

A visual is a thing the game draws, by what it stands for; resolved is drawn with an asset from the library, a fallback is drawn in greybox. Allowed fallbacks are named in `src/Presentation/Art/art_coverage_allowlist.json`, each with its reason.

| Kind | Requested | Resolved | Fallbacks | Unexpected |
|---|---|---|---|---|
| building | 10 | 10 | 0 | 0 |
| cliffs | 1 | 1 | 0 | 0 |
| clip | 58 | 58 | 0 | 0 |
| container | 1 | 1 | 0 | 0 |
| creature | 4 | 4 | 0 | 0 |
| debug | 1 | 0 | 1 | 0 |
| door | 10 | 10 | 0 | 0 |
| dressing | 23 | 23 | 0 | 0 |
| effect | 2 | 2 | 0 | 0 |
| effect_recipe | 2 | 2 | 0 | 0 |
| emitter | 35 | 35 | 0 | 0 |
| icon | 11 | 11 | 0 | 0 |
| node | 4 | 4 | 0 | 0 |
| person | 5 | 5 | 0 | 0 |
| scatter | 8 | 8 | 0 | 0 |
| scenery | 4 | 4 | 0 | 0 |
| sky | 1 | 1 | 0 | 0 |
| station | 2 | 2 | 0 | 0 |
| structure | 583 | 583 | 0 | 0 |
| structure_surface | 111 | 111 | 0 | 0 |
| terrain | 5 | 5 | 0 | 0 |
| water | 2 | 2 | 0 | 0 |
| weapon | 1 | 1 | 0 | 0 |

## Unexpected fallbacks

None.

## Allowed fallbacks

- `debug:discovery_rings`: the F3 overlay's discovery rings, flat translucent discs - allowed: the F3 debug overlay's markers, drawn only while a developer has the overlay on; never shown in normal play (charter §3: no markers)
