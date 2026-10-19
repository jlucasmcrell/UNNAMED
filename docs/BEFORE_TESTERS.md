# Before outside testers (task 23)

The gaps FEATURE_SWEEP §3.1 ranks as "needed before outside testers", checked against the branch and built where missing. The report with each
item's status is `worker_tasks/logs/23_before_testers_report.md`; this page is what the code now does.

## Keys and controls
- Every action rebinds on the Controls page, with conflict warnings, reset to defaults, hold or press-on-press-off for sprint, walk, crouch and guard,
  look sensitivity, invert-Y and field of view (these existed). New: `auto_run` (the old `autorun`, read from a file that names it), `dialogue_skip`,
  and the action bars' keys (`quick_N`, `bar_modifier`, `bar_page_N`, `gear_set_N`) are the same actions in the same list - nothing is duplicated.
- **Auto-run** (`AutoRunRules`): the key (the tilde) starts running forward; forward, back or the key again stops it; strafing and the camera leave it
  running, and Shift still sprints; death or downing, opening a conversation and a blocked way (under 0.4 m/s for 0.6 s) stop it.
- First launch offers a controls preset: W A S D, the arrow keys, or "I will set my own" (`ControlPresets`).

## Accessibility
- **Subtitles**: a caption strip above the action bars carries every bark and ambient line with the speaker in capitals, a size (Normal to Largest over the
  text size, held to what three wrapped lines can take of a third of the screen) and a background (none, dim, solid). Conversation lines keep the dialogue
  panel, which names its speaker. With subtitles off an unvoiced bark still shows over the speaker's head.
  The strip stands in a band over the dock, at most three captions high; an older caption that no longer fits goes first. The HUD keeps named regions
  (`HudRegions`: status at the top left, compass, tracker and notices at the top right, the dock, the subtitle band, the chat feed and combat log beside
  the dock) that never meet, at any interface size from 75 to 200 %: the tracker shrinks to its title and notices wait when the room is short.
- **Colour never alone**: harm/aid, ready/refused, done/failed, requirement met/unmet and the compass marks each carry a word or a different shape
  (`ColourWords.Needs`: "10 - met", "10 - you have 7"; the compass names its marks in words and metres).
- **200 %**: text size goes to 200 % and the interface size to 200 %; the dock shrinks its slots to fit; the UI-shot pass pictures every screen at both.

## Settings
One file (`user://client_prefs.json`), one screen, applied at once, kept across sessions, never pausing anything: window mode, resolution, vsync, frame cap,
tier and dials, render scale and brightness; five volume buses plus the interface bus; look, field of view, camera shake (a solid blow on the character
jolts the view), head bob (first person; motion blur is not offered until a blur is drawn); interface and text size, contrast,
colour vision, notices (how long, and which kinds show: discoveries, items, progress, party, gear, world), subtitles, guidance, chat, the action bars
(each of bars 2 to 6 on or off, at the right edge, locked, a size slider each).

## Story, tracker and compass
Main-story track, `on_new_game`, `defeat_boss`, `defeat_creature`, `travel_to`, `waystone_known` and the tracked beat's compass mark already existed. New: a
second mark for the **nearest objective of any other active quest** (a ring; the tracked beat is a diamond), and "Quest: Renn Vale, 120 m" /
"Nearest: Sel Arien, 40 m" in words under the dial (`CompassMarkerModel`).

## Onboarding
The 43 hints of `ONBOARDING_HINTS.yaml` are content (`content/hints`), plus the owner's auto-run hint. Six moments joined `HintTriggers.All` and are named by
the system each names: `bar_opened` (the Bars menu, a page arrow), `stash_opened`, `trainer_opened`, `item_worn`, `chat_opened`, `reputation_opened`.
Three new categories (`social`, `hiring`, `identity`). Light guidance shows the first hint of each category, in the YAML's order. The first-launch
screen opens with "Before you go out" (text size, subtitles, colours, controls, guidance) in the draft's words and ends in "Open the road"; the three
explainers (downed and death, nothing stops, the world goes on) are on the Controls (Help) screen. Diegetic lines (Renn, Kera, journal notes) are content for
later: no dialogue was written.

## Party
Cap (`config.party max_members`), three stances, wait or follow (the "hold" order), and tactics - stay close, **focus my target**, protect the injured,
save healing - existed except the focus tactic: with it on, a companion's first blow goes to the creature the character struck in the last ten seconds.
The player downed with a companion near is the ruling already built (`PlayerDowned`).

## Notices and dialogue
Notices can be switched off by kind and stay for a configurable time. In a conversation the skip key silences the line and takes a lone, plain reply;
held, it fast-forwards through lone replies, and never chooses between replies or takes one that does something.

## Persistence
Schema 34, one commit: `held_family`, `family_bars` and `focus_targets` on the player (the focus tactic is kept beside the party's tactics so those frozen
shapes did not move). 33 -> 34 holds no family (the first tick adopts the weapon in hand), no layouts and no focus target. Fixture v34 by M2.Probe.