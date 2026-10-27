using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UnderrailSaveEditor.Core;

namespace UnderrailSaveEditor.App.Services
{
    /// <summary>The game's icons and portraits as frozen WPF images (cached; safe to create on any thread).</summary>
    public static class GameIcons
    {
        private static readonly Dictionary<string, ImageSource> Cache = new Dictionary<string, ImageSource>();

        /// <summary>Set once the game is loaded.</summary>
        public static GameRuntime Runtime { get; set; }

        public static ImageSource Icon(string name) => Get("icon:" + name, () => Runtime?.Images.Icon(name));

        public static ImageSource Portrait(string name, string size = "l") => Get($"portrait:{name}_{size}", () => Runtime?.Images.Portrait(name, size));

        private static ImageSource Get(string key, System.Func<GameImage> load)
        {
            lock (Cache)
            {
                if (Cache.TryGetValue(key, out var cached))
                    return cached;
            }
            ImageSource image = null;
            var raw = string.IsNullOrEmpty(key) ? null : load();
            if (raw != null)
            {
                var bitmap = BitmapSource.Create(raw.Width, raw.Height, 96, 96, PixelFormats.Pbgra32, null, raw.Pixels, raw.Width * 4);
                bitmap.Freeze();
                image = bitmap;
            }
            lock (Cache)
                Cache[key] = image;
            return image;
        }
    }
}
