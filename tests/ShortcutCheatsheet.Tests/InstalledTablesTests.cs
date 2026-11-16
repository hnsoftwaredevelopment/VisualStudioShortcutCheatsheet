using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShortcutCheatsheet.CommandTables;

namespace ShortcutCheatsheet.Tests
{
    /// <summary>
    /// Runs the reader over the command tables of every extension installed on this machine (VS 2026, per user).
    /// Not a fixed expectation: it catches formats the fixtures do not cover. Skipped where nothing is installed.
    /// </summary>
    [TestClass]
    public class InstalledTablesTests
    {
        public TestContext TestContext { get; set; }

        [TestMethod]
        public void Every_installed_command_table_can_be_read()
        {
            string vs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\VisualStudio");
            var pkgdefs = Directory.Exists(vs)
                ? Directory.EnumerateDirectories(vs, "18.0_*")
                    .Select(d => Path.Combine(d, "Extensions"))
                    .Where(Directory.Exists)
                    .SelectMany(d => Directory.GetFiles(d, "*.pkgdef", SearchOption.AllDirectories))
                    .Where(f => Regex.IsMatch(File.ReadAllText(f), @"\\Menus\]", RegexOptions.IgnoreCase))
                    .ToList()
                : new List<string>();
            if (pkgdefs.Count == 0)
                Assert.Inconclusive("No VS 2026 extensions with menus installed.");

            var watch = Stopwatch.StartNew();
            int read = 0, keys = 0;
            var failures = new List<string>();
            foreach (string pkgdef in pkgdefs)
            {
                string dll = Path.ChangeExtension(pkgdef, ".dll");
                if (!File.Exists(dll))
                    continue; // native package or a different file name; handled when the pkgdef is read properly
                var menus = PkgdefMenus.Parse(File.ReadAllText(pkgdef));
                byte[] bytes = menus.Count == 0 ? null : CommandTableResource.FindInPackage(dll, menus[0].ResourceName);
                if (bytes == null)
                {
                    failures.Add("no table: " + pkgdef);
                    continue;
                }
                try
                {
                    var table = CommandTableReader.Read(bytes);
                    read++;
                    keys += table.KeyBindings.Count;
                    TestContext.WriteLine($"{Path.GetFileName(pkgdef),-50} commands={table.Commands.Count,4} keys={table.KeyBindings.Count,3}");
                }
                catch (FormatException ex)
                {
                    failures.Add($"{pkgdef}: {ex.Message}");
                }
            }
            TestContext.WriteLine($"read={read} keys={keys} failures={failures.Count} time={watch.ElapsedMilliseconds} ms");
            Assert.AreEqual(0, failures.Count, string.Join(Environment.NewLine, failures));
        }

        [TestMethod]
        public void Visual_Studio_names_commands_that_DTE_does_not()
        {
            // The merge editor's "Take Incoming" has no canonical name in DTE; VS's own command table has its text.
            string vs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft Visual Studio\18");
            string ide = Directory.Exists(vs)
                ? Directory.EnumerateDirectories(vs).SelectMany(e => new[] { Path.Combine(e, @"Common7\IDE") }.Concat(Directory.EnumerateDirectories(e).Select(c => Path.Combine(c, @"Common7\IDE")))).FirstOrDefault(Directory.Exists)
                : null;
            if (ide == null)
                Assert.Inconclusive("Visual Studio 2026 is not installed.");

            var watch = Stopwatch.StartNew();
            var origins = ShortcutCheatsheet.Origin.ExtensionCommands.Build(Enumerable.Empty<ExtensionCommon.VisualStudio.ExtensionSource>()).WithVisualStudioNames(ide);
            TestContext.WriteLine($"{watch.ElapsedMilliseconds} ms");
            Assert.IsTrue(origins.TryFindVisualStudio(new CommandId(new Guid("a0a3cdb6-6b05-493f-af52-bb8975ab2055"), 0x0508), out var takeIncoming));
            Assert.AreEqual("Take Incoming", takeIncoming.ButtonText);

            watch.Restart();
            origins.WithVisualStudioNames(ide);
            Assert.IsTrue(watch.ElapsedMilliseconds < 100, "the second time comes from the cache");
        }
    }
}
