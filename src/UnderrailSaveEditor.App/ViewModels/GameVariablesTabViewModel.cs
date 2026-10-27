using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using UnderrailSaveEditor.App.Services;
using UnderrailSaveEditor.Core;

namespace UnderrailSaveEditor.App.ViewModels
{
    public class GameVariableDisplayModel : ViewModelBase
    {
        private string _value;
        private string _valueType;
        private bool _isSet;

        public string Name { get; set; }

        /// <summary>From the game's dialogs: the type they use, and which dialogs use the variable (null if unknown).</summary>
        public VariableInfo Known { get; set; }

        public string UsedIn => Known == null ? "" : string.Join(", ", Known.Dialogs.Take(3)) + (Known.Dialogs.Count > 3 ? $" (+{Known.Dialogs.Count - 3})" : "");

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

        /// <summary>False for a variable the game knows but this save doesn't have yet.</summary>
        public bool IsSet
        {
            get => _isSet;
            set => SetProperty(ref _isSet, value);
        }

        public void Update(object value)
        {
            IsSet = true;
            Value = GameVariables.Format(value);
            ValueType = value?.GetType().Name ?? "null";
        }

        public void Unset()
        {
            IsSet = false;
            Value = "(unset)";
            ValueType = Known?.ValueType.Name ?? "";
        }
    }

    /// <summary>
    /// The save's global state variables. With "Show all variables", also every variable the game's dialogs use that the
    /// save doesn't have yet (unset); setting one adds it to the save with the type the game expects.
    /// Dialog-only scratch variables ("tempcontext.") and variables made at run time (event timers...) aren't listed.
    /// </summary>
    public class GameVariablesTabViewModel : TabViewModelBase
    {
        private string _searchText;
        private bool _showAll;
        private GameVariableDisplayModel _selectedVariable;
        private string _editName;
        private string _editValue;
        private List<GameVariableDisplayModel> _unsetKnown = new List<GameVariableDisplayModel>();

        public GameVariablesTabViewModel()
        {
            AllVariables = new ObservableCollection<GameVariableDisplayModel>();
            FilteredVariables = new ObservableCollection<GameVariableDisplayModel>();

            ApplyEditCommand = new RelayCommand(ExecuteApplyEdit, () => SelectedVariable != null && !string.IsNullOrWhiteSpace(EditName) && Save != null);
            AddVariableCommand = new RelayCommand(ExecuteAddVariable, () => !string.IsNullOrWhiteSpace(EditName) && Save != null);
            DeleteVariableCommand = new RelayCommand(ExecuteDeleteVariable, () => SelectedVariable?.IsSet == true);
        }

        /// <summary>Variables stored in the save.</summary>
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

        /// <summary>Also list the game's known variables that this save hasn't set.</summary>
        public bool ShowAll
        {
            get => _showAll;
            set
            {
                if (SetProperty(ref _showAll, value))
                    FilterVariables();
            }
        }

        public string CountText => $"{FilteredVariables.Count} shown · {AllVariables.Count} set" +
            (ShowAll ? $" · {_unsetKnown.Count} unset" : "");

        public GameVariableDisplayModel SelectedVariable
        {
            get => _selectedVariable;
            set
            {
                if (SetProperty(ref _selectedVariable, value))
                {
                    EditName = value?.Name ?? string.Empty;
                    EditValue = value == null ? string.Empty : value.IsSet ? value.Value : DefaultText(value.Known?.ValueType);
                    OnPropertyChanged(nameof(SelectedHint));
                }
            }
        }

