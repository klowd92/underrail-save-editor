using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using UnderrailSaveEditor.App.Models;
using UnderrailSaveEditor.App.Services;

namespace UnderrailSaveEditor.App.ViewModels
{
    public class SaveBrowserViewModel : ViewModelBase
    {
        private readonly SaveFileService _saveService;
        private string _savesDirectory;
        private SaveFileInfo _selectedSave;
        private bool _isLoading;

        public SaveBrowserViewModel(SaveFileService saveService)
        {
            _saveService = saveService;
            SaveFiles = new ObservableCollection<SaveFileInfo>();

            BrowseDirectoryCommand = new RelayCommand(ExecuteBrowseDirectory);
            LoadSelectedCommand = new RelayCommand(ExecuteLoadSelected, () => SelectedSave != null);
            RefreshCommand = new RelayCommand(ExecuteRefresh);
        }

        public ObservableCollection<SaveFileInfo> SaveFiles { get; }

        public string SavesDirectory
        {
            get => _savesDirectory;
            set
            {
                if (SetProperty(ref _savesDirectory, value))
                {
                    RefreshSaves();
                }
            }
        }

        public SaveFileInfo SelectedSave
        {
            get => _selectedSave;
            set
            {
                if (SetProperty(ref _selectedSave, value) && value != null)
                {
                    SaveSelected?.Invoke(value.FolderPath);
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public ICommand BrowseDirectoryCommand { get; }
        public ICommand LoadSelectedCommand { get; }
        public ICommand RefreshCommand { get; }

        public event Action<string> SaveSelected;

        public void RefreshSaves()
        {
            IsLoading = true;
            SaveFiles.Clear();

            try
            {
                if (Directory.Exists(_savesDirectory))
                {
                    var saves = _saveService.DiscoverSaveFiles(_savesDirectory)
                        .OrderByDescending(s => s.SaveTime)
                        .ToList();

                    foreach (var save in saves)
                    {
                        SaveFiles.Add(save);
                    }
                    LoadPortraitsInBackground(saves);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading saves: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>Portraits need a peek into each global.dat, so they fill in after the list is shown.</summary>
        private void LoadPortraitsInBackground(System.Collections.Generic.List<SaveFileInfo> saves)
        {
            var runtime = _saveService.Runtime;
            if (runtime == null)
                return;
            var dispatcher = System.Windows.Application.Current.Dispatcher;
            System.Threading.Tasks.Task.Run(() =>
            {
                foreach (var save in saves.Where(s => !s.IsUnreadable))
                {
                    try
                    {
                        var image = GameIcons.Portrait(Core.SaveSummary.PeekPortrait(runtime, save.FolderPath), "s");
                        if (image != null)
                            dispatcher.BeginInvoke(new Action(() => save.Portrait = image));
                    }
                    catch (Exception) { }
                }
            });
        }

        private void ExecuteBrowseDirectory()
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select Underrail saves directory",
                SelectedPath = _savesDirectory
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                SavesDirectory = dialog.SelectedPath;
                AppSettings.SavesDirectory = dialog.SelectedPath;
            }
        }

        private void ExecuteLoadSelected()
        {
            if (SelectedSave != null)
            {
                SaveSelected?.Invoke(SelectedSave.FolderPath);
            }
        }

        private void ExecuteRefresh()
        {
            RefreshSaves();
        }
    }
}
