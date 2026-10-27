using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using UnderrailSaveEditor.Core;

using UnderrailSaveEditor.App.Services;

namespace UnderrailSaveEditor.App.ViewModels
{
    public class GameVariableDisplayModel : ViewModelBase
    {
        private string _name;
        private string _value;
        private string _valueType;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
        }

        public string ValueType
        {
            get => _valueType;
            set => SetProperty(ref _valueType, value);
        }

        public void Update(object value)
        {
            Value = GameVariables.Format(value);
            ValueType = value?.GetType().Name ?? "null";
        }
    }

    public class GameVariablesTabViewModel : TabViewModelBase
    {
        private string _searchText;
        private GameVariableDisplayModel _selectedVariable;
        private string _editName;
        private string _editValue;

        public GameVariablesTabViewModel()
        {
            AllVariables = new ObservableCollection<GameVariableDisplayModel>();
            FilteredVariables = new ObservableCollection<GameVariableDisplayModel>();

            ApplyEditCommand = new RelayCommand(ExecuteApplyEdit, () => SelectedVariable != null && !string.IsNullOrWhiteSpace(EditName) && Save != null);
            AddVariableCommand = new RelayCommand(ExecuteAddVariable, () => !string.IsNullOrWhiteSpace(EditName) && Save != null);
            DeleteVariableCommand = new RelayCommand(ExecuteDeleteVariable, () => SelectedVariable != null);
        }

        public ObservableCollection<GameVariableDisplayModel> AllVariables { get; }
        public ObservableCollection<GameVariableDisplayModel> FilteredVariables { get; }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                    FilterVariables();
            }
        }

        public GameVariableDisplayModel SelectedVariable
        {
            get => _selectedVariable;
            set
            {
                if (SetProperty(ref _selectedVariable, value))
                {
                    EditName = value?.Name ?? string.Empty;
                    EditValue = value?.Value ?? string.Empty;
                }
            }
        }

        public string EditName
        {
            get => _editName;
            set => SetProperty(ref _editName, value);
        }

        public string EditValue
        {
            get => _editValue;
            set => SetProperty(ref _editValue, value);
        }

        public ICommand ApplyEditCommand { get; }
        public ICommand AddVariableCommand { get; }
        public ICommand DeleteVariableCommand { get; }

        private void ExecuteApplyEdit() => SetVariable(SelectedVariable.Name);

        /// <summary>Adds the variable, or updates it if one with that name exists.</summary>
        private void ExecuteAddVariable() => SetVariable(EditName.Trim());

        private void SetVariable(string name)
        {
            object value;
            try
            {
                // An existing variable keeps its type: editing an int with "abc" is refused rather than turned into a string.
                value = GameVariables.Parse(EditValue, Save.Variables.Get(name)?.GetType());
            }
            catch (Exception ex) when (ex is FormatException || ex is OverflowException || ex is ArgumentException || ex is NotSupportedException)
            {
                Dialogs.Show($"'{EditValue}' is not a valid value for '{name}' ({Save.Variables.Get(name)?.GetType().Name}).",
                    "Invalid value", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Edit(() => Save.Variables.Set(name, value));

            var model = AllVariables.FirstOrDefault(v => v.Name == name);
            if (model == null)
            {
                model = new GameVariableDisplayModel { Name = name };
                AllVariables.Add(model);
                FilterVariables();
            }
            model.Update(Save.Variables.Get(name));
            SelectedVariable = model;
        }

        private void ExecuteDeleteVariable()
        {
            var variable = SelectedVariable;
            Edit(() => Save.Variables.Remove(variable.Name));
            AllVariables.Remove(variable);
            FilterVariables();
            SelectedVariable = null;
        }

        private void FilterVariables()
        {
            FilteredVariables.Clear();

            foreach (var variable in AllVariables)
            {
                if (string.IsNullOrWhiteSpace(SearchText) ||
                    variable.Name.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (variable.Value?.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    FilteredVariables.Add(variable);
                }
            }
        }

        protected override void OnLoad()
        {
            AllVariables.Clear();
            SelectedVariable = null;
            foreach (var kv in Save.Variables.All.OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase))
            {
                var model = new GameVariableDisplayModel { Name = kv.Key };
                model.Update(kv.Value);
                AllVariables.Add(model);
            }
            FilterVariables();
        }

        /// <summary>Variables only change through this tab, which updates its rows itself.</summary>
        protected override void OnRefresh()
        {
        }
    }
}
