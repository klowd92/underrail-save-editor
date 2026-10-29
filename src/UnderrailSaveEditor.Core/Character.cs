using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace UnderrailSaveEditor.Core
{
    /// <summary>A base ability or skill: base value (what the editor sets) and the game's modified value (with bonuses).</summary>
    public sealed class StatValue
    {
        internal StatValue(string name, string category, int value, int modified)
        {
            Name = name;
            Category = category;
            Value = value;
            ModifiedValue = modified;
        }

        /// <summary>Skills: the game's description of the skill.</summary>
        public string Description { get; internal set; } = "";
        /// <summary>Skills: the base abilities that raise it (the game shows them joined with "or").</summary>
        public IReadOnlyList<string> RelatedAbilities { get; internal set; } = new string[0];
        /// <summary>Skills: other skills it gains from, with the share of their points (0.1 = 10%).</summary>
        public IReadOnlyList<KeyValuePair<string, float>> Synergies { get; internal set; } = new KeyValuePair<string, float>[0];

        public string Name { get; }
        public string DisplayName => Text.Humanize(Name);
        public string Category { get; }
        public int Value { get; }
        public int ModifiedValue { get; }
    }

    /// <summary>
    /// The player character inside a loaded save. All reads and writes go to the game's own objects;
    /// properties with real names are used directly, the rest are located by their serialization keys.
    /// </summary>
    public sealed class Character
    {
        private readonly GameRuntime _rt;
        private readonly object _p;
        private readonly Type _t;

        internal Character(GameRuntime rt, object player)
        {
            _rt = rt;
            _p = player;
            _t = player.GetType();
        }

        public object GameObject => _p;

        // ---- identity -------------------------------------------------------------------------

        public string Name { get => Get<string>("Name"); set => Set("Name", value); }

        /// <summary>Portrait name chosen at character creation ("biotech_bl"); see GameImages.Portrait.</summary>
        public string Portrait { get => Get<string>("Portrait"); set => Set("Portrait", value); }
        public int Level { get => Get<int>("Level"); set => Set("Level", value); }

        public string Gender
        {
            get => _t.GetProperty("Gender").GetValue(_p).ToString();
            set => Set("Gender", Enum.Parse(_t.GetProperty("Gender").PropertyType, value));
        }

        public IReadOnlyList<string> GenderOptions => Enum.GetNames(_t.GetProperty("Gender").PropertyType);

        // ---- experience and unspent points (PC1 = player character) ----------------------------

        public int Experience { get => GetKey<int>("PC1", "PC1:E"); set => SetKey("PC1", "PC1:E", value); }
        public int AbilityPoints { get => GetKey<int>("PC1", "PC1:BAPC"); set => SetKey("PC1", "PC1:BAPC", value); }
        public int SkillPoints { get => GetKey<int>("PC1", "PC1:SPC"); set => SetKey("PC1", "PC1:SPC", value); }
        public int FeatPoints { get => GetKey<int>("PC1", "PC1:FC"); set => SetKey("PC1", "PC1:FC", value); }
        public int SpecializationPoints { get => GetKey<int>("PC1", "PC1:SP"); set => SetKey("PC1", "PC1:SP", value); }

        // ---- base abilities and skills ----------------------------------------------------------

        public IReadOnlyList<StatValue> BaseAbilities => StatObjects("BaseAbilities")
            .Select(s => new StatValue(StatName(s), null, _rt.Stats.GetValue(s), _rt.Stats.GetModified(s))).ToList();

        public IReadOnlyList<StatValue> Skills
        {
            get
            {
                var skills = StatObjects("Skills").ToList();
                var abilityNames = StatObjects("BaseAbilities").ToDictionary(a => a.GetType(), StatName);
                var skillNames = skills.ToDictionary(k => k.GetType(), StatName);
                return skills.Select(k =>
                {
                    var name = StatName(k);
                    return new StatValue(name, Prop(k, "Category").ToString(), _rt.Stats.GetValue(k), _rt.Stats.GetModified(k))
                    {
                        Description = _rt.SkillDescription(k, name),
                        RelatedAbilities = ((IDictionary<Type, float>)Prop(k, "BaseAbilityBonuses")).Keys
                            .Select(t => abilityNames.TryGetValue(t, out var n) ? n : null).Where(n => n != null).ToList(),
                        Synergies = ((IDictionary<Type, float>)Prop(k, "Synergies"))
                            .Where(kv => skillNames.ContainsKey(kv.Key))
                            .Select(kv => new KeyValuePair<string, float>(skillNames[kv.Key], kv.Value)).ToList(),
                    };
                }).ToList();
            }
        }

        public void SetBaseAbility(string name, int value) => _rt.Stats.SetValue(FindStat("BaseAbilities", name), value);
        public void SetSkill(string name, int value) => _rt.Stats.SetValue(FindStat("Skills", name), value);

        // ---- feats ------------------------------------------------------------------------------

        public IReadOnlyList<FeatInfo> Feats
        {
            get
            {
                var byType = _rt.Feats.ToDictionary(f => f.GameType);
                return ((IEnumerable)FeatCollection).Cast<object>()
                    .Select(f => byType.TryGetValue(f.GetType(), out var info) ? info : null)
                    .Where(f => f != null).ToList();
            }
        }

        public bool HasFeat(FeatInfo feat) => ((IEnumerable)FeatCollection).Cast<object>().Any(f => f.GetType() == feat.GameType);

        /// <summary>Adds the feat the way the level-up screen does: the feat's own on-acquire logic runs too.</summary>
        public bool AddFeat(FeatInfo feat) => (bool)GameRuntime.Invoke(FeatMethod("Add"), FeatCollection, feat.GameType);

        /// <summary>Removes the feat; the feat's own on-remove logic runs too.</summary>
        public bool RemoveFeat(FeatInfo feat) => (bool)GameRuntime.Invoke(FeatMethod("Remove"), FeatCollection, feat.GameType);

        private object FeatCollection => _t.GetProperty("Feats").GetValue(_p);

        /// <summary>The feat collection's public bool X(Type) method whose body calls List.Add / List.Remove.</summary>
        private MethodInfo FeatMethod(string listMethod)
        {
            var type = FeatCollection.GetType();
            return type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.ReturnType == typeof(bool) && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(Type))
                .Single(m => Il.Calls(m, c => c.Name == listMethod && c.DeclaringType.IsGenericType
                    && c.DeclaringType.GetGenericTypeDefinition() == typeof(List<>)));
        }

        // ---- inventory --------------------------------------------------------------------------

        private object Bag => _t.GetProperty("Bag").GetValue(_p);

        /// <summary>Items in the character's bag.</summary>
        public IReadOnlyList<Item> Inventory
        {
            get
            {
                var itemType = _rt.TypeForTag("II");
                var list = Bag.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Single(m => m.GetParameters().Length == 0 && m.ReturnType == typeof(List<>).MakeGenericType(itemType));
                return ((IEnumerable)list.Invoke(Bag, null)).Cast<object>().Select(i => _rt.Wrap(i, "Bag")).ToList();
            }
        }

        /// <summary>Equipped items by slot name (Armor, Helmet, Belt, Boots, ShieldEmitter, Weapon1, Weapon2, Quick0...).</summary>
        public IReadOnlyList<Item> Equipment
        {
            get
            {
                var gear = _t.GetProperty("Gear").GetValue(_p);
                var itemType = _rt.TypeForTag("II");
                // The gear's Dictionary<string, item> X(bool): false lists the slots; true would also unequip everything.
                var bySlot = gear.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Single(m => m.ReturnType == typeof(Dictionary<,>).MakeGenericType(typeof(string), itemType)
                        && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(bool));
                var items = new List<Item>();
                foreach (DictionaryEntry e in (IDictionary)bySlot.Invoke(gear, new object[] { false }))
                {
                    if (e.Value != null)
                        items.Add(_rt.Wrap(e.Value, (string)e.Key));
                }
                return items;
            }
        }

        /// <summary>Puts an item in the bag; stackable items merge into existing stacks like when the game picks loot up.</summary>
        public void AddItem(Item item)
        {
            var itemType = _rt.TypeForTag("II");
            var add = Bag.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance).Single(m =>
            {
                var p = m.GetParameters();
                return m.ReturnType == typeof(void) && p.Length == 2 && p[0].ParameterType == itemType && p[1].ParameterType == typeof(bool);
            });
            GameRuntime.Invoke(add, Bag, item.GameObject, true);
        }

        /// <summary>Removes an item from the bag (equipped items are not touched).</summary>
        public bool RemoveItem(Item item)
        {
            var itemType = _rt.TypeForTag("II");
            // The bag's bool X(item) that validates its argument named "item" (the other one is Contains).
            var remove = Bag.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.ReturnType == typeof(bool) && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == itemType)
                .Single(m => Il.LoadsString(m, "item"));
            return (bool)GameRuntime.Invoke(remove, Bag, item.GameObject);
        }

        // ---- specializations --------------------------------------------------------------------

        private IDictionary SpecializationMap => (IDictionary)_t.GetProperty("Specializations").GetValue(_p);

        public int GetSpecialization(string id)
        {
            var entry = SpecializationMap[id];
            return entry == null ? 0 : (int)entry.GetType().GetProperty("Value").GetValue(entry);
        }

        /// <summary>Same bookkeeping as the game: 0 removes the entry, otherwise the entry is created or updated.</summary>
        public void SetSpecialization(string id, int value)
        {
            var map = SpecializationMap;
            var entry = map[id];
            if (value <= 0)
            {
                if (entry != null)
                    map.Remove(id);
                return;
            }
            if (entry == null)
            {
                entry = Activator.CreateInstance(map.GetType().GetGenericArguments()[1]);
                map[id] = entry;
            }
            var valueProp = entry.GetType().GetProperty("Value");
            var modifiedProp = entry.GetType().GetProperty("ModifiedValue");
            int oldValue = (int)valueProp.GetValue(entry), oldModified = (int)modifiedProp.GetValue(entry);
            valueProp.SetValue(entry, value);
            modifiedProp.SetValue(entry, oldModified + (value - oldValue));
        }

        // ---- capabilities -----------------------------------------------------------------------
        // Several values come in pairs: the game recomputes the current value from the base one plus effects,
        // so the editor writes both (e.g. Darkvision and NaturalDarkvision).

        public float MovementSpeed { get => Get<float>("MovementSpeed"); set => Set("MovementSpeed", value); }
        public float BaseMovementSpeed { get => Get<float>("BaseMovementSpeed"); set => Set("BaseMovementSpeed", value); }
        public int CarryCapacity { get => Get<int>("CarryCapacity"); set => Set("CarryCapacity", value); }
        public float SightRange { get => Get<float>("SightRange"); set { Set("SightRange", value); Set("BaseSightRange", value); } }
        public bool Darkvision { get => Get<bool>("Darkvision"); set { Set("Darkvision", value); Set("NaturalDarkvision", value); } }
        public bool Truesighted { get => Get<bool>("Truesighted"); set { Set("Truesighted", value); Set("NaturallyTruesighted", value); } }
        public int Detection { get => Get<int>("Detection"); set => Set("Detection", value); }
        public float DetectionModifier { get => Get<float>("DetectionModifier"); set { Set("DetectionModifier", value); Set("BaseDetectionModifier", value); } }
        public bool Dead { get => Get<bool>("Dead"); set => Set("Dead", value); }
        public bool Passive { get => Get<bool>("Passive"); set => Set("Passive", value); }
        public bool Ethereal { get => Get<bool>("Ethereal"); set { Set("Ethereal", value); Set("EtherealBase", value); } }
        public bool PsiEmpathy { get => Get<bool>("PsiEmpathy"); set => Set("PsiEmpathy", value); }

        // ---- defense ----------------------------------------------------------------------------
        // The Defense tab of the game's combat stats window. The game recomputes these from abilities, skills, feats
        // and gear, so they are read-only here and show the values stored in the save (edits show up in-game).

        internal static readonly string[] DefenseKeys = { "CD1:D", "CD1:E", "CD1:F", "CD1:R", "CD1:Q", "CD1:PCM", "CD1:E1" };

        public int Dodge => DefenseKey<int>("CD1:D");
        public int Evasion => DefenseKey<int>("CD1:E");
        public int Fortitude => DefenseKey<int>("CD1:F");
        public int Resolve => DefenseKey<int>("CD1:R");
        /// <summary>Added to enemies' weapon crit chance against this character (0.05 = +5%).</summary>
        public double WeaponCritChanceAgainst => DefenseKey<double>("CD1:Q");
        /// <summary>Added to enemies' psi crit chance against this character (0.05 = +5%).</summary>
        public double PsiCritChanceAgainst => DefenseKey<double>("CD1:PCM");
        /// <summary>Armor penalty from worn gear, 0 to 0.95.</summary>
        public double ArmorPenalty => DefenseKey<double>("CD1:E1");

        private T DefenseKey<T>(string key)
        {
            var defense = Prop(_p, "Defense");
            return (T)Il.FieldForKey(defense.GetType(), key).GetValue(defense);
        }

        /// <summary>Single-flag names of the game's CharacterImmunities enum, in declaration order.</summary>
        public IReadOnlyList<string> ImmunityOptions
        {
            get
            {
                var enumType = _t.GetProperty("ImmunitiesBase").PropertyType;
                return Enum.GetNames(enumType).Where(n =>
                {
                    long v = Convert.ToInt64(Enum.Parse(enumType, n));
                    return v != 0 && (v & (v - 1)) == 0 && n.IndexOf("Placeholder", StringComparison.OrdinalIgnoreCase) < 0;
                }).ToList();
            }
        }

        public bool HasImmunity(string name) => (Flags("Immunities") & Flag(name)) != 0;

        /// <summary>Sets or clears the flag on both the base immunities and the current (effect-included) immunities.</summary>
        public void SetImmunity(string name, bool enabled)
        {
            foreach (var prop in new[] { "ImmunitiesBase", "Immunities" })
            {
                long v = enabled ? Flags(prop) | Flag(name) : Flags(prop) & ~Flag(name);
                Set(prop, Enum.ToObject(_t.GetProperty(prop).PropertyType, v));
            }
        }

        private long Flags(string prop) => Convert.ToInt64(_t.GetProperty(prop).GetValue(_p));
        private long Flag(string name) => Convert.ToInt64(Enum.Parse(_t.GetProperty("ImmunitiesBase").PropertyType, name));

        // ---- helpers ----------------------------------------------------------------------------

        private T Get<T>(string property) => (T)_t.GetProperty(property).GetValue(_p);

        private void Set(string property, object value) => _t.GetProperty(property).SetValue(_p, value);

        private T GetKey<T>(string tag, string key) => (T)Il.FieldForKey(_rt.TypeForTag(tag), key).GetValue(_p);

        private void SetKey(string tag, string key, object value) => Il.FieldForKey(_rt.TypeForTag(tag), key).SetValue(_p, value);

        private IEnumerable<object> StatObjects(string collectionProperty) =>
            ((IEnumerable)_t.GetProperty(collectionProperty).GetValue(_p)).Cast<object>();

        private static string StatName(object stat) => (string)Prop(stat, "Name");

        /// <summary>Reads a public property by name, preferring the most derived declaration if one is hidden with 'new'.</summary>
        private static object Prop(object o, string name) =>
            o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).First(p => p.Name == name).GetValue(o);

        private object FindStat(string collectionProperty, string name) =>
            StatObjects(collectionProperty).FirstOrDefault(s => string.Equals(StatName(s), name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"No {collectionProperty} entry named '{name}'.", nameof(name));
    }
}
