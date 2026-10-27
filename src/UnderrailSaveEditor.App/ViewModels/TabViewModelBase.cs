using System;
using UnderrailSaveEditor.Core;

namespace UnderrailSaveEditor.App.ViewModels
{
    /// <summary>
    /// A tab that edits the loaded save. Edits are written straight to the game objects, which recalculate
    /// derived values; the main view model then calls <see cref="Refresh"/> on every tab so all of them show
    /// the game's current numbers.
    /// </summary>
    public abstract class TabViewModelBase : ViewModelBase
    {
        private int _refreshDepth;

        protected SaveGame Save { get; private set; }

        /// <summary>True while values are being copied from the model into the view model.</summary>
        protected bool IsRefreshing => _refreshDepth > 0;

        /// <summary>Raised after this tab changed the model.</summary>
        public event Action ModelChanged;

        public void Load(SaveGame save)
        {
            Save = save;
            _refreshDepth++;
            try { OnLoad(); }
            finally { _refreshDepth--; }
        }

        public void Refresh()
        {
            if (Save == null)
                return;
            _refreshDepth++;
            try { OnRefresh(); }
            finally { _refreshDepth--; }
        }

        /// <summary>Builds the tab's items for a newly loaded save (then refreshes them).</summary>
        protected virtual void OnLoad() => OnRefresh();

        /// <summary>Copies current model values into the existing items.</summary>
        protected abstract void OnRefresh();

        /// <summary>
        /// Applies a change to the model unless the change came from a refresh. If the game rejects the change,
        /// the error is shown and the refresh that follows puts every tab back in line with the model.
        /// </summary>
        protected void Edit(Action change)
        {
            if (IsRefreshing || Save == null)
                return;
            try
            {
                change();
            }
            catch (Exception ex)
            {
                Services.Dialogs.Show($"The game rejected this change:\n\n{ex.Message}", "Edit failed",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
            finally
            {
                ModelChanged?.Invoke();
            }
        }
    }
}
