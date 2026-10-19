# Action bars and gear sets

The owner's request: "I don't want to be one of those games limiting the number of usable skills."

## Action bars

- Six bars of twelve slots. Bar 1 is the quick bar of schema 23, widened from eight slots to twelve; bars 2 to 6 are new (schema 32).
- **Owner rulings of 2026-10-18** (the "before testers" pass, `docs/BEFORE_TESTERS.md`): paging is **Ctrl and 1 to 6** (Shift stays sprint only), also by the arrows on the main bar and by Alt and the wheel, and it is optional - most players just show more bars; every extra bar is **off by default** and each is switched on or off in Settings (Interface, Action bars) as well as from the Bars menu; **gear sets in a fight change the weapon and the off hand only** (see below); **the main bar follows the weapon in hand** (see below); and each bar has a **size slider, 75 to 150 %**.
- A slot holds an item, a known formula, a known technique or a gear-set switch (`gearset.set_N`). A thing is on one bar at most once; it may be on several bars.
- The main bar is at the bottom centre with the vitals directly above it, as slim bars: health and stamina on one row, and focus, strain and resonance on a second that shows only while they matter. Every slot of every bar is **44 px** at 1080 lines (shrunk with the interface so twelve always fit the width, grown with the text size); the key is in the slot's **top right corner**, the count or cost at its **bottom right**, never over what the slot holds; bars 2 to 6 stack tightly above the vitals with no title (a tooltip names the bar); the page arrows, the lock and the Bars menu are four small icon buttons at the main bar's right end. It shows one of the six (the page): **Ctrl** and 1 to 6, Alt and the wheel, or its own arrows (the modifiers rebindable). Further bars are shown or hidden from the bar's menu, stacked above the vitals or in columns at the right edge. Which are shown, the side placement and the lock are interface preferences (`InterfacePrefs`), not save state.
- Keys: `quick_1` to `quick_12` (1 to 9, 0, -, =) use the main bar's slots. Every action is in `SettingsStore.Remappable`, so the Controls page lists and rebinds it; conflicts are checked per context (`Binding.Contexts`), so a page key shares its digit with a slot key and the page modifier shares Shift with the sprint.
- Editing: drag a row from Skills (formulas, techniques) or the inventory (usable items) or the character sheet (gear sets) onto a slot; drag between slots to move; right click clears; the Lock button switches all of it off. Everything is the existing `AssignQuickSlotCommand` (it gained a `Bar`), so nothing pauses and nothing bypasses the simulation.
- Display: icon (or the name in short when none is bound), key, cost or count, a dim and a shape (`…` a wait, `–` a want) when not ready, and the availability word and reason in the tooltip. The availability is the Skills page's one evaluator (`AvailabilityRules`) through `ActionBarModel`.
- Persistence: schema 32. The 31 -> 32 step pads the quick bar to twelve slots (its eight keep their places), gives five empty further bars and no gear sets.

## Gear sets

- Up to ten named sets of what is worn. A set keeps the instance of each worn item and its definition.
- `SaveGearSetCommand`, `DeleteGearSetCommand`, `SwapGearSetCommand`, `CancelGearSwapCommand`; the system owns `StateSlice.GearSets`.
- A switch is a timed action: 2 s calm, 4 s in a fight (any hostile creature engaged, or a blow in progress). **In a fight only the weapon and the off hand change** (there is no ammunition slot; arrows are carried): the set's armour pieces are skipped, not queued, and a short line says "Armour stays on in a fight" (`GearSwapped.ArmourKept`). It is done at its tick by the equipment system's own `UnequipCommand` / `EquipCommand`, so requirements and the "hands are not changed mid-swing or under a guard" rule still apply and are reported per piece.
- Only what is carried is put on. A piece that is not carried is reported missing, never conjured, and its slot is left as it was. A slot the set leaves bare is unequipped.
- Switchable from the character sheet, an action-bar slot or a hotkey (`gear_set_1` to `gear_set_10`, unbound by default).
- A switch under way is transient: a load starts none.

## Canon notes (26_UI_INFORMATION_REQUIREMENTS.md section 8)

- Canon wants the bar to stay at eight slots, "never growing with level", with bar sets following the held item, kept in client prefs outside the save, and never showing a cooldown that is not a real cost. The owner's request overrides the first three here: twelve slots, six bars, and the layout is saved with the character (schema 32). The cooldown shown is only the game's real technique recovery. Bar sets by held item (canon 8.2) are built, saved with the character: **bar 1's layout is remembered per weapon family** (bare hands, and each weapon's skill - `WeaponFamilies`). A change of the weapon in hand, by a gear-set switch or by hand, shows that family's layout; editing bar 1 edits the current family's; the first time a family is held it copies the bar showing. Schema 34 (`held_family`, `family_bars`); a save from before it adopts the family held on its first tick, so the existing bar becomes that family's layout.
