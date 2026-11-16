using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using ExtensionCommon.VisualStudio;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShortcutCheatsheet.Collecting;
using ShortcutCheatsheet.CommandTables;
using ShortcutCheatsheet.Export;
using ShortcutCheatsheet.Model;
using ShortcutCheatsheet.Origin;

namespace ShortcutCheatsheet.Tests
{
    [TestClass]
    public class CheatSheetTests
    {
        private static readonly Guid VsStd2K = new Guid("1496a755-94de-11d0-8c3f-00c04fc2aae2");
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 13, 14, 0, 0, TimeSpan.FromHours(1));

        private static RawBinding Vs(string name, string scope, string keys, uint id = 1) => new RawBinding(name, name, VsStd2K, id, scope, keys);

        [TestMethod]
        public void Splits_scope_and_keys()
        {
            Assert.IsTrue(RawBinding.TrySplit("Text Editor::Ctrl+E, V", out string scope, out string keys));
            Assert.AreEqual("Text Editor", scope);
            Assert.AreEqual("Ctrl+E, V", keys);
            Assert.IsTrue(RawBinding.TrySplit("Global::Ctrl+Shift+:", out _, out keys)); // the key itself may be a colon
            Assert.AreEqual("Ctrl+Shift+:", keys);
            Assert.IsFalse(RawBinding.TrySplit("Ctrl+E", out _, out _));
            Assert.IsFalse(RawBinding.TrySplit("Global::", out _, out _));
        }

        [TestMethod]
        [DataRow("Edit.CommentSelection", null, "Edit", "Comment Selection")]
        [DataRow("Debug.Autos.Search", null, "Debug", "Autos Search")]
        [DataRow("EditorContextMenus.CodeWindow.Markdown.MakeBold", null, "Markdown", "Make Bold")]
        [DataRow("OtherContextMenus.LeftMarginContextMenu.ToggleBookmark", null, "Context Menu", "Toggle Bookmark")]
        [DataRow("CodeMaid.CleanupActiveDocument", "&Cleanup Active Document", "CodeMaid", "Cleanup Active Document")]
        [DataRow("Tools.XAMLDesignerOptions", "Options...", "Tools", "Options")]
        [DataRow("", "Save && Close", "Other", "Save & Close")]
        public void Describes_command_names(string canonical, string button, string category, string name)
        {
            Assert.AreEqual((category, name), CommandNames.Describe(canonical, button));
        }

        [TestMethod]
        public void Reads_your_own_shortcuts_from_settings_json()
        {
            const string json = "/* Visual Studio Settings File */\n{\n" +
                "  \"environment.keyboardShortcuts.customizations\": [\n" +
                "    { \"scheme\": \"default\", \"command\": \"Debug.Autos.Search\", \"shortcut\": \"Ctrl+E\", \"scope\": \"Autos Window\", \"isRemoval\": true },\n" +
                "    { \"scheme\": \"default\", \"command\": \"Edit.Duplicate\", \"shortcut\": \"Ctrl+Shift+D\", \"scope\": \"Text Editor\", \"isRemoval\": false }\n" +
                "  ],\n  \"other.setting\": 1\n}";
            var c = Customizations.Parse(json);
            Assert.AreEqual(1, c.Added);
            Assert.AreEqual(1, c.Removed);
            Assert.IsTrue(c.IsAdded("Edit.Duplicate", "Text Editor", "Ctrl+Shift+D"));
            Assert.IsFalse(c.IsAdded("Edit.Duplicate", "Global", "Ctrl+Shift+D"));
            Assert.AreEqual(0, Customizations.Parse("{ not json").Added);
            Assert.AreEqual(0, Customizations.Parse("{}").Added);
        }

