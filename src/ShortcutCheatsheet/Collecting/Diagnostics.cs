using Microsoft.VisualStudio.Shell;

namespace ShortcutCheatsheet.Collecting
{
    /// <summary>
    /// Problems the user cannot solve (an internal VS API that changed, a command table we cannot read) go to the
    /// ActivityLog of VS, not to the window. Start VS with /log to see them in %AppData%\Microsoft\VisualStudio\...\ActivityLog.xml.
    /// </summary>
    internal static class Diagnostics
    {
        private const string Source = "Shortcut Cheatsheet";

        public static void Log(Collection collection)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (collection.Problem != null)
                ActivityLog.TryLogWarning(Source, "Extension Manager not available, the extension folders were read instead: " + collection.Problem);
            foreach (var extension in collection.Sheet.Extensions)
            {
                if (extension.Problem != null)
                    ActivityLog.TryLogWarning(Source, $"Command table of {extension.Name} {extension.Version}: {extension.Problem}");
            }
        }
    }
}
