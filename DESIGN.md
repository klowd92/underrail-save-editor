# Design

## Why v1 failed

v1 re-implemented the game's classes and serialized them with its own BinaryFormatter setup.
The game wrote nothing wrong, but v1's files declare the editor's assembly for every type:

```
game:  ...0c 02 00 00 00 01 'X' ...                        (assembly alias "X", type "TG")
v1:    ...0c 02 00 00 00 'UnderrailSaveEditor, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null' ...
```

The game resolves types through its own binder, which only knows its short aliases (`X` = tag lookup, `G` = encoded generics).
On the very first type it throws `Unable to resolve data element type: TG`, and the game reports the save as corrupted.
v1's binder handled `BindToType` (reading) but not `BindToName` (writing).

Nine saves in `Documents\My Games\Underrail\Saves` were written this way and the game can't load them
(`Junkyard new`, `newsave`, `bunker_modified`, `cp_arena-2`, `crafting11_modified`, `crafting11_original`, `craftingtest`, `cral`, `testing`).

## Approach: the game as a library

`underrail.exe` is a 32-bit .NET Framework 4 assembly. Any x86 net48 process can load it and call its code.
The game saves like this (decompiled names from build 1.3.1.2):

```csharp
// global.dat
eoq.SerializeToBinaryFileCompressed<cyt>(path, state, dds.a(), eh9.c, true);
// info.dat
eoq.SerializeToBinaryFile<a3r>(path, info, dds.a(), eh9.c, true);
```

The editor calls the same methods with the same binder and version.

Verification on real saves:

- Load and save with no edits, then a structural diff of the two object graphs (~180k paths):
  - identical except `TG:FR`, a `System.Random` the game re-creates on every load;
  - and `TG:DS`, the field added in data version 707 (the save was 706).
- Edits change only the edited values plus what the game's own recalculation derives from them (carry capacity, weapon damage, ...).

### Startup (mirrors the game's own startup)

1. `Assembly.LoadFrom(underrail.exe)`, with an `AssemblyResolve` handler for the game's DLLs; the working directory is set to the game folder.
2. Create the game's assembly resolver, build its tag-to-type map, and assign it to the binder.
   Building the map also sets the current data model version (`[b3d(708)]`).
   Without this the binder throws "Assembly resolver not set!", and files would carry minor version 0, which the game reads as the legacy format.
3. Register the resolver as a service (`bpo.RegisterService<ei2>`); character recalculation looks it up there.
4. Register a stand-in for the game window's host service (`d67`).
   Recalculation loads equipped item definitions through it, and only needs the content root (`<game>\data`).
   It is a `RealProxy` that answers every string member with that folder.

No other game services are started. No graphics, audio or GOG Galaxy.

## Finding things in an obfuscated assembly

Obfuscated names change with every game build (`dds` was `dc5` one build earlier), so nothing refers to them. Handles used instead:

| Need | Handle |
|---|---|
| Root types (`TG` game state, `SGI` save info, `PC1` player character) | `binder.BindToType("X", tag)`: the game's own tag lookup |
| Serializer | the type with `DeserializeFromBinaryFileCompressed` (real name) |
| Data model version | static class with a non-public `long` equal to 10^10; its public `long` is the current version |
| A field behind a serialization key (`PC1:E` experience, `TG:PC` player, `S4:V` stat value...) | IL of the declaring type's `GetObjectData`: the `ldfld` that follows `ldstr "<key>"` (`Il.FieldForKey`) |
| Stat setter | `Stat<int>` (real name): its only public `void(int)` |
| Feat add / remove | the feat collection's `bool X(Type)` method whose IL calls `List<>.Add` / `List<>.Remove` |
| Feat registry | static class with `GetFeats<T>` / `GetFeat<T>` (real names), private `Dictionary<string, feat>` |
| Specialization registry | static class with `GetSpecializations<T>` (real name) |
| Host service interface | the interface with `get_TargetElapsedTime` |
| Character properties | real public names on the character class: `Name`, `Level`, `Gender`, `BaseAbilities`, `Skills`, `Feats`, `Specializations`, `Immunities`, `CarryCapacity`, ... |
| Game variables | `Ouroboros.Common.Data.PropertyCollection` (real name and API) |
| Game version | a new `SGI` object is stamped with it in its constructor |
| Item data | instance properties keep real names (`Stacks`, `Definition`, `Durability`, `QualityLevel`...); definition fields by key (`I:N` name, `I:MS` max stack, `I:I` indestructible) |
| Bag | character `Bag`: `List<item> X()` lists it; `void X(item, bool)` adds (merging stacks); `bool X(item)` whose IL checks an argument named `"item"` removes |
| Equipment | character `Gear`: `Dictionary<string, item> X(bool)` with **false** (true also unequips everything) |
| Item library | static class with `GetItemDefinition` + `CreateDefinitonProvider`; private static `Dictionary<path, definition>` cache |
| New item | `LibraryItemDefinitionProvider<T>(path)` (real name) wrapped in the instance class from the definition base's static `Type X(Type)` |
| Tooltip | the static method returning rich text (`RT`) with 9 parameters, (item, character, ...); read through keys `RT:L` / `RTL:S` / `RTLS:T` / `RTLS:P` / `P1:C` |
| Blueprints | every non-abstract subclass of the root class with `Slots` and `TechnicalName`; craft = public `(character, Dictionary<string, item>)` method |
| Blueprint slot | its constructor's IL maps parameters to fields: (filter, label, optional, count, bonus, ...); filter = the `iCOMF` interface's `bool X(item)` |

