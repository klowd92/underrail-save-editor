using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;

namespace UnderrailSaveEditor.Core
{
    /// <summary>A decoded image: premultiplied BGRA pixels (WPF's Pbgra32 layout).</summary>
    public sealed class GameImage
    {
        internal GameImage(int width, int height, byte[] pixels)
        {
            Width = width;
            Height = height;
            Pixels = pixels;
        }

        public int Width { get; }
        public int Height { get; }
        public byte[] Pixels { get; }
    }

    /// <summary>
    /// The game's icons and portraits. Textures are XNA content files (.xnb, LZX-compressed), decoded without a
    /// graphics device (see <see cref="Xnb"/>). Icons are layered 33px cells (with a 1px gutter) of atlas textures, indexed by
    /// data\icons\icons.iconset (read with the game's serializer: name -> layers of atlas, grid cell, opacity).
    /// </summary>
    public sealed class GameImages
    {
        private const int CellSize = 33;

        private readonly GameRuntime _rt;
        private readonly string _dataDir;
        private readonly object _gate = new object();
        private readonly Dictionary<string, (int W, int H, Color[] Data)> _textures = new Dictionary<string, (int, int, Color[])>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, GameImage> _icons = new Dictionary<string, GameImage>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, GameImage> _portraits = new Dictionary<string, GameImage>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, List<IconCell>> _iconSet;

        private struct IconCell
        {
            public string Texture;
            public int X, Y, Width, Height;
            public float Alpha;
        }

        internal GameImages(GameRuntime rt)
        {
            _rt = rt;
            _dataDir = Path.Combine(rt.GameDirectory, "data");
        }

        /// <summary>The last decoding failure (for diagnostics); images that fail come back as null.</summary>
        public string LastError { get; private set; }

        /// <summary>An icon by the game's name ("Feat.as", "Psi.Pyrokinesis", "Components.123"...), or null.</summary>
        public GameImage Icon(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            lock (_gate)
            {
                if (_icons.TryGetValue(name, out var cached))
                    return cached;
                GameImage image = null;
                try
                {
                    if (IconSet.TryGetValue(name, out var layers))
                        image = Compose(layers);
                    else if (TryAtlasIndex(name, out var cell))
                        image = Compose(new List<IconCell> { cell });
                }
                catch (Exception ex) { LastError = $"{name}: {ex}"; }
                _icons[name] = image;
                return image;
            }
        }

        /// <summary>
        /// A character portrait by the name saved with the character ("biotech_bl"); size "s" (small), "l" (large) or "h" (huge).
        /// Portraits live in data\portraits\{male,female,genderless,...}.
        /// </summary>
        public GameImage Portrait(string portrait, string size = "l")
        {
            if (string.IsNullOrWhiteSpace(portrait))
                return null;
            lock (_gate)
            {
                var key = portrait + "_" + size;
                if (_portraits.TryGetValue(key, out var cached))
                    return cached;
                GameImage image = null;
                try
                {
                    var root = Path.Combine(_dataDir, "portraits");
                    var file = Directory.EnumerateFiles(root, key + ".xnb", SearchOption.AllDirectories).FirstOrDefault()
                        ?? Directory.EnumerateFiles(root, portrait + "_*.xnb", SearchOption.AllDirectories).FirstOrDefault();
                    if (file != null)
                    {
                        var texture = Texture(file.Substring(_dataDir.Length + 1, file.Length - _dataDir.Length - 1 - ".xnb".Length));
                        if (texture.Data != null)
                            image = Crop(texture, 0, 0, texture.W, texture.H);
                    }
                }
                catch (Exception ex) { LastError = $"{key}: {ex}"; }
                _portraits[key] = image;
                return image;
            }
        }

        /// <summary>A whole atlas texture from data\icons (for diagnostics).</summary>
        public GameImage Atlas(string name)
        {
            lock (_gate)
            {
                var texture = Texture("icons/" + name.ToLowerInvariant());
                return texture.Data == null ? null : Crop(texture, 0, 0, texture.W, texture.H);
            }
        }

        private HashSet<string> _portraitNames;

        /// <summary>Every portrait name the game offers (data\portraits\*\NAME_l.xnb), sorted.</summary>
        public IReadOnlyCollection<string> PortraitNames
        {
            get
            {
                lock (_gate)
                {
                    if (_portraitNames == null)
                    {
                        var root = Path.Combine(_dataDir, "portraits");
                        _portraitNames = new HashSet<string>(
                            Directory.Exists(root)
                                ? Directory.EnumerateFiles(root, "*_l.xnb", SearchOption.AllDirectories)
                                    .Select(Path.GetFileNameWithoutExtension).Select(n => n.Substring(0, n.Length - 2))
                                : Enumerable.Empty<string>(),
                            StringComparer.OrdinalIgnoreCase);
                    }
                    return _portraitNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
                }
            }
        }

