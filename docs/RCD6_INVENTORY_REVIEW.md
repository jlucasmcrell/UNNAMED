# RC d6 inventory source candidate

Worn equipment now leaves pack placement, capacity checks and pack rows. Its weight still counts. Equipping checks the projected slots before changing equipment or publishing events; explicit unequip refuses a full pack. InventorySystem owns repacking, reached through commands from EquipmentSystem.

The live paper doll reuses CreatorPreview and the game's native MH/MPFB figure factory, current model, materials, clothes and held weapon. It uses a centred camera inside its own small viewport, rendered only while visible. Ten equipment buttons surround it, with equipped item names and their existing inventory icons; dragging a carried equipment row to its matching slot submits the normal Equip command. A worn slot selects its real item details and clicks to unequip. Trading/container context retains its context column.

In the normal backpack view, details occupy a fixed-height scroll strip beside the list, under the smaller paper doll. Bag slots live in that same right column. Compact carried rows leave at least eight complete rows visible at1080; the running review checks the actual visible rectangles. Trading/container views retain their original context column and place details below the pack. Hovering changes its contents without changing list height or scroll position. No detail content can overlap the list viewport.

CPU: all 14 PackTests pass, including actual equip -> fill last slot -> refused unequip -> make room -> successful unequip, with unchanged carried weight and identity. Existing full-pack fixtures now correctly count three packed starting stacks; the starting sword is worn. Presentation compiles with zero errors.

Review hooks are opt-in only. They use actual Window Graphics Capture client-area PNGs from FFmpeg, with the cursor, capture border and display border disabled. Each screenshot records and checks its actual pixel dimensions. A copied normal RC d5 Edric/Tavar save provides a real story companion; the owner's profile is never opened for writing. The UI gate checks pack/detail rectangles and scroll stability, worn items absent from bag rows, real transfer/equipment controls, the dismissal warning, item identity on return, and continued world ticks. The away digest may run first in the same process.

The initial MPFB1080/720 gates passed; the follow-up8-row/name/icon MPFB gate passed too, and the matched MH follow-up is pending. #46 remains partial until salvage and the first-full-bag hint are implemented and reviewed; no package or shortcut promotion is authorised by this source candidate.
