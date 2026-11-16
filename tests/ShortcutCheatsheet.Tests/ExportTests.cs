using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShortcutCheatsheet.Export;
using ShortcutCheatsheet.Model;

namespace ShortcutCheatsheet.Tests
{
    [TestClass]
    public class ExportTests
    {
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);

        private static CheatSheet Sheet() => new CheatSheet
        {
            Generated = "2026-11-16T10:00:00Z",
            VisualStudio = "Community 18.0",
            Shortcuts =
            {
                new Shortcut { Scope = "Global", Keys = "Ctrl+Shift+B", Name = "Build Solution", Category = "Build", Command = "Build.BuildSolution" },
                new Shortcut { Scope = "Text Editor", Keys = "Ctrl+E, V", Name = "Duplicate", Category = "Edit", Command = "Edit.Duplicate" },
                new Shortcut { Scope = "Text Editor", Keys = "Ctrl+,", Name = "Go To All", Category = "Edit", Command = "Edit.GoToAll" },
                new Shortcut { Scope = "Global", Keys = "Alt+Up Arrow", Name = "Move | Up <fast>", Category = "Edit", IsCustom = true },
                new Shortcut { Scope = "Global", Keys = "Ctrl+Alt+M", Name = "Toggle Bookmark", Category = "Bookmark Studio", Extension = "Bookmark Studio" },
                new Shortcut
                {
                    Scope = "Global", Keys = "Ctrl+Shift+B", Name = "Bookmarks Window", Category = "Bookmark Studio", Extension = "Bookmark Studio",
                    NotApplied = true, UsedBy = new List<string> { "Build Solution" },
                },
            },
        };

        private static ExportDocument Document(ShortcutFilter filter = null, Grouping grouping = Grouping.Scope) =>
            ExportDocument.Create(Sheet(), filter ?? new ShortcutFilter(), grouping, null, Now);

        [TestMethod]
        public void Document_FollowsFilterAndGrouping()
        {
            var all = Document();
            CollectionAssert.AreEqual(new[] { "Global", "Text Editor" }, all.Groups.Select(g => g.Name).ToArray(), "Global first");
            Assert.AreEqual(6, all.Shortcuts.Count());
            Assert.AreEqual(6, all.Sheet.Shortcuts.Count);
            StringAssert.StartsWith(all.Subtitle, "Visual Studio Community 18.0 \u00B7 ");

            var filtered = ExportDocument.Create(Sheet(), new ShortcutFilter { OnlyExtensions = true }, Grouping.Extension, "only extensions", Now);
            Assert.AreEqual(2, filtered.Sheet.Shortcuts.Count);
            Assert.AreEqual("Bookmark Studio", filtered.Groups.Single().Name);
            StringAssert.EndsWith(filtered.Subtitle, "\u00B7 only extensions");

            var flat = Document(grouping: Grouping.Keys);
            Assert.IsNull(flat.Groups.Single().Name);
            Assert.IsNull(flat.Groups.Single().Subgroups.Single().Name);
        }

        [TestMethod]
        public void Csv_QuotesWhatNeedsIt()
        {
            string csv = CsvExporter.ToText(Document(grouping: Grouping.Keys), ",");
            var lines = csv.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual(7, lines.Length);
            Assert.AreEqual(string.Join(",", CsvExporter.Columns), lines[0]);
            Assert.IsTrue(lines.Any(l => l.StartsWith("Text Editor,\"Ctrl+,\",Go To All,")), "a comma in the keys is quoted");
            Assert.IsTrue(lines.Any(l => l.Contains(",yes,Build Solution")), "not applied, with what uses the keys");

            // With a semicolon (Dutch Excel) \"Ctrl+,\" needs no quotes.
            StringAssert.Contains(CsvExporter.ToText(Document(), ";"), "Text Editor;Ctrl+,;Go To All");
        }

