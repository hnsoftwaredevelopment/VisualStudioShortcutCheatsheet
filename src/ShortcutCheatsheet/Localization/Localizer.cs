using System;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using ShortcutCheatsheet.Options;

namespace ShortcutCheatsheet.Localization
{
    /// <summary>
    /// Resource names in Strings.resx. By hand instead of a generated Designer class, because that class always
    /// uses the thread's CurrentUICulture, not the language chosen in the options.
    /// </summary>
    internal static class Strings
    {
        public const string OptionsRootDescription = nameof(OptionsRootDescription);
        public const string OptionsCategoryGeneral = nameof(OptionsCategoryGeneral);
        public const string OptionsCategoryExport = nameof(OptionsCategoryExport);
        public const string OptionsLanguageName = nameof(OptionsLanguageName);
        public const string OptionsLanguageDescription = nameof(OptionsLanguageDescription);
        public const string LanguageAutomatic = nameof(LanguageAutomatic);
        public const string OptionsGroupingName = nameof(OptionsGroupingName);
        public const string OptionsGroupingDescription = nameof(OptionsGroupingDescription);
        public const string OptionsPaperName = nameof(OptionsPaperName);
        public const string OptionsPaperDescription = nameof(OptionsPaperDescription);
        public const string OptionsThemeName = nameof(OptionsThemeName);
        public const string OptionsThemeDescription = nameof(OptionsThemeDescription);
        public const string OptionsColumnsName = nameof(OptionsColumnsName);
        public const string OptionsColumnsDescription = nameof(OptionsColumnsDescription);

        // Enum values are the enum type name plus the value name ("GroupingScope", "PaperChoiceA4"); see LocalizedEnumConverter.
    }

    /// <summary>
    /// Strings in the language chosen in the options. The resources are embedded as "VSPackage" (LogicalName in the
    /// .csproj), so VS finds the same strings for registration.json and [ProvideOptionPage].
    /// </summary>
    internal static class Localizer
    {
        private static readonly ResourceManager ResourceManager = new ResourceManager("VSPackage", typeof(Localizer).Assembly);

        private static volatile CultureInfo _culture = CultureInfo.GetCultureInfo("en");

        /// <summary>The current language. Safe to read from any thread.</summary>
        public static CultureInfo Culture => _culture;

        public static string Get(string name) => ResourceManager.GetString(name, _culture) ?? name;

        public static string Format(string name, params object[] args) => string.Format(_culture, Get(name), args);

        /// <summary>Resolves the language from the setting. Called when the package loads and after each settings change.</summary>
        public static void Refresh(IServiceProvider serviceProvider, ExtensionLanguage setting)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            SetCulture(setting == ExtensionLanguage.Automatic ? GetVisualStudioCulture(serviceProvider) : LanguageInfo.ToCulture(setting));
        }

        /// <summary>Sets the language directly. Used by Refresh and by tests that run without VS.</summary>
        internal static void SetCulture(CultureInfo culture) => _culture = culture ?? CultureInfo.GetCultureInfo("en");

        /// <summary>
        /// The language the VS UI shows. CurrentUICulture is unreliable: with VS on "Same as Windows" it can be a
        /// language VS has no language pack for, while VS shows English.
        /// </summary>
        private static CultureInfo GetVisualStudioCulture(IServiceProvider serviceProvider)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (serviceProvider.GetService(typeof(SUIHostLocale)) is IUIHostLocale hostLocale && ErrorHandler.Succeeded(hostLocale.GetUILocale(out uint lcid)))
            {
                try
                {
                    return LanguageInfo.MatchVisualStudioLanguage(CultureInfo.GetCultureInfo((int)lcid));
                }
                catch (CultureNotFoundException)
                {
                    // Unknown LCID; fall back below.
                }
            }
            return LanguageInfo.MatchVisualStudioLanguage(CultureInfo.CurrentUICulture);
        }
    }

    // The standard attributes only take constant strings. These look the text up when the options PropertyGrid
    // asks for it, so it follows the chosen language.

    internal sealed class LocalizedDisplayNameAttribute : DisplayNameAttribute
    {
        private readonly string _resourceName;
        public LocalizedDisplayNameAttribute(string resourceName) => _resourceName = resourceName;
        public override string DisplayName => Localizer.Get(_resourceName);
    }

    internal sealed class LocalizedDescriptionAttribute : DescriptionAttribute
    {
        private readonly string _resourceName;
        public LocalizedDescriptionAttribute(string resourceName) => _resourceName = resourceName;
        public override string Description => Localizer.Get(_resourceName);
    }

    /// <summary>Uses the GetLocalizedString hook that CategoryAttribute has for this.</summary>
    internal sealed class LocalizedCategoryAttribute : CategoryAttribute
    {
        public LocalizedCategoryAttribute(string resourceName) : base(resourceName) { }
        protected override string GetLocalizedString(string value) => Localizer.Get(value);
    }

    /// <summary>
    /// Shows an enum on the legacy options page with the text from Strings.resx: resource "Grouping" + "Scope" for
    /// Grouping.Scope. DialogPage also stores the value through the converter, so ConvertFrom accepts both the text
    /// and the enum name; a text from another language falls back to the first value.
    /// </summary>
    internal class LocalizedEnumConverter<T> : EnumConverter where T : struct, Enum
    {
        public LocalizedEnumConverter() : base(typeof(T)) { }

        public static string DisplayName(T value) => Localizer.Get(typeof(T).Name + value);

        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
        {
            if (destinationType == typeof(string) && value is T typed)
                return DisplayName(typed);
            return base.ConvertTo(context, culture, value, destinationType);
        }

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            if (value is string text)
            {
                foreach (T item in Enum.GetValues(typeof(T)))
                {
                    if (text == DisplayName(item) || string.Equals(text, item.ToString(), StringComparison.OrdinalIgnoreCase))
                        return item;
                }
                return default(T);
            }
            return base.ConvertFrom(context, culture, value);
        }
    }
}
