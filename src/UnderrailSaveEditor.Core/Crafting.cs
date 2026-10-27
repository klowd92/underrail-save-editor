using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace UnderrailSaveEditor.Core
{
    /// <summary>A crafting blueprint as defined by the game (one class per blueprint).</summary>
    public sealed class Blueprint
    {
        internal Blueprint(object blueprint, IReadOnlyList<BlueprintSlot> slots)
        {
            GameObject = blueprint;
            var t = blueprint.GetType();
            object P(string name) { try { return t.GetProperty(name).GetValue(blueprint); } catch (Exception) { return null; } }
            TechnicalName = (string)P("TechnicalName");
            var localized = P("LocalizedName") as string;
            Name = string.IsNullOrWhiteSpace(localized) ? (string)P("Name") ?? TechnicalName : localized;
            Category = P("Category")?.ToString() ?? "";
            Slots = slots;
        }

        internal object GameObject { get; }
        public string Name { get; }
        public string TechnicalName { get; }
        public string Category { get; }
        public IReadOnlyList<BlueprintSlot> Slots { get; }

        public override string ToString() => Name;
    }

    /// <summary>A component slot of a blueprint ("metalPlate", "enhancement1"...).</summary>
    public sealed class BlueprintSlot
    {
        private readonly GameRuntime _rt;
        private readonly object _filter;
        private readonly MethodInfo _accepts;
        private IReadOnlyList<LibraryItem> _candidates;

        internal BlueprintSlot(GameRuntime rt, string key, object slot, IDictionary<int, FieldInfo> ctorFields)
        {
            _rt = rt;
            Key = key;
            object Arg(int index) => ctorFields.TryGetValue(index, out var f) ? f.GetValue(slot) : null;
            // Slot constructor: (filter, label, optional, count, optionalBonus, notConsumed, addsRequirement, type).
            _filter = Arg(0);
            Label = Arg(1) as string ?? key;
            Optional = Arg(2) as bool? ?? false;
            Count = Arg(3) as int? ?? 1;
            OptionalBonus = Arg(4) as float? ?? 0f;
            NotConsumed = Arg(5) as bool? ?? false;
            _accepts = _filter?.GetType().GetInterfaces()
                .SelectMany(i => i.GetMethods())
                .FirstOrDefault(m => m.ReturnType == typeof(bool) && m.GetParameters().Length == 1);
        }

        public string Key { get; }
        public string Label { get; }
        public string DisplayName => Text.Humanize(Label);
        public bool Optional { get; }
        public int Count { get; }
        public float OptionalBonus { get; }
        public bool NotConsumed { get; }

        /// <summary>True if the game's slot filter accepts this item.</summary>
        public bool Accepts(Item item)
        {
            if (_accepts == null)
                return true;
            try { return (bool)_accepts.Invoke(_filter, new[] { item.GameObject }); }
            catch (TargetInvocationException) { return false; }
        }

        /// <summary>Library items the slot accepts (computed once; uses the shared sample instances).</summary>
        public IReadOnlyList<LibraryItem> Candidates => _candidates ?? (_candidates =
            _rt.LibraryItems.Where(li => _rt.SampleItem(li) is Item sample && Accepts(sample)).ToList());

        public override string ToString() => DisplayName;
    }

    public sealed class CraftResult
    {
        internal CraftResult(Item item, IReadOnlyDictionary<string, int> requirements, bool success, string error)
        {
            Item = item;
            SkillRequirements = requirements;
            Success = success;
            Error = error;
        }

        /// <summary>The crafted item (null if the combination is invalid).</summary>
        public Item Item { get; }
        /// <summary>Skill name -> required level, as computed by the game.</summary>
        public IReadOnlyDictionary<string, int> SkillRequirements { get; }
        public bool Success { get; }
        /// <summary>The game's message when the combination is invalid ("Incompatible barrel type.").</summary>
        public string Error { get; }
    }

    public sealed partial class GameRuntime
    {
        private IReadOnlyList<Blueprint> _blueprints;
        private readonly Dictionary<string, Item> _samples = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Every blueprint class in the game (the base class has Slots and TechnicalName), like the game's own registry.</summary>
        public IReadOnlyList<Blueprint> Blueprints
        {
            get
            {
                if (_blueprints != null)
                    return _blueprints;
                bool IsBlueprint(Type t) => t != null && t.GetProperty("Slots") != null && t.GetProperty("TechnicalName") != null;
                var baseType = _types.Single(t => t.IsAbstract && IsBlueprint(t) && !IsBlueprint(t.BaseType));
                var list = new List<Blueprint>();
                foreach (var type in _types.Where(t => !t.IsAbstract && baseType.IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null))
                {
                    object bp;
                    try { bp = Activator.CreateInstance(type); }
                    catch (TargetInvocationException) { continue; }
                    var slots = new List<BlueprintSlot>();
                    foreach (DictionaryEntry e in (IDictionary)baseType.GetProperty("Slots").GetValue(bp))
                    {
                        var ctor = e.Value.GetType().GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
                        slots.Add(new BlueprintSlot(this, (string)e.Key, e.Value, Il.ParameterFields(ctor)));
                    }
                    list.Add(new Blueprint(bp, slots));
                }
                return _blueprints = list.OrderBy(b => b.Category).ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        /// <summary>One shared instance per library item (slot filters, previews). Do not put it in a save.</summary>
        public Item SampleItem(LibraryItem item)
        {
            lock (_samples)
            {
                if (!_samples.TryGetValue(item.Path, out var sample))
                {
                    try { sample = CreateItem(item.Path, 1, 100); }
                    catch (Exception) { sample = null; }
                    _samples[item.Path] = sample;
                }
                return sample;
            }
        }

        /// <summary>
        /// Runs the blueprint exactly like the game's crafting window. <paramref name="crafter"/> matters: several
        /// blueprints add bonuses for crafter feats. Pass null to craft without feats.
        /// </summary>
        public CraftResult Craft(Blueprint blueprint, IDictionary<string, Item> components, Character crafter)
        {
            var bp = blueprint.GameObject;
            var craft = bp.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance).Single(m =>
            {
                var p = m.GetParameters();
                return p.Length == 2 && p[0].ParameterType == TypeForTag("C1") && p[1].ParameterType.IsGenericType
                    && p[1].ParameterType.GetGenericTypeDefinition() == typeof(Dictionary<,>);
            });
            var dict = (IDictionary)Activator.CreateInstance(craft.GetParameters()[1].ParameterType);
            foreach (var kv in components.Where(kv => kv.Value != null))
                dict[kv.Key] = kv.Value.GameObject;

            var result = Invoke(craft, bp, crafter?.GameObject, dict);
            var fields = result.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);
            var items = (IList)fields.First(f => typeof(IList).IsAssignableFrom(f.FieldType)).GetValue(result);
            var reqs = (IDictionary<string, int>)fields.First(f => f.FieldType == typeof(Dictionary<string, int>)).GetValue(result);
            var success = (bool)fields.First(f => f.FieldType == typeof(bool)).GetValue(result);
            var error = (string)fields.First(f => f.FieldType == typeof(string)).GetValue(result);
            var item = items != null && items.Count > 0 ? Wrap(items[0], null) : null;
            return new CraftResult(item, new Dictionary<string, int>(reqs ?? new Dictionary<string, int>()), success, error);
        }
    }
}
