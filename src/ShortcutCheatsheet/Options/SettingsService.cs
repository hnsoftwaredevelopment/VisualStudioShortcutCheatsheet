using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities.UnifiedSettings;
using ShortcutCheatsheet.Localization;
using Task = System.Threading.Tasks.Task;

namespace ShortcutCheatsheet.Options
{
    /// <summary>
    /// Service ID for Unified Settings. The real type is in an internal VS interop assembly, so this empty interface
    /// only supplies the GUID for GetServiceAsync.
    /// </summary>
    [Guid("e3684f31-344e-42ea-9047-b620fdc7ac25")]
    internal interface SVsUnifiedSettingsManager
    {
    }

    /// <summary>
    /// The one place the extension reads its settings. Uses Unified Settings (Options\registration.json) when VS has
    /// it switched on, otherwise the legacy DialogPage. The legacy page reads and writes through this class, so
    /// both Options dialogs show the same values. Same design as XAML Resource Preview.
    /// </summary>
    internal static class SettingsService
    {
        private static volatile ExtensionSettings _current = ExtensionSettings.Default;
        private static AsyncPackage _package;
        private static ISettingsManager _settingsManager;

        // Held so the change subscription is not garbage collected.
        private static IDisposable Subscription { get; set; }

        /// <summary>Raised on the UI thread after the settings were read again.</summary>
        public static event EventHandler Changed;

        /// <summary>The current settings. Safe to read from any thread.</summary>
        public static ExtensionSettings Current => _current;

        /// <summary>True when Unified Settings is the store.</summary>
        public static bool IsUnifiedSettingsActive { get; private set; }

        /// <summary>For tests, without VS.</summary>
        internal static void SetCurrentForTests(ExtensionSettings settings) => _current = settings ?? ExtensionSettings.Default;

        public static async Task InitializeAsync(AsyncPackage package, CancellationToken cancellationToken)
        {
            _package = package;

            // Null when Unified Settings does not exist. Analyzer VSSDK006 requires the null check.
            // The analyzer only sees the null check on a local variable, not on the field.
            var settingsManager = await package.GetServiceAsync(typeof(SVsUnifiedSettingsManager)) as ISettingsManager;
            _settingsManager = settingsManager;

            await package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            Reload();

            if (settingsManager != null)
            {
                // The callback thread is not guaranteed, so the handler switches to the UI thread.
                Subscription = settingsManager.GetReader().SubscribeToChanges(OnUnifiedSettingsChanged, SettingMonikers.All);
            }
        }

        private static void OnUnifiedSettingsChanged(SettingsUpdate update)
        {
            _package.JoinableTaskFactory.RunAsync(async () =>
            {
                await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                Reload();
            }).FileAndForget("ShortcutCheatsheet/SettingsChanged");
        }

        /// <summary>Reads all settings again and applies the language.</summary>
        public static void Reload()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            ExtensionSettings settings = TryReadUnifiedSettings();
            IsUnifiedSettingsActive = settings != null;
            if (settings == null)
                settings = ((GeneralOptionsPage)_package.GetDialogPage(typeof(GeneralOptionsPage))).ToSettings();

            _current = settings;
            Localizer.Refresh(_package, settings.Language);
            Changed?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Writes the values of the legacy page to Unified Settings, so both dialogs agree.</summary>
        public static void WriteToUnifiedSettings(ExtensionSettings settings)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // The writer name appears in VS logging.
            ISettingsWriter writer = _settingsManager.GetWriter("Shortcut Cheatsheet");
            writer.EnqueueChange(SettingMonikers.Language, LanguageInfo.ToSettingValue(settings.Language));
            writer.EnqueueChange(SettingMonikers.Grouping, ExtensionSettings.GroupingToSetting(settings.Grouping));
            writer.EnqueueChange(SettingMonikers.Paper, ExtensionSettings.PaperToSetting(settings.Paper));
            writer.EnqueueChange(SettingMonikers.Theme, ExtensionSettings.ThemeToSetting(settings.Theme));
            writer.EnqueueChange(SettingMonikers.Columns, settings.Columns);

            // RequestCommit may ask the user for approval first. Commit() is obsolete.
            writer.RequestCommit("Changed on the legacy options page");
        }

        /// <summary>Null when Unified Settings is unavailable or switched off (classic mode).</summary>
        private static ExtensionSettings TryReadUnifiedSettings()
        {
            if (_settingsManager == null)
                return null;

            ISettingsReader reader = _settingsManager.GetReader();
            SettingRetrieval<string> language = reader.GetValue<string>(SettingMonikers.Language, SettingReadOptions.NoRequirements);
            if (language.Outcome == SettingRetrievalOutcome.NotSupportedInClassicMode)
                return null;

            var defaults = ExtensionSettings.Default;
            return new ExtensionSettings(
                LanguageInfo.FromSettingValue(ValueOr(language, "automatic")),
                ExtensionSettings.GroupingFromSetting(ValueOr(reader.GetValue<string>(SettingMonikers.Grouping, SettingReadOptions.NoRequirements), null)),
                ExtensionSettings.PaperFromSetting(ValueOr(reader.GetValue<string>(SettingMonikers.Paper, SettingReadOptions.NoRequirements), null)),
                ExtensionSettings.ThemeFromSetting(ValueOr(reader.GetValue<string>(SettingMonikers.Theme, SettingReadOptions.NoRequirements), null)),
                ValueOr(reader.GetValue<int>(SettingMonikers.Columns, SettingReadOptions.NoRequirements), defaults.Columns));
        }

        // Any outcome other than Success gives the default.
        private static T ValueOr<T>(SettingRetrieval<T> retrieval, T fallback) =>
            retrieval.Outcome == SettingRetrievalOutcome.Success ? retrieval.Value : fallback;
    }
}
