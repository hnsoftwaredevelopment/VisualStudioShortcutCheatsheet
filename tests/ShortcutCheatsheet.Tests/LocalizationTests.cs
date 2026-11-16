using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShortcutCheatsheet.Export;
using ShortcutCheatsheet.Localization;
using ShortcutCheatsheet.Model;
using ShortcutCheatsheet.Options;

namespace ShortcutCheatsheet.Tests
{
    /// <summary>Every language has every text, with the same placeholders, and the code and XAML only ask for texts that exist.</summary>
    [TestClass]
    public class LocalizationTests
    {
        private static string Folder => Path.Combine(TestPaths.RepositoryRoot, "src", "ShortcutCheatsheet", "Localization");

        private static Dictionary<string, string> Read(string file) =>
            XDocument.Load(file).Root.Elements("data").ToDictionary(d => (string)d.Attribute("name"), d => (string)d.Element("value"));

        private static IEnumerable<string> Translations => Directory.GetFiles(Folder, "Strings.*.resx");

        private static string[] Placeholders(string text) => Regex.Matches(text ?? "", @"\{\d+\}").Cast<Match>().Select(m => m.Value).Distinct().OrderBy(p => p).ToArray();

        [TestMethod]
        public void Every_language_of_the_options_has_a_translation()
        {
            var cultures = Translations.Select(f => Path.GetFileNameWithoutExtension(f).Substring("Strings.".Length)).ToList();
            foreach (ExtensionLanguage language in LanguageInfo.All.Where(l => l != ExtensionLanguage.Automatic && l != ExtensionLanguage.English))
                CollectionAssert.Contains(cultures, LanguageInfo.ToCulture(language).Name, language.ToString());
            Assert.AreEqual(14, cultures.Count);
        }

        [TestMethod]
        public void Translations_have_the_same_names_and_placeholders()
        {
            var english = Read(Path.Combine(Folder, "Strings.resx"));
            foreach (string file in Translations)
            {
                var translated = Read(file);
                CollectionAssert.AreEquivalent(english.Keys.ToArray(), translated.Keys.ToArray(), Path.GetFileName(file));
                foreach (var pair in english)
                {
                    CollectionAssert.AreEqual(Placeholders(pair.Value), Placeholders(translated[pair.Key]), Path.GetFileName(file) + ": " + pair.Key);
                    Assert.IsFalse(string.IsNullOrWhiteSpace(translated[pair.Key]), Path.GetFileName(file) + ": " + pair.Key);
                }
            }
        }

        [TestMethod]
        public void Xaml_asks_only_for_texts_that_exist()
        {
            var names = Read(Path.Combine(Folder, "Strings.resx")).Keys;
            string xaml = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot, @"src\ShortcutCheatsheet\ToolWindow\CheatsheetControl.xaml"));
            var used = Regex.Matches(xaml, @"Texts\[(\w+)\]").Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
            Assert.IsTrue(used.Count >= 10, "the XAML binds its texts");
            foreach (string name in used)
                CollectionAssert.Contains(names.ToArray(), name, name);
            Assert.IsFalse(Regex.IsMatch(xaml, @"(Content|Text|ToolTip|AutomationProperties\.Name)=""[A-Za-z][^""{]*"""), "a fixed English text in the XAML");
        }

        [TestMethod]
        public void Code_asks_only_for_texts_that_exist()
        {
            var names = Read(Path.Combine(Folder, "Strings.resx")).Keys;
            foreach (FieldInfo field in typeof(Strings).GetFields())
                CollectionAssert.Contains(names.ToArray(), (string)field.GetValue(null), field.Name);
        }

        [TestMethod]
        public void Satellites_load_and_exports_follow_the_language()
        {
            try
            {
                Localizer.SetCulture(CultureInfo.GetCultureInfo("nl"));
                Assert.AreEqual("Vernieuwen", Localizer.Get("WindowRefresh"));
                Assert.AreEqual("Waar het werkt", LocalizedEnumConverter<Grouping>.DisplayName(Grouping.Scope));

                var sheet = new CheatSheet { VisualStudio = "Community 18.0" };
                sheet.Shortcuts.Add(new Shortcut { Scope = "Global", Keys = "Ctrl+B", Name = "Build", Category = "Build", IsCustom = true });
                var document = ExportDocument.Create(sheet, new ShortcutFilter(), Grouping.Scope, null, new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero));
                Assert.AreEqual("Sneltoetsen in Visual Studio", document.Title);
                StringAssert.Contains(document.Subtitle, "november 2026");
                StringAssert.Contains(MarkdownExporter.ToText(document), "| Toetsen | Command | Opmerkingen |");
                StringAssert.Contains(HtmlExporter.ToText(document), "<html lang=\"nl\">");
                StringAssert.StartsWith(CsvExporter.ToText(document, ";"), "Scope;Keys;Name", "CSV stays English");

                Localizer.SetCulture(CultureInfo.GetCultureInfo("ja"));
                Assert.AreEqual("最新の情報に更新", Localizer.Get("WindowRefresh"));
            }
            finally
            {
                Localizer.SetCulture(CultureInfo.GetCultureInfo("en"));
            }
        }
    }
}
