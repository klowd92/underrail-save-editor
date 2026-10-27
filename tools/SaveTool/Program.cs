using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnderrailSaveEditor.Core;

// Command-line front end to UnderrailSaveEditor.Core, used for testing and scripting.
//   SaveTool info <save>                      dump the player character
//   SaveTool catalog                          list feats and specializations known to the game
//   SaveTool edit <save> <outDir> [ops...]    load, apply ops, save to outDir (outDir may equal <save>)
// ops: name=X level=N xp=N points:skill|feat|ability|spec=N ability:Strength=N skill:Guns=N
//      feat+:<id> feat-:<id> spec:<id>=N immunity+:<Name> immunity-:<Name> var:<name>=<value> var-:<name>
// The game directory comes from UNDERRAIL_DIR or is auto-detected.
static class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: SaveTool info <save> | catalog | edit <save> <outDir> [ops...]");
            return 2;
        }
        var sw = Stopwatch.StartNew();
        var gameDir = Environment.GetEnvironmentVariable("UNDERRAIL_DIR") ?? GameLocator.FindGameDirectory()
            ?? throw new DirectoryNotFoundException("Underrail not found; set UNDERRAIL_DIR.");
        var rt = GameRuntime.Load(gameDir);
        Console.WriteLine($"game {rt.GameVersion} at {rt.GameDirectory}, data model {SaveSummary.FormatVersion(rt.DataModelVersion)} ({sw.ElapsedMilliseconds} ms)");

        switch (args[0])
        {
            case "info":
                Dump(rt, SaveGame.Load(rt, args[1]));
                break;
            case "catalog":
                foreach (var f in rt.Feats)
                    Console.WriteLine($"feat {f.Id,-28} {f.Name,-28} {f.DisplayName,-30} lvl {f.MinimalLevel,2} {(f.Purchasable ? "" : "[not purchasable]")} {(f.Visible ? "" : "[hidden]")} {f.Dlc}");
                foreach (var s in rt.Specializations)
                    Console.WriteLine($"spec {s.Id,-36} {s.FullName,-40} max {s.MaxPoints} feat {s.RequiredFeat?.Id}");
                Console.WriteLine($"{rt.Feats.Count} feats, {rt.Specializations.Count} specializations");
                break;
            case "items":
            {
                var save = SaveGame.Load(rt, args[1]);
                var sw2 = Stopwatch.StartNew();
                foreach (var item in save.Player.Equipment.Concat(save.Player.Inventory))
                    Console.WriteLine($"{item.Location,-14} {item.Name,-40} x{item.Stacks,-4} {(item.HasQuality ? "Q" + item.Quality : ""),-5} {(item.HasDurability ? $"dur {item.Durability:0}/{item.MaxDurability}" : ""),-14} {item.LibraryPath}  icon={item.IconName} {(rt.Images.Icon(item.IconName) == null ? "MISSING" : "")}");
                var first = save.Player.Equipment.FirstOrDefault();
                if (first != null)
                {
                    Console.WriteLine($"--- tooltip of {first.Name}:");
                    Console.WriteLine(Colored(first.Tooltip(save.Player)));
                }
                Console.WriteLine($"({sw2.ElapsedMilliseconds} ms)");
                break;
            }
            case "library":
            {
                var sw2 = Stopwatch.StartNew();
                var lib = rt.LibraryItems;
                Console.WriteLine($"{lib.Count} library items, {lib.Count(i => i.IsComponent)} components ({sw2.ElapsedMilliseconds} ms)");
                if (args.Length >= 2 && args[1] == "quality")
                {
                    foreach (var g in lib.Where(i => i.IsComponent).GroupBy(i => rt.SampleItem(i)?.HasQuality == true))
                        Console.WriteLine($"quality={g.Key} ({g.Count()}): " + string.Join(", ", g.Select(i => i.Name).Take(45)));
                    break;
                }
                foreach (var li in lib.Where(i => args.Length < 2 || i.Name.IndexOf(args[1], StringComparison.OrdinalIgnoreCase) >= 0).Take(40))
                    Console.WriteLine($"  {li.Category,-14} {li.Name,-40} {li.Path}{(li.IsComponent ? " [component]" : "")}");
                break;
            }
            case "blueprints":
            {
                var sw2 = Stopwatch.StartNew();
                foreach (var bp in rt.Blueprints.Where(b => args.Length < 2 || b.TechnicalName.Equals(args[1], StringComparison.OrdinalIgnoreCase)))
                {
                    Console.WriteLine($"{bp.Category,-11} {bp.Name,-32} [{bp.TechnicalName}]");
                    foreach (var slot in bp.Slots)
                        Console.WriteLine($"     {slot.Key,-16} {slot.DisplayName,-24} {(slot.Optional ? "optional" : "required")} x{slot.Count} bonus {slot.OptionalBonus}"
                            + (args.Length >= 2 ? "  <- " + string.Join(", ", slot.Candidates.Take(12).Select(c => c.Name)) + $" ({slot.Candidates.Count})" : ""));
                }
                Console.WriteLine($"{rt.Blueprints.Count} blueprints ({sw2.ElapsedMilliseconds} ms)");
                break;
            }
            case "craft":
            {
                // craft <save|-> <TechnicalName> slotKey=libraryPath:quality ...
                var save = args[1] == "-" ? null : SaveGame.Load(rt, args[1]);
                var bp = rt.Blueprints.Single(b => b.TechnicalName.Equals(args[2], StringComparison.OrdinalIgnoreCase));
                var parts = new System.Collections.Generic.Dictionary<string, Item>();
                foreach (var a in args.Skip(3))
                {
                    var kv = a.Split('=');
                    var pq = kv[1].Split(':');
                    parts[kv[0]] = rt.CreateItem(pq[0], 1, pq.Length > 1 ? int.Parse(pq[1]) : (int?)null);
                }
                var sw2 = Stopwatch.StartNew();
                var result = rt.Craft(bp, parts, save?.Player);
                Console.WriteLine($"success {result.Success} error '{result.Error}' ({sw2.ElapsedMilliseconds} ms)");
                Console.WriteLine("requires " + string.Join(", ", result.SkillRequirements.Select(kv => $"{kv.Key} {kv.Value}")));
                if (result.Item != null)
                    Console.WriteLine(Colored(result.Item.Tooltip(save?.Player)));
                break;
            }
            case "check":
            {
                var problems = rt.SelfCheck();
                Console.WriteLine($"tested version: {rt.IsTestedVersion}; self-check problems: {problems.Count}");
                foreach (var pr in problems)
                    Console.WriteLine("  " + pr);
                return problems.Count == 0 ? 0 : 1;
            }
            case "portraits":
            {
                var sw2 = Stopwatch.StartNew();
                int n = 0;
                foreach (var dir in System.IO.Directory.GetDirectories(args[1]))
                {
                    n++;
                    string peek = null, real = null;
                    try { peek = SaveSummary.PeekPortrait(rt, dir); } catch (Exception ex) { peek = "ERR " + ex.Message; }
                    if (args.Length > 2) { try { real = SaveGame.Load(rt, dir).Player.Portrait; } catch (Exception) { real = "?"; } }
                    Console.WriteLine($"{System.IO.Path.GetFileName(dir),-40} {peek,-20} {real}{(real != null && real != "?" && real != peek ? "  <-- MISMATCH" : "")}");
                }
                Console.WriteLine($"{n} saves ({sw2.ElapsedMilliseconds} ms)");
                break;
            }
            case "describe":
            {
                foreach (var f in rt.Feats.Where(f => f.Id.Equals(args[1], StringComparison.OrdinalIgnoreCase) || f.DisplayName.Equals(args[1], StringComparison.OrdinalIgnoreCase)))
                {
                    Console.WriteLine($"FEAT {f.DisplayName}: {f.Description}");
                    var who = args.Length > 2 ? SaveGame.Load(rt, args[2]).Player : null;
                    foreach (var r in f.Requirements(who))
                        Console.WriteLine($"   requires {r.Text} {(r.Met == null ? "" : r.Met.Value ? "[met]" : "[NOT met]")}");
                }
                if (args.Length > 2)
                    foreach (var k in SaveGame.Load(rt, args[2]).Player.Skills.Where(k => k.Name.Equals(args[1], StringComparison.OrdinalIgnoreCase)))
                        Console.WriteLine($"SKILL {k.Name}: {k.Description}\n   abilities: {string.Join(" or ", k.RelatedAbilities)}\n   synergies: {string.Join(", ", k.Synergies.Select(x => $"{x.Key} {x.Value:P0}"))}");
                foreach (var sp in rt.Specializations.Where(x => x.Id.StartsWith(args[1], StringComparison.OrdinalIgnoreCase)))
                    Console.WriteLine($"SPEC {sp.FullName}: {sp.Description}");
                Console.WriteLine("GENERAL feats: " + rt.Localize("Feats/GeneralInfo"));
                Console.WriteLine("GENERAL specs: " + rt.Localize("Specialization/GeneralInfo"));
                break;
            }
            case "image":
            {
                // image icon|portrait <name> <out.png>
                var sw2 = Stopwatch.StartNew();
                var img = args[1] == "portrait" ? rt.Images.Portrait(args[2]) : args[1] == "atlas" ? rt.Images.Atlas(args[2]) : rt.Images.Icon(args[2]);
                if (img == null) { Console.WriteLine("not found: " + rt.Images.LastError); return 1; }
                using (var bmp = new System.Drawing.Bitmap(img.Width, img.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
                {
                    var bits = bmp.LockBits(new System.Drawing.Rectangle(0, 0, img.Width, img.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);
                    System.Runtime.InteropServices.Marshal.Copy(img.Pixels, 0, bits.Scan0, img.Pixels.Length);
                    bmp.UnlockBits(bits);
                    bmp.Save(args[3], System.Drawing.Imaging.ImageFormat.Png);
                }
                Console.WriteLine($"{img.Width}x{img.Height} -> {args[3]} ({sw2.ElapsedMilliseconds} ms)");
                break;
            }
            case "edit":
            {
                var save = SaveGame.Load(rt, args[1]);
                Console.WriteLine($"loaded {save.FolderPath} ({sw.ElapsedMilliseconds} ms)");
                foreach (var op in args.Skip(3))
                    Apply(rt, save, op);
                var backup = save.Save(args[2], Path.Combine(Path.GetTempPath(), "UnderrailSaveEditorBackups"));
                Console.WriteLine($"saved to {args[2]} ({sw.ElapsedMilliseconds} ms){(backup != null ? ", backup in " + backup : "")}");
                Dump(rt, SaveGame.Load(rt, args[2]));
                break;
            }
            default:
                Console.Error.WriteLine("unknown command " + args[0]);
                return 2;
        }
        return 0;
    }

    static string Colored(RichText text) => string.Join("\n", text.Lines.Select(l =>
        string.Concat(l.Select(sp => $"\u001b[38;2;{sp.R};{sp.G};{sp.B}m{sp.Text}")) + "\u001b[0m"));

    static void Apply(GameRuntime rt, SaveGame save, string op)
    {
        var p = save.Player;
        string Arg(string prefix) => op.Substring(prefix.Length);
        (string key, string value) Kv(string prefix)
        {
            var s = Arg(prefix);
            int i = s.LastIndexOf('=');
            return (s.Substring(0, i), s.Substring(i + 1));
        }
        FeatInfo Feat(string id) => rt.Feats.FirstOrDefault(f => new[] { f.Id, f.Name, f.DisplayName.Replace(" ", "") }
            .Any(n => string.Equals(n, id, StringComparison.OrdinalIgnoreCase)))
            ?? throw new ArgumentException("unknown feat " + id);

        if (op.StartsWith("name=")) p.Name = Arg("name=");
        else if (op.StartsWith("level=")) p.Level = int.Parse(Arg("level="));
        else if (op.StartsWith("xp=")) p.Experience = int.Parse(Arg("xp="));
        else if (op.StartsWith("points:"))
        {
            var (k, v) = Kv("points:");
            int n = int.Parse(v);
            switch (k)
            {
                case "skill": p.SkillPoints = n; break;
                case "feat": p.FeatPoints = n; break;
                case "ability": p.AbilityPoints = n; break;
                case "spec": p.SpecializationPoints = n; break;
                default: throw new ArgumentException(op);
            }
        }
        else if (op.StartsWith("ability:")) { var (k, v) = Kv("ability:"); p.SetBaseAbility(k, int.Parse(v)); }
        else if (op.StartsWith("skill:")) { var (k, v) = Kv("skill:"); p.SetSkill(k, int.Parse(v)); }
        else if (op.StartsWith("feat+:")) Console.WriteLine($"  add feat {Arg("feat+:")}: {p.AddFeat(Feat(Arg("feat+:")))}");
        else if (op.StartsWith("feat-:")) Console.WriteLine($"  remove feat {Arg("feat-:")}: {p.RemoveFeat(Feat(Arg("feat-:")))}");
        else if (op.StartsWith("spec:")) { var (k, v) = Kv("spec:"); p.SetSpecialization(k, int.Parse(v)); }
        else if (op.StartsWith("immunity+:")) p.SetImmunity(Arg("immunity+:"), true);
        else if (op.StartsWith("immunity-:")) p.SetImmunity(Arg("immunity-:"), false);
        else if (op.StartsWith("item+:"))
        {
            var pq = Arg("item+:").Split(':');
            var item = rt.CreateItem(pq[0], pq.Length > 1 ? int.Parse(pq[1]) : 1, pq.Length > 2 ? int.Parse(pq[2]) : (int?)null);
            p.AddItem(item);
        }
        else if (op.StartsWith("craft+:"))
        {
            // craft+:<TechnicalName>|slot=path:quality|slot=path
            var parts = Arg("craft+:").Split('|');
            var bp = rt.Blueprints.Single(b => b.TechnicalName.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
            var components = parts.Skip(1).Select(x => x.Split('=')).ToDictionary(kv => kv[0], kv =>
            {
                var pq = kv[1].Split(':');
                return rt.CreateItem(pq[0], 1, pq.Length > 1 ? int.Parse(pq[1]) : (int?)null);
            });
            var result = rt.Craft(bp, components, p);
            if (!result.Success)
                throw new ArgumentException("craft failed: " + result.Error);
            p.AddItem(result.Item);
            Console.WriteLine($"  crafted {result.Item.Name}");
        }
        else if (op.StartsWith("item-:"))
        {
            var target = p.Inventory.First(i => string.Equals(i.LibraryPath, Arg("item-:"), StringComparison.OrdinalIgnoreCase) || i.Name == Arg("item-:"));
            Console.WriteLine($"  removed {target.Name}: {p.RemoveItem(target)}");
        }
        else if (op.StartsWith("var-:")) save.Variables.Remove(Arg("var-:"));
        else if (op.StartsWith("var:"))
        {
            var (k, v) = Kv("var:");
            save.Variables.Set(k, GameVariables.Parse(v, save.Variables.Get(k)?.GetType()));
        }
        else throw new ArgumentException("unknown op " + op);
        Console.WriteLine("  applied " + op);
    }

    static void Dump(GameRuntime rt, SaveGame save)
    {
        var s = save.Summary;
        var p = save.Player;
        Console.WriteLine($"save   {s.FolderName}: {s.CharacterName} L{s.CharacterLevel} @ {s.Location}, game {s.GameVersion}, format {SaveSummary.FormatVersion(s.DataModelVersion)}, saved {s.SaveTime:g}");
        Console.WriteLine($"player {p.Name} ({p.Gender}) L{p.Level} xp {p.Experience}; unspent: ability {p.AbilityPoints}, skill {p.SkillPoints}, feat {p.FeatPoints}, spec {p.SpecializationPoints}");
        Console.WriteLine("abilities " + string.Join(", ", p.BaseAbilities.Select(a => $"{a.Name} {a.Value}({a.ModifiedValue})")));
        Console.WriteLine("skills    " + string.Join(", ", p.Skills.Select(a => $"{a.Name} {a.Value}({a.ModifiedValue})")));
        Console.WriteLine("feats     " + string.Join(", ", p.Feats.Select(f => f.Name)));
        Console.WriteLine("specs     " + string.Join(", ", rt.Specializations
            .Select(x => (x.Id, v: p.GetSpecialization(x.Id))).Where(x => x.v > 0).Select(x => $"{x.Id}={x.v}")));
        Console.WriteLine($"caps      move {p.MovementSpeed}/{p.BaseMovementSpeed} carry {p.CarryCapacity} sight {p.SightRange} darkvision {p.Darkvision} truesight {p.Truesighted} detection {p.Detection} detmod {p.DetectionModifier} dead {p.Dead} ethereal {p.Ethereal} psiEmpathy {p.PsiEmpathy}");
        Console.WriteLine("immune    " + string.Join(", ", p.ImmunityOptions.Where(p.HasImmunity)));
        Console.WriteLine($"variables {save.Variables.All.Count}");
    }
}
