using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace UnderrailSaveEditor.App.Services
{
    /// <summary>
    /// Message boxes and prompts in the editor's style: borderless window, orange frame, the editor's own title bar.
    /// Used instead of MessageBox, whose white Windows title bar doesn't fit the theme.
    /// </summary>
    public static class Dialogs
    {
        public static MessageBoxResult Show(string message, string title, MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None)
        {
            string[] labels;
            MessageBoxResult[] results;
            switch (buttons)
            {
                case MessageBoxButton.YesNo:
                    labels = new[] { "Yes", "No" };
                    results = new[] { MessageBoxResult.Yes, MessageBoxResult.No };
                    break;
                case MessageBoxButton.OKCancel:
                    labels = new[] { "OK", "Cancel" };
                    results = new[] { MessageBoxResult.OK, MessageBoxResult.Cancel };
                    break;
                case MessageBoxButton.YesNoCancel:
                    labels = new[] { "Yes", "No", "Cancel" };
                    results = new[] { MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel };
                    break;
                default:
                    labels = new[] { "OK" };
                    results = new[] { MessageBoxResult.OK };
                    break;
            }
            int choice = Choose(title, message, image, labels);
            return choice < 0 ? (buttons == MessageBoxButton.OK ? MessageBoxResult.OK : results.Last()) : results[choice];
        }

        /// <summary>Shows a message with custom buttons; returns the index of the button pressed, or -1 if closed.</summary>
        public static int Choose(string title, string message, MessageBoxImage image, params string[] buttons)
        {
            int result = -1;
            var window = CreateWindow(title);
            var body = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
            body.Children.Add(new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                Foreground = Brush(image == MessageBoxImage.Error || image == MessageBoxImage.Warning ? "TextWhite" : "TextPrimary"),
                Margin = new Thickness(0, 0, 0, 18),
            });
            body.Children.Add(ButtonRow(buttons, i => { result = i; window.Close(); }));
            SetBody(window, body);
            window.ShowDialog();
            return result;
        }

        /// <summary>A one-line text prompt; returns null if cancelled.</summary>
        public static string Prompt(string title, string prompt, string initialValue)
        {
            string result = null;
            var window = CreateWindow(title);
            var box = new TextBox { Text = initialValue, Height = 30, FontSize = 14, Margin = new Thickness(0, 8, 0, 18) };
            var body = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
            body.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap, FontSize = 14, Foreground = Brush("TextPrimary") });
            body.Children.Add(box);
            body.Children.Add(ButtonRow(new[] { "OK", "Cancel" }, i =>
            {
                if (i == 0)
                    result = box.Text.Trim();
                window.Close();
            }));
            box.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { result = box.Text.Trim(); window.Close(); }
                if (e.Key == Key.Escape) window.Close();
            };
            SetBody(window, body);
            window.Loaded += (s, e) => { box.Focus(); box.SelectAll(); };
            window.ShowDialog();
            return result;
        }

        private static Window CreateWindow(string title)
        {
            var window = new Window
            {
                Title = title,
                Width = 480,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None,
                WindowStartupLocation = Application.Current?.MainWindow?.IsLoaded == true ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                ShowInTaskbar = false,
                Background = Brush("BackgroundDark"),
                BorderBrush = Brush("AccentOrange"),
                BorderThickness = new Thickness(2),
            };
            if (Application.Current?.MainWindow?.IsLoaded == true)
                window.Owner = Application.Current.MainWindow;
            window.KeyDown += (s, e) => { if (e.Key == Key.Escape) window.Close(); };
            return window;
        }

        private static void SetBody(Window window, UIElement body)
        {
            var close = new Button { Content = "✕", Width = 40, Height = 30, Style = (Style)Application.Current.FindResource("WindowCloseButton") };
            close.Click += (s, e) => window.Close();
            var titleText = new TextBlock
            {
                Text = window.Title,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("AccentOrange"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
            };
            var bar = new DockPanel { Height = 32, Background = Brush("BackgroundMedium") };
            DockPanel.SetDock(close, Dock.Right);
            bar.Children.Add(close);
            bar.Children.Add(titleText);
            bar.MouseLeftButtonDown += (s, e) => window.DragMove();

            var titleBorder = new Border { BorderBrush = Brush("AccentOrange"), BorderThickness = new Thickness(0, 0, 0, 2), Child = bar };
            var root = new DockPanel();
            DockPanel.SetDock(titleBorder, Dock.Top);
            root.Children.Add(titleBorder);
            root.Children.Add(body);
            window.Content = root;
        }

        private static UIElement ButtonRow(string[] labels, System.Action<int> onClick)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                var button = new Button
                {
                    Content = labels[i],
                    MinWidth = 100,
                    Margin = new Thickness(10, 0, 0, 0),
                    IsDefault = i == 0,
                    Style = (Style)Application.Current.FindResource(i == 0 ? "GreenButton" : "OrangeButton"),
                };
                button.Click += (s, e) => onClick(index);
                row.Children.Add(button);
            }
            return row;
        }

        private static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
    }
}
