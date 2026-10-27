using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using UnderrailSaveEditor.App.Services;

namespace UnderrailSaveEditor.App.ViewModels
{
    public class GeneralTabViewModel : TabViewModelBase
    {
        private string _characterName;
        private string _gender;
        private int _level;
        private int _experience;
        private int _skillPoints;
        private int _featPoints;
        private int _abilityPoints;
        private int _specializationPoints;
        private IReadOnlyList<string> _genderOptions = new string[0];

        public GeneralTabViewModel()
        {
            IncrementSkillPointsCommand = new RelayCommand(() => SkillPoints++);
            DecrementSkillPointsCommand = new RelayCommand(() => { if (SkillPoints > 0) SkillPoints--; });
            IncrementFeatPointsCommand = new RelayCommand(() => FeatPoints++);
            DecrementFeatPointsCommand = new RelayCommand(() => { if (FeatPoints > 0) FeatPoints--; });
            IncrementAbilityPointsCommand = new RelayCommand(() => AbilityPoints++);
            DecrementAbilityPointsCommand = new RelayCommand(() => { if (AbilityPoints > 0) AbilityPoints--; });
            IncrementSpecPointsCommand = new RelayCommand(() => SpecializationPoints++);
            DecrementSpecPointsCommand = new RelayCommand(() => { if (SpecializationPoints > 0) SpecializationPoints--; });
            PreviousPortraitCommand = new RelayCommand(() => StepPortrait(-1), () => Save != null);
            NextPortraitCommand = new RelayCommand(() => StepPortrait(1), () => Save != null);
        }

        public ICommand PreviousPortraitCommand { get; }
        public ICommand NextPortraitCommand { get; }

        private string _portraitName;

        /// <summary>The portrait chosen at character creation ("biotech_bl").</summary>
        public string PortraitName
        {
            get => _portraitName;
            private set
            {
                if (SetProperty(ref _portraitName, value))
                    OnPropertyChanged(nameof(Portrait));
            }
        }

        public ImageSource Portrait => GameIcons.Portrait(PortraitName, "l");

        /// <summary>Cycles through every portrait the game offers, like the arrows on the character creation screen.</summary>
        private void StepPortrait(int step)
        {
            var names = Save.Runtime.Images.PortraitNames.ToList();
            if (names.Count == 0)
                return;
            int index = names.FindIndex(n => string.Equals(n, PortraitName, StringComparison.OrdinalIgnoreCase));
            var next = names[((index < 0 ? 0 : index + step) % names.Count + names.Count) % names.Count];
            PortraitName = next;
            Edit(() => Save.Player.Portrait = next);
        }

        public ICommand IncrementSkillPointsCommand { get; }
        public ICommand DecrementSkillPointsCommand { get; }
        public ICommand IncrementFeatPointsCommand { get; }
        public ICommand DecrementFeatPointsCommand { get; }
        public ICommand IncrementAbilityPointsCommand { get; }
        public ICommand DecrementAbilityPointsCommand { get; }
        public ICommand IncrementSpecPointsCommand { get; }
        public ICommand DecrementSpecPointsCommand { get; }

        public string CharacterName
        {
            get => _characterName;
            set { if (SetProperty(ref _characterName, value)) Edit(() => Save.Player.Name = value); }
        }

        public string Gender
        {
            get => _gender;
            set { if (SetProperty(ref _gender, value) && value != null) Edit(() => Save.Player.Gender = value); }
        }

        public int Level
        {
            get => _level;
            set { if (SetProperty(ref _level, value)) Edit(() => Save.Player.Level = value); }
        }

        public int Experience
        {
            get => _experience;
            set { if (SetProperty(ref _experience, value)) Edit(() => Save.Player.Experience = value); }
        }

        public int SkillPoints
        {
            get => _skillPoints;
            set { if (SetProperty(ref _skillPoints, value)) Edit(() => Save.Player.SkillPoints = value); }
        }

        public int FeatPoints
        {
            get => _featPoints;
            set { if (SetProperty(ref _featPoints, value)) Edit(() => Save.Player.FeatPoints = value); }
        }

        public int AbilityPoints
        {
            get => _abilityPoints;
            set { if (SetProperty(ref _abilityPoints, value)) Edit(() => Save.Player.AbilityPoints = value); }
        }

        public int SpecializationPoints
        {
            get => _specializationPoints;
            set { if (SetProperty(ref _specializationPoints, value)) Edit(() => Save.Player.SpecializationPoints = value); }
        }

        public IReadOnlyList<string> GenderOptions
        {
            get => _genderOptions;
            private set => SetProperty(ref _genderOptions, value);
        }

        protected override void OnLoad()
        {
            GenderOptions = Save.Player.GenderOptions;
            OnRefresh();
        }

        protected override void OnRefresh()
        {
            var p = Save.Player;
            PortraitName = p.Portrait;
            CharacterName = p.Name;
            Gender = p.Gender;
            Level = p.Level;
            Experience = p.Experience;
            SkillPoints = p.SkillPoints;
            FeatPoints = p.FeatPoints;
            AbilityPoints = p.AbilityPoints;
            SpecializationPoints = p.SpecializationPoints;
        }
    }
}
