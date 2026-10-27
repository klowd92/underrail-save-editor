using System.Reflection;

namespace UnderrailSaveEditor.App
{
    /// <summary>Author and copyright information shown by the "?" button.</summary>
    public static class About
    {
        public const string Author = "klowd92";
        public const string AuthorLinks = "github.com/klowd92 · reddit.com/u/klowd92";

        public static string Version => Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        public static string Text =>
            $"Underrail Save Editor {Version}\n" +
            $"Author: {Author} ({AuthorLinks})\n\n" +
            "An unofficial, fan-made tool. It is not affiliated with, endorsed by or supported by Stygian Software.\n\n" +
            "Underrail and all of its icons, portraits, artwork, text, game data, code and other resources and intellectual " +
            "property shown or used by this editor are © Stygian Software. The editor does not include any of them: it reads " +
            "them from your own installation of the game at run time.\n\n" +
            "Back up your saves before editing. Use at your own risk.";
    }
}
