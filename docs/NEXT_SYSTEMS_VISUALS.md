# Hollow Day scenery — IN PROGRESS

Branch: visual/hollow-day, based on systems/calendar-events d61fc9d7.

Dressing entries may name their festival. Those entries start hidden and follow Simulation.Festivals on the exterior/interior view, including after load/absence. It uses the systems branch's calendar and active-hour rules; it does not add a second calendar.

Source authoring: tools/asset_pipeline/author_hollow_day.py. Isolated artwork is D:/Otherreach_Scratch/codex_systems_visuals_20261031/hollowday/art03, with full .blend, native GLBs and HOLLOW_DAY_ART_RECEIPT.json. The stall is 3DModelsCC0's acquired CC0 MarketStall with its original native PBR maps; bunting uses full 4K Poly Haven rough_linen; the small frontier tally frame has39 hanging wooden staves and full 4K rough_wood. Acquired files remain hash-identical. Source03 adds wind pivots without changing source02 world vertices or embedded image bytes (ART02_ART03_EQUIVALENCE.json). This is a source candidate, not an approved placement. The native Talvess tower is not claimed.

The material contact sheet is posted for Claude at C:/Otherreach_External_Assets/NEXT_SYSTEMS_VISUALS/hollow_day/HOLLOW_DAY_SOURCE_CONTACT.jpg. It is CPU Cycles source review, not in-game proof. Runtime FestivalWind moves native hanging pivots continuously from the existing WindField signal. Five frontier-yard placements are in Art/hollow_day_dressing.json, loaded alongside ordinary dressing only in region.starting_frontier. They keep the gathering point434,744, the workbench and the road clear; actual native terrain/route/crowd inspection remains due.

The existing public TextureCache class compressed all15 image references successfully into9 signed BC7 files in an isolated root. NATIVE_CACHE_AUDIT03.json records original GLB/image sizes, DDS hashes and mip chains. Stock BC7 block alignment stores the4096x4105 linen maps as4096x4108; no acquired source or embedded image was rewritten. Native source stage is queued to check actual materials,73 wind pivots and visibility through the real Dressing/SetFestivals calls; explicit test FestivalViews are not simulation/calendar proof.

Release build:0 errors/31 existing warnings. Still required: native source/material verdict, actual yard terrain/route/crowd clearance, active/inactive-day in-game proof and Claude verdict. This is NOT FINISHED.