Lesson learned: don't read arbitrary static fields to find something. That runs static constructors of unrelated game classes, and one of them broke the type resolver.

## Editing semantics

- **Stats** (abilities, skills) are set through the game's setter. It fires the change event, and the character recalculates everything derived (`efk.bl()`).
  If the modified value didn't move, it is shifted by the same delta.
- **Feats** use the game's add/remove, so the feat's `ar`/`as` hooks run (grant a special attack, set Psi Empathy, raise an ability...).
- **Specializations**: same bookkeeping as the game (0 removes the entry).
- **Paired values**: base and current are both written, because the game recomputes current = base + effects.
  The pairs are `Immunities`/`ImmunitiesBase`, `Darkvision`/`NaturalDarkvision`, `Truesighted`/`NaturallyTruesighted`, `Ethereal`/`EtherealBase`, `SightRange`/`BaseSightRange`, `DetectionModifier`/`BaseDetectionModifier`.
- **info.dat**: character name and level are refreshed from the player on save, as the game does.
- **Older saves**: written with the current data model version. Version-dependent conversions happen in the game's deserialization constructors, which already ran on load.

## Items and crafting

- **Library items** are deserialized in parallel with the game's serializer. Its data-model-version context is `[ThreadStatic]`, so this is safe.
  They are then put into the item library's cache. This takes about 0.25 s; the game's own lookup path took 29 s for the same 1,463 files.
- **Tooltips** come from the game's UI code. It asks for services that only exist in a running game (interface-size settings, to pick fonts).
  While a tooltip is built, a fallback service provider hands out inert stubs: interfaces get proxies, values get zero, classes get a new instance.
  It is removed right after, so rules code never silently gets a stub.
  Without a loaded save, a blank player character is used, because the game always passes one.
- **Quality-less components**: the game has no flag; loot generators decide per call site.
  Barrels, scopes, cases and so on have flat skill requirements (25, 30...), while quality parts have multipliers below 5.
  `Item.HasQuality` uses that rule; the crafting spreadsheet project verified it on every component.
  Giving a quality to a flat part makes the game compute absurd requirements (Mechanics 2721).
- Crafting was checked against values verified in game: Super Steel Q120 + Regenerative Vest Q120 + Super Steel Q120 gives Mechanics 132, Tailoring 131, Electronics 74, Biology 74, value 40,440.
  Crafted items (their definitions are embedded, not library paths) survive save and reload.
- Not supported yet: equipping or unequipping items.

## Images (icons and portraits)

- **Textures** are XNA 4.0 `.xnb` files, LZX-compressed. Creating an XNA graphics device fails on some machines (`DeviceLostException`), so none is used.
  The bytes are unpacked with XNA's own internal `DecompressStream` (reflection), and the Texture2D payload is parsed and decoded in `Xnb` (Color, DXT1/3/5).
- **`icons.iconset`** is a game data file (tag `ISD`, read with the game's serializer).
  Each entry `ID` has a name (`ID:N`), a cell size (`ID:S`, 33px) and layers (`ID:P` → `IPD:TN` atlas, `IPD:P` grid cell, `IPD:A` opacity).
  Cells sit on a 34px grid with a 1px gutter: (x·34+1, y·34+1). Layers are composited in order with their opacity (e.g. Aimed Shot = 50% disc + figure + 35% glow).
- **Generated icons**: like the game's icon index, every class implementing the generator interface (one method returning `List<ID>`) is called.
  Blueprints are generators; they define crafted items' layered icons (`pistol.2.4.rs`, `glove.3.e2.m1`). `eub` defines the ammo icons.
- **Numbered icons** `Atlas.N` (item definitions' `I:VM`, e.g. `Components.123`) are cell (N % 10, N / 10) of the atlas, as in the game's lookup.
- **Feat icons**: `"Feat." + feat code`. Specializations use their feat's icon.
- **Portraits**: `data\portraits\<gender>\<name>_{s,l,h}.xnb`; the name is the character's `Portrait` (`C1:P2`).
  The save list doesn't load each save; `SaveSummary.PeekPortrait` decompresses `global.dat` and takes the first string record that names a known portrait.
  The player is the first object serialized. Verified against full loads on all 100 readable saves; about 0.5 s for 109 saves.
- **Feat requirements**: the feat's `List<RQX> X()`, each requirement's `ToString()` (the game's own wording: "Agility 7", "Any psi skill 40", "Feat: Psi Empathy") and its `bool X(character)` for met/unmet. This is what the game's feat screen shows.
- **Skill details**: description `Localize("Skills/" + Name + variant)`, related abilities from `BaseAbilityBonuses` and synergies from `Synergies` (real property names, keyed by ability/skill class), as on the game's skill screen.
- **Descriptions**: feats `Localize("Feats/" + key, args)` and specializations `Localize("Specialization/" + id, args)`, the same calls the game's screens make.
  The localization class is found as the static class whose `string X(string)` splits keys on "/".

## UI

The v1 WPF views and theme are unchanged.
The view models now edit the live game objects: every edit writes through, the game recalculates, and every tab re-reads (`TabViewModelBase.Refresh`).
So effective values, remaining points and feat side effects show immediately.
Lists come from the game: skills and categories, abilities, feats, specializations, immunity flags and genders.
