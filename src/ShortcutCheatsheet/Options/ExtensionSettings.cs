using System;
using System.Collections.Generic;
using System.Globalization;
using ShortcutCheatsheet.Export;
using ShortcutCheatsheet.Model;

namespace ShortcutCheatsheet.Options
{
    /// <summary>Paper size as chosen in the options; Automatic follows the Windows region.</summary>
    public enum PaperChoice
    {
        Automatic,
        A4,
        Letter,
    }

    public enum ExportTheme
    {
        Light,
        Dark,
    }

    /// <summary>Monikers from registration.json. The part before the last dot must be a category defined there.</summary>
    internal static class SettingMonikers
    {
        public const string Language = "shortcutCheatsheet.general.language";
        public const string Grouping = "shortcutCheatsheet.general.grouping";
        public const string Paper = "shortcutCheatsheet.export.paperSize";
        public const string Theme = "shortcutCheatsheet.export.theme";
        public const string Columns = "shortcutCheatsheet.export.columns";

        public static readonly string[] All = { Language, Grouping, Paper, Theme, Columns };
    }

    /// <summary>All settings as one immutable value, so any thread can read <see cref="SettingsService.Current"/>.</summary>
    internal sealed class ExtensionSettings
    {
        public const int MinColumns = 1;
        public const int MaxColumns = 4;

        public static readonly ExtensionSettings Default = new ExtensionSettings(ExtensionLanguage.Automatic, Grouping.Scope, PaperChoice.Automatic, ExportTheme.Light, 3);

        public ExtensionSettings(ExtensionLanguage language, Grouping grouping, PaperChoice paper, ExportTheme theme, int columns)
        {
            Language = language;
            Grouping = grouping;
            Paper = paper;
            Theme = theme;
            Columns = Math.Max(MinColumns, Math.Min(MaxColumns, columns));
        }

        public ExtensionLanguage Language { get; }

        /// <summary>The grouping the window starts with.</summary>
        public Grouping Grouping { get; }

        public PaperChoice Paper { get; }
        public ExportTheme Theme { get; }
        public int Columns { get; }

        /// <summary>How PDF and PNG pages look with these settings.</summary>
        public RenderOptions ToRenderOptions(RegionInfo region = null) => new RenderOptions
        {
            Paper = ResolvePaper(Paper, region ?? RegionInfo.CurrentRegion),
            Dark = Theme == ExportTheme.Dark,
            Columns = Columns,
        };

        /// <summary>
        /// Letter where it is the usual size (the United States, Canada, Mexico and a few countries in Latin America
        /// and Asia), A4 everywhere else. RegionInfo.IsMetric is no help: Canada is metric but prints on Letter.
        /// </summary>
        public static PaperSize ResolvePaper(PaperChoice choice, RegionInfo region)
        {
            if (choice == PaperChoice.A4)
                return PaperSize.A4;
            if (choice == PaperChoice.Letter)
                return PaperSize.Letter;
            return region != null && LetterRegions.Contains(region.TwoLetterISORegionName) ? PaperSize.Letter : PaperSize.A4;
        }

        private static readonly HashSet<string> LetterRegions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "US", "CA", "MX", "PR", "CL", "CO", "VE", "CR", "GT", "SV", "PA", "DO", "PH",
        };

        // --- The string values in registration.json ---

        private static readonly string[] GroupingValues = { "scope", "category", "extension", "keys" };

        public static string GroupingToSetting(Grouping grouping) => GroupingValues[(int)grouping];

        public static Grouping GroupingFromSetting(string value)
        {
            int index = Array.IndexOf(GroupingValues, value);
            return index < 0 ? Grouping.Scope : (Grouping)index;
        }

        public static string PaperToSetting(PaperChoice paper) => paper == PaperChoice.A4 ? "a4" : paper == PaperChoice.Letter ? "letter" : "automatic";

        public static PaperChoice PaperFromSetting(string value) => value == "a4" ? PaperChoice.A4 : value == "letter" ? PaperChoice.Letter : PaperChoice.Automatic;

        public static string ThemeToSetting(ExportTheme theme) => theme == ExportTheme.Dark ? "dark" : "light";

        public static ExportTheme ThemeFromSetting(string value) => value == "dark" ? ExportTheme.Dark : ExportTheme.Light;
    }
}