        [TestMethod]
        public void Finds_the_package_dll_in_a_pkgdef()
        {
            const string pkgdef = "[$RootKey$\\Packages\\{acf2e531-e047-4046-9f60-a0765ab33ef5}]\n" +
                "\"Assembly\"=\"ShortcutCheatsheet, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null\"\n" +
                "\"CodeBase\"=\"$PackageFolder$\\ShortcutCheatsheet.dll\"\n" +
                "[$RootKey$\\Packages\\{11111111-2222-3333-4444-555555555555}]\n" +
                "\"Assembly\"=\"Other, Version=1.0.0.0\"\n" +
                "[$RootKey$\\Menus]\n\"CodeBase\"=\"$PackageFolder$\\NotAPackage.dll\"\n";
            var map = PkgdefPackages.Assemblies(pkgdef, @"C:\Ext");
            Assert.AreEqual(@"C:\Ext\ShortcutCheatsheet.dll", map[new Guid("acf2e531-e047-4046-9f60-a0765ab33ef5")]);
            Assert.AreEqual(@"C:\Ext\Other.dll", map[new Guid("11111111-2222-3333-4444-555555555555")]);
            Assert.AreEqual(2, map.Count);
            Assert.IsNull(PkgdefPackages.ResolveFolder(@"$RootFolder$\x.dll", @"C:\Ext"));
        }

        [TestMethod]
        public void Gives_extension_commands_their_extension()
        {
            // This extension's own build output is a complete extension folder: manifest, pkgdef and DLL.
            var origins = ExtensionCommands.Build(new[] { new ExtensionSource(BuildOutput(), null, false) });
            var ours = new RawBinding("View.ShortcutCheatsheet", "View.ShortcutCheatsheet", ShortcutCheatsheetPackage.CommandSet,
                (uint)ShortcutCheatsheetPackage.OpenWindowCommandId, "Global", "Ctrl+K, Ctrl+K");

            var sheet = CheatSheetBuilder.Build(new[] { ours, Vs("Edit.Duplicate", "Text Editor", "Ctrl+E, V") }, origins, null, "Test", Now);

            var shortcut = sheet.Shortcuts.Single(s => s.Command == "View.ShortcutCheatsheet");
            Assert.AreEqual("Shortcut Cheatsheet", shortcut.Extension);
            Assert.AreEqual("Shortcut Cheatsheet", shortcut.Name, "the button text from the command table");
            Assert.AreEqual("View", shortcut.Category);
            Assert.IsNull(sheet.Shortcuts.Single(s => s.Command == "Edit.Duplicate").Extension, "VS itself has no extension name");
            Assert.AreEqual(1, sheet.Extensions.Single(e => e.Name == "Shortcut Cheatsheet").Shortcuts);
        }

        [TestMethod]
        [DataRow((byte)0x42, 6, (byte)0, 0, "Ctrl+Shift+B")]
        [DataRow((byte)0x4B, 4, (byte)0x51, 4, "Ctrl+K, Ctrl+Q")]
        [DataRow((byte)0x26, 3, (byte)0, 0, "Shift+Alt+Up Arrow")]
        [DataRow((byte)0x70, 7, (byte)0, 0, "Ctrl+Shift+Alt+F1")]
        [DataRow((byte)0x6B, 4, (byte)0, 0, "Ctrl+Num +")]
        [DataRow((byte)0x4D, 4, (byte)0x20, 0, "Ctrl+M, Space")]
        [DataRow((byte)0xBD, 4, (byte)0, 0, "Ctrl+-")]
        [DataRow((byte)0xE5, 4, (byte)0, 0, null)]
        public void Writes_keys_from_a_command_table_as_VS_does(byte key1, int mod1, byte key2, int mod2, string expected)
        {
            Assert.AreEqual(expected, KeyText.Format(new KeyStroke(key1, (KeyModifiers)mod1), new KeyStroke(key2, (KeyModifiers)mod2)));
        }

