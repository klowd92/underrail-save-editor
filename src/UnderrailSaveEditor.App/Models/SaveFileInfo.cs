using System;
using System.ComponentModel;
using System.IO;
using System.Windows.Media;
using UnderrailSaveEditor.Core;

namespace UnderrailSaveEditor.App.Models
{
    public class SaveFileInfo : INotifyPropertyChanged
    {
        private ImageSource _portrait;

        public SaveFileInfo(SaveSummary summary)
        {
            FolderPath = summary.FolderPath;
            CharacterName = summary.CharacterName;
            CharacterLevel = summary.CharacterLevel;
            Location = summary.Location ?? "";
            SaveTime = summary.SaveTime;
            GameVersion = summary.GameVersion;
            PlayTime = summary.PlayTime;
        }

        /// <summary>A folder whose info.dat could not be read.</summary>
        public SaveFileInfo(string folderPath, string error)
        {
            FolderPath = folderPath;
            CharacterName = Path.GetFileName(folderPath);
            Location = "Unreadable: " + error;
            SaveTime = DateTime.MinValue;
            IsUnreadable = true;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public string FolderPath { get; }
        public string FolderName => Path.GetFileName(FolderPath);
        public string CharacterName { get; }
        public int CharacterLevel { get; }
        public string Location { get; }
        public DateTime SaveTime { get; }
        public Version GameVersion { get; }
        public TimeSpan? PlayTime { get; }
        public bool IsUnreadable { get; }

        /// <summary>"Zapper · Lvl 20 · Utility Tower"</summary>
        public string DetailLine => IsUnreadable ? Location : $"{CharacterName} · Lvl {CharacterLevel} · {Location}";

        /// <summary>The character's portrait (filled in the background after the list appears).</summary>
        public ImageSource Portrait
        {
            get => _portrait;
            set
            {
                _portrait = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Portrait)));
            }
        }

        public string DisplayName => !string.IsNullOrEmpty(CharacterName) ? CharacterName : FolderName;
        public string LevelDisplay => $"Level {CharacterLevel}";
        public string VersionDisplay => GameVersion?.ToString() ?? "Unknown";
        public string PlayTimeDisplay => PlayTime.HasValue
            ? $"{(int)PlayTime.Value.TotalHours}h {PlayTime.Value.Minutes}m"
            : "";
    }
}
