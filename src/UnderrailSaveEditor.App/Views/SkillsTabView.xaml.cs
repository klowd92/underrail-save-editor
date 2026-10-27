using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UnderrailSaveEditor.App.ViewModels;

namespace UnderrailSaveEditor.App.Views
{
    public partial class SkillsTabView : UserControl
    {
        public SkillsTabView()
        {
            InitializeComponent();
        }

        /// <summary>Clicking anywhere on a skill row (including its value box) shows that skill's description.</summary>
        private void OnSkillRowClicked(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is SkillsTabViewModel vm && ((FrameworkElement)sender).DataContext is StatDisplayModel skill)
                vm.SelectedSkill = skill;
        }
    }
}
