# RC d6 finding 37: readable away digest

The away sheet reports a human duration, named people, named contracts and places. It groups news, collapses repeated claims/wages/deliveries, previews at most twelve event lines and exposes the complete details through Show all events. The journal keeps the full human text. Keyboard focus stays on the expansion action when it toggles.

World news snapshots the actual dynamic actor name when an event happens; resolving a procedural slot later could identify the replacement rather than the original person. The optional facts are transient. Existing AwayLogEntry serialization, schema and authoritative state remain unchanged. Old saved raw lines without actor facts use honest generic wording instead of slot IDs. No current-person name is invented for historical news.

Validation: 398 presentation tests, 23 architecture tests, 13 targeted world/NPC-life/save/trunk tests; clean Presentation build. The new assertions reject raw IDs/slot letters, exercise named claims and arrivals, preserve full details beyond the twelve-line preview, and verify live World.Advance produces actor/place facts.

The actual game 1080p capture shows nine days and six hours, fourteen contract events and weather collapsed to six summary lines, with fifteen full details. The first purported 720p capture was still fullscreen 1080p and is rejected. The corrected opt-in Main.AwayReview gate forces windowed mode and asserts exact pixel dimensions before capture. Remaining 720p and packaged visual acceptance wait for DESKTOP BACK; no GPU gates run while paused.
