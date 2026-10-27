using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace UnderrailSaveEditor.App.ViewModels
{
    /// <summary>A base ability or skill row: editable base value plus the game's effective value.</summary>
    public class StatDisplayModel : ViewModelBase
    {
        private readonly Action<StatDisplayModel> _onChanged;
        private int _value;
        private int _effectiveValue;

        public StatDisplayModel(string name, string displayName, Action<StatDisplayModel> onChanged)
        {
            Name = name;
            DisplayName = displayName;
            _onChanged = onChanged;
        }

        /// <summary>Skills: the game's description, related base abilities and synergies (empty for abilities).</summary>
        public string Description { get; set; } = "";
        public string RelatedAbilitiesText { get; set; } = "";
        public string SynergiesText { get; set; } = "";

        /// <summary>The game's name for the stat (e.g. "Strength", "Guns").</summary>
        public string Name { get; }
        public string DisplayName { get; }

        public int Value
        {
            get => _value;
            set
            {
                if (!SetProperty(ref _value, value))
                    return;
                OnPropertyChanged(nameof(HasBonus));
                _onChanged(this);
            }
        }

        /// <summary>Value with the game's bonuses applied (abilities, feats, gear).</summary>
        public int EffectiveValue
        {
            get => _effectiveValue;
            set { if (SetProperty(ref _effectiveValue, value)) OnPropertyChanged(nameof(HasBonus)); }
        }

        public bool HasBonus => _effectiveValue != _value;
    }

    public class AbilitiesTabViewModel : TabViewModelBase
    {
        private int _remainingPoints;

        public AbilitiesTabViewModel()
        {
            Abilities = new ObservableCollection<StatDisplayModel>();
            IncrementAbilityCommand = new RelayCommand<StatDisplayModel>(a => { if (a != null) a.Value++; });
            DecrementAbilityCommand = new RelayCommand<StatDisplayModel>(a => { if (a != null && a.Value > 1) a.Value--; });
        }

        public ObservableCollection<StatDisplayModel> Abilities { get; }

        public ICommand IncrementAbilityCommand { get; }
        public ICommand DecrementAbilityCommand { get; }

        public int RemainingPoints
        {
            get => _remainingPoints;
            private set => SetProperty(ref _remainingPoints, value);
        }

        protected override void OnLoad()
        {
            Abilities.Clear();
            foreach (var a in Save.Player.BaseAbilities)
                Abilities.Add(new StatDisplayModel(a.Name, a.DisplayName, m => Edit(() => Save.Player.SetBaseAbility(m.Name, m.Value))));
            OnRefresh();
        }

        protected override void OnRefresh()
        {
            var current = Save.Player.BaseAbilities.ToDictionary(a => a.Name);
            foreach (var model in Abilities)
            {
                if (!current.TryGetValue(model.Name, out var stat))
                    continue;
                model.Value = stat.Value;
                model.EffectiveValue = stat.ModifiedValue;
            }
            RemainingPoints = Save.Player.AbilityPoints;
        }
    }
}
