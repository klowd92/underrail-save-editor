using System.Windows;
using UnderrailSaveEditor.App.ViewModels;

namespace UnderrailSaveEditor.App
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            var viewModel = new MainViewModel();
            DataContext = viewModel;
            Loaded += (s, e) =>
            {
                if (!viewModel.InitializeGame())
                    Close();
            };
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e) => Services.Dialogs.Show(About.Text, "About Underrail Save Editor");

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
