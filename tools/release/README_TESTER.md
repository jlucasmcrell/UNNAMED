# Otherreach — playtest

Thank you for playing. This is an early, unfinished playtest of Otherreach, a single-player fantasy RPG. You wake on a ridge above Ashen Hollow, a waystation on the frontier, and follow the road toward Cairnwold. Talk to the people who live there, take on work, gather and craft, travel by the Waystones, and see what lies under the hill. Expect rough edges: finding them is what this playtest is for.

## Install and run

1. Extract the whole download into one folder you can write to, for example `C:\Games\Otherreach`. Keep the folder together; do not copy `Otherreach.exe` out of it.
2. Double-click **PLAY.bat** or **Otherreach.exe**. Both use your saved display settings; the first launch selects quality for your graphics card. Change it in Settings.
3. **PLAY Ultra 4K.bat** is for 4K screens and graphics cards with 16 GB of memory or more.

Nothing else needs installing, and the game never asks for administrator rights.

**Windows SmartScreen:** the game is not code-signed, so Windows may say "Windows protected your PC". Choose **More info**, then **Run anyway**.

**The first launch is slower** while the game prepares its shaders. Later launches are quicker.

## System requirements

| | Minimum | Recommended |
| --- | --- | --- |
| System | Windows 10 or 11, 64-bit | Windows 10 or 11, 64-bit |
| Graphics | Vulkan graphics card with 12 GB of memory (RTX 3060 12 GB, RTX 4070, RX 6700 XT or better), Low quality at 1920 x 1080 | 16 GB or more, High quality at 2560 x 1440 |
| Processor | 6 cores | 8 cores or more |
| Memory | 16 GB | 32 GB |
| Storage | the unpacked folder, on an SSD | SSD |

**Graphics cards with 8 GB of memory are not supported in this playtest:** the game runs out of graphics memory and slows to a crawl. We are working on it for the next one.

Quality, resolution, render scale and the individual effects are in **Settings > Graphics**. If the game runs slowly, choose a lower quality or a lower render scale there.

## Controls

Keys follow their position on the keyboard, not the letter: on an AZERTY keyboard, move with **Z Q S D**. Every key can be changed in **Settings > Controls**. Press **F1** in the game for this list.

| Action | Key |
| --- | --- |
| Move | W A S D, or the arrow keys |
| Look | Mouse |
| Sprint (hold) | Shift |
| Walk (hold) | Ctrl |
| Jump | Space |
| Crouch or stand | X |
| Auto-run, on or off | ` (the key left of 1) |
| First person, and back | V (the mouse wheel zooms) |
| Change shoulder | Q |
| Interact: open, take, talk, work, help up | E |
| Attack, or shoot the bow | Left mouse |
| Guard, or aim the bow (hold) | Right mouse |
| Dodge | C |
| Use a salve | H |
| Action bar | 1 to 9, 0, -, = |
| Tell your companion to follow or wait | G |
| Answer in a conversation | 1 to 9 |
| Inventory | Tab or I |
| Take all, from an open container | R |
| Journal / Map / Character / Skills / Party | J / M / K / P / O |
| Save now | F5 |
| All saves, backups too | L |
| Report a problem | F8 |
| Menu, or close the open screen | Escape |
| Controls and help | F1 |

There is no quick-load: the world is one continuous life. To go back to an earlier save, open **All saves** (L) and choose it.

## Reporting a problem

Press **F8** (or Escape > **Report a problem**), write what happened, and choose **Write the report**. The game writes a zip into the **reports** folder inside the game folder and shows you where it is. It holds:

- what you wrote, the build number and your system (graphics card, its memory, processor, memory, Windows version, window size, quality);
- a picture of the screen as it was when you pressed F8;
- the game's log, your settings and your current save.

**Nothing is sent.** The game never connects to the internet. To send a report, attach the zip to a comment on the Otherreach itch.io page, or use the feedback link on otherreach.com/playtest. Where you were, what you did and what you expected help most. If the game closed by itself, the next launch says so and offers to open the log folder; press F8 once you are back in.

## Saves and logs

- Saves: the **saves** folder inside the game folder. Copy it before you replace the game with a newer build. The game also saves by itself.
- Reports: the **reports** folder inside the game folder.
- The log: `%APPDATA%\Godot\app_userdata\Otherreach\logs\godot.log` (F1 shows the exact path).
- Settings: `%APPDATA%\Godot\app_userdata\Otherreach\client_prefs.json`.

## Known issues

- Some clothing is interim and looks modern up close (buttons, pockets). Period garments and worn armour come later.
- The first time you enter the Barrowworks the screen stays black for a while as the rooms are prepared. This is expected.
- Short hitches can happen as new parts of the frontier load in.

## Credits

Choose **Credits** on the title screen: everyone whose work is in the game, every licence, and how generative AI was used in making it. The licence texts are in the **THIRD_PARTY_NOTICES** folder (start with NOTICE.txt).
