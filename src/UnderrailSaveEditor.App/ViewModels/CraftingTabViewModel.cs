using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using UnderrailSaveEditor.Core;

namespace UnderrailSaveEditor.App.ViewModels
{
    public class SlotOption
    {
        public SlotOption(LibraryItem item) => Item = item;
        public LibraryItem Item { get; }
        public string Name => Item?.Name ?? "(empty)";
        public System.Windows.Media.ImageSource Icon => Item == null ? null : Services.GameIcons.Icon(Item.IconName);
        public override string ToString() => Name;
    }

    public class CraftSlotModel : ViewModelBase
    {
        private readonly GameRuntime _rt;
        private readonly Action _onChanged;
        private SlotOption _selected;
        private int _quality = 160;

        public CraftSlotModel(GameRuntime rt, BlueprintSlot slot, Action onChanged)
        {
            _rt = rt;
            Slot = slot;
            _onChanged = onChanged;
            Options = new[] { new SlotOption(null) }.Concat(slot.Candidates.Select(c => new SlotOption(c))).ToList();
            _selected = Options[0];
        }

        public BlueprintSlot Slot { get; }
        public string DisplayName => Slot.DisplayName + (Slot.Optional ? " (optional)" : "");
        public bool Optional => Slot.Optional;
        public IReadOnlyList<SlotOption> Options { get; }

        public SlotOption Selected
        {
            get => _selected;
            set
            {
                if (!SetProperty(ref _selected, value ?? Options[0]))
                    return;
                OnPropertyChanged(nameof(HasQuality));
                OnPropertyChanged(nameof(IsFilled));
                _onChanged();
            }
        }

        public bool IsFilled => Selected?.Item != null;
        public bool HasQuality => IsFilled && _rt.SampleItem(Selected.Item)?.HasQuality == true;

        public int Quality
        {
            get => _quality;
            set { if (SetProperty(ref _quality, value) && IsFilled) _onChanged(); }
        }

        /// <summary>A fresh component instance for this slot, or null when empty.</summary>
        public Item CreateComponent() => IsFilled ? _rt.CreateItem(Selected.Item.Path, 1, HasQuality ? Quality : (int?)null) : null;
    }

    public class RequirementModel
    {
        public string Skill { get; set; }
        public int Required { get; set; }
        public int Have { get; set; }
        public bool Met => Have >= Required;
        public string Text => $"{Core.Text.Humanize(Skill)} {Required}";
        public string HaveText => $"(you: {Have})";
    }

    /// <summary>
    /// Crafting simulator: pick a blueprint and components, and the game's own blueprint code computes the item,
    /// its stats and the skill requirements, with the loaded character as the crafter (crafting feats apply).
    /// </summary>
    public class CraftingTabViewModel : TabViewModelBase
    {
        private string _selectedCategory = "All";
        private string _blueprintSearch;
        private Blueprint _selectedBlueprint;
        private RichText _resultTooltip;
        private string _message;
        private bool _messageIsError;
        private CraftResult _lastResult;

        public CraftingTabViewModel()
        {
            Categories = new ObservableCollection<string>();
            FilteredBlueprints = new ObservableCollection<Blueprint>();
            Slots = new ObservableCollection<CraftSlotModel>();
            Requirements = new ObservableCollection<RequirementModel>();
            CreateCommand = new RelayCommand(ExecuteCreate, () => _lastResult?.Success == true && _lastResult.Item != null);
            ClearCommand = new RelayCommand(() => { foreach (var s in Slots) s.Selected = null; }, () => Slots.Count > 0);
        }

        public ObservableCollection<string> Categories { get; }
        public ObservableCollection<Blueprint> FilteredBlueprints { get; }
        public ObservableCollection<CraftSlotModel> Slots { get; }
        public ObservableCollection<RequirementModel> Requirements { get; }

        public ICommand CreateCommand { get; }
        public ICommand ClearCommand { get; }

        public string SelectedCategory
        {
            get => _selectedCategory;
            set { if (SetProperty(ref _selectedCategory, value)) FilterBlueprints(); }
        }

        public string BlueprintSearch
        {
            get => _blueprintSearch;
            set { if (SetProperty(ref _blueprintSearch, value)) FilterBlueprints(); }
        }

        public Blueprint SelectedBlueprint
        {
            get => _selectedBlueprint;
            set
            {
                if (!SetProperty(ref _selectedBlueprint, value))
                    return;
                BuildSlots();
            }
        }

        public RichText ResultTooltip
        {
            get => _resultTooltip;
            private set
            {
                SetProperty(ref _resultTooltip, value);
                OnPropertyChanged(nameof(ResultIcon));
            }
        }

        public System.Windows.Media.ImageSource ResultIcon =>
            _resultTooltip != null && _lastResult?.Item != null ? Services.GameIcons.Icon(_lastResult.Item.IconName) : null;

        public string Message
        {
            get => _message;
            private set => SetProperty(ref _message, value);
        }