        /// <summary>Type, state and the dialogs that use the selected variable.</summary>
        public string SelectedHint
        {
            get
            {
                var v = SelectedVariable;
                if (v == null)
                    return "Select a variable, or type a name and value and press Add New.";
                var state = v.IsSet ? "Set in this save." : "Not set in this save: the game treats it as false / 0 / empty until something sets it.";
                var type = v.Known != null ? $" The game uses it as {FriendlyType(v.Known.ValueType)}." : "";
                var dialogs = v.Known != null && v.Known.Dialogs.Count > 0 ? $" Used in dialogs: {string.Join(", ", v.Known.Dialogs)}." : "";
                return state + type + dialogs;
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
            // An existing variable keeps its type; an unset one gets the type the game's dialogs use.
            var type = Save.Variables.Get(name)?.GetType() ?? FindModel(name)?.Known?.ValueType ?? KnownByName(name)?.ValueType;
            object value;
            try
            {
                value = GameVariables.Parse(EditValue, type);
            }
            catch (Exception ex) when (ex is FormatException || ex is OverflowException || ex is ArgumentException || ex is NotSupportedException)
            {
                Dialogs.Show($"'{EditValue}' is not a valid value for '{name}' ({FriendlyType(type)}).",
                    "Invalid value", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Edit(() => Save.Variables.Set(name, value));

            var model = AllVariables.FirstOrDefault(v => v.Name == name);
            if (model == null)
            {
                model = _unsetKnown.FirstOrDefault(v => v.Name == name) ?? new GameVariableDisplayModel { Name = name, Known = KnownByName(name) };
                _unsetKnown.Remove(model);
                AllVariables.Add(model);
                model.Update(Save.Variables.Get(name));
                FilterVariables();
            }
            else
            {
                model.Update(Save.Variables.Get(name));
            }
            SelectedVariable = model;
            OnPropertyChanged(nameof(SelectedHint));
        }

        private void ExecuteDeleteVariable()
        {
            var variable = SelectedVariable;
            Edit(() => Save.Variables.Remove(variable.Name));
            AllVariables.Remove(variable);
            if (variable.Known != null)
            {
                // The game still uses it: it goes back to the "unset" list.
                variable.Unset();
                _unsetKnown.Add(variable);
            }
            FilterVariables();
            SelectedVariable = ShowAll && variable.Known != null ? variable : null;
        }

        private void FilterVariables()
        {
            var selected = SelectedVariable;
            FilteredVariables.Clear();
            IEnumerable<GameVariableDisplayModel> rows = AllVariables;
            if (ShowAll)
                rows = rows.Concat(_unsetKnown);
            foreach (var variable in rows.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(SearchText) ||
                    variable.Name.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (variable.IsSet && variable.Value?.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    variable.UsedIn.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    FilteredVariables.Add(variable);
                }
            }
            if (selected != null && FilteredVariables.Contains(selected))
                SelectedVariable = selected;
            OnPropertyChanged(nameof(CountText));
        }

        protected override void OnLoad()
        {
            AllVariables.Clear();
            SelectedVariable = null;
            IReadOnlyList<VariableInfo> known;
            try { known = Save.Runtime.KnownVariables; }
            catch (Exception) { known = new VariableInfo[0]; }
            var knownByName = known.ToDictionary(v => v.Name, StringComparer.Ordinal);

            foreach (var kv in Save.Variables.All)
            {
                var model = new GameVariableDisplayModel { Name = kv.Key, Known = knownByName.TryGetValue(kv.Key, out var k) ? k : null };
                model.Update(kv.Value);
                AllVariables.Add(model);
            }
            var set = new HashSet<string>(AllVariables.Select(v => v.Name), StringComparer.Ordinal);
            _unsetKnown = known.Where(v => !set.Contains(v.Name)).Select(v =>
            {
                var model = new GameVariableDisplayModel { Name = v.Name, Known = v };
                model.Unset();
                return model;
            }).ToList();
            FilterVariables();
        }

        /// <summary>Variables only change through this tab, which updates its rows itself.</summary>
        protected override void OnRefresh()
        {
        }

        private GameVariableDisplayModel FindModel(string name) =>
            AllVariables.FirstOrDefault(v => v.Name == name) ?? _unsetKnown.FirstOrDefault(v => v.Name == name);

        private VariableInfo KnownByName(string name) => Save?.Runtime.KnownVariables.FirstOrDefault(v => v.Name == name);

        private static string DefaultText(Type type) =>
            type == typeof(bool) ? "True" : type == typeof(int) || type == typeof(float) ? "1" : "";

        private static string FriendlyType(Type type) =>
            type == null ? "a value" : type == typeof(bool) ? "true/false" : type == typeof(int) ? "a whole number"
            : type == typeof(float) || type == typeof(double) ? "a number" : type == typeof(string) ? "text" : type.Name;
    }
}
