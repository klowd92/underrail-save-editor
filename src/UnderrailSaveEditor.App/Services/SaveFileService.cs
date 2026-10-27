using System;
using System.Collections.Generic;
using System.IO;
using UnderrailSaveEditor.App.Models;
using UnderrailSaveEditor.Core;

namespace UnderrailSaveEditor.App.Services
{
    /// <summary>File-level operations: game runtime, save discovery, load and save. Editing goes through SaveGame.Player.</summary>
    public class SaveFileService
    {
        public GameRuntime Runtime { get; private set; }

        public static string BackupDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UnderrailSaveEditor", "Backups");

        public void LoadGame(string gameDirectory) => Runtime = GameRuntime.Load(gameDirectory);

        public IEnumerable<SaveFileInfo> DiscoverSaveFiles(string directory)
        {
            if (Runtime == null || !Directory.Exists(directory))
                yield break;

            foreach (var folder in Directory.GetDirectories(directory))
            {
                if (!File.Exists(Path.Combine(folder, SaveGame.InfoFile)))
                    continue;

                SaveFileInfo info;
                try
                {
                    info = new SaveFileInfo(SaveSummary.Read(Runtime, folder));
                }
                catch (Exception ex)
                {
                    // Typically a save written by another tool that the game itself can't read either.
                    info = new SaveFileInfo(folder, ex.Message);
                }
                yield return info;
            }
        }

        public SaveGame LoadSave(string folderPath) => SaveGame.Load(Runtime, folderPath);

        /// <summary>Writes the save; returns the backup folder of the replaced files (or null).</summary>
        public string WriteSave(SaveGame save, string folderPath) => save.Save(folderPath, BackupDirectory);
    }
}
