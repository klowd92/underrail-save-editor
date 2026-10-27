# Underrail Save Editor v2

Saves are read and written by the game's own code (underrail.exe loaded as a library). See DESIGN.md before changing Core.

## Build (from WSL)

The Ubuntu dotnet SDK can't build WPF; use the Windows SDK:
```
"/mnt/c/Program Files/dotnet/dotnet.exe" build "$(wslpath -w UnderrailSaveEditor.sln)" -c Release -v q -nologo
```
Built .exe files need `chmod +x` before running them from WSL. Close the running editor first (`powershell.exe -Command "Stop-Process -Name UnderrailSaveEditor"`) or the build can't overwrite it.

## Test

- `SaveTool.exe check` runs the startup self-check (all game hooks resolve, tested version). Run it after a game update.
- `tools/SaveTool/bin/Release/net48/SaveTool.exe info|catalog|items|library [text|quality]|blueprints [tech]|craft <save|-> <tech> slot=path:Q...|edit ...` (Windows paths; the game folder comes from UNDERRAIL_DIR or auto-detection).
  `edit` ops include `item+:path:count:quality`, `item-:path`, `craft+:Tech|slot=path:Q|...`.
- GUI checks: drive the app with UI Automation from `powershell.exe` and capture the window with `PrintWindow`. `Invoke` on a button that opens a modal dialog blocks, so invoke it from a second runspace; owned dialogs appear under the main window in the automation tree, not at the desktop root. Virtualized lists only expose visible rows, so filter first.
- `python3 tools/savediff.py a/global.dat b/global.dat` shows what an edit changed. Expect `TG:FR` (Random re-created on every load) and the default weapon's item id to differ always.
- Never write into an existing save of the user's; write to a new folder (Save As / `edit <save> <newDir>`).
- Saves written by the v1 editor contain the assembly name `UnderrailSaveEditor` and can't be loaded by the game or by Core.

## Game updates

Tested versions live in `GameRuntime.TestedGameVersions` / `TestedDataModelVersions` (GameRuntime.Check.cs).
After verifying a new game version (`check`, a round-trip plus `savediff.py`, and an in-game load), add it there.

## Rules for Core

- Never hard-code obfuscated names (they change every game build). Use serialization tags, keys (`Il.FieldForKey`), real public names, or IL patterns.
- Don't read arbitrary static fields while searching for something: it runs unrelated static constructors and broke the type resolver once.
- Decompiled source for the current build: `~/projects/underrail/underrail/` (obfuscated names in it match underrail.exe 1.3.1.2).
