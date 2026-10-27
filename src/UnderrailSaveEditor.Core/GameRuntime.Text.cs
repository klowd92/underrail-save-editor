using System;
using System.Linq;
using System.Reflection;

namespace UnderrailSaveEditor.Core
{
    public sealed partial class GameRuntime
    {
        private MethodInfo _localize;
        private MethodInfo _localizeFormat;

        /// <summary>
        /// The game's localized text for a key such as "Feats/GeneralInfo" (data\knowledge\*.k files), or "" if unknown.
        /// Found as the static class whose string X(string) splits keys on "/" and that also has string Y(string, params object[]).
        /// </summary>
        public string Localize(string key, params object[] args)
        {
            if (_localize == null)
            {
                foreach (var t in _types.Where(t => t.IsAbstract && t.IsSealed))
                {
                    var methods = t.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.ReturnType == typeof(string)).ToList();
                    var plain = methods.FirstOrDefault(m => m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string) && Il.LoadsString(m, "/"));
                    var format = methods.FirstOrDefault(m => m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType == typeof(object[])
                        && m.GetParameters()[1].IsDefined(typeof(ParamArrayAttribute)));
                    if (plain != null && format != null)
                    {
                        _localize = plain;
                        _localizeFormat = format;
                        break;
                    }
                }
            }
            try
            {
                var text = args != null && args.Length > 0
                    ? (string)_localizeFormat?.Invoke(null, new object[] { key, args })
                    : (string)_localize?.Invoke(null, new object[] { key });
                return text ?? "";
            }
            catch (Exception) { return ""; }
        }

        /// <summary>A feat's description, formatted with the feat's own numbers (as the game's feat screen does).</summary>
        internal string FeatDescription(object feat)
        {
            var t = feat.GetType();
            var methods = t.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => !m.IsSpecialName && m.GetParameters().Length == 0).ToList();
            // The text key: the virtual string method that joins the feat code and a variant with "-".
            var key = methods.FirstOrDefault(m => m.ReturnType == typeof(string) && m.IsVirtual && Il.LoadsString(m.GetBaseDefinition(), "-"));
            var args = methods.FirstOrDefault(m => m.ReturnType == typeof(object[]));
            try
            {
                return Localize("Feats/" + (string)key?.Invoke(feat, null), (object[])args?.Invoke(feat, null) ?? new object[0]);
            }
            catch (Exception) { return ""; }
        }

        /// <summary>
        /// A skill's description, like the game's skill screen: "Skills/" + name + the skill's variant suffix
        /// (its own virtual string method, e.g. for DLC-changed skills).
        /// </summary>
        internal string SkillDescription(object skill, string name)
        {
            var skillBase = TypeForTag("S5");
            var variant = skill.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(m =>
                !m.IsSpecialName && m.IsVirtual && m.ReturnType == typeof(string) && m.GetParameters().Length == 0
                && m.Name != nameof(ToString) && m.GetBaseDefinition().DeclaringType == skillBase);
            string suffix = "";
            try { suffix = (string)variant?.Invoke(skill, null) ?? ""; }
            catch (Exception) { }
            var text = Localize("Skills/" + name + suffix);
            return string.IsNullOrEmpty(text) ? Localize("Skills/" + name) : text;
        }

        /// <summary>A specialization's description, formatted with its own numbers.</summary>
        internal string SpecializationDescription(string id, object definition)
        {
            var args = definition.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.ReturnType == typeof(object[]) && m.GetParameters().Length == 0);
            try { return Localize("Specialization/" + id, (object[])args?.Invoke(definition, null) ?? new object[0]); }
            catch (Exception) { return ""; }
        }
    }
}
