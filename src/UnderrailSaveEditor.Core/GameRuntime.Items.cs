using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace UnderrailSaveEditor.Core
{
    public sealed partial class GameRuntime
    {
        private readonly Dictionary<string, FieldInfo> _definitionFields = new Dictionary<string, FieldInfo>();
        private IReadOnlyList<LibraryItem> _libraryItems;
        private readonly Dictionary<string, object> _definitions = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private Type _itemLibrary;
        private MethodInfo _tooltipMethod;
        private readonly StubServiceProvider _stubs = new StubServiceProvider();

        /// <summary>The game's item library: the static class with GetItemDefinition and CreateDefinitonProvider (sic).</summary>
        private Type ItemLibrary => _itemLibrary ?? (_itemLibrary =
            _types.Single(t => t.GetMethod("GetItemDefinition") != null && t.GetMethod("CreateDefinitonProvider") != null));

        internal FieldInfo ItemDefinitionField(string key)
        {
            lock (_definitionFields)
            {
                if (!_definitionFields.TryGetValue(key, out var field))
                    _definitionFields[key] = field = Il.FieldForKey(TypeForTag("I"), key);
                return field;
            }
        }

        /// <summary>
        /// A component definition ("CI") whose skill requirement factors ("CI:SR" -> "CSR:F") are all multipliers
        /// (below 5) rather than flat requirements. Verified against every component by the crafting spreadsheet project.
        /// </summary>
        internal bool IsQualityScaled(object definition)
        {
            if (!TypeForTag("CI").IsInstanceOfType(definition))
                return false;
            var requirements = Il.FieldForKey(TypeForTag("CI"), "CI:SR").GetValue(definition) as IEnumerable;
            var factors = new List<double>();
            foreach (var r in requirements ?? new object[0])
                factors.Add(Convert.ToDouble(Il.FieldForKey(r.GetType(), "CSR:F").GetValue(r)));
            return factors.Count > 0 && factors.All(f => f < 5);
        }

        internal string LibraryPathOf(object definitionProvider) =>
            definitionProvider == null ? null : (string)Il.FieldForKeyInHierarchy(definitionProvider.GetType(), "LIDP:P").GetValue(definitionProvider);

        /// <summary>
        /// Every item definition in data\rules\items. They are deserialized in parallel with the game's serializer
        /// (its version context is thread-static) and then put into the game's item library cache.
        /// </summary>
        public IReadOnlyList<LibraryItem> LibraryItems
        {
            get
            {
                lock (_definitions)
                {
                    if (_libraryItems != null)
                        return _libraryItems;
                    var root = Path.Combine(GameDirectory, "data", "rules", "items");
                    var definitionType = TypeForTag("I");
                    var componentType = TypeForTag("CI");
                    var nameField = ItemDefinitionField("I:N");
                    var files = Directory.GetFiles(root, "*.item", SearchOption.AllDirectories);
                    var loaded = new object[files.Length];
                    System.Threading.Tasks.Parallel.For(0, files.Length, i =>
                    {
                        try { loaded[i] = Deserialize(files[i], definitionType, true); }
                        catch (Exception) { }
                    });

                    var cache = LibraryCache;
                    var list = new List<LibraryItem>();
                    for (int i = 0; i < files.Length; i++)
                    {
                        if (loaded[i] == null)
                            continue;
                        var rel = files[i].Substring(root.Length).TrimStart('\\', '/');
                        rel = rel.Substring(0, rel.Length - ".item".Length).Replace('/', '\\');
                        _definitions[rel] = loaded[i];
                        if (!cache.Contains(rel))
                            cache[rel] = loaded[i];
                        var name = nameField.GetValue(loaded[i]) as string;
                        list.Add(new LibraryItem(rel, string.IsNullOrWhiteSpace(name) ? Path.GetFileName(rel) : name, componentType.IsInstanceOfType(loaded[i]),
                            ItemDefinitionField("I:VM").GetValue(loaded[i]) as string));
                    }
                    return _libraryItems = list.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
                }
            }
        }

        /// <summary>The item library's private static Dictionary&lt;string path, definition&gt;.</summary>
        private IDictionary LibraryCache => (IDictionary)ItemLibrary.GetFields(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(f => typeof(IDictionary).IsAssignableFrom(f.FieldType) && f.FieldType.GetGenericArguments()[0] == typeof(string)).GetValue(null);

        /// <summary>
        /// Creates a new item instance from the library, like the game's item factory: a library definition provider
        /// for the path, wrapped in the instance class the definition type maps to.
        /// </summary>
        public Item CreateItem(string libraryPath, int stacks = 1, int? quality = null)
        {
            var definitionType = TypeForTag("I");
            object definition;
            lock (_definitions)
            {
                if (!_definitions.TryGetValue(libraryPath, out definition))
                {
                    var getDefinition = ItemLibrary.GetMethod("GetItemDefinition").MakeGenericMethod(definitionType);
                    definition = Invoke(getDefinition, null, libraryPath, false) ?? throw new ArgumentException($"The game has no item '{libraryPath}'.");
                    _definitions[libraryPath] = definition;
                }
            }
            var providerType = Assembly.GetType("TimelapseVertigo.Rules.Items.LibraryItemDefinitionProvider`1").MakeGenericType(definition.GetType());
            var provider = Activator.CreateInstance(providerType, libraryPath);
            // The definition base's static Type X(Type) maps a definition class to its instance class.
            var instanceTypeOf = definitionType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Single(m => m.ReturnType == typeof(Type) && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(Type));
            var instanceType = (Type)instanceTypeOf.Invoke(null, new object[] { definition.GetType() })
                ?? throw new ArgumentException($"'{libraryPath}' can't be instantiated as an item.");
            var instance = Activator.CreateInstance(instanceType, provider);
            var item = new Item(this, instance, null) { Stacks = Math.Max(1, stacks) };
            if (quality.HasValue && item.HasQuality)
                item.Quality = quality.Value;
            return item;
        }

        internal Item Wrap(object instance, string location) => instance == null ? null : new Item(this, instance, location);

        /// <summary>
        /// Builds the game's tooltip for an item (the static UI method taking item and character and returning rich text "RT"),
        /// with inert stubs standing in for UI-only services such as interface-size settings.
        /// </summary>
        internal RichText Tooltip(object item, object character)
        {
            if (_tooltipMethod == null)
            {
                var richText = TypeForTag("RT");
                var itemType = TypeForTag("II");
                var characterType = TypeForTag("C1");
                _tooltipMethod = _types.Where(t => t.IsAbstract && t.IsSealed)
                    .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    .Single(m => m.ReturnType == richText && m.GetParameters().Length == 9
                        && m.GetParameters()[0].ParameterType == itemType && m.GetParameters()[1].ParameterType == characterType);
            }

            // The game always has a character when it shows a tooltip; fall back to a blank one.
            character = character ?? BlankCharacter;
            object rt;
            using (UseServiceStubs())
                rt = Invoke(_tooltipMethod, null, item, character, null, true, false, null, null, null, false);
            return ReadRichText(rt);
        }

        private object _blankCharacter;

        /// <summary>A fresh player character with no feats or skills (for tooltips and crafting without a loaded save).</summary>
        internal object BlankCharacter => _blankCharacter ?? (_blankCharacter = Activator.CreateInstance(TypeForTag("P2"), true));

        private RichText ReadRichText(object rt)
        {
            var lines = new List<IReadOnlyList<RichSpan>>();
            foreach (var line in (IEnumerable)Il.FieldForKey(rt.GetType(), "RT:L").GetValue(rt) ?? new object[0])
            {
                var spans = new List<RichSpan>();
                foreach (var span in (IEnumerable)Il.FieldForKey(line.GetType(), "RTL:S").GetValue(line) ?? new object[0])
                {
                    var text = (string)Il.FieldForKey(span.GetType(), "RTLS:T").GetValue(span);
                    var format = Il.FieldForKey(span.GetType(), "RTLS:P").GetValue(span);
                    byte r = 255, g = 255, b = 255;
                    if (format != null)
                    {
                        var color = Il.FieldForKey(format.GetType(), "P1:C").GetValue(format);
                        var ct = color.GetType();
                        r = (byte)ct.GetProperty("R").GetValue(color);
                        g = (byte)ct.GetProperty("G").GetValue(color);
                        b = (byte)ct.GetProperty("B").GetValue(color);
                    }
                    if (!string.IsNullOrEmpty(text))
                        spans.Add(new RichSpan(text, r, g, b));
                }
                lines.Add(spans);
            }
            return new RichText(lines);
        }

        /// <summary>Temporarily installs the inert stub provider as the game's fallback service provider.</summary>
        private IDisposable UseServiceStubs()
        {
            var locator = _types.Single(t => t.GetMethod("RegisterService", BindingFlags.Public | BindingFlags.Static) != null);
            var setFallback = locator.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.ReturnType == typeof(void) && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(IServiceProvider));
            setFallback.Invoke(null, new object[] { _stubs });
            return new Scope(() => setFallback.Invoke(null, new object[] { null }));
        }

        private sealed class Scope : IDisposable
        {
            private Action _onDispose;
            public Scope(Action onDispose) => _onDispose = onDispose;
            public void Dispose()
            {
                _onDispose?.Invoke();
                _onDispose = null;
            }
        }
    }
}