        internal bool IsPortraitName(string name)
        {
            if (_portraitNames == null)
                _ = PortraitNames;
            return _portraitNames.Contains(name);
        }

        private Dictionary<string, List<IconCell>> IconSet
        {
            get
            {
                if (_iconSet != null)
                    return _iconSet;
                _iconSet = new Dictionary<string, List<IconCell>>(StringComparer.OrdinalIgnoreCase);
                var set = _rt.Deserialize(Path.Combine(_dataDir, "icons", "icons.iconset"), _rt.TypeForTag("ISD"), true);
                foreach (var icon in ((IEnumerable)Il.FieldForKey(set.GetType(), "ISD:I").GetValue(set)).Cast<object>().Concat(GeneratedIcons()))
                {
                    var t = icon.GetType();
                    var name = (string)Il.FieldForKey(t, "ID:N").GetValue(icon);
                    var size = (Vector2)Il.FieldForKey(t, "ID:S").GetValue(icon);
                    if (size.X < 1 || size.Y < 1)
                        size = new Vector2(CellSize, CellSize);
                    var layers = new List<IconCell>();
                    foreach (var part in (IEnumerable)Il.FieldForKey(t, "ID:P").GetValue(icon))
                    {
                        var pt = part.GetType();
                        var cell = (Point)Il.FieldForKey(pt, "IPD:P").GetValue(part);
                        layers.Add(new IconCell
                        {
                            Texture = (string)Il.FieldForKey(pt, "IPD:TN").GetValue(part),
                            X = cell.X * ((int)size.X + 1) + 1,
                            Y = cell.Y * ((int)size.Y + 1) + 1,
                            Width = (int)size.X,
                            Height = (int)size.Y,
                            Alpha = Convert.ToSingle(Il.FieldForKey(pt, "IPD:A").GetValue(part)),
                        });
                    }
                    if (name != null && layers.Count > 0)
                        _iconSet[name] = layers;
                }
                return _iconSet;
            }
        }

        /// <summary>
        /// Icons the game builds in code, like its icon index does: every class implementing the generator interface
        /// (one method returning a list of icon definitions "ID"). Crafting blueprints are generators: they define the
        /// layered icons of crafted items ("pistol.2.4.rs" = frame 2, barrel 4, rapid reloader + smart module).
        /// </summary>
        private IEnumerable<object> GeneratedIcons()
        {
            var iconType = _rt.TypeForTag("ID");
            var listType = typeof(List<>).MakeGenericType(iconType);
            var generator = _rt.Types.FirstOrDefault(t => t.IsInterface && t.GetMethods().Length == 1 && t.GetMethods()[0].ReturnType == listType
                && t.GetMethods()[0].GetParameters().Length == 0);
            if (generator == null)
                yield break;
            var method = generator.GetMethods()[0];
            foreach (var type in _rt.Types.Where(t => !t.IsAbstract && generator.IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null))
            {
                IEnumerable icons;
                try { icons = (IEnumerable)method.Invoke(Activator.CreateInstance(type), null); }
                catch (Exception) { continue; }
                foreach (var icon in icons ?? new object[0])
                    yield return icon;
            }
        }

        /// <summary>
        /// Item icons are named "Atlas.N" (definition key I:VM, e.g. "Components.123"): like the game's icon lookup,
        /// cell (N % 10, N / 10) of the atlas.
        /// </summary>
        private bool TryAtlasIndex(string name, out IconCell cell)
        {
            cell = default;
            int dot = name.LastIndexOf('.');
            if (dot <= 0 || !int.TryParse(name.Substring(dot + 1), out int index))
                return false;
            var atlas = name.Substring(0, dot);
            var texture = Texture("icons/" + atlas.ToLowerInvariant());
            int x = index % 10 * (CellSize + 1) + 1, y = index / 10 * (CellSize + 1) + 1;
            if (texture.Data == null || x + CellSize > texture.W || y + CellSize > texture.H)
                return false;
            cell = new IconCell { Texture = atlas, X = x, Y = y, Width = CellSize, Height = CellSize, Alpha = 1 };
            return true;
        }

