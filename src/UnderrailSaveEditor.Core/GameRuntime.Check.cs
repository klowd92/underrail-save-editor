using System;
using System.Collections.Generic;
using System.Linq;

namespace UnderrailSaveEditor.Core
{
    public sealed partial class GameRuntime
    {
        /// <summary>Game versions the editor has been verified against (load, edit, save, reload, structural diff).</summary>
        public static readonly IReadOnlyList<Version> TestedGameVersions = new[] { new Version(1, 3, 1, 2) };

        /// <summary>Save data model versions written by the tested game versions (major.minor).</summary>
        public static readonly IReadOnlyList<long> TestedDataModelVersions = new[] { 44L * 10000000000L + 708 };

        public bool IsTestedVersion => TestedGameVersions.Contains(GameVersion) && TestedDataModelVersions.Contains(DataModelVersion);

        /// <summary>
        /// Resolves every game hook the editor depends on and returns the ones that fail. The editor finds the
        /// game's code by stable handles rather than obfuscated names, so it can survive game updates; this check
        /// catches an update that changed one of those handles before anything is edited or written.
        /// </summary>
        public IReadOnlyList<string> SelfCheck()
        {
            var problems = new List<string>();
            void Check(string what, Action probe)
            {
                try { probe(); }
                catch (Exception ex) { problems.Add($"{what}: {ex.GetBaseException().Message}"); }
            }

            foreach (var tag in new[] { "TG", "SGI", "C1", "PC1", "P2", "II", "I", "CI", "RT", "ISD", "ID", "RQX", "S5" })
                Check($"type tag {tag}", () => TypeForTag(tag));
            Check("game state fields", () =>
            {
                Il.FieldForKey(TypeForTag("TG"), "TG:PC");
                Il.FieldForKey(TypeForTag("TG"), "TG:GP");
            });
            Check("player points fields", () =>
            {
                foreach (var key in new[] { "PC1:E", "PC1:BAPC", "PC1:SPC", "PC1:FC", "PC1:SP" })
                    Il.FieldForKey(TypeForTag("PC1"), key);
            });
            Check("character properties", () =>
            {
                var t = TypeForTag("C1");
                foreach (var name in new[] { "Name", "Level", "Gender", "Portrait", "BaseAbilities", "Skills", "Feats", "Specializations",
                    "Bag", "Gear", "Immunities", "ImmunitiesBase", "MovementSpeed", "CarryCapacity" })
                {
                    if (t.GetProperty(name) == null)
                        throw new MissingMemberException(t.Name, name);
                }
            });
            Check("feat registry", () => { if (Feats.Count == 0) throw new InvalidOperationException("no feats"); });
            Check("specialization registry", () => { if (Specializations.Count == 0) throw new InvalidOperationException("no specializations"); });
            Check("item library", () => { if (LibraryItems.Count == 0) throw new InvalidOperationException("no items"); });
            Check("blueprints", () => { if (Blueprints.Count == 0) throw new InvalidOperationException("no blueprints"); });
            Check("localization", () => { if (string.IsNullOrEmpty(Localize("Feats/GeneralInfo"))) throw new InvalidOperationException("no text"); });
            return problems;
        }
    }
}
