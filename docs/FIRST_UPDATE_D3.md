# D3 quest presentation

Base: `claude/pp11-fixes`. Owner brief D3 in `BRIEFS_FIRST_UPDATE_20261027.md`.

Real quest-start and objective-activation events queue story-sized, centred cards using the journal's title, summary and active objectives. Acceptance includes the giver; later steps use a smaller Next card. Cards last 7.5 seconds with a short fade, dismiss with Escape or J, and T tracks their quest. Entry activation is coalesced with acceptance. Nothing pauses or holds the player. Loading/starting another character clears old cards.

The tracker reads all active quests: chosen quest first and expanded, then oldest starts with stable ID ties. Five rows maximum, plus an honest +N more count. The expanded quest includes the existing marker's distance, or Nearby when arrived. Other rows have a short step; their full text is in the tooltip/journal. Quest rows take priority over incidental toast space.

Click a quest title to track it. Hold the existing free-cursor key and point at a row, or focus it, then T to track it. T retains chat everywhere else; the contextual quest key does not change existing chat bindings. J opens the normal journal. No save-schema, authoritative quest or production-content changes.

Focused unit tests cover active filtering, tracked-first ordering, stable fallback, five-row cap/remainder, entry-card coalescing, queue expiry, dismissal and reset. Native proof: two viewport stills from a private populated game with named Rook. Only the private fixture marks three existing side quests on_new_game; all card text, events and quest state use the production paths. Setup primes existing lazy views while frozen, then resumes ordinary gameplay; no simulated approach walk. The review hook requires both OTHERREACH_D3_REVIEW and the private fixture marker. Receipts and images are outside the repository at D:/Otherreach_Scratch/d3_quest_20261027.
