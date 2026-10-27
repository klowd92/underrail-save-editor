using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;

namespace UnderrailSaveEditor.Core
{
    /// <summary>
    /// Hosts the game's own assembly (underrail.exe) in this process and exposes its save-file serialization.
    /// Saves are read and written by the game's code, so the output is exactly what the game itself produces.
    ///
    /// The assembly is obfuscated and its names change with every game build. Nothing here refers to an
    /// obfuscated name: types are found through the game's serialization binder (short tags like "TG", "P2"),
    /// real public names that survive obfuscation (GetObjectData, Stat`1, GetFeats, Name, Level...) and IL patterns.
    /// </summary>
    public sealed partial class GameRuntime
    {
        internal const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        private static readonly object Gate = new object();
        private static GameRuntime _current;

        private readonly Type[] _types;
        private readonly Type _serializer;
        private readonly FieldInfo _versionField;
        private readonly Dictionary<string, Type> _tagCache = new Dictionary<string, Type>();
        private Version _gameVersion;
        private IReadOnlyList<FeatInfo> _feats;
        private IReadOnlyList<SpecializationInfo> _specializations;

        private GameRuntime(string gameDirectory)
        {
            GameDirectory = gameDirectory;
            var exe = Path.Combine(gameDirectory, "underrail.exe");
            if (!File.Exists(exe))
                throw new FileNotFoundException("underrail.exe was not found in the game directory.", exe);

            // The game resolves its own DLLs (SFML, Galaxy, ...) relative to its folder.
            AppDomain.CurrentDomain.AssemblyResolve += ResolveFromGameDirectory;
            Directory.SetCurrentDirectory(gameDirectory);

            Assembly = Assembly.LoadFrom(exe);
            try { _types = Assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { _types = ex.Types.Where(t => t != null).ToArray(); }

            Binder = BootstrapTypeResolver();

            // Data model version = major * 10^10 + minor. The class holding it is the static class with the 10^10 multiplier;
            // its public long is the version the game writes into every file it saves.
            var versionType = _types.Single(t => t.IsAbstract && t.IsSealed && t.GetFields(All).Any(f =>
                f.IsStatic && !f.IsPublic && !f.IsLiteral && f.FieldType == typeof(long) && (long)f.GetValue(null) == 10000000000L));
            _versionField = versionType.GetFields(All).Single(f => f.IsStatic && f.IsPublic && f.FieldType == typeof(long));
            if (DataModelVersion % 10000000000L == 0)
                throw new InvalidOperationException("The game's data model version was not initialized; refusing to write files the game would misread.");

            _serializer = _types.Single(t => t.GetMethod("DeserializeFromBinaryFileCompressed") != null);
            Stats = new StatAccessor(_types.Single(t => t.IsGenericTypeDefinition && t.Name == "Stat`1").MakeGenericType(typeof(int)));
        }

        /// <summary>Loads the game assembly once per process. A second call with another directory fails: restart to switch.</summary>
        public static GameRuntime Load(string gameDirectory)
        {
            lock (Gate)
            {
                gameDirectory = Path.GetFullPath(gameDirectory);
                if (_current == null)
                    _current = new GameRuntime(gameDirectory);
                else if (!string.Equals(_current.GameDirectory, gameDirectory, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"The game is already loaded from '{_current.GameDirectory}'. Restart the editor to use another installation.");
                return _current;
            }
        }

        public string GameDirectory { get; }
        public Assembly Assembly { get; }
        /// <summary>The game's version (e.g. 1.3.1.2). A new save summary object is stamped with it, as when the game saves.</summary>
        public Version GameVersion
        {
            get
            {
                if (_gameVersion == null)
                {
                    try { _gameVersion = (Version)TypeForTag("SGI").GetProperty("GameVersion").GetValue(Activator.CreateInstance(TypeForTag("SGI"))); }
                    catch (Exception) { _gameVersion = new Version(0, 0); }
                }
                return _gameVersion;
            }
        }
        public SerializationBinder Binder { get; }
        public long DataModelVersion => (long)_versionField.GetValue(null);
        internal StatAccessor Stats { get; }

        /// <summary>Every type of the game assembly.</summary>
        internal IReadOnlyList<Type> Types => _types;

        public IReadOnlyList<FeatInfo> Feats => _feats ?? (_feats = BuildFeatCatalog());
        public IReadOnlyList<SpecializationInfo> Specializations => _specializations ?? (_specializations = BuildSpecializationCatalog());

        /// <summary>Resolves a serialization tag ("TG", "P2", "SGI", ...) to the game type, through the game's own binder.</summary>
        public Type TypeForTag(string tag)
        {
            lock (_tagCache)
            {
                if (!_tagCache.TryGetValue(tag, out var type))
                {
                    type = Binder.BindToType("X", tag) ?? throw new TypeLoadException($"The game has no type tagged '{tag}'.");
                    _tagCache[tag] = type;
                }
                return type;
            }
        }

        /// <summary>A public enum of the game that kept its real name (Gender, CharacterImmunities, SkillCategory...).</summary>
        public Type GameEnum(string name) => _types.Single(t => t.IsEnum && t.IsPublic && t.Name == name);

        internal object Deserialize(string path, Type type, bool compressed)
        {
            var method = _serializer.GetMethod(compressed ? "DeserializeFromBinaryFileCompressed" : "DeserializeFromBinaryFile").MakeGenericMethod(type);
            return Invoke(method, null, path, Binder, true);
        }

        internal void Serialize(string path, Type type, object graph, bool compressed)
        {
            var method = _serializer.GetMethod(compressed ? "SerializeToBinaryFileCompressed" : "SerializeToBinaryFile").MakeGenericMethod(type);
            Invoke(method, null, path, graph, Binder, DataModelVersion, true);
        }

        internal static object Invoke(MethodBase method, object target, params object[] args)
        {
            try { return method.Invoke(target, args); }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        private Assembly ResolveFromGameDirectory(object sender, ResolveEventArgs e)
        {
            var name = new AssemblyName(e.Name).Name;
            foreach (var ext in new[] { ".dll", ".exe" })
            {
                var path = Path.Combine(GameDirectory, name + ext);
                if (File.Exists(path))
                    return Assembly.LoadFrom(path);
            }
            return null;
        }

        /// <summary>
        /// Mirrors what the game does at startup before it touches any data file: create its assembly resolver,
        /// build the tag-to-type map (this also sets the current data model version) and hand it to the binder.
        /// </summary>
        private SerializationBinder BootstrapTypeResolver()
        {
            var binderType = _types.Single(t => t.BaseType == typeof(SerializationBinder) && t.GetFields(All).Any(f => f.IsStatic && f.FieldType.IsInterface));
            var resolverField = binderType.GetFields(All).Single(f => f.IsStatic && f.FieldType.IsInterface);
            var resolverType = _types.First(t => resolverField.FieldType.IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null);
            var root = resolverType;
            while (root.BaseType != typeof(object))
                root = root.BaseType;

            var resolver = Activator.CreateInstance(resolverType);
            var methods = root.GetMethods(All).Where(m => m.IsPublic && m.ReturnType == typeof(void)).ToList();
            foreach (var setter in methods.Where(m => m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(bool)))
                Invoke(setter, resolver, true);
            Invoke(methods.First(m => m.IsVirtual && m.GetParameters().Length == 0), resolver);
            Invoke(methods.Single(m => !m.IsVirtual && m.GetParameters().Length == 0), resolver); // builds the type map
            resolverField.SetValue(null, resolver);

            // The game also registers the resolver as a service; character stat recalculation looks it up there.
            var registerService = _types.Select(t => t.GetMethod("RegisterService", BindingFlags.Public | BindingFlags.Static))
                .Single(m => m != null && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
            Invoke(registerService.MakeGenericMethod(resolverField.FieldType), null, resolver);

            // The game window registers itself as the "host" service (content root, window handle, frame time).
            // Character recalculation loads equipped item definitions through it, which only needs the content root.
            var hostService = _types.Single(t => t.IsInterface && t.GetMethod("get_TargetElapsedTime") != null);
            Invoke(registerService.MakeGenericMethod(hostService), null, new HostServiceStub(hostService, Path.Combine(GameDirectory, "data")).GetTransparentProxy());

            var accessor = binderType.GetMethods(All).Single(m => m.IsStatic && m.ReturnType == binderType && m.GetParameters().Length == 0);
            return (SerializationBinder)accessor.Invoke(null, null);
        }

        /// <summary>The game's feat registry: the static class with GetFeats&lt;T&gt;, holding a Dictionary&lt;string id, feat&gt;.</summary>
        private IReadOnlyList<FeatInfo> BuildFeatCatalog()
        {
            var registry = _types.Single(t => t.GetMethod("GetFeats") != null && t.GetMethod("GetFeat") != null);
            var map = (IDictionary)registry.GetFields(All).Single(f => f.IsStatic && typeof(IDictionary).IsAssignableFrom(f.FieldType)
                && f.FieldType.GetGenericArguments()[0] == typeof(string)).GetValue(null);
            var list = new List<FeatInfo>();
            foreach (DictionaryEntry e in map)
                list.Add(new FeatInfo(this, (string)e.Key, e.Value));
            return list.OrderBy(f => f.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>The game's specialization registry: the static class with GetSpecializations&lt;T&gt;.</summary>
        private IReadOnlyList<SpecializationInfo> BuildSpecializationCatalog()
        {
            var registry = _types.Single(t => t.GetMethod("GetSpecializations") != null);
            var map = (IDictionary)registry.GetFields(All).Single(f => f.IsStatic && typeof(IDictionary).IsAssignableFrom(f.FieldType)
                && f.FieldType.GetGenericArguments()[0] == typeof(string)).GetValue(null);
            var featsByType = Feats.ToDictionary(f => f.GameType);
            var list = new List<SpecializationInfo>();
            foreach (DictionaryEntry e in map)
                list.Add(new SpecializationInfo(this, (string)e.Key, e.Value, featsByType));
            return list.OrderBy(s => s.FullName, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    /// <summary>Access to TimelapseVertigo.Rules.Characters.Stat&lt;int&gt;: base value ("S4:V") and modified value ("S4:MV").</summary>
    internal sealed class StatAccessor
    {
        private readonly FieldInfo _value;
        private readonly FieldInfo _modified;
        private readonly MethodInfo _setValue;
        private readonly MethodInfo _setModified;

        public StatAccessor(Type statOfInt)
        {
            _value = Il.FieldForKey(statOfInt, "S4:V");
            _modified = Il.FieldForKey(statOfInt, "S4:MV");
            var setters = statOfInt.GetMethods(GameRuntime.All).Where(m => m.ReturnType == typeof(void)
                && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(int)).ToList();
            _setValue = setters.Single(m => m.IsPublic);          // raises ValueChanged, like the game's UI does
            _setModified = setters.Single(m => m.IsAssembly);     // applies the stat's own clamping
        }

        public int GetValue(object stat) => (int)_value.GetValue(stat);
        public int GetModified(object stat) => (int)_modified.GetValue(stat);

        /// <summary>Sets the base value through the game's setter and keeps the modified value's bonus on top of it.</summary>
        public void SetValue(object stat, int value)
        {
            int oldValue = GetValue(stat), oldModified = GetModified(stat);
            if (value == oldValue)
                return;
            GameRuntime.Invoke(_setValue, stat, value);
            if (GetModified(stat) == oldModified)
                GameRuntime.Invoke(_setModified, stat, oldModified + (value - oldValue));
        }
    }
}
