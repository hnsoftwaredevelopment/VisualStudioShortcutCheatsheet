using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using ShortcutCheatsheet.Options;
using ShortcutCheatsheet.ToolWindow;
using Task = System.Threading.Tasks.Task;

namespace ShortcutCheatsheet
{
    /// <summary>
    /// The package: the menu item View > Other Windows > Shortcut Cheatsheet and the tool window.
    /// VS loads it when the menu item is clicked or the window was open at startup.
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuidString)]
    // 100 and 101 are names in Localization\Strings.resx. IsInUnifiedSettings keeps the legacy page out of the new
    // Options dialog, where Options\registration.json takes its place.
    [ProvideOptionPage(typeof(GeneralOptionsPage), "Shortcut Cheatsheet", "General", 100, 101, true,
        IsInUnifiedSettings = true, UnifiedSettingsCategoryMoniker = "shortcutCheatsheet.general")]
    [ProvideSettingsManifest(PackageRelativeManifestFile = @"Options\registration.json")]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    // Opens as a tab next to Solution Explorer.
    [ProvideToolWindow(typeof(CheatsheetWindow), Style = VsDockStyle.Tabbed, Window = "3AE79031-E1BC-11D0-8F78-00A0C9110057")]
    public sealed class ShortcutCheatsheetPackage : AsyncPackage
    {
        public const string PackageGuidString = "acf2e531-e047-4046-9f60-a0765ab33ef5";
        public const string CommandSetGuidString = "c239636b-81ce-4645-9854-f50b92eb121c";
        public static readonly Guid CommandSet = new Guid(CommandSetGuidString);
        public const int OpenWindowCommandId = 0x0100;

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            // Before the window: it needs the language and the grouping.
            await SettingsService.InitializeAsync(this, cancellationToken);
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            if (await GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commands)
            {
                commands.AddCommand(new MenuCommand((s, e) => ShowWindow(), new CommandID(CommandSet, OpenWindowCommandId)));
            }
        }

        private void ShowWindow()
        {
            JoinableTaskFactory.RunAsync(async () =>
            {
                var pane = await ShowToolWindowAsync(typeof(CheatsheetWindow), 0, true, DisposalToken);
                if (pane?.Frame == null)
                    throw new NotSupportedException("Cannot create the Shortcut Cheatsheet window.");
            }).FileAndForget("ShortcutCheatsheet/ShowWindow");
        }
    }
}
