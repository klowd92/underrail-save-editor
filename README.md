# Underrail Save Editor

An unofficial save game editor for [Underrail](https://www.underrail.com/) by Stygian Software.

Unlike most save editors, it doesn't reimplement the save format. It loads the game's own code (`underrail.exe`) as a
library and lets the game read and write the files, so a save written by the editor is what the game itself would write.
Editing goes through the game's own rules too: raising Strength updates carry capacity and weapon damage, and adding a
feat grants its abilities, exactly as in the game.

## Features

- **Save browser**: every save with the character's portrait, level and location.
- **General**: portrait, name, gender, level, experience, and unspent skill / feat / ability / specialization points.
- **Base abilities** and **skills**. Skills show the effective value with bonuses, the game's description, the related ability and synergies.
- **Feats**: all feats of the installed game and DLCs, with the game's icons, descriptions and requirements (checked against the character).
- **Specializations**: points per specialization, with descriptions and maximums from the game.
- **Capabilities**: movement, vision, detection, status flags and immunities.
- **Inventory**: equipped items and bag with the game's tooltips. Edit stacks, quality and durability; remove items; add any of the game's 1,400+ items.
- **Crafting simulator**: all blueprints, laid out like the game's crafting window. Pick components and qualities to see the resulting item, its stats and the skill requirements.
  The game's own blueprint code computes the result, with the character's crafting feats applied. *Create* puts the item in the bag.
- **Variables**: the global quest and world-state flags.

**Safety:** every save is written to temporary files, read back with the game's code, and only then moved into place.
Replaced files are backed up to `%LOCALAPPDATA%\UnderrailSaveEditor\Backups`. **Save As** writes a new save folder.

## Requirements

- Windows with .NET Framework 4.8.
- **Underrail installed** (GOG or Steam). The editor finds it automatically or asks for the folder.
  Icons, portraits and texts are read from your installation; none of the game's files are included here.
- Tested with **Underrail 1.3.1.2** (save format 44.708). On startup the editor checks that every part of the game's code it needs is present.
  It refuses to run if something is missing, and warns before continuing on an untested version.

## Build

Requires the .NET SDK (6 or newer, on Windows; WPF needs the Windows Desktop SDK). Nothing else to install: XNA 4.0 is referenced from the GAC, where Underrail installs it.

```
dotnet build UnderrailSaveEditor.sln -c Release
```

Run `src\UnderrailSaveEditor.App\bin\Release\net48\UnderrailSaveEditor.exe`. Keep `UnderrailSaveEditor.Core.dll` next to it.

Everything targets **x86**, because `underrail.exe` is a 32-bit assembly and must be loaded into a 32-bit process.

## Project layout

| Path | Contents |
|---|---|
| `src/UnderrailSaveEditor.Core` | Loads the game's code and exposes saves, the character, items, crafting, icons and texts. |
| `src/UnderrailSaveEditor.App` | The WPF editor. |
| `tools/SaveTool` | Command-line front end to Core, for testing and scripting (`check`, `info`, `items`, `catalog`, `blueprints`, `craft`, `edit`...). |
| `tools/savediff.py` | Structural diff of two save files. Needs an MS-NRBF reader module (`nrbf.py`) on its path. |
| `DESIGN.md` | How it works: hosting the game's code, finding things in an obfuscated assembly, icons, and why an earlier editor's saves were rejected by the game. |

## Disclaimer

This is an unofficial, fan-made tool. It is not affiliated with, endorsed by or supported by Stygian Software.
Underrail and all of its icons, portraits, artwork, text, game data, code and other resources and intellectual property
are © Stygian Software. This repository contains none of them. The editor reads them from the user's own installation at run time.

Back up your saves before editing. Use at your own risk.

## Author

**klowd92** ([GitHub](https://github.com/klowd92) · [Reddit](https://www.reddit.com/user/klowd92))
