using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Xml.Linq;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShortcutCheatsheet.Export;
using ShortcutCheatsheet.Localization;
using ShortcutCheatsheet.Model;
using ShortcutCheatsheet.Options;

namespace ShortcutCheatsheet.Tests
{
    /// <summary>The settings live in four places: registration.json, the C# code, the legacy page and Strings.resx.</summary>
    [TestClass]
    public class SettingsTests
    {
        private static string Source(params string[] parts) => Path.Combine(new[] { TestPaths.RepositoryRoot, "src", "ShortcutCheatsheet" }.Concat(parts).ToArray());

        private static Dictionary<string, object> Registration()
        {
            // JavaScriptSerializer does not know comments; registration.json starts with one.
            string text = Regex.Replace(File.ReadAllText(Source("Options", "registration.json")), @"/\*.*?\*/", "", RegexOptions.Singleline);
            return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(text);
        }

        private static Dictionary<string, object> Properties() => (Dictionary<string, object>)Registration()["properties"];

        private static Dictionary<string, object> Property(string moniker) => (Dictionary<string, object>)Properties()[moniker];

        private static string[] Enum(string moniker) => ((System.Collections.ArrayList)Property(moniker)["enum"]).Cast<string>().ToArray();

        private static HashSet<string> ResxNames() =>
            new HashSet<string>(XDocument.Load(Source("Localization", "Strings.resx")).Root.Elements("data").Select(d => (string)d.Attribute("name")));

        [TestMethod]
        public void Registration_has_exactly_the_monikers_of_the_code()
        {
            CollectionAssert.AreEquivalent(SettingMonikers.All, Properties().Keys.ToArray());
            var categories = ((Dictionary<string, object>)Registration()["categories"]).Keys;
            foreach (string moniker in SettingMonikers.All)
                CollectionAssert.Contains(categories.ToArray(), moniker.Substring(0, moniker.LastIndexOf('.')), moniker);
        }

        [TestMethod]
        public void Registration_defaults_match_the_code()
        {
            var d = ExtensionSettings.Default;
            Assert.AreEqual(LanguageInfo.ToSettingValue(d.Language), Property(SettingMonikers.Language)["default"]);
            Assert.AreEqual(ExtensionSettings.GroupingToSetting(d.Grouping), Property(SettingMonikers.Grouping)["default"]);
            Assert.AreEqual(ExtensionSettings.PaperToSetting(d.Paper), Property(SettingMonikers.Paper)["default"]);
            Assert.AreEqual(ExtensionSettings.ThemeToSetting(d.Theme), Property(SettingMonikers.Theme)["default"]);
            Assert.AreEqual(d.Columns, Property(SettingMonikers.Columns)["default"]);
            Assert.AreEqual(ExtensionSettings.MinColumns, Property(SettingMonikers.Columns)["minimum"]);
            Assert.AreEqual(ExtensionSettings.MaxColumns, Property(SettingMonikers.Columns)["maximum"]);
        }

        [TestMethod]
        public void Every_enum_value_round_trips()
        {
            CollectionAssert.AreEqual(Enum(SettingMonikers.Grouping),
                System.Enum.GetValues(typeof(Grouping)).Cast<Grouping>().Select(ExtensionSettings.GroupingToSetting).ToArray());
            foreach (string value in Enum(SettingMonikers.Grouping))
                Assert.AreEqual(value, ExtensionSettings.GroupingToSetting(ExtensionSettings.GroupingFromSetting(value)));
            foreach (string value in Enum(SettingMonikers.Paper))
                Assert.AreEqual(value, ExtensionSettings.PaperToSetting(ExtensionSettings.PaperFromSetting(value)));
            foreach (string value in Enum(SettingMonikers.Theme))
                Assert.AreEqual(value, ExtensionSettings.ThemeToSetting(ExtensionSettings.ThemeFromSetting(value)));
            foreach (string value in Enum(SettingMonikers.Language))
                Assert.AreEqual(value, LanguageInfo.ToSettingValue(LanguageInfo.FromSettingValue(value)));
            Assert.AreEqual(Grouping.Scope, ExtensionSettings.GroupingFromSetting("nonsense"), "an unknown value gives the default");
        }