        [TestMethod]
        public void Reports_default_bindings_that_VS_did_not_apply()
        {
            // The fixture wants Ctrl+Shift+F1 (Global) for "First Command"; here Build Solution already has it.
            var table = CommandTableReader.Read(File.ReadAllBytes(TestPaths.Fixture(@"CommandTables\Fixture.cto")));
            var origins = ExtensionCommands.FromTables(new[] { (new ExtensionInfo("Fixture", "1.0", @"C:\Ext"), table) });
            var fixtureSet = new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            var sheet = CheatSheetBuilder.Build(new[]
            {
                Vs("Build.BuildSolution", "Global", "Ctrl+Shift+F1", 0x20),
                // The chord in the Text Editor was applied, so it is a normal shortcut and no report.
                new RawBinding("Tools.FixtureSecond", "", fixtureSet, 0x101, "Text Editor", "Ctrl+K, Ctrl+Q"),
                // Alt+D for "Other Set Command" is missing too, but nobody holds Alt+D: not reported.
            }, origins, null, "Test", Now);

            var skipped = sheet.Shortcuts.Single(s => s.NotApplied);
            Assert.AreEqual("Global", skipped.Scope);
            Assert.AreEqual("Ctrl+Shift+F1", skipped.Keys);
            Assert.AreEqual("First Command", skipped.Name);
            Assert.AreEqual("Fixture", skipped.Extension);
            CollectionAssert.AreEqual(new[] { "Build Solution" }, skipped.UsedBy);
            Assert.IsFalse(sheet.Shortcuts.Any(s => s.NotApplied && s.Keys.StartsWith("Alt")));
            Assert.AreEqual(0, sheet.Extensions.Single().Shortcuts - 1, "the applied chord counts, the skipped key does not");
        }

        [TestMethod]
        public void A_binding_you_removed_yourself_is_not_reported()
        {
            var table = CommandTableReader.Read(File.ReadAllBytes(TestPaths.Fixture(@"CommandTables\Fixture.cto")));
            var origins = ExtensionCommands.FromTables(new[] { (new ExtensionInfo("Fixture", "1.0", @"C:\Ext"), table) });
            var removed = Customizations.Parse("{\"environment.keyboardShortcuts.customizations\":[{\"command\":\"Tools.FixtureFirst\",\"shortcut\":\"Ctrl+Shift+F1\",\"scope\":\"Global\",\"isRemoval\":true}]}");
            var sheet = CheatSheetBuilder.Build(new[] { Vs("Build.BuildSolution", "Global", "Ctrl+Shift+F1", 0x20) }, origins, removed, "Test", Now);
            Assert.IsFalse(sheet.Shortcuts.Any(s => s.NotApplied));
        }

        [TestMethod]
        public void Two_commands_on_one_key_are_listed_without_a_mark()
        {
            // VS 2026 does not let you make this; the old and new merge editor do have it. Nothing to do about it.
            var sheet = CheatSheetBuilder.Build(new[]
            {
                Vs("Merge.TakeLeft", "Merge Editor Window", "F10", 1),
                Vs("Merge.NextConflict", "Merge Editor Window", "F10", 2),
                Vs("Edit.LineDown", "Text Editor", "Ctrl+E, V", 5),
                Vs("Edit.LineDown", "Text Editor", "Ctrl+E,V", 5), // the same binding written differently
            }, null, null, "Test", Now);

            Assert.AreEqual(2, sheet.Shortcuts.Count(s => s.Keys == "F10"));
            Assert.IsFalse(sheet.Shortcuts.Any(s => s.NotApplied));
            Assert.AreEqual(1, sheet.Shortcuts.Count(s => s.Command == "Edit.LineDown"), "duplicate removed");
        }

        [TestMethod]
        public void Uses_the_extension_as_category_when_the_name_has_none()
        {
            var origins = ExtensionCommands.Build(new[] { new ExtensionSource(BuildOutput(), null, false) });
            var unnamedPrefix = new RawBinding("ShortcutCheatsheet", "", ShortcutCheatsheetPackage.CommandSet, (uint)ShortcutCheatsheetPackage.OpenWindowCommandId, "Global", "Ctrl+K, Ctrl+K");
            var sheet = CheatSheetBuilder.Build(new[] { unnamedPrefix }, origins, null, "Test", Now);
            Assert.AreEqual("Shortcut Cheatsheet", sheet.Shortcuts.Single().Category);
        }

