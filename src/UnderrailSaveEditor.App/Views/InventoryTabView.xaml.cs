using System.Windows.Controls;

namespace UnderrailSaveEditor.App.Views
{
    public partial class InventoryTabView : UserControl
    {
        public InventoryTabView()
        {
            InitializeComponent();
        }

        /// <summary>The list is rebuilt after every edit; keep the selected item in view.</summary>
        private void OnItemSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var list = (ListBox)sender;
            if (list.SelectedItem != null)
                list.ScrollIntoView(list.SelectedItem);
        }
    }
}