        [TestMethod]
        public void Every_string_registration_and_the_page_use_exists()
        {
            var names = ResxNames();
            foreach (Match m in Regex.Matches(File.ReadAllText(Source("Options", "registration.json")), @"""@(\w+);\{([0-9a-f-]+)\}"""))
            {
                Assert.IsTrue(names.Contains(m.Groups[1].Value), m.Groups[1].Value + " is not in Strings.resx");
                Assert.AreEqual(new Guid(ShortcutCheatsheetPackage.PackageGuidString), new Guid(m.Groups[2].Value), m.Value);
            }
            foreach (FieldInfo field in typeof(Strings).GetFields())
                Assert.IsTrue(names.Contains((string)field.GetValue(null)), field.Name + " is not in Strings.resx");
            foreach (Type type in new[] { typeof(Grouping), typeof(PaperChoice), typeof(ExportTheme) })
                foreach (string value in System.Enum.GetNames(type))
                    Assert.IsTrue(names.Contains(type.Name + value), type.Name + value + " is not in Strings.resx");
            Assert.IsTrue(names.Contains("100") && names.Contains("101"), "names for [ProvideOptionPage]");
        }

        [TestMethod]
        public void Legacy_page_is_linked_to_its_category()
        {
            var general = (Dictionary<string, object>)((Dictionary<string, object>)Registration()["categories"])["shortcutCheatsheet.general"];
            Assert.AreEqual(new Guid(GeneralOptionsPage.PageGuidString), new Guid((string)general["legacyOptionPageId"]));
            Assert.AreEqual(typeof(GeneralOptionsPage).GUID, new Guid(GeneralOptionsPage.PageGuidString));

            var attribute = typeof(ShortcutCheatsheetPackage).GetCustomAttributes(typeof(ProvideOptionPageAttribute), false).Cast<ProvideOptionPageAttribute>().Single();
            Assert.AreEqual("shortcutCheatsheet.general", attribute.UnifiedSettingsCategoryMoniker);
        }

        [TestMethod]
        public void Paper_follows_the_region_when_automatic()
        {
            Assert.AreEqual(PaperSize.Letter, ExtensionSettings.ResolvePaper(PaperChoice.Automatic, new RegionInfo("US")));
            Assert.AreEqual(PaperSize.Letter, ExtensionSettings.ResolvePaper(PaperChoice.Automatic, new RegionInfo("CA")), "metric, but Letter");
            Assert.AreEqual(PaperSize.A4, ExtensionSettings.ResolvePaper(PaperChoice.Automatic, new RegionInfo("NL")));
            Assert.AreEqual(PaperSize.A4, ExtensionSettings.ResolvePaper(PaperChoice.A4, new RegionInfo("US")), "a choice wins");
            Assert.AreEqual(PaperSize.Letter, ExtensionSettings.ResolvePaper(PaperChoice.Letter, new RegionInfo("NL")));
        }

        [TestMethod]
        public void Render_options_follow_the_settings()
        {
            var options = new ExtensionSettings(ExtensionLanguage.Automatic, Grouping.Scope, PaperChoice.Letter, ExportTheme.Dark, 9).ToRenderOptions(new RegionInfo("NL"));
            Assert.AreEqual(PaperSize.Letter, options.Paper);
            Assert.IsTrue(options.Dark);
            Assert.AreEqual(ExtensionSettings.MaxColumns, options.Columns, "clamped");
        }

        [TestMethod]
        public void Enum_texts_come_from_the_resources()
        {
            Localizer.SetCulture(CultureInfo.GetCultureInfo("en"));
            Assert.AreEqual("Where it works", LocalizedEnumConverter<Grouping>.DisplayName(Grouping.Scope));
            var converter = new LocalizedEnumConverter<PaperChoice>();
            Assert.AreEqual("Letter", converter.ConvertTo(PaperChoice.Letter, typeof(string)));
            Assert.AreEqual(PaperChoice.A4, converter.ConvertFrom("A4"));
            Assert.AreEqual(PaperChoice.Letter, converter.ConvertFrom("letter"), "the enum name, as an older store has it");
            Assert.AreEqual(PaperChoice.Automatic, converter.ConvertFrom("Automatisch"), "text from another language");
        }
    }
}
