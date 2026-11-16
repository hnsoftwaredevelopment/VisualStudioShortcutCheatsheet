using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ShortcutCheatsheet.Model
{
    /// <summary>
    /// Turns a canonical command name into a category and a readable name.
    /// <list type="bullet">
    /// <item>"Edit.CommentSelection" → Edit, "Comment Selection"</item>
    /// <item>"EditorContextMenus.CodeWindow.Markdown.MakeBold" → Markdown, "Make Bold": the context-menu path says
    /// where the item is in a menu, not what it does, so it is dropped.</item>
    /// <item>"OtherContextMenus.LeftMarginContextMenu.ToggleBookmark" → Context Menu, "Toggle Bookmark"</item>
    /// </list>
    /// </summary>
    internal static class CommandNames
    {
        public const string ContextMenuCategory = "Context Menu";
        public const string NoNameCategory = "Other";

        public static (string Category, string Name) Describe(string canonicalName, string buttonText = null)
        {
            string fromButton = CleanButtonText(buttonText);
            if (string.IsNullOrWhiteSpace(canonicalName))
                return (NoNameCategory, fromButton);

            var parts = canonicalName.Split('.').Where(p => p.Length > 0).ToList();
            string category;
            if (parts.Count > 0 && parts[0].EndsWith("ContextMenus", StringComparison.Ordinal))
            {
                parts.RemoveRange(0, Math.Min(2, parts.Count)); // "EditorContextMenus" and the menu, such as "CodeWindow"
                if (parts.Count >= 2)
                {
                    category = parts[0];
                    parts.RemoveAt(0);
                }
                else
                    category = ContextMenuCategory;
            }
            else if (parts.Count >= 2)
            {
                category = parts[0];
                parts.RemoveAt(0);
            }
            else
                category = NoNameCategory;

            string name = fromButton ?? (parts.Count == 0 ? canonicalName : string.Join(" ", parts.Select(SplitWords)));
            return (category, name);
        }

        /// <summary>"&amp;Cleanup Active Document..." → "Cleanup Active Document". Null for empty text.</summary>
        public static string CleanButtonText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            string cleaned = text.Replace("&&", "\u0001").Replace("&", "").Replace("\u0001", "&").Trim();
            if (cleaned.EndsWith("...", StringComparison.Ordinal))
                cleaned = cleaned.Substring(0, cleaned.Length - 3).TrimEnd();
            return cleaned.Length == 0 ? null : cleaned;
        }

        /// <summary>"CommentSelection" → "Comment Selection", "XAMLDesigner" → "XAML Designer".</summary>
        public static string SplitWords(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
                return identifier;
            string spaced = Regex.Replace(identifier, @"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");
            var sb = new StringBuilder(spaced.Length);
            foreach (char c in spaced)
                sb.Append(c == '_' ? ' ' : c);
            return sb.ToString().Trim();
        }
    }
}