        /// <summary>Draws the icon's layers in order, each with its own opacity, like the game's icon renderer.</summary>
        private GameImage Compose(List<IconCell> layers)
        {
            int w = layers[0].Width, h = layers[0].Height;
            var pixels = new byte[w * h * 4];
            foreach (var layer in layers)
            {
                var texture = Texture("icons/" + layer.Texture.ToLowerInvariant());
                if (texture.Data == null)
                    continue;
                for (int row = 0; row < h && layer.Y + row < texture.H; row++)
                {
                    for (int col = 0; col < w && layer.X + col < texture.W; col++)
                    {
                        var c = texture.Data[(layer.Y + row) * texture.W + layer.X + col];
                        int i = (row * w + col) * 4;
                        // Premultiplied "source over": dst = src * a + dst * (1 - srcAlpha * a).
                        float a = layer.Alpha, inv = 1 - c.A / 255f * a;
                        pixels[i] = (byte)Math.Min(255, c.B * a + pixels[i] * inv);
                        pixels[i + 1] = (byte)Math.Min(255, c.G * a + pixels[i + 1] * inv);
                        pixels[i + 2] = (byte)Math.Min(255, c.R * a + pixels[i + 2] * inv);
                        pixels[i + 3] = (byte)Math.Min(255, c.A * a + pixels[i + 3] * inv);
                    }
                }
            }
            return new GameImage(w, h, pixels);
        }

        private (int W, int H, Color[] Data) Texture(string asset)
        {
            if (_textures.TryGetValue(asset, out var cached))
                return cached;
            (int, int, Color[]) result = (0, 0, null);
            try { result = Xnb.ReadTexture(Path.Combine(_dataDir, asset + ".xnb")); }
            catch (Exception ex) { LastError = $"{asset}: {ex}"; }
            _textures[asset] = result;
            return result;
        }

        private static GameImage Crop((int W, int H, Color[] Data) texture, int x, int y, int width, int height)
        {
            width = Math.Min(width, texture.W - x);
            height = Math.Min(height, texture.H - y);
            if (width <= 0 || height <= 0)
                return null;
            var pixels = new byte[width * height * 4];
            for (int row = 0; row < height; row++)
            {
                for (int col = 0; col < width; col++)
                {
                    var c = texture.Data[(y + row) * texture.W + x + col];
                    int i = (row * width + col) * 4;
                    pixels[i] = c.B;
                    pixels[i + 1] = c.G;
                    pixels[i + 2] = c.R;
                    pixels[i + 3] = c.A;
                }
            }
            return new GameImage(width, height, pixels);
        }
    }

    /// <summary>
    /// Reads XNA 4.0 .xnb texture files without a graphics device: the LZX decompression is XNA's own
    /// (its internal DecompressStream), the Texture2D payload is parsed and decoded here.
    /// </summary>
    internal static class Xnb
    {
        public static (int W, int H, Color[] Data) ReadTexture(string path)
        {
            var file = File.ReadAllBytes(path);
            if (file.Length < 10 || file[0] != 'X' || file[1] != 'N' || file[2] != 'B')
                throw new InvalidDataException("Not an XNB file.");
            bool compressed = (file[5] & 0x80) != 0;
            int fileSize = BitConverter.ToInt32(file, 6);
            Stream payload;
            if (compressed)
            {
                int decompressedSize = BitConverter.ToInt32(file, 10);
                var input = new MemoryStream(file, 14, file.Length - 14);
                var type = typeof(Vector2).Assembly.GetType("Microsoft.Xna.Framework.Content.DecompressStream", true);
                var ctor = type.GetConstructor(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                    null, new[] { typeof(Stream), typeof(int), typeof(int) }, null);
                var decompress = (Stream)ctor.Invoke(new object[] { input, fileSize - 14, decompressedSize });
                var buffer = new byte[decompressedSize];
                int read = 0;
                while (read < decompressedSize)
                {
                    int n = decompress.Read(buffer, read, decompressedSize - read);
                    if (n <= 0)
                        break;
                    read += n;
                }
                payload = new MemoryStream(buffer, 0, read);
            }
            else
            {
                payload = new MemoryStream(file, 10, file.Length - 10);
            }

            using (var r = new BinaryReader(payload))
            {
                int readers = Read7BitInt(r);
                for (int i = 0; i < readers; i++)
                {
                    r.ReadString();   // type reader name
                    r.ReadInt32();    // version
                }
                Read7BitInt(r);       // shared resources
                if (Read7BitInt(r) == 0)
                    throw new InvalidDataException("Empty XNB.");
                int format = r.ReadInt32();
                int width = r.ReadInt32();
                int height = r.ReadInt32();
                r.ReadInt32();        // mip count
                int size = r.ReadInt32();
                var data = r.ReadBytes(size);
                return (width, height, Decode(format, width, height, data));
            }
        }

