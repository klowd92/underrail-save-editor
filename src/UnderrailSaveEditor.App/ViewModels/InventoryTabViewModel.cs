using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using UnderrailSaveEditor.Core;

using UnderrailSaveEditor.App.Services;

namespace UnderrailSaveEditor.App.ViewModels
{
    public class InventoryItemModel
    {
        public InventoryItemModel(Item item)
        {
            Item = item;
            Name = item.Name;
            Location = item.IsEquipped ? Core.Text.Humanize(item.Location) : "Bag";
            Details = string.Join("  ", new[]
            {
                item.Stacks > 1 ? "×" + item.Stacks : null,
                item.HasQuality ? "Q" + item.Quality : null,
            }.Where(s => s != null));
        }

        public Item Item { get; }
        public System.Windows.Media.ImageSource Icon => GameIcons.Icon(Item.IconName);
        public string Name { get; }
        public string Location { get; }
        public string Details { get; }
        public bool IsEquipped => Item.IsEquipped;
    }

    /// <summary>Character inspector: equipped items and bag, the game's tooltips, item edits and adding library items.</summary>
    public class InventoryTabViewModel : TabViewModelBase
    {
        private string _searchText;
        private InventoryItemModel _selectedItem;
        private RichText _tooltip;
        private int _editStacks;
        private int _editQuality;
        private double _editDurability;
        private string _librarySearch;
        private LibraryItem _selectedLibraryItem;
        private int _addQuantity = 1;
        private int _addQuality = 100;

        public InventoryTabViewModel()
        {
            Items = new ObservableCollection<InventoryItemModel>();
            FilteredItems = new ObservableCollection<InventoryItemModel>();
            LibraryResults = new ObservableCollection<LibraryItem>();

            ApplyCommand = new RelayCommand(ExecuteApply, () => SelectedItem != null);
            RemoveCommand = new RelayCommand(ExecuteRemove, () => SelectedItem != null && !SelectedItem.IsEquipped);
            RepairCommand = new RelayCommand(() => EditDurability = SelectedItem.Item.MaxDurability, () => SelectedItem?.Item.HasDurability == true);
            AddCommand = new RelayCommand(ExecuteAdd, () => SelectedLibraryItem != null && Save != null);
        }

        public ObservableCollection<InventoryItemModel> Items { get; }
        public ObservableCollection<InventoryItemModel> FilteredItems { get; }
        public ObservableCollection<LibraryItem> LibraryResults { get; }

        public ICommand ApplyCommand { get; }
        public ICommand RemoveCommand { get; }
        public ICommand RepairCommand { get; }
        public ICommand AddCommand { get; }

        public string SearchText
        {
            get => _searchText;
            set { if (SetProperty(ref _searchText, value)) FilterItems(); }
        }

        public string Summary => Save == null ? "" :
            $"{Items.Count(i => !i.IsEquipped)} in bag, {Items.Count(i => i.IsEquipped)} equipped · " +
            $"weight {Items.Sum(i => i.Item.Weight):0.#} · value {Items.Sum(i => i.Item.Value):N0}";

