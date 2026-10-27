using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace UnderrailSaveEditor.Core
{
    /// <summary>An item instance: in the character's bag, equipped, or freshly created (e.g. a crafting result).</summary>
    public sealed class Item
    {
        private readonly GameRuntime _rt;
        private readonly Type _t;

        internal Item(GameRuntime rt, object instance, string location)
        {
            _rt = rt;
            GameObject = instance;
            _t = instance.GetType();
            Location = location;
        }

        public object GameObject { get; }

        /// <summary>"Bag", or the equipment slot name ("Armor", "Weapon1", "Quick0"...), or null for a loose item.</summary>
        public string Location { get; }
        public bool IsEquipped => Location != null && Location != "Bag";

        private object Definition => Prop("Definition");

        public string Name => (string)_rt.ItemDefinitionField("I:N").GetValue(Definition) ?? "?";

        /// <summary>The game's icon name for the item (definition key I:VM, e.g. "Components.123").</summary>
        public string IconName => _rt.ItemDefinitionField("I:VM").GetValue(Definition) as string;

        /// <summary>Path below data\rules\items for items defined in the game's library; null for crafted/unique items.</summary>
        public string LibraryPath => (bool)Prop("IsLibraryItem") ? _rt.LibraryPathOf(Prop("DefinitionProvider")) : null;

        public int Stacks { get => (int)Prop("Stacks"); set => SetProp("Stacks", value); }
        public int MaxStack => Math.Max(1, (int)_rt.ItemDefinitionField("I:MS").GetValue(Definition));

        /// <summary>
        /// True for components whose stats and crafting requirements scale with quality (plates, frames, vests...).
        /// The instance class always has a QualityLevel, but parts like barrels and scopes are quality-less: the game
        /// gives them 0 and their skill requirement is a flat number (25, 30...) instead of a multiplier below ~5.
        /// </summary>
        public bool HasQuality => _t.GetProperty("QualityLevel") != null && (Quality > 0 || _rt.IsQualityScaled(Definition));
        public int Quality { get => _t.GetProperty("QualityLevel") != null ? (int)Prop("QualityLevel") : 0; set => SetProp("QualityLevel", value); }

        public int MaxDurability => (int)Prop("MaxDurability");
        /// <summary>False for indestructible items (definition key I:I), which the game shows without durability.</summary>
        public bool HasDurability => !(bool)_rt.ItemDefinitionField("I:I").GetValue(Definition) && MaxDurability > 0;
        public double Durability { get => (double)Prop("Durability"); set => SetProp("Durability", value); }

        public double Weight => (double)Prop("Weight");
        public int Value => (int)Prop("ItemValue");

        /// <summary>The game's own tooltip for this item, as seen by <paramref name="viewer"/> (may be null).</summary>
        public RichText Tooltip(Character viewer) => _rt.Tooltip(GameObject, viewer?.GameObject);

        public override string ToString() => Stacks > 1 ? $"{Name} ({Stacks})" : Name;

        private object Prop(string name) => _t.GetProperty(name).GetValue(GameObject);
        private void SetProp(string name, object value) => _t.GetProperty(name).SetValue(GameObject, value);
    }

    /// <summary>An item definition file under data\rules\items.</summary>
    public sealed class LibraryItem
    {
        internal LibraryItem(string path, string name, bool isComponent, string iconName)
        {
            IconName = iconName;
            Path = path;
            Name = name;
            IsComponent = isComponent;
            int slash = path.IndexOf('\\');
            Category = Text.Humanize(slash > 0 ? path.Substring(0, slash) : "misc");
        }

        public string Path { get; }
        public string Name { get; }
        public string Category { get; }
        public bool IsComponent { get; }
        public string IconName { get; }

        public override string ToString() => Name;
    }

    /// <summary>The game's rich text (tooltips): lines of colored spans.</summary>
    public sealed class RichText
    {
        internal RichText(IReadOnlyList<IReadOnlyList<RichSpan>> lines)
        {
            Lines = lines;
        }

        public IReadOnlyList<IReadOnlyList<RichSpan>> Lines { get; }

        public override string ToString() => string.Join("\n", Lines.Select(l => string.Concat(l.Select(s => s.Text))));
    }

    /// <summary>Builds rich text for the editor's own messages, in the same shape as the game's.</summary>
    public sealed class RichTextBuilder
    {
        private readonly List<IReadOnlyList<RichSpan>> _lines = new List<IReadOnlyList<RichSpan>>();

        public RichTextBuilder Line(string text, byte r, byte g, byte b)
        {
            _lines.Add(new[] { new RichSpan(text, r, g, b) });
            return this;
        }

        public RichText Build() => new RichText(_lines.ToList());
    }

    public sealed class RichSpan
    {
        internal RichSpan(string text, byte r, byte g, byte b)
        {
            Text = text;
            R = r;
            G = g;
            B = b;
        }

        public string Text { get; }
        public byte R { get; }
        public byte G { get; }
        public byte B { get; }
    }
}