        [TestMethod]
        public void A_system_component_counts_as_Visual_Studio()
        {
            // Container Tools and the like sit in Common7\IDE\Extensions but are installed with a workload.
            string folder = Path.Combine(Path.GetTempPath(), "ShortcutCheatsheet.Tests." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                foreach (string file in Directory.GetFiles(BuildOutput()).Where(f => !f.EndsWith(".vsix", StringComparison.OrdinalIgnoreCase)))
                    File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
                string manifest = Path.Combine(folder, "extension.vsixmanifest");
                string xml = File.ReadAllText(manifest);
                File.WriteAllText(manifest, System.Text.RegularExpressions.Regex.Replace(xml, @"<Installation(\s|>)", "<Installation SystemComponent=\"true\"$1"));

                var origins = ExtensionCommands.Build(new[] { new ExtensionSource(folder, null, false) });
                Assert.AreEqual(0, origins.Extensions.Count);
                Assert.IsFalse(origins.TryFind(new CommandTables.CommandId(ShortcutCheatsheetPackage.CommandSet, (uint)ShortcutCheatsheetPackage.OpenWindowCommandId), out _, out _));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
        [TestMethod]
        public void Sorts_global_first_and_removes_duplicates()
        {
            var sheet = CheatSheetBuilder.Build(new[]
            {
                Vs("Edit.Duplicate", "Text Editor", "Ctrl+E, V", 4),
                Vs("File.SaveAll", "Global", "Ctrl+Shift+S", 5),
                Vs("Build.BuildSolution", "Global", "Ctrl+Shift+B", 6),
                Vs("File.SaveAll", "Global", "Ctrl+Shift+S", 5),
            }, null, null, "Test", Now);

            CollectionAssert.AreEqual(new[] { "Build.BuildSolution", "File.SaveAll", "Edit.Duplicate" }, sheet.Shortcuts.Select(s => s.Command).ToArray());
        }

        [TestMethod]
        public void Marks_your_own_shortcuts()
        {
            var custom = Customizations.Parse("{\"environment.keyboardShortcuts.customizations\":[{\"command\":\"Edit.Duplicate\",\"shortcut\":\"Ctrl+Shift+D\",\"scope\":\"Text Editor\",\"isRemoval\":false}]}");
            var sheet = CheatSheetBuilder.Build(new[] { Vs("Edit.Duplicate", "Text Editor", "Ctrl+Shift+D"), Vs("Edit.Duplicate", "Text Editor", "Ctrl+E, V") }, null, custom, "Test", Now);
            Assert.IsTrue(sheet.Shortcuts.Single(s => s.Keys == "Ctrl+Shift+D").IsCustom);
            Assert.IsFalse(sheet.Shortcuts.Single(s => s.Keys == "Ctrl+E, V").IsCustom);
        }

        [TestMethod]
        public void Exports_json_that_reads_back()
        {
            var origins = ExtensionCommands.Build(new[] { new ExtensionSource(BuildOutput(), null, false) });
            var sheet = CheatSheetBuilder.Build(new[]
            {
                new RawBinding("View.ShortcutCheatsheet", "", ShortcutCheatsheetPackage.CommandSet, (uint)ShortcutCheatsheetPackage.OpenWindowCommandId, "Global", "Ctrl+K, Ctrl+K"),
                Vs("Edit.Duplicate", "Text Editor", "Ctrl+E, V"),
            }, origins, null, "Community 18.0", Now);

            string json = JsonExporter.ToText(sheet);
            StringAssert.Contains(json, "\"extension\": \"Shortcut Cheatsheet\"");
            StringAssert.Contains(json, "\"generated\": \"2026-11-13T14:00:00+01:00\"");
            Assert.AreEqual(1, json.Split(new[] { "\"extension\":" }, StringSplitOptions.None).Length - 1, "VS shortcuts leave the field out");
            Assert.IsFalse(json.Contains("\"custom\""), "false is left out");

            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var back = (CheatSheet)new DataContractJsonSerializer(typeof(CheatSheet)).ReadObject(stream);
                Assert.AreEqual(2, back.Shortcuts.Count);
                Assert.AreEqual(ShortcutCheatsheetPackage.CommandSet, back.Shortcuts.First().CommandGuid);
            }
        }

        /// <summary>The newest Builds\ShortcutCheatsheet\&lt;configuration&gt; folder with a pkgdef.</summary>
        private static string BuildOutput()
        {
            string builds = Path.Combine(TestPaths.RepositoryRoot, "Builds", "ShortcutCheatsheet");
            return Directory.GetFiles(builds, "ShortcutCheatsheet.pkgdef", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Select(Path.GetDirectoryName)
                .First();
        }
    }
}
