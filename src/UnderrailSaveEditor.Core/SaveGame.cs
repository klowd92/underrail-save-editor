using System;
using System.IO;

namespace UnderrailSaveEditor.Core
{
    /// <summary>
    /// One save folder. global.dat (game state, gzip) and info.dat (summary) are read and written by the game's own
    /// serializer; every other file in the folder (bulk.map, chunkN.*, screenshot.png) is left untouched.
    /// </summary>
    public sealed class SaveGame
    {
        public const string GlobalFile = "global.dat";
        public const string InfoFile = "info.dat";

        private readonly GameRuntime _rt;
        private readonly object _global;
        private readonly object _info;

        private SaveGame(GameRuntime rt, string folder, object global, object info, long fileVersion)
        {
            _rt = rt;
            FolderPath = folder;
            _global = global;
            _info = info;
            FileDataModelVersion = fileVersion;
            Player = new Character(rt, Il.FieldForKey(rt.TypeForTag("TG"), "TG:PC").GetValue(global));
            Variables = new GameVariables(Il.FieldForKey(rt.TypeForTag("TG"), "TG:GP").GetValue(global));
        }

        public static SaveGame Load(GameRuntime rt, string folder)
        {
            folder = Path.GetFullPath(folder);
            var globalPath = Path.Combine(folder, GlobalFile);
            var global = rt.Deserialize(globalPath, rt.TypeForTag("TG"), true);
            var info = rt.Deserialize(Path.Combine(folder, InfoFile), rt.TypeForTag("SGI"), false);
            return new SaveGame(rt, folder, global, info, SaveSummary.ReadDataModelVersion(globalPath));
        }

        public string FolderPath { get; }
        public GameRuntime Runtime => _rt;
        public Character Player { get; }
        public GameVariables Variables { get; }

        /// <summary>The data model version the save was written with. Saving writes the game's current version.</summary>
        public long FileDataModelVersion { get; }
        public bool WillUpgradeFormat => FileDataModelVersion != _rt.DataModelVersion;

        public SaveSummary Summary => SaveSummary.FromInfo(FolderPath, _info, FileDataModelVersion);

