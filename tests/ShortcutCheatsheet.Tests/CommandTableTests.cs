using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShortcutCheatsheet.CommandTables;

namespace ShortcutCheatsheet.Tests
{
    [TestClass]
    public class CommandTableTests
    {
        private static readonly XNamespace Vsct = "http://schemas.microsoft.com/VisualStudio/2005-10-18/CommandTable";
        private static readonly Guid FixtureSet = new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        private static readonly Guid OtherSet = new Guid("12345678-9abc-def0-1234-56789abcdef0");
        private static readonly Guid TextEditor = new Guid("8B382828-6202-11d1-8870-0000F87579D2");
        private static readonly Guid VsStd97 = CommandTable.GlobalScope;

        private static CommandTable ReadFixture() =>
            CommandTableReader.Read(File.ReadAllBytes(TestPaths.Fixture(@"CommandTables\Fixture.cto")));

        [TestMethod]
        public void Reads_the_commands_the_table_defines()
        {
            var table = ReadFixture();

            CollectionAssert.AreEquivalent(
                new[] { "First Command", "Second Command", "Without Keys", "Other Set Command" },
                table.Commands.Select(c => c.ButtonText).ToArray());
            var first = table.Commands.Single(c => c.Id.Equals(new CommandId(FixtureSet, 0x100)));
            Assert.AreEqual("cmdidFirst", first.SymbolName);
            Assert.AreEqual(".Tools.FixtureFirst", first.CanonicalName);
            Assert.AreEqual("", table.Commands.Single(c => c.ButtonText == "Second Command").CanonicalName);
            Assert.IsTrue(table.Defines(new CommandId(OtherSet, 0x200)));
        }

        [TestMethod]
        public void A_command_that_is_only_placed_is_not_defined()
        {
            // The fixture puts Cut (guidVSStd97, 0x10) in its group and gives it a key binding: still a VS command.
            var table = ReadFixture();
            Assert.IsFalse(table.Defines(new CommandId(VsStd97, 0x10)));
            Assert.IsTrue(table.KeyBindings.Any(k => k.Command.Equals(new CommandId(VsStd97, 0x10))));
        }

        [TestMethod]
        public void Reads_key_bindings_with_scope_and_chords()
        {
            var keys = ReadFixture().KeyBindings;
            Assert.AreEqual(4, keys.Count);

            var first = keys.Single(k => k.Command.Equals(new CommandId(FixtureSet, 0x100)));
            Assert.AreEqual(VsStd97, first.Editor);
            Assert.AreEqual(new KeyStroke(0x70, KeyModifiers.Control | KeyModifiers.Shift), first.First); // Ctrl+Shift+F1
            Assert.IsTrue(first.Second.IsEmpty);

            var chord = keys.Single(k => k.Command.Equals(new CommandId(FixtureSet, 0x101)));
            Assert.AreEqual(TextEditor, chord.Editor);
            Assert.AreEqual(new KeyStroke((byte)'K', KeyModifiers.Control), chord.First);
            Assert.AreEqual(new KeyStroke((byte)'Q', KeyModifiers.Control), chord.Second);

            var alt = keys.Single(k => k.Command.Equals(new CommandId(OtherSet, 0x200)));
            Assert.AreEqual(new KeyStroke((byte)'D', KeyModifiers.Alt), alt.First);
        }

        [TestMethod]
        public void Matches_what_VSCT_decompiles()
        {
            // VSCT.exe turns the .cto back into XML (tools\build-fixtures.ps1): that is what VS itself reads.
            foreach (string cto in Directory.GetFiles(TestPaths.Fixture("CommandTables"), "*.cto"))
            {
                var table = CommandTableReader.Read(File.ReadAllBytes(cto));
                var xml = XDocument.Load(Path.ChangeExtension(cto, ".decompiled.vsct"));
                var guids = xml.Descendants(Vsct + "GuidSymbol").ToDictionary(e => (string)e.Attribute("name"), e => new Guid((string)e.Attribute("value")));
                var ids = xml.Descendants(Vsct + "IDSymbol").ToDictionary(
                    e => (string)e.Parent.Attribute("name") + "/" + (string)e.Attribute("name"),
                    e => Convert.ToUInt32((string)e.Attribute("value"), 16));
                CommandId Id(XElement e) => new CommandId(guids[(string)e.Attribute("guid")], ids[(string)e.Attribute("guid") + "/" + (string)e.Attribute("id")]);

                var buttons = xml.Descendants(Vsct + "Button").Select(Id).ToList();
                CollectionAssert.AreEquivalent(buttons, table.Commands.Select(c => c.Id).ToList(), Path.GetFileName(cto));

                var expected = xml.Descendants(Vsct + "KeyBinding").Select(e => Describe(Id(e), guids[(string)e.Attribute("editor")],
                    Stroke((string)e.Attribute("key1"), (string)e.Attribute("mod1")),
                    Stroke((string)e.Attribute("key2"), (string)e.Attribute("mod2")))).ToList();
                var actual = table.KeyBindings.Select(k => Describe(k.Command, k.Editor, k.First, k.Second)).ToList();
                CollectionAssert.AreEquivalent(expected, actual, Path.GetFileName(cto));
            }
        }

