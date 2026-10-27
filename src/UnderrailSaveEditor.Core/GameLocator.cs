using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace UnderrailSaveEditor.Core
{
    /// <summary>Finds the Underrail installation and the saves folder on this machine.</summary>
    public static class GameLocator
    {
        private const string GogGameId = "1447688349";
        private const string SteamAppId = "250520";

        public static string DefaultSavesDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Underrail", "Saves");

        public static bool IsGameDirectory(string dir) => !string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, "underrail.exe"));

        /// <summary>Returns the first candidate folder that contains underrail.exe, or null.</summary>
        public static string FindGameDirectory() => Candidates().FirstOrDefault(IsGameDirectory);

        private static IEnumerable<string> Candidates()
        {
            yield return ReadRegistry(@"SOFTWARE\GOG.com\Games\" + GogGameId, "path");
            yield return ReadRegistry(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + GogGameId + "_is1", "InstallLocation");
            yield return ReadRegistry(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App " + SteamAppId, "InstallLocation");
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            yield return Path.Combine(pf86, "GOG Galaxy", "Games", "UnderRail");
            yield return Path.Combine(pf86, "Steam", "steamapps", "common", "Underrail");
            yield return @"C:\GOG Games\Underrail";
        }

        private static string ReadRegistry(string key, string value)
        {
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                try
                {
                    using (var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry32))
                    using (var k = root.OpenSubKey(key))
                    {
                        if (k?.GetValue(value) is string s && s.Length > 0)
                            return s;
                    }
                }
                catch (Exception) { }
            }
            return null;
        }
    }
}
