using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using UnderrailSaveEditor.App.Services;
using UnderrailSaveEditor.Core;

namespace UnderrailSaveEditor.App.ViewModels
{
    public class SpecializationDisplayModel : ViewModelBase
    {
        private readonly Action<SpecializationDisplayModel> _onChanged;
        private int _points;
        private bool _hasFeat;

        public SpecializationDisplayModel(SpecializationInfo spec, Action<SpecializationDisplayModel> onChanged)
        {
            Spec = spec;
            _onChanged = onChanged;
        }

        public SpecializationInfo Spec { get; }
        public string DisplayName => Spec.FullName;
        public int MaxPoints => Spec.MaxPoints;
        public ImageSource Icon => Spec.RequiredFeat == null ? null : GameIcons.Icon(Spec.RequiredFeat.IconName);

        public int Points
        {
            get => _points;
            set
            {
                if (!SetProperty(ref _points, value))
                    return;
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(PointsText));
                _onChanged(this);
            }
        }

        public string PointsText => $"{Points} / {MaxPoints}";
        public bool IsActive => Points > 0;

        /// <summary>The character has the feat this specialization improves.</summary>
        public bool HasFeat
        {
            get => _hasFeat;
            set => SetProperty(ref _hasFeat, value);
        }
    }

    /// <summary>The game's specialization screen: point list on the left, description on the right.</summary>
    public class SpecializationsTabViewModel : TabViewModelBase
    {
        private string _searchText;
        private bool _showAll;
        private int _remainingPoints;
        private SpecializationDisplayModel _selected;

        public SpecializationsTabViewModel()
        {
            AllSpecializations = new ObservableCollection<SpecializationDisplayModel>();
            FilteredSpecializations = new ObservableCollection<SpecializationDisplayModel>();

            IncrementPointsCommand = new RelayCommand<SpecializationDisplayModel>(s => { if (s != null) { s.Points++; Selected = s; } });
            DecrementPointsCommand = new RelayCommand<SpecializationDisplayModel>(s => { if (s != null && s.Points > 0) { s.Points--; Selected = s; } });
        }

        public ObservableCollection<SpecializationDisplayModel> AllSpecializations { get; }
        public ObservableCollection<SpecializationDisplayModel> FilteredSpecializations { get; }

        public ICommand IncrementPointsCommand { get; }
        public ICommand DecrementPointsCommand { get; }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                    FilterSpecializations();
            }
        }

        /// <summary>Unchecked: only specializations of feats the character has (or that already have points), like the game.</summary>
        public bool ShowAll
        {
            get => _showAll;
            set
            {
                if (SetProperty(ref _showAll, value))
                    FilterSpecializations();
            }
        }

        public int ActiveSpecCount => AllSpecializations.Count(s => s.IsActive);

        public int RemainingPoints
        {
            get => _remainingPoints;
            private set => SetProperty(ref _remainingPoints, value);
        }

        public SpecializationDisplayModel Selected
        {
            get => _selected;
            set
            {
                if (!SetProperty(ref _selected, value))
                    return;
                OnPropertyChanged(nameof(DetailTitle));
                OnPropertyChanged(nameof(DetailIcon));
                OnPropertyChanged(nameof(DetailText));
                OnPropertyChanged(nameof(DetailFacts));
            }
        }

        public string DetailTitle => Selected?.DisplayName ?? "Specialization";
        public ImageSource DetailIcon => Selected?.Icon;
        public string DetailText => Selected != null ? Selected.Spec.Description : Save?.Runtime.Localize("Specialization/GeneralInfo") ?? "";

        public string DetailFacts
        {
            get
            {
                if (Selected == null)
                    return "";
                var feat = Selected.Spec.RequiredFeat;
                return $"Improves the feat: {feat?.DisplayName ?? "?"}{(Selected.HasFeat ? "" : " (the character doesn't have it)")}\nMaximum points: {Selected.MaxPoints}";
            }
        }

        /// <summary>The list is the game's specialization registry (feat, label, max points and descriptions come from the game).</summary>
        protected override void OnLoad()
        {
            if (AllSpecializations.Count == 0)
            {
                foreach (var spec in Save.Runtime.Specializations)
                    AllSpecializations.Add(new SpecializationDisplayModel(spec, m => Edit(() => Save.Player.SetSpecialization(m.Spec.Id, m.Points))));
            }
            Selected = null;
            OnRefresh();
            FilterSpecializations();
            OnPropertyChanged(nameof(DetailText));
        }

        protected override void OnRefresh()
        {
            var owned = new HashSet<FeatInfo>(Save.Player.Feats);
            bool featsChanged = false;
            foreach (var spec in AllSpecializations)
            {
                spec.Points = Save.Player.GetSpecialization(spec.Spec.Id);
                bool hasFeat = spec.Spec.RequiredFeat != null && owned.Contains(spec.Spec.RequiredFeat);
                featsChanged |= hasFeat != spec.HasFeat;
                spec.HasFeat = hasFeat;
            }
            // A feat was added or removed elsewhere: its specializations appear or disappear in the default view.
            if (featsChanged && !ShowAll)
                FilterSpecializations();
            RemainingPoints = Save.Player.SpecializationPoints;
            OnPropertyChanged(nameof(ActiveSpecCount));
            OnPropertyChanged(nameof(DetailFacts));
        }

        /// <summary>Specializations with points first, then the rest alphabetically (the game's order).</summary>
        private void FilterSpecializations()
        {
            var selected = Selected;
            FilteredSpecializations.Clear();
            foreach (var spec in AllSpecializations.OrderByDescending(s => s.IsActive).ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                if (!ShowAll && !spec.HasFeat && !spec.IsActive)
                    continue;
                if (!string.IsNullOrWhiteSpace(SearchText) &&
                    spec.DisplayName.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                FilteredSpecializations.Add(spec);
            }
            Selected = FilteredSpecializations.Contains(selected) ? selected : null;
        }
    }
}
