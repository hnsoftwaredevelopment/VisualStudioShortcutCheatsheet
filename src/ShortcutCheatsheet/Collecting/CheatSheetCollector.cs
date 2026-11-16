using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using ExtensionCommon.VisualStudio;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Settings;
using Microsoft.VisualStudio.Threading;
using ShortcutCheatsheet.Model;
using ShortcutCheatsheet.Origin;
using Task = System.Threading.Tasks.Task;

namespace ShortcutCheatsheet.Collecting
{
    /// <summary>The result of one collection, with timings for the status line.</summary>
    internal sealed class Collection
    {
        public CheatSheet Sheet { get; set; }
        public int Commands { get; set; }
        public long DteMilliseconds { get; set; }
        public long OriginMilliseconds { get; set; }
        public string Problem { get; set; }
    }

    /// <summary>
    /// Collects everything for a cheat sheet: DTE on the UI thread, the extension folders and settings.json on a
    /// background thread.
    /// </summary>
    internal static class CheatSheetCollector
    {
        public static async Task<Collection> CollectAsync(AsyncPackage package, CancellationToken token)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(token);
            var dte = await package.GetServiceAsync(typeof(DTE)) as DTE2 ?? throw new InvalidOperationException("DTE is not available.");
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(token);

            var watch = Stopwatch.StartNew();
            var raw = DteBindingReader.Read(dte, out int commands);
            long dteTime = watch.ElapsedMilliseconds;
            string version = dte.Edition + " " + dte.Version;
            string ideFolder = Path.GetDirectoryName(dte.FullName); // ...\Common7\IDE, where devenv.exe is
            string settingsFile = Path.Combine(new ShellSettingsManager(package).GetApplicationDataFolder(ApplicationDataFolder.LocalSettings), "settings.json");

            var list = await InstalledExtensions.GetAsync(package);
            await TaskScheduler.Default;
            token.ThrowIfCancellationRequested();

            watch.Restart();
            var origins = ExtensionCommands.Build(list.Sources, token).WithVisualStudioNames(ideFolder, token);
            long originTime = watch.ElapsedMilliseconds;
            var customizations = ReadCustomizations(settingsFile);

            return new Collection
            {
                Sheet = CheatSheetBuilder.Build(raw, origins, customizations, version, DateTimeOffset.Now),
                Commands = commands,
                DteMilliseconds = dteTime,
                OriginMilliseconds = originTime,
                Problem = list.Problem,
            };
        }

        private static Customizations ReadCustomizations(string settingsFile)
        {
            try
            {
                return File.Exists(settingsFile) ? Customizations.Parse(File.ReadAllText(settingsFile)) : Customizations.None;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return Customizations.None; // VS may be writing the file; the marks are a bonus, not a reason to fail
            }
        }
    }
}