        public bool MessageIsError
        {
            get => _messageIsError;
            private set => SetProperty(ref _messageIsError, value);
        }

        protected override void OnLoad()
        {
            if (Categories.Count == 0)
            {
                Categories.Add("All");
                foreach (var c in Save.Runtime.Blueprints.Select(b => b.Category).Distinct())
                    Categories.Add(c);
                FilterBlueprints();
            }
            Recompute();
        }

        /// <summary>The character may have changed (skills, feats): craft again so requirements and bonuses follow.</summary>
        protected override void OnRefresh() => Recompute();

        private void FilterBlueprints()
        {
            if (Save == null)
                return;
            FilteredBlueprints.Clear();
            foreach (var bp in Save.Runtime.Blueprints)
            {
                if (SelectedCategory != "All" && bp.Category != SelectedCategory)
                    continue;
                if (!string.IsNullOrWhiteSpace(BlueprintSearch) && bp.Name.IndexOf(BlueprintSearch, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                FilteredBlueprints.Add(bp);
            }
        }

        private bool _building;

        private void BuildSlots()
        {
            _building = true;
            try
            {
                Slots.Clear();
                if (SelectedBlueprint != null)
                {
                    foreach (var slot in SelectedBlueprint.Slots)
                    {
                        var model = new CraftSlotModel(Save.Runtime, slot, Recompute);
                        // Start with the first candidate in every required slot so there is something to look at.
                        if (!slot.Optional && model.Options.Count > 1)
                            model.Selected = model.Options[1];
                        Slots.Add(model);
                    }
                    PickWorkingDefaults();
                }
            }
            finally
            {
                _building = false;
            }
            Recompute();
        }

        /// <summary>
        /// The first candidate of each required slot may not fit together (e.g. a frame and a barrel of another
        /// caliber). Try the other candidates of the later required slots until the game accepts the combination.
        /// </summary>
        private void PickWorkingDefaults()
        {
            bool Works()
            {
                try
                {
                    var parts = Slots.Where(s => s.IsFilled).ToDictionary(s => s.Slot.Key, s => s.CreateComponent());
                    return Save.Runtime.Craft(SelectedBlueprint, parts, Save.Player).Success;
                }
                catch (Exception) { return false; }
            }

            foreach (var slot in Slots.Where(s => !s.Optional).Skip(1))
            {
                if (Works())
                    return;
                foreach (var option in slot.Options.Skip(1).Take(40))
                {
                    slot.Selected = option;
                    if (Works())
                        return;
                }
                slot.Selected = slot.Options.Count > 1 ? slot.Options[1] : null;
            }
        }

        private void Recompute()
        {
            if (_building)
                return;
            Requirements.Clear();
            _lastResult = null;
            if (Save == null || SelectedBlueprint == null)
            {
                ResultTooltip = null;
                Message = SelectedBlueprint == null ? "Choose a blueprint." : null;
                return;
            }

            try
            {
                var parts = Slots.Where(s => s.IsFilled).ToDictionary(s => s.Slot.Key, s => s.CreateComponent());
                _lastResult = Save.Runtime.Craft(SelectedBlueprint, parts, Save.Player);
            }
            catch (Exception ex)
            {
                ResultTooltip = null;
                Message = "The game could not craft this combination: " + ex.Message;
                MessageIsError = true;
                return;
            }

            var skills = Save.Player.Skills.ToDictionary(s => s.Name, s => s.ModifiedValue, StringComparer.OrdinalIgnoreCase);
            foreach (var req in _lastResult.SkillRequirements.Where(r => r.Value > 0))
                Requirements.Add(new RequirementModel { Skill = req.Key, Required = req.Value, Have = skills.TryGetValue(req.Key, out var v) ? v : 0 });

            if (_lastResult.Success && _lastResult.Item != null)
            {
                try { ResultTooltip = _lastResult.Item.Tooltip(Save.Player); }
                catch (Exception ex) { ResultTooltip = new RichTextBuilder().Line(_lastResult.Item.Name, 255, 255, 255).Line(ex.Message, 200, 80, 80).Build(); }
                bool canCraft = Requirements.All(r => r.Met);
                Message = canCraft ? "Your character could craft this." : "Your character lacks the skill to craft this in game.";
                MessageIsError = !canCraft;
            }
            else
            {
                ResultTooltip = null;
                var missing = Slots.Where(s => !s.Optional && !s.IsFilled).Select(s => s.Slot.DisplayName).ToList();
                Message = missing.Count > 0 ? "Missing: " + string.Join(", ", missing)
                    : string.IsNullOrEmpty(_lastResult.Error) ? "This combination can't be crafted." : _lastResult.Error;
                MessageIsError = true;
            }
        }

        /// <summary>Puts the crafted item into the character's bag (components are not needed or consumed).</summary>
        private void ExecuteCreate()
        {
            var item = _lastResult.Item;
            Edit(() => Save.Player.AddItem(item));
            Message = $"{item.Name} was added to the bag.";
            MessageIsError = false;
        }
    }
}
