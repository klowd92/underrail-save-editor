using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using UnderrailSaveEditor.Core;

namespace UnderrailSaveEditor.App.Views
{
    /// <summary>Shows the game's rich text (item tooltips) with the game's own colors.</summary>
    public class RichTextView : TextBlock
    {
        public static readonly DependencyProperty RichTextProperty = DependencyProperty.Register(
            nameof(RichText), typeof(RichText), typeof(RichTextView),
            new PropertyMetadata(null, (d, e) => ((RichTextView)d).Render((RichText)e.NewValue)));

        public RichTextView()
        {
            TextWrapping = TextWrapping.Wrap;
            FontFamily = new FontFamily("Consolas");
            FontSize = 13;
            LineHeight = 18;
        }

        public RichText RichText
        {
            get => (RichText)GetValue(RichTextProperty);
            set => SetValue(RichTextProperty, value);
        }

        private void Render(RichText text)
        {
            Inlines.Clear();
            if (text == null)
                return;
            bool first = true;
            foreach (var line in text.Lines)
            {
                if (!first)
                    Inlines.Add(new LineBreak());
                first = false;
                foreach (var span in line)
                    Inlines.Add(new Run(span.Text) { Foreground = new SolidColorBrush(Color.FromRgb(span.R, span.G, span.B)) });
            }
            // The first line is the item name: make it stand out like the game's header font.
            if (Inlines.FirstInline is Run title)
            {
                title.FontSize = 16;
                title.FontWeight = FontWeights.Bold;
            }
        }
    }
}
