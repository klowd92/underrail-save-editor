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
    /// <summary>One requirement line: met (true), not met (false) or unknown (null).</summary>
    public class RequirementLine
    {
        public RequirementLine(string text, bool? met)
        {
            Text = text;
            Met = met;
        }

        public string Text { get; }
        public bool? Met { get; }
        public string Mark => Met == null ? "•" : Met.Value ? "✓" : "✗";
        public bool IsUnmet => Met == false;
    }

    public class FeatToggleModel : ViewModelBase
    {
        private readonly FeatsTabViewModel _parent;
        private bool _isActive;

        public FeatToggleModel(FeatsTabViewModel parent, FeatInfo feat)
        {
            _parent = parent;
            Feat = feat;
        }

        public FeatInfo Feat { get; }
        public string DisplayName => Feat.DisplayName;
        public ImageSource Icon => GameIcons.Icon(Feat.IconName);

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (SetProperty(ref _isActive, value))
                    _parent.OnFeatToggled(this);
            }
        }
    }

    /// <summary>The game's feat screen: feat list with icons on the left, the selected feat's description on the right.</summary>
    public class FeatsTabViewModel : TabViewModelBase
    {
        private string _searchText;
        private bool _showAllFeats = true;
        private int _remainingFeatPoints;
        private FeatToggleModel _selectedFeat;

        public FeatsTabViewModel()
        {
            AllFeats = new ObservableCollection<FeatToggleModel>();
            FilteredFeats = new ObservableCollection<FeatToggleModel>();
            ToggleSelectedCommand = new RelayCommand(() => SelectedFeat.IsActive = !SelectedFeat.IsActive, () => SelectedFeat != null);
        }

        public ObservableCollection<FeatToggleModel> AllFeats { get; }
        public ObservableCollection<FeatToggleModel> FilteredFeats { get; }
        public ICommand ToggleSelectedCommand { get; }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                    FilterFeats();
            }
        }

        /// <summary>Unchecked: only the character's feats (like the game's default view).</summary>
        public bool ShowAllFeats
        {
            get => _showAllFeats;
            set
            {
                if (SetProperty(ref _showAllFeats, value))
                    FilterFeats();
            }
        }

        public int RemainingFeatPoints
        {
            get => _remainingFeatPoints;
            set => SetProperty(ref _remainingFeatPoints, value);
        }

        public int ActiveFeatCount => AllFeats.Count(f => f.IsActive);

        public FeatToggleModel SelectedFeat
        {
            get => _selectedFeat;
            set
            {
                if (!SetProperty(ref _selectedFeat, value))
                    return;
                OnPropertyChanged(nameof(DetailTitle));
                OnPropertyChanged(nameof(DetailIcon));
                OnPropertyChanged(nameof(DetailText));
                OnPropertyChanged(nameof(DetailFacts));
                OnPropertyChanged(nameof(DetailRequirements));
                OnPropertyChanged(nameof(ToggleText));
            }
        }

        /// <summary>
        /// The feat's requirements as the game lists them ("Agility 7", "Any psi skill 40", "Feat: Psi Empathy"),
        /// plus the minimal level, each marked against the loaded character.
        /// </summary>
        public IReadOnlyList<RequirementLine> DetailRequirements
        {
            get
            {
                var f = SelectedFeat?.Feat;
                if (f == null || Save == null)
                    return new RequirementLine[0];
                var lines = f.Requirements(Save.Player).Select(r => new RequirementLine(r.Text, r.Met)).ToList();
                int level = Math.Max(1, f.MinimalLevel);
                lines.Add(new RequirementLine($"Minimal level {level}", Save.Player.Level >= level));
                return lines;
            }
        }

        // ---- detail panel (general info when nothing is selected, like the game) ----

        public string DetailTitle => SelectedFeat?.DisplayName ?? "Feats";
        public ImageSource DetailIcon => SelectedFeat?.Icon;
        public string DetailText => SelectedFeat != null ? SelectedFeat.Feat.Description : Save?.Runtime.Localize("Feats/GeneralInfo") ?? "";

        public string DetailFacts
        {
            get
            {
                var f = SelectedFeat?.Feat;
                if (f == null)
                    return "";
                var facts = new List<string>();
                if (!string.IsNullOrEmpty(f.Dlc))
                    facts.Add("DLC: " + (f.Dlc == "xpbl" ? "Expedition" : f.Dlc == "xphw" ? "Heavy Duty" : f.Dlc));
                if (!f.Purchasable)
                    facts.Add("Not offered at level-up (granted by the story or other means)");
                if (f.Veteran)
                    facts.Add("Veteran feat");
                return string.Join("\n", facts);
            }
        }

        public string ToggleText => SelectedFeat?.IsActive == true ? "Remove feat" : "Add feat";

        /// <summary>The feat list is the game's own feat registry, so it always matches the installed game and DLCs.</summary>
        protected override void OnLoad()
        {
            if (AllFeats.Count == 0)
            {
                foreach (var feat in Save.Runtime.Feats)
                    AllFeats.Add(new FeatToggleModel(this, feat));
            }
            OnRefresh();
            FilterFeats();
            OnPropertyChanged(nameof(DetailText));
        }

        protected override void OnRefresh()
        {
            var owned = Save.Player.Feats.ToList();
            foreach (var feat in AllFeats)
                feat.IsActive = owned.Contains(feat.Feat);
            RemainingFeatPoints = Save.Player.FeatPoints;
            OnPropertyChanged(nameof(ActiveFeatCount));
            OnPropertyChanged(nameof(ToggleText));
            OnPropertyChanged(nameof(DetailRequirements));
        }

        public void OnFeatToggled(FeatToggleModel feat)
        {
            Edit(() =>
            {
                if (feat.IsActive)
                    Save.Player.AddFeat(feat.Feat);
                else
                    Save.Player.RemoveFeat(feat.Feat);
            });
            OnPropertyChanged(nameof(ActiveFeatCount));
            OnPropertyChanged(nameof(ToggleText));
        }

        /// <summary>The character's feats first, then the rest, each alphabetically.</summary>
        private void FilterFeats()
        {
            var selected = SelectedFeat;
            FilteredFeats.Clear();
            foreach (var feat in AllFeats.OrderByDescending(f => f.IsActive).ThenBy(f => f.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(SearchText) &&
                    feat.DisplayName.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (!ShowAllFeats && !feat.IsActive)
                    continue;
                FilteredFeats.Add(feat);
            }
            SelectedFeat = FilteredFeats.Contains(selected) ? selected : null;
        }
    }
}
