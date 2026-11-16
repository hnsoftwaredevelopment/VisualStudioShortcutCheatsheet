using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ShortcutCheatsheet.Localization;

namespace ShortcutCheatsheet.Export
{
    internal enum ExportFormat
    {
        Pdf,
        Png,
        Html,
        Markdown,
        Csv,
        Json,
        Xml,
    }

    /// <summary>Picks the writer for a format and writes the file(s).</summary>
    internal static class Exporter
    {
        /// <summary>
        /// In the order of the Save dialog: the formats to read and print first, the data formats last. Name is a
        /// resource name for the formats whose name is a description ("Web page"), otherwise the name itself.
        /// </summary>
        public static readonly IReadOnlyList<(ExportFormat Format, string Name, string Extension)> Formats = new[]
        {
            (ExportFormat.Pdf, "PDF", ".pdf"),
            (ExportFormat.Png, Strings.FormatPng, ".png"),
            (ExportFormat.Html, Strings.FormatHtml, ".html"),
            (ExportFormat.Markdown, "Markdown", ".md"),
            (ExportFormat.Csv, Strings.FormatCsv, ".csv"),
            (ExportFormat.Json, "JSON", ".json"),
            (ExportFormat.Xml, "XML", ".xml"),
        };

        /// <summary>"PDF (*.pdf)|*.pdf|PNG image (*.png)|*.png|..." for a file dialog, in the current language.</summary>
        public static string DialogFilter => string.Join("|", Formats.Select(f => Localizer.Get(f.Name) + " (*" + f.Extension + ")|*" + f.Extension));

        /// <summary>The format for a file name, by its extension; null when it is not one we write.</summary>
        public static ExportFormat? FromPath(string path)
        {
            string extension = Path.GetExtension(path ?? string.Empty);
            if (string.Equals(extension, ".htm", StringComparison.OrdinalIgnoreCase))
                return ExportFormat.Html;
            if (string.Equals(extension, ".markdown", StringComparison.OrdinalIgnoreCase))
                return ExportFormat.Markdown;
            foreach (var f in Formats)
                if (string.Equals(f.Extension, extension, StringComparison.OrdinalIgnoreCase))
                    return f.Format;
            return null;
        }

        /// <summary>Writes the document and returns the files written (PNG may write one per page). PDF and PNG need an STA thread.</summary>
        public static IReadOnlyList<string> Write(ExportDocument document, ExportFormat format, string path, RenderOptions options)
        {
            if (format == ExportFormat.Png)
                return PngExporter.Write(document, options, path);

            using (var file = File.Create(path))
            {
                switch (format)
                {
                    case ExportFormat.Pdf: PdfExporter.Write(document, options, file); break;
                    case ExportFormat.Html: HtmlExporter.Write(document, file); break;
                    case ExportFormat.Markdown: MarkdownExporter.Write(document, file); break;
                    case ExportFormat.Csv: CsvExporter.Write(document, file); break;
                    case ExportFormat.Json: JsonExporter.Write(document.Sheet, file); break;
                    case ExportFormat.Xml: XmlExporter.Write(document, file); break;
                    default: throw new ArgumentOutOfRangeException(nameof(format));
                }
            }
            return new[] { path };
        }
    }
}
