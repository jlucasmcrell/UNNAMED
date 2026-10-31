# Field Guide in-game proof — IN REVIEW

Branch: visual/field-guide, based on systems/field-guide33d0dbed. Runtime source ae8599ef.

The public UI screenshot route accepts --field-guide-only alongside --ui-shots <output-directory>. It opens the ordinary F1 HelpPanel over the running world, waits180 real world frames and60 layout frames, writes screen_help.png, and quits before the unrelated walkthrough.

Personally inspected the original1920x1080 viewport: title, search field, three control columns, keycaps and close controls are readable over the real game/HUD. Unaltered original posted at C:/Otherreach_External_Assets/NEXT_SYSTEMS_VISUALS/field_guide/FIELD_GUIDE_OPEN.png for Claude's verdict. No mock/composite/generated art, no population/plant changes, no owner profile used.

The source tree is clean; public Release0errors /31existing warnings, FieldGuide domain tests5/5. docs/FIELD_GUIDE_PROOF.json records exact assembly, older native-host binary/PCK, image hashes and launch scope. Capture used a frozen public assembly in an older private native host, not a fresh RC export: it aborted0xC0000374 after capture/normal CaptureQuit (451.52-second lease). This is the requested in-game visual artifact, not clean-exit/world/performance acceptance. Claude's visual verdict remains pending; NOT FINISHED yet.
