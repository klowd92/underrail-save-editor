using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace UnderrailSaveEditor.Core
{
    /// <summary>A game state variable the game's dialogs read or write (whether or not a save has it yet).</summary>
    public sealed class VariableInfo
    {
        internal VariableInfo(string name, Type valueType, IReadOnlyList<string> dialogs)
        {
            Name = name;
            ValueType = valueType;
            Dialogs = dialogs;
        }

        public string Name { get; }
        /// <summary>bool, int, float, string or Guid, from the conditions and actions that use it.</summary>
        public Type ValueType { get; }
        /// <summary>Dialog files (without extension) that use the variable.</summary>
        public IReadOnlyList<string> Dialogs { get; }
    }

    public sealed partial class GameRuntime
    {
        private IReadOnlyList<VariableInfo> _knownVariables;

        /// <summary>
        /// Dialog conditions and actions that name a variable by path ("context.npc_lucas_met"), by serialization tag,
        /// with the key of the path and the value type they work with.
        /// "context." paths are the global game state the save stores; "tempcontext." ones live only during a dialog.
        /// </summary>
        private static readonly (string Tag, string PathKey, Type ValueType)[] VariableUsers =
        {
            ("BC", "CIC:P", typeof(bool)),        // bool condition
            ("SBFV", "SFVA:P", typeof(bool)),     // set bool
            ("ICC", "CIC:P", typeof(int)),        // int condition
            ("SIFV", "SFVA:P", typeof(int)),      // set int
            ("MIFV", "SFVA:P", typeof(int)),      // modify int
            ("SC1", "CIC:P", typeof(string)),     // string condition
            ("SSFV", "SFVA:P", typeof(string)),   // set string
            ("FCC", "CIC:P", typeof(float)),      // float condition
            ("MFFV", "SFVA:P", typeof(float)),    // modify float
            ("SGUIDFV", "SFVA:P", typeof(Guid)),  // set Guid
        };

        /// <summary>
        /// Every global state variable used by the game's dialogs (data\dialogs\**\*.udlg), found by loading the dialogs
        /// with the game's serializer. Several seconds the first time; cached afterwards.
        /// Variables that only the game's code or runtime creates (event timers, per-boat flags...) are not listed.
        /// </summary>
        public IReadOnlyList<VariableInfo> KnownVariables
        {
            get
            {
                if (_knownVariables != null)
                    return _knownVariables;

                var users = new Dictionary<Type, (FieldInfo Path, Type ValueType)>();
                foreach (var (tag, key, valueType) in VariableUsers)
                {
                    try
                    {
                        var type = TypeForTag(tag);
                        users[type] = (Il.FieldForKeyInHierarchy(type, key), valueType);
                    }
                    catch (Exception) { }
                }

                var found = new ConcurrentDictionary<string, ConcurrentDictionary<Type, int>>(StringComparer.Ordinal);
                var dialogsByName = new ConcurrentDictionary<string, ConcurrentBag<string>>(StringComparer.Ordinal);
                var root = Path.Combine(GameDirectory, "data", "dialogs");
                var files = Directory.Exists(root) ? Directory.GetFiles(root, "*.udlg", SearchOption.AllDirectories) : new string[0];
                System.Threading.Tasks.Parallel.ForEach(files, file =>
                {
                    object dialog;
                    try { dialog = Deserialize(file, typeof(object), false); }
                    catch (Exception) { return; }
                    var dialogName = Path.GetFileNameWithoutExtension(file);
                    foreach (var node in Walk(dialog))
                    {
                        if (!users.TryGetValue(node.GetType(), out var user) && !(node.GetType().BaseType != null && users.TryGetValue(node.GetType().BaseType, out user)))
                            continue;
                        if (!(user.Path.GetValue(node) is string path) || !path.StartsWith("context.", StringComparison.Ordinal))
                            continue;
                        var name = path.Substring("context.".Length);
                        if (name.Length == 0 || name.Contains("."))   // "context.X.Y" reaches into an object, not a variable of its own
                            continue;
                        found.GetOrAdd(name, _ => new ConcurrentDictionary<Type, int>()).AddOrUpdate(user.ValueType, 1, (_, n) => n + 1);
                        var bag = dialogsByName.GetOrAdd(name, _ => new ConcurrentBag<string>());
                        if (!bag.Contains(dialogName))
                            bag.Add(dialogName);
                    }
                });

                return _knownVariables = found
                    .Select(kv => new VariableInfo(kv.Key,
                        kv.Value.OrderByDescending(t => t.Value).First().Key,
                        dialogsByName[kv.Key].Distinct().OrderBy(d => d).ToList()))
                    .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        private static readonly ConcurrentDictionary<Type, FieldInfo[]> WalkFields = new ConcurrentDictionary<Type, FieldInfo[]>();

        /// <summary>Every object reachable from <paramref name="root"/> (fields and collections), each once.</summary>
        private static IEnumerable<object> Walk(object root)
        {
            var seen = new HashSet<object>(ReferenceComparer.Instance);
            var stack = new Stack<object>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var o = stack.Pop();
                if (o == null || !seen.Add(o))
                    continue;
                yield return o;
                if (o is IEnumerable sequence && !(o is string))
                {
                    foreach (var item in sequence)
                    {
                        if (item != null && !item.GetType().IsPrimitive && !(item is string))
                            stack.Push(item);
                    }
                }
                foreach (var field in WalkFields.GetOrAdd(o.GetType(), FieldsToWalk))
                {
                    var value = field.GetValue(o);
                    if (value != null)
                        stack.Push(value);
                }
            }
        }

        private static FieldInfo[] FieldsToWalk(Type type)
        {
            var fields = new List<FieldInfo>();
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                fields.AddRange(t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(f => !f.FieldType.IsPrimitive && !f.FieldType.IsEnum && f.FieldType != typeof(string) && !f.FieldType.IsPointer
                        && !typeof(Delegate).IsAssignableFrom(f.FieldType) && !typeof(Type).IsAssignableFrom(f.FieldType)
                        && !(f.FieldType.IsValueType && !f.FieldType.IsGenericType)));
            }
            return fields.ToArray();
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
