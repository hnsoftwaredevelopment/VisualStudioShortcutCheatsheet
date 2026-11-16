using System;
using System.Collections.Generic;
using System.Linq;

namespace ShortcutCheatsheet.Model
{
    /// <summary>How the list is grouped.</summary>
    internal enum Grouping
    {
        Scope,
        Category,
        Extension,
        Keys,
    }

    /// <summary>What to show; all switches off and no text means everything.</summary>
    internal sealed class ShortcutFilter
    {
        public string Text { get; set; }
        public bool OnlyExtensions { get; set; }
        public bool OnlyCustom { get; set; }
        public bool OnlyConflicts { get; set; }

        public bool IsEmpty => string.IsNullOrWhiteSpace(Text) && !OnlyExtensions && !OnlyCustom && !OnlyConflicts;
    }

    /// <summary>
    /// Filtering and grouping of the list, without WPF, so it can be tested. The window only draws the result.
    /// </summary>
    internal static class ShortcutView
    {
        public const string VisualStudioGroup = "Visual Studio";

        /// <summary>
        /// Search text matches name, command, category, scope and extension; and keys, with or without spaces:
        /// "ctrl+e,v" finds "Ctrl+E, V". All words must match: "debug f10" finds Step Over in Debug.
        /// </summary>
        public static bool Matches(Shortcut shortcut, ShortcutFilter filter)
        {
            if (filter == null)
                return true;
            if (filter.OnlyExtensions && shortcut.Extension == null)
                return false;
            if (filter.OnlyCustom && !shortcut.IsCustom)
                return false;
            if (filter.OnlyConflicts && shortcut.ConflictsWith == null)
                return false;
            if (string.IsNullOrWhiteSpace(filter.Text))
                return true;

            string keys = KeyChord.Normalize(shortcut.Keys);
            string whole = KeyChord.Normalize(filter.Text);
            if (whole.Length > 0 && keys.Contains(whole))
                return true; // typed as keys, spaces in the chord included

            foreach (string word in filter.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!Contains(shortcut.Name, word) && !Contains(shortcut.Command, word) && !Contains(shortcut.Category, word)
                    && !Contains(shortcut.Scope, word) && !Contains(shortcut.Extension, word) && !keys.Contains(word.ToLowerInvariant()))
                    return false;
            }
            return true;
        }

        /// <summary>The two group levels for a shortcut; null means no group at that level.</summary>
        public static (string Outer, string Inner) Groups(Shortcut shortcut, Grouping grouping)
        {
            switch (grouping)
            {
                case Grouping.Scope:
                    return (shortcut.Scope, shortcut.Category);
                case Grouping.Category:
                    return (shortcut.Category, shortcut.Scope);
                case Grouping.Extension:
                    return (shortcut.Extension ?? VisualStudioGroup, shortcut.Scope);
                default:
                    return (null, null);
            }
        }

        /// <summary>Sorted for the chosen grouping: Global and Visual Studio first, then alphabetical.</summary>
        public static List<Shortcut> Sort(IEnumerable<Shortcut> shortcuts, Grouping grouping)
        {
            if (grouping == Grouping.Keys)
                return shortcuts.OrderBy(s => s.Keys, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Scope, StringComparer.CurrentCultureIgnoreCase).ToList();

            return shortcuts
                .Select(s => (Shortcut: s, Groups: Groups(s, grouping)))
                .OrderBy(x => First(x.Groups.Outer, grouping) ? 0 : 1)
                .ThenBy(x => x.Groups.Outer, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(x => x.Groups.Inner == CheatSheetBuilder.GlobalScope ? 0 : 1)
                .ThenBy(x => x.Groups.Inner, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(x => x.Shortcut.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(x => x.Shortcut.Keys, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Shortcut)
                .ToList();
        }

        private static bool First(string outer, Grouping grouping) =>
            (grouping == Grouping.Scope && outer == CheatSheetBuilder.GlobalScope) || (grouping == Grouping.Extension && outer == VisualStudioGroup);

        private static bool Contains(string text, string word) =>
            text != null && text.IndexOf(word, StringComparison.CurrentCultureIgnoreCase) >= 0;
    }
}
