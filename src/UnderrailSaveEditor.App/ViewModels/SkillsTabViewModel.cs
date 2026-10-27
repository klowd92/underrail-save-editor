using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace UnderrailSaveEditor.App.ViewModels
{
    public class SkillCategoryGroup : ViewModelBase
    {
        public string CategoryName { get; set; }
        public ObservableCollection<StatDisplayModel> Skills { get; set; }
    }

    public class SkillsTabViewModel : TabViewModelBase
    {
        private int _remainingPoints;

        public SkillsTabViewModel()
        {
            SkillCategories = new ObservableCollection<SkillCategoryGroup>();

            SetAllInCategoryCommand = new RelayCommand<object>(ExecuteSetAllInCategory);
            SetAllSkillsCommand = new RelayCommand<int>(ExecuteSetAllSkills);
            IncrementSkillCommand = new RelayCommand<StatDisplayModel>(s => { if (s != null) s.Value++; });
            DecrementSkillCommand = new RelayCommand<StatDisplayModel>(s => { if (s != null && s.Value > 0) s.Value--; });
        }

        public ObservableCollection<SkillCategoryGroup> SkillCategories { get; }

        public ICommand SetAllInCategoryCommand { get; }
        public ICommand SetAllSkillsCommand { get; }
        public ICommand IncrementSkillCommand { get; }
        public ICommand DecrementSkillCommand { get; }

        public int RemainingPoints
        {
            get => _remainingPoints;
            private set => SetProperty(ref _remainingPoints, value);
        }

        private StatDisplayModel _selectedSkill;

        /// <summary>The skill shown in the description panel (the row last clicked).</summary>
        public StatDisplayModel SelectedSkill
        {
            get => _selectedSkill;
            set
            {
                if (!SetProperty(ref _selectedSkill, value))
                    return;
                OnPropertyChanged(nameof(DetailTitle));
                OnPropertyChanged(nameof(DetailText));
                OnPropertyChanged(nameof(DetailAbilities));
                OnPropertyChanged(nameof(DetailSynergies));
            }
        }

        public string DetailTitle => SelectedSkill?.DisplayName ?? "Skills";
        public string DetailText => SelectedSkill?.Description ?? Save?.Runtime.Localize("Skills/GeneralInfo") ?? "";
        public string DetailAbilities => SelectedSkill?.RelatedAbilitiesText ?? "";
        public string DetailSynergies => SelectedSkill?.SynergiesText ?? "";

        /// <summary>Skills and their categories come from the game, in the game's order.</summary>
        protected override void OnLoad()
        {
            SelectedSkill = null;
            OnPropertyChanged(nameof(DetailText));
            SkillCategories.Clear();
            foreach (var group in Save.Player.Skills.GroupBy(s => s.Category))
            {
                SkillCategories.Add(new SkillCategoryGroup
                {
                    CategoryName = group.Key,
                    Skills = new ObservableCollection<StatDisplayModel>(group.Select(s =>
                        new StatDisplayModel(s.Name, s.DisplayName, m => Edit(() => Save.Player.SetSkill(m.Name, m.Value)))
                        {
                            Description = s.Description,
                            RelatedAbilitiesText = string.Join(" or ", s.RelatedAbilities),
                            SynergiesText = string.Join("\n", s.Synergies.Select(x => $"{Core.Text.Humanize(x.Key)}  {x.Value * 100:0.#}%")),
                        })),
                });
            }
            OnRefresh();
        }

        protected override void OnRefresh()
        {
            var current = Save.Player.Skills.ToDictionary(s => s.Name);
            foreach (var model in SkillCategories.SelectMany(c => c.Skills))
            {
                if (!current.TryGetValue(model.Name, out var stat))
                    continue;
                model.Value = stat.Value;
                model.EffectiveValue = stat.ModifiedValue;
            }
            RemainingPoints = Save.Player.SkillPoints;
        }

        private void ExecuteSetAllInCategory(object param)
        {
            if (param is object[] args && args.Length == 2 && args[0] is SkillCategoryGroup category && int.TryParse(args[1]?.ToString(), out int value))
            {
                foreach (var skill in category.Skills)
                    skill.Value = value;
            }
        }

        private void ExecuteSetAllSkills(int value)
        {
            foreach (var skill in SkillCategories.SelectMany(c => c.Skills))
                skill.Value = value;
        }
    }
}
