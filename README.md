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
- **Variables**: the global quest and world-state flags. *Show all variables* also lists the ~5,000 variables the game's dialogs use that the save hasn't set yet,
  with their type and the dialogs that use them, so they can be set too.

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

## Known limitations and TODO

**Variables** (*Show all variables* lists the variables found in the game's dialogs; some are not covered):
- [ ] **Map triggers.** Zone maps (`data\maps`) also set some variables. A sample of 400 of the ~2,500 zone files found only one new name, and a full scan takes minutes, so maps aren't scanned yet. A one-time background scan with a cache would cover them.
- [ ] **Variables used only in the game's code** (about 90 seen in saves, e.g. faction counters). The code's string literals mix state variables with item and entity property names, which can't yet be told apart reliably, so they aren't listed.
- **Variables created at run time** can't be known in advance: event start times, per-boat discovery flags (`BoatFullyDiscovered_<id>`), music overrides and map discovery flags. They appear once a save has them.
- Dialog-only scratch variables (`tempcontext.`) are deliberately not listed: the game never saves them. A few misspelled paths in the game's own data (`contex.`, `tempcortex.`) are skipped too.

**Inventory**
- [ ] Equipping and unequipping items. Equipped items can be inspected and edited, but not moved or removed.

**Compatibility**
- Tested with Underrail 1.3.1.2 on Windows. Other game versions run after a self-check and a warning; please report problems.
- [ ] Linux / Steam Deck (Proton, Wine): not tested. The game runs there, but the editor's WPF interface probably needs extra setup.

**Crafting**
- Some slots take several of the same component (the Minigun's barrel slot takes 6). The simulator fills slots with the required quantity, but in the game you need a stack of that size: with 5 barrels the game refuses to craft, without an error message.

## Disclaimer

This is an unofficial, fan-made tool. It is not affiliated with, endorsed by or supported by Stygian Software.
Underrail and all of its icons, portraits, artwork, text, game data, code and other resources and intellectual property
are © Stygian Software. This repository contains none of them. The editor reads them from the user's own installation at run time.

Back up your saves before editing. Use at your own risk.

## Author

**klowd92** ([GitHub](https://github.com/klowd92) · [Reddit](https://www.reddit.com/user/klowd92))