        /// <summary>
        /// Writes the save into <paramref name="targetFolder"/> (default: where it was loaded from).
        /// Each file is written to a temporary name, read back with the game's deserializer, and only then moved
        /// into place. Files being replaced are first copied to <paramref name="backupRoot"/> when one is given.
        /// Saving to a new folder copies the untouched files (map chunks, screenshot) from the original save.
        /// Returns the backup folder, or null if nothing was backed up.
        /// </summary>
        public string Save(string targetFolder = null, string backupRoot = null)
        {
            targetFolder = Path.GetFullPath(targetFolder ?? FolderPath);
            bool sameFolder = string.Equals(targetFolder.TrimEnd('\\', '/'), FolderPath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

            // info.dat carries the name and level shown in the game's load menu; the game refreshes them on every save too.
            var infoType = _info.GetType();
            infoType.GetProperty("CharacterName").SetValue(_info, Player.Name);
            infoType.GetProperty("CharacterLevel").SetValue(_info, Player.Level);

            Directory.CreateDirectory(targetFolder);
            if (!sameFolder)
            {
                foreach (var file in Directory.GetFiles(FolderPath))
                {
                    var name = Path.GetFileName(file);
                    if (!name.Equals(GlobalFile, StringComparison.OrdinalIgnoreCase) && !name.Equals(InfoFile, StringComparison.OrdinalIgnoreCase))
                        File.Copy(file, Path.Combine(targetFolder, name), true);
                }
            }

            string backup = null;
            if (backupRoot != null && File.Exists(Path.Combine(targetFolder, GlobalFile)))
            {
                backup = Path.Combine(backupRoot, Path.GetFileName(targetFolder) + "_" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(backup);
                foreach (var name in new[] { GlobalFile, InfoFile })
                {
                    var src = Path.Combine(targetFolder, name);
                    if (File.Exists(src))
                        File.Copy(src, Path.Combine(backup, name), true);
                }
            }

            var globalTmp = WriteVerified(targetFolder, GlobalFile, _rt.TypeForTag("TG"), _global, true);
            var infoTmp = WriteVerified(targetFolder, InfoFile, _rt.TypeForTag("SGI"), _info, false);
            Commit(globalTmp, Path.Combine(targetFolder, GlobalFile));
            Commit(infoTmp, Path.Combine(targetFolder, InfoFile));
            return backup;
        }

        private string WriteVerified(string folder, string name, Type type, object graph, bool compressed)
        {
            var tmp = Path.Combine(folder, name + ".tmp");
            try
            {
                _rt.Serialize(tmp, type, graph, compressed);
                var check = _rt.Deserialize(tmp, type, compressed);
                if (check == null || check.GetType() != type)
                    throw new InvalidDataException($"{name} did not read back as {type.Name}.");
                return tmp;
            }
            catch
            {
                File.Delete(tmp);
                throw;
            }
        }

        private static void Commit(string tmp, string final)
        {
            if (File.Exists(final))
                File.Replace(tmp, final, null);
            else
                File.Move(tmp, final);
        }
    }

    /// <summary>The info.dat summary (what the game's load menu shows) plus the global.dat format version.</summary>
    public sealed class SaveSummary
    {
        private static readonly Guid FileGuid = new Guid("{838B53F9-361F-4332-BAAE-0D17865D0854}");

        public string FolderPath { get; private set; }
        public string FolderName => Path.GetFileName(FolderPath);
        public string CharacterName { get; private set; }
        public int CharacterLevel { get; private set; }
        public string Location { get; private set; }
        public DateTime SaveTime { get; private set; }
        public Version GameVersion { get; private set; }
        public TimeSpan? PlayTime { get; private set; }
        public string Difficulty { get; private set; }
        public long DataModelVersion { get; private set; }

        public static SaveSummary Read(GameRuntime rt, string folder)
        {
            var info = rt.Deserialize(Path.Combine(folder, SaveGame.InfoFile), rt.TypeForTag("SGI"), false);
            return FromInfo(folder, info, ReadDataModelVersion(Path.Combine(folder, SaveGame.GlobalFile)));
        }

        internal static SaveSummary FromInfo(string folder, object info, long version)
        {
            object P(string name) => info.GetType().GetProperty(name).GetValue(info);
            return new SaveSummary
            {
                FolderPath = folder,
                CharacterName = (string)P("CharacterName"),
                CharacterLevel = (int)P("CharacterLevel"),
                Location = (string)P("Location"),
                SaveTime = (DateTime)P("SaveTime"),
                GameVersion = (Version)P("GameVersion"),
                PlayTime = (TimeSpan?)P("PlayTime"),
                Difficulty = P("Difficulty")?.ToString(),
                DataModelVersion = version,
            };
        }

        /// <summary>
        /// The player's portrait name without loading the save: the first string record in global.dat that names
        /// a portrait the game has. The player character is the first object the game serializes, so its portrait
        /// comes before any other character's. Returns null if none is found.
        /// </summary>
        public static string PeekPortrait(GameRuntime rt, string folder)
        {
            var path = Path.Combine(folder, SaveGame.GlobalFile);
            if (!File.Exists(path))
                return null;
            byte[] data;
            using (var file = File.OpenRead(path))
            {
                file.Position = 24;
                using (var gz = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionMode.Decompress))
                {
                    data = new byte[512 * 1024];
                    int read = 0, n;
                    while (read < data.Length && (n = gz.Read(data, read, data.Length - read)) > 0)
                        read += n;
                    Array.Resize(ref data, read);
                }
            }
            // MS-NRBF BinaryObjectString: 0x06, int32 object id, 7-bit length, UTF-8 text.
            for (int i = 0; i + 6 < data.Length; i++)
            {
                if (data[i] != 0x06)
                    continue;
                int len = data[i + 5];
                if (len < 2 || len > 60 || i + 6 + len > data.Length)
                    continue;
                bool plain = true;
                for (int k = 0; k < len && plain; k++)
                {
                    byte b = data[i + 6 + k];
                    plain = b == '_' || b == '-' || (b >= '0' && b <= '9') || (b >= 'a' && b <= 'z') || (b >= 'A' && b <= 'Z');
                }
                if (!plain)
                    continue;
                var text = System.Text.Encoding.ASCII.GetString(data, i + 6, len);
                if (rt.Images.IsPortraitName(text))
                    return text;
            }
            return null;
        }

        /// <summary>Reads the 24-byte header: file GUID + data model version (0 if absent).</summary>
        public static long ReadDataModelVersion(string path)
        {
            if (!File.Exists(path))
                return 0;
            using (var fs = File.OpenRead(path))
            {
                var buf = new byte[24];
                if (fs.Read(buf, 0, 24) != 24)
                    return 0;
                var guid = new byte[16];
                Array.Copy(buf, guid, 16);
                return new Guid(guid) == FileGuid ? BitConverter.ToInt64(buf, 16) : 0;
            }
        }

        public static string FormatVersion(long v) => v == 0 ? "?" : $"{v / 10000000000L}.{v % 10000000000L}";
    }
}
