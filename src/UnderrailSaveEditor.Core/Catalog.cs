using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace UnderrailSaveEditor.Core
{
    /// <summary>A feat as defined by the game (one singleton instance per feat class in the game's registry).</summary>
    public sealed class FeatInfo
    {
        internal FeatInfo(GameRuntime rt, string id, object feat)
        {
            _rt = rt;
            Id = id;
            GameObject = feat;
            GameType = feat.GetType();
            Name = Read<string>("Name");
            DisplayName = Read<string>("LocalizedName");
            if (string.IsNullOrWhiteSpace(DisplayName) || DisplayName.StartsWith("{", StringComparison.Ordinal))
                DisplayName = Text.Humanize(string.IsNullOrWhiteSpace(Name) ? id : Name);
            MinimalLevel = Read<int>("MinimalLevel");
            Purchasable = Read<bool>("Purchasable");
            Visible = Read<bool>("Visible");
            Veteran = Read<bool>("Veteran");
            Dlc = Read<string>("DLC");
        }

        private readonly GameRuntime _rt;
        private string _description;

        public string Id { get; }
        public string Name { get; }
        public string DisplayName { get; }

        /// <summary>The game's description of the feat (localized, with the feat's numbers filled in).</summary>
        public string Description => _description ?? (_description = _rt.FeatDescription(GameObject));
        public int MinimalLevel { get; }
        /// <summary>False for feats the game grants through story/oddities rather than level-up picks.</summary>
        public bool Purchasable { get; }
        public bool Visible { get; }
        public bool Veteran { get; }
        public string Dlc { get; }

        /// <summary>
        /// The feat's requirements as the game words them ("Agility 7", "Any psi skill 40", "Feat: Psi Empathy"),
        /// each with whether <paramref name="character"/> meets it (null when no character is given).
        /// </summary>
        public IReadOnlyList<FeatRequirement> Requirements(Character character = null)
        {
            var result = new List<FeatRequirement>();
            var requirementType = _rt.TypeForTag("RQX");
            var listMethod = GameType.GetMethods(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(m =>
                m.GetParameters().Length == 0 && m.ReturnType == typeof(List<>).MakeGenericType(requirementType));
            System.Collections.IEnumerable list;
            try { list = (System.Collections.IEnumerable)listMethod?.Invoke(GameObject, null); }
            catch (Exception) { return result; }
            // Each requirement's bool X(character) tells whether the character meets it.
            var check = requirementType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.ReturnType == typeof(bool) && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == _rt.TypeForTag("C1"));
            foreach (var requirement in list ?? new object[0])
            {
                bool? met = null;
                if (character != null && check != null)
                {
                    try { met = (bool)check.Invoke(requirement, new[] { character.GameObject }); }
                    catch (Exception) { }
                }
                string text;
                try { text = requirement.ToString(); }
                catch (Exception) { text = requirement.GetType().Name; }
                result.Add(new FeatRequirement(text, met));
            }
            return result;
        }

        /// <summary>The game's icon name for the feat ("Feat." + feat code).</summary>
        public string IconName => "Feat." + Id;
        internal Type GameType { get; }
        internal object GameObject { get; }

        public override string ToString() => DisplayName;

        private T Read<T>(string property)
        {
            try { return (T)GameObject.GetType().GetProperty(property).GetValue(GameObject); }
            catch (Exception) { return default; } // a few getters need in-game services (e.g. localization)
        }
    }

    public sealed class FeatRequirement
    {
        internal FeatRequirement(string text, bool? met)
        {
            Text = text;
            Met = met;
        }

        public string Text { get; }
        /// <summary>Whether the character meets it; null if unknown.</summary>
        public bool? Met { get; }

        public override string ToString() => Text;
    }

    /// <summary>A specialization (spent with specialization points; each belongs to a feat, e.g. Gunslinger: Initiative).</summary>
    public sealed class SpecializationInfo
    {
        internal SpecializationInfo(GameRuntime rt, string id, object definition, IDictionary<Type, FeatInfo> featsByType)
        {
            _rt = rt;
            _definition = definition;
            Id = id;
            var methods = definition.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.IsAbstract == false && m.GetBaseDefinition().DeclaringType.IsAbstract
                    && m.GetBaseDefinition().IsAbstract && m.GetParameters().Length == 0)
                .ToList();

            // The definition's abstract members are: required feat type, id, display label ("Blitz: Cooldown") and max points.
            var featType = methods.Where(m => m.ReturnType == typeof(Type)).Select(m => (Type)m.Invoke(definition, null)).FirstOrDefault();
            var label = methods.Where(m => m.ReturnType == typeof(string)).Select(m => (string)m.Invoke(definition, null))
                .FirstOrDefault(s => s != id);
            MaxPoints = methods.Where(m => m.ReturnType == typeof(int)).Select(m => (int)m.Invoke(definition, null)).FirstOrDefault();
            if (featType != null && featsByType.TryGetValue(featType, out var feat))
                RequiredFeat = feat;
            DisplayName = string.IsNullOrWhiteSpace(label) ? Text.Humanize(id) : label;
            Group = RequiredFeat?.DisplayName ?? "";
        }

        private readonly GameRuntime _rt;
        private readonly object _definition;
        private string _description;

        public string Id { get; }

        /// <summary>The game's description of the specialization (localized, with its numbers filled in).</summary>
        public string Description => _description ?? (_description = _rt.SpecializationDescription(Id, _definition));
        public string Group { get; }
        public string DisplayName { get; }
        public int MaxPoints { get; }
        public FeatInfo RequiredFeat { get; }

        /// <summary>The game's label, prefixed with the feat when the label doesn't already say it.</summary>
        public string FullName => Group.Length == 0 || DisplayName.StartsWith(Group, StringComparison.OrdinalIgnoreCase)
            ? DisplayName
            : $"{Group}: {DisplayName}";
        public override string ToString() => FullName;
    }

    public static class Text
    {
        /// <summary>"GunslingerInitiative" -> "Gunslinger Initiative", "aimed_shot" -> "Aimed Shot".</summary>
        public static string Humanize(string s)
        {
            if (string.IsNullOrEmpty(s))
                return s;
            s = s.Replace('_', ' ');
            s = Regex.Replace(s, "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");
            return string.Join(" ", s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));
        }
    }
}