        private static int Read7BitInt(BinaryReader r)
        {
            int result = 0, shift = 0;
            byte b;
            do
            {
                b = r.ReadByte();
                result |= (b & 0x7F) << shift;
                shift += 7;
            } while ((b & 0x80) != 0);
            return result;
        }

        /// <summary>SurfaceFormat: 0 = Color (RGBA), 4 = Dxt1, 5 = Dxt3, 6 = Dxt5.</summary>
        private static Color[] Decode(int format, int width, int height, byte[] data)
        {
            var pixels = new Color[width * height];
            switch (format)
            {
                case 0:
                    for (int i = 0; i < pixels.Length; i++)
                        pixels[i] = new Color(data[i * 4], data[i * 4 + 1], data[i * 4 + 2], data[i * 4 + 3]);
                    return pixels;
                case 4:
                case 5:
                case 6:
                    int blockSize = format == 4 ? 8 : 16, offset = 0;
                    for (int by = 0; by < (height + 3) / 4; by++)
                    {
                        for (int bx = 0; bx < (width + 3) / 4; bx++, offset += blockSize)
                            DecodeBlock(format, data, offset, pixels, bx * 4, by * 4, width, height);
                    }
                    return pixels;
                default:
                    throw new NotSupportedException($"Texture format {format} is not supported.");
            }
        }

        private static void DecodeBlock(int format, byte[] d, int o, Color[] px, int x0, int y0, int w, int h)
        {
            int colorOffset = format == 4 ? o : o + 8;
            ushort c0 = BitConverter.ToUInt16(d, colorOffset), c1 = BitConverter.ToUInt16(d, colorOffset + 2);
            uint indices = BitConverter.ToUInt32(d, colorOffset + 4);
            var palette = new int[4][];
            palette[0] = Rgb565(c0);
            palette[1] = Rgb565(c1);
            bool fourColors = format != 4 || c0 > c1;
            if (fourColors)
            {
                palette[2] = Mix(palette[0], palette[1], 2, 1, 3);
                palette[3] = Mix(palette[0], palette[1], 1, 2, 3);
            }
            else
            {
                palette[2] = Mix(palette[0], palette[1], 1, 1, 2);
                palette[3] = new[] { 0, 0, 0 };
            }

            var alpha = new byte[16];
            if (format == 5)
            {
                for (int i = 0; i < 16; i++)
                    alpha[i] = (byte)(((d[o + i / 2] >> ((i % 2) * 4)) & 0xF) * 17);
            }
            else if (format == 6)
            {
                byte a0 = d[o], a1 = d[o + 1];
                ulong bits = 0;
                for (int i = 0; i < 6; i++)
                    bits |= (ulong)d[o + 2 + i] << (8 * i);
                for (int i = 0; i < 16; i++)
                {
                    int code = (int)((bits >> (3 * i)) & 7);
                    alpha[i] = code == 0 ? a0 : code == 1 ? a1
                        : a0 > a1 ? (byte)(((8 - code) * a0 + (code - 1) * a1) / 7)
                        : code == 6 ? (byte)0 : code == 7 ? (byte)255 : (byte)(((6 - code) * a0 + (code - 1) * a1) / 5);
                }
            }

            for (int i = 0; i < 16; i++)
            {
                int x = x0 + i % 4, y = y0 + i / 4;
                if (x >= w || y >= h)
                    continue;
                int code = (int)((indices >> (2 * i)) & 3);
                var c = palette[code];
                byte a = format == 4 ? (byte)(!fourColors && code == 3 ? 0 : 255) : alpha[i];
                // XNA content is premultiplied; DXT stores the premultiplied color, so it is kept as is.
                px[y * w + x] = new Color((byte)c[0], (byte)c[1], (byte)c[2], a);
            }
        }

        private static int[] Rgb565(ushort v) => new[] { (v >> 11 & 31) * 255 / 31, (v >> 5 & 63) * 255 / 63, (v & 31) * 255 / 31 };

        private static int[] Mix(int[] a, int[] b, int wa, int wb, int div) =>
            new[] { (a[0] * wa + b[0] * wb) / div, (a[1] * wa + b[1] * wb) / div, (a[2] * wa + b[2] * wb) / div };
    }

    public sealed partial class GameRuntime
    {
        private GameImages _images;

        /// <summary>The game's icons and portraits.</summary>
        public GameImages Images => _images ?? (_images = new GameImages(this));
    }
}
