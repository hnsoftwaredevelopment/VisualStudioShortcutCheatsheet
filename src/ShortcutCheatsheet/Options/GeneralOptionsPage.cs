using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using ShortcutCheatsheet.Localization;
using ShortcutCheatsheet.Model;

namespace ShortcutCheatsheet.Options
{
    /// <summary>
    /// The page in the legacy Options dialog (Tools > Options > Shortcut Cheatsheet > General), and the store when
    /// Unified Settings is off. When it is on, the page reads and writes through <see cref="SettingsService"/>.
    /// The GUID must equal "legacyOptionPageId" in registration.json.
    /// </summary>
    [ComVisible(true)]
    [Guid(PageGuidString)]
    public sealed class GeneralOptionsPage : DialogPage
    {
        public const string PageGuidString = "5e2c8a47-3b1d-4f69-9a0e-7d4c2b8f1a63";

        public GeneralOptionsPage() => FromSettings(ExtensionSettings.Default);

        [LocalizedCategory(Strings.OptionsCategoryGeneral)]
        [LocalizedDisplayName(Strings.OptionsLanguageName)]
        [LocalizedDescription(Strings.OptionsLanguageDescription)]
        [DefaultValue(ExtensionLanguage.Automatic)]
        public ExtensionLanguage Language { get; set; }

        [LocalizedCategory(Strings.OptionsCategoryGeneral)]
        [LocalizedDisplayName(Strings.OptionsGroupingName)]
        [LocalizedDescription(Strings.OptionsGroupingDescription)]
        [TypeConverter(typeof(LocalizedEnumConverter<Grouping>))]
        [DefaultValue(Grouping.Scope)]
        public Grouping Grouping { get; set; }

        [LocalizedCategory(Strings.OptionsCategoryExport)]
        [LocalizedDisplayName(Strings.OptionsPaperName)]
        [LocalizedDescription(Strings.OptionsPaperDescription)]
        [TypeConverter(typeof(LocalizedEnumConverter<PaperChoice>))]
        [DefaultValue(PaperChoice.Automatic)]
        public PaperChoice PaperSize { get; set; }

        [LocalizedCategory(Strings.OptionsCategoryExport)]
        [LocalizedDisplayName(Strings.OptionsThemeName)]
        [LocalizedDescription(Strings.OptionsThemeDescription)]
        [TypeConverter(typeof(LocalizedEnumConverter<ExportTheme>))]
        [DefaultValue(ExportTheme.Light)]
        public ExportTheme Theme { get; set; }

        [LocalizedCategory(Strings.OptionsCategoryExport)]
        [LocalizedDisplayName(Strings.OptionsColumnsName)]
        [LocalizedDescription(Strings.OptionsColumnsDescription)]
        [DefaultValue(3)]
        public int Columns { get; set; }

        internal ExtensionSettings ToSettings() => new ExtensionSettings(Language, Grouping, PaperSize, Theme, Columns);

        private void FromSettings(ExtensionSettings settings)
        {
            Language = settings.Language;
            Grouping = settings.Grouping;
            PaperSize = settings.Paper;
            Theme = settings.Theme;
            Columns = settings.Columns;
        }

        /// <summary>Shows the values from Unified Settings when the page opens.</summary>
        protected override void OnActivate(CancelEventArgs e)
        {
            if (SettingsService.IsUnifiedSettingsActive)
                FromSettings(SettingsService.Current);
            base.OnActivate(e);
        }

        public override void LoadSettingsFromStorage()
        {
            base.LoadSettingsFromStorage();
            if (SettingsService.IsUnifiedSettingsActive)
                FromSettings(SettingsService.Current);
        }

        /// <summary>After OK: to Unified Settings when it is on, otherwise to the DialogPage store.</summary>
        public override void SaveSettingsToStorage()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (SettingsService.IsUnifiedSettingsActive)
                SettingsService.WriteToUnifiedSettings(ToSettings());
            else
                base.SaveSettingsToStorage();
            SettingsService.Reload();
        }
    }
}
