# Research notes

Findings about the game itself, from the decompiled source (`~/projects/underrail/underrail/`, matches 1.3.1.2) and the data files.
Class names below are the obfuscated 1.3.1.2 names; they change every build, so search for the quoted strings or tags instead.

The [Underrail wiki](https://www.stygiansoftware.com/wiki/) already documents most feats, items and how to get them. Check it first
and spend effort on what it marks unknown.

## How to investigate

**Loading underrail.exe from a script.** It is a 32-bit assembly.
- Use the 32-bit Windows PowerShell, `/mnt/c/Windows/SysWOW64/WindowsPowerShell/v1.0/powershell.exe`. The default 64-bit one fails with `BadImageFormatException`.
- `Set-Location` to the game folder, set `[Environment]::CurrentDirectory` to it as well, then call `[Reflection.Assembly]::LoadFrom`, so its dependencies resolve.
- `GetTypes()` throws `ReflectionTypeLoadException`; use its `.Types` without the nulls.

**Loading our own DLLs from WSL paths.** `\\wsl.localhost\...` paths fail with 0x80131515 (network location). Use
`[Reflection.Assembly]::Load([IO.File]::ReadAllBytes(...))` instead.

**Renamed overrides.** The obfuscator renames virtual property overrides. For example, the feat base class declares `Enabled`, and
Assassinate overrides its getter with a method called `ae6()`. This has two consequences:
- PowerShell's `$feat.Enabled` returns nothing for such overrides. Call `$baseType.GetProperty('Enabled').GetValue($feat, $null)`.
- Grepping the decompiled source for `override bool Enabled` misses them.

**Decompiled `.cs` files.** They are UTF-8 with BOM and CRLF line endings, so grep's `$` anchor never matches. Use Python, or `\r?$`.

**Data files.** The layout is a 16-byte GUID, 8 more bytes, then an MS-NRBF stream, gzip-compressed when bytes 24..25 are `1f 8b`
(items and maps are). Always parse with `nrbf.py` (from `~/projects/underrail-item-crafter/tools`). A hand-rolled byte scan found
nothing at all.

Skip `.xnb` files (about 74,000 textures and sounds). A full scan of the remaining ~14,000 files takes about a minute. Don't `str()` an
nrbf object graph: it is cyclic and hangs. Print types or chosen fields only.

**Reading a dialog's tags.** Find the class for a tag with `grep -l '^\[eiz("TAG")\]' *.cs`. Common ones:
- `BC`: bool variable check
- `SBFV` / `MFFV` / `MIFV`: set bool / add float / add int
- `ICC` / `FCC`: int / float compare; `CT` codes are 1 ==, 2 >, 3 <, 4 >=, 5 <=, 6 !=
- `MPBAC` / `MXPBAC`: base ability at least / at most; ability enum order is Str, Dex, Agi, Con, Per, Will, Int
- `PHF`: has feat (`I` inverts)
- `EJA`: run a job
- `XJL`: a named script hook, `[ayh("NAME")]` in code

Dialog variables are `context.<name>` and are the same game variables code reads with `GetPrimitivePropertyValue<T>("<name>")`.

**"Is X obtainable?" — check every grant path before saying no:**
- direct `typeof(X)` in code;
- data tags:
  - `G09:QN`: dialog grants a feat
  - `FR:FTN`: an NPC has a feat
  - `BSI:BN`: blueprint item
  - `B:N`: terminal download
  - `DLBPJ:BN`: script teaches a blueprint
- string keys inside `[ayh]` script hooks. The Fusion Cannon and Fusion Emitter blueprints are taught this way, by using the frame and
  emitter you get from disassembling the Heavy Duty fusion cannon;
- the item generator pools (`dtf`) for merchant and loot stock;
- dev-console commands (`pgiveblueprint`, `ceo` copy-character).

Some content only exists in some playthroughs: see *Fragments*.

## Feats

The level-up screen (`eis`) lists a feat only if all of these hold:
- `Enabled` and `Visible`;
- `Purchasable`, or the character already has it;
- not `Veteran` (unless the character is past the level cap);
- its DLC is installed;
- `IsAvailableToMutant`, if the character is a mutant.

The editor lists every feat class regardless.

- **Disabled (`Enabled = false`)**: Assassinate and Shroomhead. Neither has any effect code.
  - **Assassinate** (key `"A"`): "a snipe dealing over 50% of remaining health instantly kills an unaware biological target". Nothing
    references the class, so it does nothing even on the two NPCs that have it (Ezra and Mordre at Mushroom Cove). It has no DLC tag and
    sits in a metadata block with the oldest feats, all with terse keys (`S` Snipe, `AS`, `IM`, `SS`, `PE`, `DS`), so it is almost
    certainly an early-alpha feat.
  - **Shroomhead**: an alpha feat (wiki), on one NPC (`invadingeel1`).
- **Not purchasable, granted by the game**: the tattoos and Philosophy I–III (tattoo booth); Psi Empathy, Fisherman, Fine Tuned,
  Shoemaker, Echoing Soliloquy and Beyond Cold (dialogs); the Expedition story feats; Forceful Innervation (code). Capacitor and Hunter
  appear on NPCs.

## Blueprints

There are 119 blueprints (registry `d79`, key = TechnicalName).
- A new character knows a starting set.
- Others come from blueprint items (`BSI`), terminal downloads, the Disassemble feat (Disassemble Item), and script hooks (fusion cannon
  and emitter).
- Mind Darts and Shield Recharger have blueprint items that are placed nowhere. Mind Darts is an alpha blueprint replaced by Trance.
- Two map items carry blueprint names that match no blueprint: a quest item, Optoelectric Nuclear Battery Blueprint (`cv7`), and a
  leftover placeholder named "Heavy" (`cc10`).

## Fragments

Optional content pieces (`data/fragments`) are picked once per playthrough from a seeded random (`a4i`). Each category has a chance
that is adjusted after each pick (`FRC:C`, `FRC:F`), and each fragment has a weight (`RFD:F`). The pick is recorded as the game variable
`frag_<id>`.

Example: `playerBuff_frags` gets exactly one fragment per game. Coral (`gc_gypsy`) has weight 6 against six fragments of weight 1,
so she is in roughly half of all games.

## Coral and charms

Full write-up for wiki maintainers: https://claude.ai/artifact/V6zT5JeKtVxSpo4sr9vNqq. In short:
- Effects live in `kl` and the charm state in `dgj`.
- Hidden belief `gp_sm` (0 to 1) multiplies every effect. Curse flag `gp_crp` and intensity `gp_crps` drive the curse.
- Most effects are gated on the PC's real clock (`DateTime.Now`).
- Dog, Rathound and White Cat paws do nothing.
- More charms on you means weaker bonuses and stronger penalties.

## Character defense values

The game's combat stats window shows these from the character's `Defense` object (tag `CD1`):

| Value | Key |
| --- | --- |
| Dodge | `CD1:D` |
| Evasion | `CD1:E` |
| Fortitude | `CD1:F` |
| Resolve | `CD1:R` |
| Weapon crit chance against you | `CD1:Q` |
| Psi crit chance against you | `CD1:PCM` |
| Armor penalty | `CD1:E1` (0 to 0.95) |
| Damage-taken multiplier per damage type | `CD1:DTM` |

The game recomputes them, so the editor shows them read-only.

Resist chance against an effect, from Fortitude or Resolve (`qw.a`):

```
clamp(5%, 1 - 0.7 * power / stat, 75%)
```
