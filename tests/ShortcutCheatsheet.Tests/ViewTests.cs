using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShortcutCheatsheet.CommandTables;
using ShortcutCheatsheet.Model;

namespace ShortcutCheatsheet.Tests
{
    [TestClass]
    public class ViewTests
    {
        private static Shortcut S(string scope, string keys, string name, string category, string command = null,
            string extension = null, bool custom = false, bool conflict = false) =>
            new Shortcut
            {
                Scope = scope, Keys = keys, Name = name, Category = category, Command = command, Extension = extension,
                IsCustom = custom, ConflictsWith = conflict ? new System.Collections.Generic.List<string> { "Other.Command" } : null,
            };

        [TestMethod]
        [DataRow("Ctrl+E, V", "Ctrl|E;V")]
        [DataRow("Ctrl+Shift+F1", "Ctrl|Shift|F1")]
        [DataRow("Ctrl+Num +", "Ctrl|Num +")]
        [DataRow("Ctrl++", "Ctrl|+")]
        [DataRow("Ctrl+,", "Ctrl|,")]
        [DataRow("Ctrl+K, Ctrl+,", "Ctrl|K;Ctrl|,")]
        [DataRow("Shift+Alt+Space", "Shift|Alt|Space")]
        [DataRow("F5", "F5")]
        [DataRow("Ctrl+\\, Ctrl+Tab", "Ctrl|\\;Ctrl|Tab")]
        public void Splits_keys_into_strokes_and_key_caps(string keys, string expected)
        {
            string actual = string.Join(";", KeyChord.Parse(keys).Select(stroke => string.Join("|", stroke)));
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Shows_arrows_as_symbols()
        {
            Assert.AreEqual("\u2191", KeyChord.Display("Up Arrow"));
            Assert.AreEqual("\u2193", KeyChord.Display("Down Arrow"));
            Assert.AreEqual("\u2190", KeyChord.Display("Left Arrow"));
            Assert.AreEqual("\u2192", KeyChord.Display("Right Arrow"));
            Assert.AreEqual("PgUp", KeyChord.Display("PgUp"));
            Assert.AreEqual("Ctrl", KeyChord.Display("Ctrl"));
        }

        [TestMethod]
        public void Hides_filters_that_would_find_nothing()
        {
            var model = new ToolWindow.CheatsheetViewModel { OnlyCustom = true };
            model.Sheet = new CheatSheet { Shortcuts = { S("Global", "F5", "Start", "Debug") } };
            Assert.IsFalse(model.HasCustom);
            Assert.IsFalse(model.HasConflicts);
            Assert.IsFalse(model.OnlyCustom, "a hidden filter is switched off");
            Assert.AreEqual("1 shortcut.", model.Summary);

            model.Sheet = new CheatSheet { Shortcuts = { S("Text Editor", "Ctrl+Shift+D", "Duplicate", "Edit", custom: true, conflict: true) } };
            Assert.IsTrue(model.HasCustom);
            Assert.IsTrue(model.HasConflicts);
        }

        [TestMethod]
        public void Searches_names_and_keys()
        {
            var duplicate = S("Text Editor", "Ctrl+E, V", "Duplicate", "Edit", "Edit.Duplicate");
            var stepOver = S("Global", "F10", "Step Over", "Debug", "Debug.StepOver");
            var bookmark = S("Global", "Shift+Alt+N", "Go To Next Bookmark", "Bookmark Studio", "GoToNextBookmark", "Bookmark Studio");

            Assert.IsTrue(ShortcutView.Matches(duplicate, new ShortcutFilter { Text = "dupl" }));
            Assert.IsTrue(ShortcutView.Matches(duplicate, new ShortcutFilter { Text = "ctrl+e,v" }), "keys without the space");
            Assert.IsTrue(ShortcutView.Matches(duplicate, new ShortcutFilter { Text = "Ctrl+E, V" }), "keys as VS shows them");
            Assert.IsTrue(ShortcutView.Matches(stepOver, new ShortcutFilter { Text = "debug f10" }), "every word somewhere");
            Assert.IsFalse(ShortcutView.Matches(stepOver, new ShortcutFilter { Text = "debug f11" }));
            Assert.IsTrue(ShortcutView.Matches(bookmark, new ShortcutFilter { Text = "bookmark studio" }), "the extension name");
        }

        [TestMethod]
        public void Filters_extensions_your_own_and_conflicts()
        {
            var vs = S("Global", "F5", "Start", "Debug");
            var ext = S("Global", "Shift+Alt+B", "Bookmark Manager", "View", extension: "Bookmark Studio", conflict: true);
            var mine = S("Text Editor", "Ctrl+Shift+D", "Duplicate", "Edit", custom: true);

            Assert.IsTrue(new ShortcutFilter().IsEmpty);
            CollectionAssert.AreEqual(new[] { ext }, new[] { vs, ext, mine }.Where(s => ShortcutView.Matches(s, new ShortcutFilter { OnlyExtensions = true })).ToArray());
            CollectionAssert.AreEqual(new[] { mine }, new[] { vs, ext, mine }.Where(s => ShortcutView.Matches(s, new ShortcutFilter { OnlyCustom = true })).ToArray());
            CollectionAssert.AreEqual(new[] { ext }, new[] { vs, ext, mine }.Where(s => ShortcutView.Matches(s, new ShortcutFilter { OnlyConflicts = true })).ToArray());
        }

        [TestMethod]
        public void Groups_and_sorts_per_choice()
        {
            var vs = S("Text Editor", "Ctrl+E, V", "Duplicate", "Edit");
            var global = S("Global", "F5", "Start", "Debug");
            var ext = S("Text Editor", "Ctrl+Alt+F12", "Go To Resource Definition", "ResourcePreview", extension: "XAML Resource Preview");
            var all = new[] { vs, ext, global };

            Assert.AreEqual(("Text Editor", "Edit"), ShortcutView.Groups(vs, Grouping.Scope));
            Assert.AreEqual(("Edit", "Text Editor"), ShortcutView.Groups(vs, Grouping.Category));
            Assert.AreEqual(("Visual Studio", "Text Editor"), ShortcutView.Groups(vs, Grouping.Extension));
            Assert.AreEqual(("XAML Resource Preview", "Text Editor"), ShortcutView.Groups(ext, Grouping.Extension));

            CollectionAssert.AreEqual(new[] { global, vs, ext }, ShortcutView.Sort(all, Grouping.Scope), "Global first");
            CollectionAssert.AreEqual(new[] { global, vs, ext }, ShortcutView.Sort(all, Grouping.Extension), "Visual Studio first");
            CollectionAssert.AreEqual(new[] { ext, vs, global }, ShortcutView.Sort(all, Grouping.Keys), "alphabetical on keys");
        }

        [TestMethod]
        public void Image_manifest_vsct_and_code_agree()
        {
            string root = TestPaths.RepositoryRoot;
            var manifest = XDocument.Load(Path.Combine(root, @"src\ShortcutCheatsheet\ShortcutCheatsheet.imagemanifest"));
            XNamespace im = "http://schemas.microsoft.com/VisualStudio/ImageManifestSchema/2014";
            var guid = manifest.Descendants(im + "Guid").Single(e => (string)e.Attribute("Name") == "ShortcutCheatsheetImages");
            var id = manifest.Descendants(im + "ID").Single(e => (string)e.Attribute("Name") == "Cheatsheet");
            Assert.AreEqual(new Guid(ImageMonikers.GuidString), new Guid((string)guid.Attribute("Value")));
            Assert.AreEqual(ImageMonikers.CheatsheetId, int.Parse((string)id.Attribute("Value")));

            XNamespace vsct = "http://schemas.microsoft.com/VisualStudio/2005-10-18/CommandTable";
            var symbol = XDocument.Load(Path.Combine(root, @"src\ShortcutCheatsheet\ShortcutCheatsheetPackage.vsct"))
                .Descendants(vsct + "GuidSymbol").Single(e => (string)e.Attribute("name") == "guidShortcutCheatsheetImages");
            Assert.AreEqual(new Guid(ImageMonikers.GuidString), new Guid((string)symbol.Attribute("value")));
            Assert.AreEqual(ImageMonikers.CheatsheetId, Convert.ToInt32((string)symbol.Element(vsct + "IDSymbol").Attribute("value")));
        }

        [TestMethod]
        public void Reads_a_table_of_several_blocks()
        {
            // Large.cto includes KnownImageIds.vsct: more than 64 KB, so the .cto has several compressed blocks.
            byte[] cto = File.ReadAllBytes(TestPaths.Fixture(@"CommandTables\Large.cto"));
            Assert.IsTrue(BitConverter.ToInt32(cto, 8) > 64 * 1024, "the fixture must be larger than one block");
            var table = CommandTableReader.Read(cto);
            Assert.AreEqual("Large Command", table.Commands.Single().ButtonText);
            Assert.AreEqual(1, table.KeyBindings.Count);
        }
    }
}
