using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UnderrailSaveEditor.App.Services
{
    /// <summary>Remembers the game and saves folders between runs (%APPDATA%\UnderrailSaveEditor\settings.ini).</summary>
    public static class AppSettings
    {
        private static readonly string FilePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UnderrailSaveEditor", "settings.ini");

        private static Dictionary<string, string> _values;

        public static string GameDirectory { get => Get(nameof(GameDirectory)); set => Set(nameof(GameDirectory), value); }
        public static string SavesDirectory { get => Get(nameof(SavesDirectory)); set => Set(nameof(SavesDirectory), value); }

        private static Dictionary<string, string> Values
        {
            get
            {
                if (_values != null)
                    return _values;
                _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (var line in File.ReadAllLines(FilePath))
                    {
                        int i = line.IndexOf('=');
                        if (i > 0)
                            _values[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                return _values;
            }
        }

        private static string Get(string key) => Values.TryGetValue(key, out var v) && v.Length > 0 ? v : null;

        private static void Set(string key, string value)
        {
            Values[key] = value ?? "";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, Values.Select(kv => kv.Key + "=" + kv.Value));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
