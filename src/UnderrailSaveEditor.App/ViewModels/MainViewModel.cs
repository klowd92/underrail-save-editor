using System;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Input;
using UnderrailSaveEditor.App.Services;
using UnderrailSaveEditor.Core;

namespace UnderrailSaveEditor.App.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly SaveFileService _saveService;
        private readonly TabViewModelBase[] _tabs;
        private SaveGame _currentSave;
        private string _currentSavePath;
        private bool _hasUnsavedChanges;
        private string _statusText = "No save loaded";
        private string _gameText = "";

        public MainViewModel()
        {
            _saveService = new SaveFileService();

            SaveBrowser = new SaveBrowserViewModel(_saveService);
            GeneralTab = new GeneralTabViewModel();
            AbilitiesTab = new AbilitiesTabViewModel();
            SkillsTab = new SkillsTabViewModel();
            FeatsTab = new FeatsTabViewModel();
            SpecializationsTab = new SpecializationsTabViewModel();
            CapabilitiesTab = new CapabilitiesTabViewModel();
            InventoryTab = new InventoryTabViewModel();
            CraftingTab = new CraftingTabViewModel();
            GameVariablesTab = new GameVariablesTabViewModel();
            _tabs = new TabViewModelBase[] { GeneralTab, AbilitiesTab, SkillsTab, FeatsTab, SpecializationsTab, CapabilitiesTab, InventoryTab, CraftingTab, GameVariablesTab };

            SaveBrowser.SaveSelected += OnSaveSelected;
            foreach (var tab in _tabs)
                tab.ModelChanged += OnModelChanged;

            SaveCommand = new RelayCommand(ExecuteSave, () => IsSaveLoaded);
            SaveAsCommand = new RelayCommand(ExecuteSaveAs, () => IsSaveLoaded);
            ReloadCommand = new RelayCommand(ExecuteReload, () => IsSaveLoaded);
            ChangeGameFolderCommand = new RelayCommand(ExecuteChangeGameFolder);
        }

        // Child ViewModels
        public SaveBrowserViewModel SaveBrowser { get; }
        public GeneralTabViewModel GeneralTab { get; }
        public AbilitiesTabViewModel AbilitiesTab { get; }
        public SkillsTabViewModel SkillsTab { get; }
        public FeatsTabViewModel FeatsTab { get; }
        public SpecializationsTabViewModel SpecializationsTab { get; }
        public CapabilitiesTabViewModel CapabilitiesTab { get; }
        public InventoryTabViewModel InventoryTab { get; }
        public CraftingTabViewModel CraftingTab { get; }
        public GameVariablesTabViewModel GameVariablesTab { get; }

        // Commands
        public ICommand SaveCommand { get; }
        public ICommand SaveAsCommand { get; }
        public ICommand ReloadCommand { get; }
        public ICommand ChangeGameFolderCommand { get; }

        // Properties
        public bool IsSaveLoaded => _currentSave != null;

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        /// <summary>Game version and data format the editor is using, shown in the status bar.</summary>
        public string GameText
        {
            get => _gameText;
            set => SetProperty(ref _gameText, value);
        }

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set => SetProperty(ref _hasUnsavedChanges, value);
        }

        public string CurrentSavePath => _currentSavePath;

        /// <summary>Finds the game (settings, registry, usual folders, or asks) and loads its code. False if the user gave up.</summary>
        public bool InitializeGame()
        {
            var dir = AppSettings.GameDirectory;
            if (!GameLocator.IsGameDirectory(dir))
                dir = GameLocator.FindGameDirectory();

            if (!GameLocator.IsGameDirectory(dir))
            {
                var choice = Dialogs.Choose("Underrail not found",
                    "The editor reads and writes saves with Underrail's own code, so it needs the game's installation folder.\n\n" +
                    "Underrail was not found in the usual places (GOG and Steam install folders, and their registry entries).\n\n" +
                    "Please choose the folder that contains underrail.exe.",
                    MessageBoxImage.Warning, "Choose folder...", "Exit");
                if (choice != 0)
                    return false;
                dir = AskForGameDirectory(null);
                if (dir == null)
                    return false;
            }

            try
            {
                _saveService.LoadGame(dir);
                AppSettings.GameDirectory = dir;
                var rt = _saveService.Runtime;

                // Every hook into the game's code must resolve before anything is read or written.
                var problems = rt.SelfCheck();
                if (problems.Count > 0)
                {
                    Dialogs.Show($"This version of Underrail ({rt.GameVersion}, save format {SaveSummary.FormatVersion(rt.DataModelVersion)}) is not supported: " +
                        "the editor could not find parts of the game's code it relies on.\n\n" + string.Join("\n", problems.Take(8)) +
                        "\n\nNo save was read or modified.", "Unsupported game version", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
                if (!rt.IsTestedVersion)
                {
                    var tested = string.Join(", ", GameRuntime.TestedGameVersions);
                    var choice = Dialogs.Choose("Untested game version",
                        $"This editor was tested with Underrail {tested}; your game is {rt.GameVersion} (save format {SaveSummary.FormatVersion(rt.DataModelVersion)}).\n\n" +
                        "All of the editor's checks passed, and saves are written by the game's own code, so it will most likely work. " +
                        "Keep a copy of your saves (the editor also backs up every file it replaces).",
                        MessageBoxImage.Warning, "Continue", "Exit");
                    if (choice != 0)
                        return false;
                }

                GameIcons.Runtime = rt;
                GameText = $"Underrail {rt.GameVersion} · save format {SaveSummary.FormatVersion(rt.DataModelVersion)}";
                SaveBrowser.SavesDirectory = AppSettings.SavesDirectory ?? GameLocator.DefaultSavesDirectory;
                return true;
            }
            catch (Exception ex)
            {
                AppSettings.GameDirectory = null; // ask again next time
                Dialogs.Show($"Could not load the game's code from:\n{dir}\n\n{ex.Message}\n\nThe editor will ask for the game folder the next time it starts.",
                    "Underrail Save Editor", MessageBoxButton.OK, MessageBoxImage.Error);
                return false; // the assembly may be half-loaded; a restart is the clean way to retry
            }
        }

        /// <summary>Folder picker that only accepts a folder containing underrail.exe; null if cancelled.</summary>
        private static string AskForGameDirectory(string current)
        {
            while (true)
            {
                var dialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Select the Underrail installation folder (the one containing underrail.exe).",
                    SelectedPath = current ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                };
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return null;
                if (GameLocator.IsGameDirectory(dialog.SelectedPath))
                    return dialog.SelectedPath;
                Dialogs.Show($"underrail.exe was not found in:\n{dialog.SelectedPath}\n\nPlease choose the folder Underrail is installed in.",
                    "Underrail not found", MessageBoxButton.OK, MessageBoxImage.Warning);
                current = dialog.SelectedPath;
            }
        }

        /// <summary>Switching installations needs a fresh process (the game's code is already loaded), so it applies on restart.</summary>
        private void ExecuteChangeGameFolder()
        {
            var dir = AskForGameDirectory(_saveService.Runtime?.GameDirectory);
            if (dir == null || string.Equals(dir, _saveService.Runtime?.GameDirectory, StringComparison.OrdinalIgnoreCase))
                return;
            AppSettings.GameDirectory = dir;
            Dialogs.Show($"The editor will use the game in:\n{dir}\n\nRestart the editor for the change to take effect.",
                "Game folder", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnSaveSelected(string folderPath)
        {
            if (HasUnsavedChanges && folderPath != _currentSavePath)
            {
                var result = Dialogs.Show(
                    "You have unsaved changes. Discard them and open another save?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                    return;
            }
            LoadSave(folderPath);
        }

        public void LoadSave(string folderPath)
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                _currentSave = _saveService.LoadSave(folderPath);
                _currentSavePath = folderPath;
                HasUnsavedChanges = false;

                foreach (var tab in _tabs)
                    tab.Load(_currentSave);

                var player = _currentSave.Player;
                StatusText = $"Loaded: {player.Name} - Level {player.Level}"
                    + (_currentSave.WillUpgradeFormat ? $" · older save format {SaveSummary.FormatVersion(_currentSave.FileDataModelVersion)}, upgraded on save like the game does" : "");
                OnPropertyChanged(nameof(IsSaveLoaded));
            }
            catch (Exception ex)
            {
                Dialogs.Show($"Failed to load save:\n{folderPath}\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void ExecuteSave()
        {
            if (_currentSave == null || string.IsNullOrEmpty(_currentSavePath))
                return;
            WriteSave(_currentSavePath);
        }

        private void ExecuteSaveAs()
        {
            if (_currentSave == null)
                return;

            var name = Dialogs.Prompt("Save As", "Name of the new save (it appears under this name in the game's load menu):",
                Path.GetFileName(_currentSavePath) + " edited");
            if (string.IsNullOrEmpty(name))
                return;
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                Dialogs.Show("The name contains characters that can't be used in a folder name.", "Save As", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var target = Path.Combine(Path.GetDirectoryName(_currentSavePath), name);
            if (Directory.Exists(target) && Dialogs.Show($"A save named '{name}' already exists. Overwrite it?", "Save As",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            if (WriteSave(target))
            {
                LoadSave(target);
                StatusText = $"Saved as '{name}'";
            }
        }

        private bool WriteSave(string folder)
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                var backup = _saveService.WriteSave(_currentSave, folder);
                HasUnsavedChanges = false;
                StatusText = $"Saved: {_currentSave.Player.Name}" + (backup != null ? $" (previous files backed up to {backup})" : "");
                SaveBrowser.RefreshSaves();
                return true;
            }
            catch (Exception ex)
            {
                Dialogs.Show($"Failed to save. The existing save was not modified.\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void ExecuteReload()
        {
            if (string.IsNullOrEmpty(_currentSavePath))
                return;

            if (HasUnsavedChanges)
            {
                var result = Dialogs.Show(
                    "You have unsaved changes. Are you sure you want to reload?",
                    "Confirm Reload",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;
            }

            LoadSave(_currentSavePath);
        }

        /// <summary>A tab changed the game objects: the game may have recalculated other values, so every tab re-reads.</summary>
        private void OnModelChanged()
        {
            HasUnsavedChanges = true;
            foreach (var tab in _tabs)
                tab.Refresh();
        }
    }
}