        [TestMethod]
        public void Reads_the_table_of_this_extension_from_its_dll()
        {
            // End to end: PEReader, the embedded VSPackage.resources and the compressed table.
            string dll = typeof(ShortcutCheatsheetPackage).Assembly.Location;
            byte[] bytes = CommandTableResource.FindInPackage(dll);
            Assert.IsNotNull(bytes);

            var table = CommandTableReader.Read(bytes);
            var command = table.Commands.Single(c => c.Id.Equals(new CommandId(ShortcutCheatsheetPackage.CommandSet, (uint)ShortcutCheatsheetPackage.OpenWindowCommandId)));
            Assert.AreEqual("Shortcut Cheatsheet", command.ButtonText);
            Assert.AreEqual(".View.ShortcutCheatsheet", command.CanonicalName);
        }

        [TestMethod]
        public void Reads_CodeMaid_from_its_satellite_when_installed()
        {
            // A real third-party extension whose table is in en-US\...resources.dll. Skipped on machines without it.
            string extensions = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\VisualStudio");
            string dll = Directory.Exists(extensions)
                ? Directory.EnumerateDirectories(extensions, "18.0_*").Where(d => !d.EndsWith("Exp", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(d => SafeFiles(Path.Combine(d, "Extensions"), "SteveCadwallader.CodeMaid*.dll"))
                    .FirstOrDefault(f => !f.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
                : null;
            if (dll == null)
                Assert.Inconclusive("CodeMaid is not installed.");

            var table = CommandTableReader.Read(CommandTableResource.FindInPackage(dll));
            Assert.IsTrue(table.KeyBindings.Count >= 20, $"{table.KeyBindings.Count} key bindings");
            var codeMaidSet = table.KeyBindings.First().Command.Guid;
            Assert.IsTrue(table.Commands.All(c => c.Id.Guid == codeMaidSet), "CodeMaid places VS commands, but defines only its own");
            Assert.IsTrue(table.Commands.Any(c => c.ButtonText.Contains("Cleanup Active Document")));
        }

        [TestMethod]
        public void Rejects_data_that_is_not_a_command_table()
        {
            Assert.ThrowsExactly<FormatException>(() => CommandTableReader.Read(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }));
            var cto = File.ReadAllBytes(TestPaths.Fixture(@"CommandTables\Fixture.cto"));
            Assert.ThrowsExactly<FormatException>(() => CommandTableReader.Read(cto.Take(cto.Length / 2).ToArray()));
            var otherVersion = (byte[])cto.Clone();
            otherVersion[4] = 6;
            Assert.ThrowsExactly<FormatException>(() => CommandTableReader.Read(otherVersion));
        }

        [TestMethod]
        public void A_dll_without_a_table_gives_null()
        {
            Assert.IsNull(CommandTableResource.FindInPackage(typeof(CommandTableTests).Assembly.Location));
            Assert.IsNull(CommandTableResource.FindInAssembly(TestPaths.Fixture(@"CommandTables\Fixture.vsct")));
        }

        [TestMethod]
        public void Reads_the_menu_resources_of_a_pkgdef()
        {
            const string pkgdef = "[$RootKey$\\Packages\\{acf2e531-e047-4046-9f60-a0765ab33ef5}]\r\n@=\"x\"\r\n" +
                "[$RootKey$\\Menus]\r\n\"{4c82e17d-927e-42d2-8460-b473ac7df316}\"=\", Menus.ctmenu, 1\"\r\n" +
                "\"{634d2762-4905-4843-b74c-649d410524f9}\"=\"Other.dll, 1000, 1\"\r\n" +
                "[$RootKey$\\ToolWindows\\{68b99b08-124c-4da3-ace2-dcd8c36dbd5a}]\r\n\"{00000000-0000-0000-0000-000000000001}\"=\", NotAMenu, 1\"\r\n";
            var menus = PkgdefMenus.Parse(pkgdef);
            Assert.AreEqual(2, menus.Count);
            Assert.AreEqual(new Guid("4c82e17d-927e-42d2-8460-b473ac7df316"), menus[0].Package);
            Assert.AreEqual("Menus.ctmenu", menus[0].ResourceName);
            Assert.AreEqual("", menus[0].Satellite);
            Assert.AreEqual("1000", menus[1].ResourceName);
            Assert.AreEqual("Other.dll", menus[1].Satellite);
        }

        private static IEnumerable<string> SafeFiles(string folder, string pattern)
        {
            try { return Directory.Exists(folder) ? Directory.GetFiles(folder, pattern, SearchOption.AllDirectories) : Array.Empty<string>(); }
            catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
        }

        private static string Describe(CommandId command, Guid editor, KeyStroke first, KeyStroke second) =>
            $"{command} {editor} {first.Key:X2}/{(int)first.Modifiers} {second.Key:X2}/{(int)second.Modifiers}";

        /// <summary>A key as VSCT writes it: 'K' for a character, VK_F1 for a virtual key; modifiers as "Control Shift".</summary>
        private static KeyStroke Stroke(string key, string modifiers)
        {
            if (string.IsNullOrEmpty(key))
                return default;
            byte code = key.StartsWith("'") ? (byte)key[1] : VirtualKeys[key];
            var mods = KeyModifiers.None;
            foreach (string m in (modifiers ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                mods |= (KeyModifiers)Enum.Parse(typeof(KeyModifiers), m == "Windows" ? "Windows" : m);
            return new KeyStroke(code, mods);
        }

        private static readonly Dictionary<string, byte> VirtualKeys = new Dictionary<string, byte>
        {
            ["VK_F1"] = 0x70, ["VK_F2"] = 0x71, ["VK_F3"] = 0x72, ["VK_F12"] = 0x7B,
            ["VK_SPACE"] = 0x20, ["VK_OEM_MINUS"] = 0xBD, ["VK_OEM_PERIOD"] = 0xBE, ["VK_OEM_2"] = 0xBF,
        };
    }
}
