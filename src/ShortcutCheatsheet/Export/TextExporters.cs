using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using ShortcutCheatsheet.Localization;
using ShortcutCheatsheet.Model;

namespace ShortcutCheatsheet.Export
{
    /// <summary>
    /// One row per shortcut, for Excel. Excel only reads UTF-8 with a BOM and expects the list separator of the culture.
    /// Column names and values stay English in every language, so formulas and scripts always see the same names.
    /// </summary>
    internal static class CsvExporter
    {
        public static readonly string[] Columns = { "Scope", "Keys", "Name", "Category", "Command", "Extension", "Custom", "Not applied", "Used by" };

        public static string ToText(ExportDocument document, string separator)
        {
            var text = new StringBuilder();
            text.AppendLine(string.Join(separator, Columns.Select(c => Field(c, separator))));
            foreach (var s in document.Sheet.Shortcuts)
            {
                string[] row =
                {
                    s.Scope, s.Keys, s.Name, s.Category, s.Command, s.Extension,
                    s.IsCustom ? "yes" : "", s.NotApplied ? "yes" : "", s.UsedBy == null ? "" : string.Join("; ", s.UsedBy),
                };
                text.AppendLine(string.Join(separator, row.Select(f => Field(f, separator))));
            }
            return text.ToString();
        }

        public static void Write(ExportDocument document, Stream output) =>
            WriteText(output, ToText(document, CultureInfo.CurrentCulture.TextInfo.ListSeparator), withBom: true);

        /// <summary>Quotes a field when it holds the separator, a quote or a line break ("Ctrl+," needs it with a comma).</summary>
        private static string Field(string value, string separator)
        {
            value = value ?? string.Empty;
            if (value.Contains(separator) || value.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0)
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        internal static void WriteText(Stream output, string text, bool withBom)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(text);
            if (withBom)
                output.Write(new byte[] { 0xEF, 0xBB, 0xBF }, 0, 3);
            output.Write(bytes, 0, bytes.Length);
        }
    }

    /// <summary>The same data as the JSON export, as XML.</summary>
    internal static class XmlExporter
    {
        public static void Write(ExportDocument document, Stream output)
        {
            var settings = new XmlWriterSettings { Indent = true, IndentChars = "  ", Encoding = new UTF8Encoding(false) };
            using (var writer = XmlWriter.Create(output, settings))
                new DataContractSerializer(typeof(CheatSheet)).WriteObject(writer, document.Sheet);
        }

        public static string ToText(ExportDocument document)
        {
            using (var stream = new MemoryStream())
            {
                Write(document, stream);
                return new UTF8Encoding(false).GetString(stream.ToArray());
            }
        }
    }

    /// <summary>Tables per group with the keys in &lt;kbd&gt;, which GitHub and most viewers draw as keys.</summary>
    internal static class MarkdownExporter
    {
        public static string ToText(ExportDocument document)
        {
            var text = new StringBuilder();
            text.Append("# ").AppendLine(Escape(document.Title)).AppendLine();
            if (!string.IsNullOrEmpty(document.Subtitle))
                text.AppendLine(Escape(document.Subtitle)).AppendLine();

            foreach (var group in document.Groups)
            {
                if (group.Name != null)
                    text.Append("## ").AppendLine(Escape(group.Name)).AppendLine();
                foreach (var subgroup in group.Subgroups)
                {
                    if (subgroup.Name != null)
                        text.Append("### ").AppendLine(Escape(subgroup.Name)).AppendLine();
                    text.Append("| ").Append(Escape(Localizer.Get(Strings.ExportColumnKeys))).Append(" | ").Append(Escape(Localizer.Get(Strings.ExportColumnCommand)))
                        .Append(" | ").Append(Escape(Localizer.Get(Strings.ExportColumnNotes))).AppendLine(" |");
                    text.AppendLine("| --- | --- | --- |");
                    foreach (var s in subgroup.Shortcuts)
                        text.Append("| ").Append(Keys(s.Keys)).Append(" | ").Append(Escape(s.Name)).Append(" | ").Append(Escape(Notes(s, group.Name))).AppendLine(" |");
                    text.AppendLine();
                }
            }
            return text.ToString();
        }

        public static void Write(ExportDocument document, Stream output) => CsvExporter.WriteText(output, ToText(document), withBom: false);

        private static string Keys(string keys) =>
            string.Join(", ", KeyChord.Parse(keys).Select(stroke => string.Join("+", stroke.Select(k => "<kbd>" + KeyText(KeyChord.Display(k)) + "</kbd>"))));

        /// <summary>A key inside &lt;kbd&gt;: HTML-encoded, and the characters Markdown still acts on there escaped.</summary>
        private static string KeyText(string key)
        {
            var result = new StringBuilder();
            foreach (char c in WebUtility.HtmlEncode(key))
            {
                if (c == '|' || c == '\\' || c == '*' || c == '_' || c == '`')
                    result.Append('\\');
                result.Append(c);
            }
            return result.ToString();
        }

        /// <summary>Pipes split the table; backslash, * and _ change the text. HTML-like text is shown as typed.</summary>
        private static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            var result = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (c == '|' || c == '\\' || c == '*' || c == '_' || c == '`' || c == '<' || c == '[' || c == ']')
                    result.Append('\\');
                result.Append(c);
            }
            return result.ToString();
        }

        internal static string Notes(Shortcut s, string groupName) => ExportText.Notes(s, groupName);
    }

    /// <summary>Text that the readable formats share, in the language of the extension.</summary>
    internal static class ExportText
    {
        /// <summary>Extension (unless the group already says so), "yours", and why a binding was not applied.</summary>
        public static string Notes(Shortcut s, string groupName)
        {
            var parts = new List<string>();
            if (s.Extension != null && s.Extension != groupName && s.Extension != s.Category)
                parts.Add(s.Extension);
            if (s.IsCustom)
                parts.Add(Localizer.Get(Strings.Yours));
            if (s.NotApplied)
                parts.Add(NotAppliedText(s));
            return string.Join(" · ", parts);
        }

        public static string NotAppliedText(Shortcut s) =>
            s.UsedBy != null && s.UsedBy.Count > 0
                ? Localizer.Format(Strings.NoteNotAppliedFormat, string.Join(", ", s.UsedBy))
                : Localizer.Get(Strings.NoteNotApplied);
    }
}