        [TestMethod]
        public void Csv_HasBomForExcel()
        {
            using (var stream = new MemoryStream())
            {
                CsvExporter.Write(Document(), stream);
                CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }, stream.ToArray().Take(3).ToArray());
            }
        }

        [TestMethod]
        public void Markdown_KeysAsCodeAndEscaped()
        {
            string md = MarkdownExporter.ToText(Document());
            StringAssert.StartsWith(md, "# Visual Studio shortcuts");
            StringAssert.Contains(md, "## Global");
            StringAssert.Contains(md, "### Build");
            StringAssert.Contains(md, "| `Ctrl+E, V` | Duplicate |");
            Assert.IsFalse(md.Contains("<kbd>"), "Obsidian shows <kbd> at the start of a cell as text");
            Assert.AreEqual("`` Ctrl+` ``", MarkdownExporter.Keys("Ctrl+`"));
            Assert.AreEqual("`Ctrl+\\|`", MarkdownExporter.Keys("Ctrl+|"));
            StringAssert.Contains(md, "| `Alt+↑` |");
            StringAssert.Contains(md, "Move \\| Up \\<fast> | yours |");
            StringAssert.Contains(md, "not applied, the keys are used by Build Solution");
            // Every table row has the same number of cells.
            foreach (string row in md.Split('\n').Where(l => l.StartsWith("| ")))
                Assert.AreEqual(4, Regex.Matches(row, @"(?<!\\)\|").Count, row);
        }

        [TestMethod]
        public void Html_IsSelfContainedAndEncoded()
        {
            string html = HtmlExporter.ToText(Document());
            StringAssert.StartsWith(html, "<!DOCTYPE html>");
            StringAssert.Contains(html, "<kbd>Ctrl</kbd><span class=\"plus\">+</span><kbd>,</kbd>");
            StringAssert.Contains(html, "Move | Up &lt;fast&gt;");
            StringAssert.Contains(html, "<tr class=\"na\"");
            StringAssert.Contains(html, "ctrl+e,v", "keys without spaces for the search");
            Assert.IsFalse(Regex.IsMatch(html, @"(src|href)\s*=\s*""https?:"), "nothing loaded from elsewhere");
            Assert.AreEqual(6, Regex.Matches(html, "<tr ").Count);
        }

        [TestMethod]
        public void JsonAndXml_HoldTheFilteredSheet()
        {
            var document = ExportDocument.Create(Sheet(), new ShortcutFilter { Text = "duplicate" }, Grouping.Scope, null, Now);

            string xml = XmlExporter.ToText(document);
            var root = XDocument.Parse(xml).Root;
            XNamespace ns = CheatSheet.XmlNamespace;
            Assert.AreEqual("cheatSheet", root.Name.LocalName);
            Assert.AreEqual("Edit.Duplicate", root.Descendants(ns + "command").Single().Value);

            using (var stream = new MemoryStream())
            {
                JsonExporter.Write(document.Sheet, stream);
                stream.Position = 0;
                var back = (CheatSheet)new DataContractJsonSerializer(typeof(CheatSheet)).ReadObject(stream);
                Assert.AreEqual("Duplicate", back.Shortcuts.Single().Name);
            }
        }

        [TestMethod]
        public void Exporter_FormatFromExtension()
        {
            Assert.AreEqual(ExportFormat.Pdf, Exporter.FromPath(@"C:\x\Shortcuts.PDF"));
            Assert.AreEqual(ExportFormat.Html, Exporter.FromPath("a.htm"));
            Assert.AreEqual(ExportFormat.Markdown, Exporter.FromPath("a.md"));
            Assert.IsNull(Exporter.FromPath("a.txt"));
            Assert.AreEqual(Exporter.Formats.Count * 2 - 1, Exporter.DialogFilter.Count(c => c == '|'));
        }

        [TestMethod]
        public void Png_PagePaths()
        {
            CollectionAssert.AreEqual(new[] { @"C:\x\a.png" }, PngExporter.PagePaths(@"C:\x\a.png", 1).ToArray());
            CollectionAssert.AreEqual(new[] { @"C:\x\a-1.png", @"C:\x\a-2.png" }, PngExporter.PagePaths(@"C:\x\a.png", 2).ToArray());
        }

        [TestMethod]
        public void Zlib_RoundTrips()
        {
            byte[] data = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("Shortcut Cheatsheet ", 500)));
            byte[] packed = PdfWriter.Zlib(data);
            Assert.AreEqual(0x78, packed[0]);
            Assert.AreEqual(0, ((packed[0] << 8) | packed[1]) % 31, "zlib header check");
            using (var raw = new DeflateStream(new MemoryStream(packed, 2, packed.Length - 6), CompressionMode.Decompress))
            using (var result = new MemoryStream())
            {
                raw.CopyTo(result);
                CollectionAssert.AreEqual(data, result.ToArray());
            }
            // Adler-32 of the data, big-endian, at the end.
            uint a = 1, b = 0;
            foreach (byte d in data) { a = (a + d) % 65521; b = (b + a) % 65521; }
            uint adler = (b << 16) | a;
            CollectionAssert.AreEqual(new[] { (byte)(adler >> 24), (byte)(adler >> 16), (byte)(adler >> 8), (byte)adler }, packed.Skip(packed.Length - 4).ToArray());
        }

        [TestMethod]
        public void Pdf_IsWellFormed()
        {
            byte[] pdf = null;
            Sta(() =>
            {
                using (var stream = new MemoryStream())
                {
                    PdfExporter.Write(Document(), new RenderOptions { Scale = 1 }, stream);
                    pdf = stream.ToArray();
                }
            });

            string text = Encoding.GetEncoding("ISO-8859-1").GetString(pdf);
            StringAssert.StartsWith(text, "%PDF-1.4");
            StringAssert.EndsWith(text, "%%EOF\n");
            StringAssert.Contains(text, "/MediaBox [0 0 842 595]");

            // startxref points at the table, and every entry at its object.
            int startxref = int.Parse(Regex.Match(text, @"startxref\n(\d+)").Groups[1].Value);
            Assert.AreEqual("xref", text.Substring(startxref, 4));
            var entries = Regex.Matches(text.Substring(startxref), @"(\d{10}) 00000 n ");
            Assert.IsTrue(entries.Count >= 6);
            for (int i = 0; i < entries.Count; i++)
                Assert.AreEqual((i + 1) + " 0 obj", text.Substring(int.Parse(entries[i].Groups[1].Value), (i + 1).ToString().Length + 6));

            // Each stream's /Length matches its data.
            foreach (Match m in Regex.Matches(text, @"/Length (\d+) >>\nstream\n"))
            {
                int start = m.Index + m.Length, length = int.Parse(m.Groups[1].Value);
                Assert.AreEqual("\nendstream", text.Substring(start + length, 10));
            }
        }

        [TestMethod]
        public void Render_SkipsNotAppliedAndFillsPages()
        {
            Sta(() =>
            {
                var options = new RenderOptions { Scale = 1 };
                var pages = SheetRenderer.RenderBitmaps(Document(), options);
                Assert.AreEqual(1, pages.Count);
                Assert.AreEqual(1123, pages[0].PixelWidth);
                Assert.AreEqual(794, pages[0].PixelHeight);
                Assert.IsTrue(PdfWriter.ToRgb(pages[0]).Any(v => v < 128), "something dark was drawn on the white page");

                // Many rows: more pages, never more than needed.
                var big = new CheatSheet { VisualStudio = "x" };
                for (int i = 0; i < 400; i++)
                    big.Shortcuts.Add(new Shortcut { Scope = "Global", Keys = "Ctrl+K, Ctrl+" + (char)('A' + i % 26), Name = "Command " + i, Category = "Cat " + (i / 20) });
                var document = ExportDocument.Create(big, new ShortcutFilter(), Grouping.Scope, null, Now);
                int count = SheetRenderer.Render(document, options).Count;
                Assert.IsTrue(count > 1 && count < 10, count.ToString());
            });
        }

        /// <summary>
        /// Writes every format from tools\Shortcuts.json (an export of a real VS) to obj\export-samples, to look at.
        /// Skipped when the file is not there; it is not in the repo.
        /// </summary>
        [TestMethod]
        public void Samples_FromRealExport()
        {
            string source = Path.Combine(TestPaths.RepositoryRoot, "tools", "Shortcuts.json");
            if (!File.Exists(source))
                Assert.Inconclusive("tools\\Shortcuts.json is not there.");
            CheatSheet sheet;
            using (var file = File.OpenRead(source))
                sheet = (CheatSheet)new DataContractJsonSerializer(typeof(CheatSheet)).ReadObject(file);

            string folder = Path.Combine(TestPaths.RepositoryRoot, "obj", "export-samples");
            Directory.CreateDirectory(folder);
            Sta(() =>
            {
                var document = ExportDocument.Create(sheet, new ShortcutFilter(), Grouping.Scope, null, Now);
                foreach (var f in Exporter.Formats)
                    Exporter.Write(document, f.Format, Path.Combine(folder, "Shortcuts" + f.Extension), new RenderOptions());
                var extensions = ExportDocument.Create(sheet, new ShortcutFilter { OnlyExtensions = true }, Grouping.Extension, "only extensions", Now);
                Exporter.Write(extensions, ExportFormat.Png, Path.Combine(folder, "Extensions.png"), new RenderOptions());
                Exporter.Write(extensions, ExportFormat.Png, Path.Combine(folder, "Extensions-dark.png"), new RenderOptions { Dark = true });
            });
        }

        /// <summary>WPF drawing needs a single-threaded apartment; the test runner's threads are MTA.</summary>
        private static void Sta(Action action)
        {
            Exception error = null;
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error != null)
                throw new AssertFailedException(error.Message, error);
        }
    }
}