        public InventoryItemModel SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (!SetProperty(ref _selectedItem, value))
                    return;
                var item = value?.Item;
                EditStacks = item?.Stacks ?? 0;
                EditQuality = item?.Quality ?? 0;
                EditDurability = item != null && item.HasDurability ? Math.Round(item.Durability) : 0;
                OnPropertyChanged(nameof(CanEditStacks));
                OnPropertyChanged(nameof(CanEditQuality));
                OnPropertyChanged(nameof(CanEditDurability));
                if (item != null || TooltipCaption == "Selected item")
                    Show(item == null ? null : SafeTooltip(item), "Selected item", item?.IconName);
            }
        }

        /// <summary>Tooltip of the item picked last, from the character's items or from the library.</summary>
        public RichText Tooltip
        {
            get => _tooltip;
            private set => SetProperty(ref _tooltip, value);
        }

        private string _tooltipCaption = "Selected item";
        public string TooltipCaption { get => _tooltipCaption; private set => SetProperty(ref _tooltipCaption, value); }

        private System.Windows.Media.ImageSource _tooltipIcon;
        public System.Windows.Media.ImageSource TooltipIcon { get => _tooltipIcon; private set => SetProperty(ref _tooltipIcon, value); }

        private void Show(RichText tooltip, string caption, string iconName)
        {
            Tooltip = tooltip;
            TooltipCaption = caption;
            TooltipIcon = GameIcons.Icon(iconName);
        }

        public bool CanEditStacks => SelectedItem != null && SelectedItem.Item.MaxStack > 1;
        public bool CanEditQuality => SelectedItem?.Item.HasQuality == true;
        public bool CanEditDurability => SelectedItem?.Item.HasDurability == true;

        public int EditStacks { get => _editStacks; set => SetProperty(ref _editStacks, value); }
        public int EditQuality { get => _editQuality; set => SetProperty(ref _editQuality, value); }
        public double EditDurability { get => _editDurability; set => SetProperty(ref _editDurability, value); }

        // ---- add from library -------------------------------------------------------------------

        public string LibrarySearch
        {
            get => _librarySearch;
            set { if (SetProperty(ref _librarySearch, value)) FilterLibrary(); }
        }

        public LibraryItem SelectedLibraryItem
        {
            get => _selectedLibraryItem;
            set
            {
                if (!SetProperty(ref _selectedLibraryItem, value))
                    return;
                var sample = value == null ? null : Save.Runtime.SampleItem(value);
                AddHasQuality = sample?.HasQuality == true;
                if (sample != null)
                    Show(SafeTooltip(sample), "Library item (preview)", sample.IconName);
            }
        }

        private bool _addHasQuality;
        public bool AddHasQuality { get => _addHasQuality; private set => SetProperty(ref _addHasQuality, value); }

        public int AddQuantity { get => _addQuantity; set => SetProperty(ref _addQuantity, value); }
        public int AddQuality { get => _addQuality; set => SetProperty(ref _addQuality, value); }

        protected override void OnLoad()
        {
            SelectedItem = null;
            FilterLibrary();
            OnRefresh();
        }

        protected override void OnRefresh()
        {
            var selected = SelectedItem?.Item.GameObject;
            Items.Clear();
            foreach (var item in Save.Player.Equipment.Concat(Save.Player.Inventory))
                Items.Add(new InventoryItemModel(item));
            FilterItems();
            OnPropertyChanged(nameof(Summary));
            // Keep the selection on the same game object; the tooltip may have changed (e.g. skills affect weapon stats).
            _selectedItem = null;
            SelectedItem = FilteredItems.FirstOrDefault(i => i.Item.GameObject == selected);
        }

        private void FilterItems()
        {
            FilteredItems.Clear();
            foreach (var item in Items)
            {
                if (string.IsNullOrWhiteSpace(SearchText)
                    || item.Name.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0
                    || item.Location.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0)
                    FilteredItems.Add(item);
            }
        }

        private void FilterLibrary()
        {
            LibraryResults.Clear();
            if (Save == null)
                return;
            IEnumerable<LibraryItem> items = Save.Runtime.LibraryItems;
            if (!string.IsNullOrWhiteSpace(LibrarySearch))
                items = items.Where(i => i.Name.IndexOf(LibrarySearch, StringComparison.OrdinalIgnoreCase) >= 0
                    || i.Path.IndexOf(LibrarySearch, StringComparison.OrdinalIgnoreCase) >= 0);
            foreach (var item in items.Take(300))
                LibraryResults.Add(item);
        }

        private void ExecuteApply()
        {
            var item = SelectedItem.Item;
            int stacks = Math.Max(1, Math.Min(EditStacks, item.MaxStack));
            if (stacks != EditStacks)
                Dialogs.Show($"{item.Name} stacks up to {item.MaxStack}; using {stacks}.", "Stacks", MessageBoxButton.OK, MessageBoxImage.Information);
            Edit(() =>
            {
                if (CanEditStacks)
                    item.Stacks = stacks;
                if (CanEditQuality)
                    item.Quality = Math.Max(0, EditQuality);
                if (CanEditDurability)
                    item.Durability = Math.Max(0, Math.Min(EditDurability, item.MaxDurability));
            });
        }

        private void ExecuteRemove()
        {
            var item = SelectedItem.Item;
            if (Dialogs.Show($"Remove {item} from the bag?", "Remove item", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            SelectedItem = null;
            Edit(() => Save.Player.RemoveItem(item));
        }

        private void ExecuteAdd()
        {
            var library = SelectedLibraryItem;
            int quantity = Math.Max(1, AddQuantity);
            Edit(() =>
            {
                // Unstackable items (weapons, components with quality) are added one instance each.
                var first = Save.Runtime.CreateItem(library.Path, 1, AddHasQuality ? AddQuality : (int?)null);
                int perItem = Math.Min(quantity, first.MaxStack);
                first.Stacks = perItem;
                Save.Player.AddItem(first);
                for (int left = quantity - perItem; left > 0; left -= perItem)
                {
                    perItem = Math.Min(left, first.MaxStack);
                    Save.Player.AddItem(Save.Runtime.CreateItem(library.Path, perItem, AddHasQuality ? AddQuality : (int?)null));
                }
            });
        }

        private RichText SafeTooltip(Item item)
        {
            try { return item.Tooltip(Save?.Player); }
            catch (Exception ex) { return new Core.RichTextBuilder().Line(item.Name, 255, 255, 255).Line("(tooltip unavailable: " + ex.Message + ")", 200, 80, 80).Build(); }
        }
    }
}
