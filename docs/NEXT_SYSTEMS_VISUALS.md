# R12 rival notices — FINISHED visual handoff; Claude verdict pending

Branch: visual/rivals, based on systems/rivals 438bdd59.

The existing notice-board sheet now carries a section of rival cards: authored name, people, Call, current regard in words, and the race tally. A first meeting uses the existing announcement surface, followed by the rival's authored line, respecting World notice settings. Bounty claims keep the existing “Taken by <name>” line; a new test exercises it with Jessa Vane. The board rebuilds when its displayed facts change, instead of rebuilding every world tick.

Validation: NoticeBoardModelTests4/4 (including identity/regard/rival claimant); Release presentation build0 errors/31 existing warnings. No new portrait/silhouette art is claimed.

Two actual unaltered1920x1080 in-game viewport captures and a contact sheet are delivered for Claude at C:/Otherreach_External_Assets/NEXT_SYSTEMS_VISUALS/rivals/final_review03. The meeting card calls the same production AnnounceRival method as the Met event. The board shows the name, people/Call, regard, race tally and Taken by Jessa Vane. Source/art/host/assembly/image receipts and precise scope are in RIVALS_PROOF.json.

This is native UI visual proof with deterministic Jessa data from the existing model test fixture, projected onto an actual posted bounty view. It is not a claim that a canon rival was installed or a bounty race played. Normal native population/art/HUD remain visible. The private fixture waits180 real main frames, clears startup notices, then pauses Main for stable views. The first arrival capture and second private fixture were rejected locally; originals are retained. The final native host aborted during teardown after both complete captures (0xC0000374,450.44sec lease released), so no clean-package, fresh-RC or performance acceptance is claimed.

FINISHED means the requested visual implementation, push and review-artifact handoff are complete. Claude's visual verdict and integration remain pending; no visual PASS is self-awarded.

Claude's15:30 verdict: PASS for the card/board, conditional on the announcement-overlap fix. The public HUD now holds its banner queue and clock while the notice board is visible, then resumes the first card and subsequent queued notices on close. Existing quest-card/landmark holding still applies. NATIVE_TOAST_GATE.json records the native headless integration check through the actual Hud facade, HudView and NoticeBoardPanel: visible before open; hidden/clock held while covered; a second notice retained; first card restored after close; second follows. Exit0 after5.97s. The accepted design/screenshots are unchanged. This fix is source/UI state proof, not a new full-world screenshot or package gate.

# Field Guide in-game proof — IN REVIEW

Branch: visual/field-guide, based on systems/field-guide33d0dbed. Runtime source ae8599ef.

The public UI screenshot route accepts --field-guide-only alongside --ui-shots <output-directory>. It opens the ordinary F1 HelpPanel over the running world, waits180 real world frames and60 layout frames, writes screen_help.png, and quits before the unrelated walkthrough.

Personally inspected the original1920x1080 viewport: title, search field, three control columns, keycaps and close controls are readable over the real game/HUD. Unaltered original posted at C:/Otherreach_External_Assets/NEXT_SYSTEMS_VISUALS/field_guide/FIELD_GUIDE_OPEN.png for Claude's verdict. No mock/composite/generated art, no population/plant changes, no owner profile used.

The source tree is clean; public Release0errors /31existing warnings, FieldGuide domain tests5/5. docs/FIELD_GUIDE_PROOF.json records exact assembly, older native-host binary/PCK, image hashes and launch scope. Capture used a frozen public assembly in an older private native host, not a fresh RC export: it aborted0xC0000374 after capture/normal CaptureQuit (451.52-second lease). This is the requested in-game visual artifact, not clean-exit/world/performance acceptance. Claude's visual verdict remains pending; NOT FINISHED yet.

Integrator check (23:24): the input map binds shoulder_swap to Key.Q and party to Key.O (Main.Input.cs); no shared key, so the O/Q question was the keycap glyph only. Merged as is.
