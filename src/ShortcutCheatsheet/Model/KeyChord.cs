using System;
using System.Collections.Generic;

namespace ShortcutCheatsheet.Model
{
    /// <summary>
    /// Splits the keys as VS shows them into strokes and keys, to draw each key as a key cap.
    /// "Ctrl+K, Ctrl+C" → [[Ctrl, K], [Ctrl, C]]; "Ctrl+Num +" → [[Ctrl, Num +]]; "Ctrl+," → [[Ctrl, ,]].
    /// </summary>
    /// <remarks>
    /// Splitting on '+' alone breaks "Num +" and "Ctrl++". Instead the known modifiers are taken off the front of a
    /// stroke; what remains is the key, whatever characters it has. Strokes of a chord are separated by ", ".
    /// </remarks>
    internal static class KeyChord
    {
        private static readonly string[] Modifiers = { "Ctrl", "Shift", "Alt", "Win" };

        public static IReadOnlyList<IReadOnlyList<string>> Parse(string keys)
        {
            var strokes = new List<IReadOnlyList<string>>();
            if (string.IsNullOrWhiteSpace(keys))
                return strokes;
            foreach (string stroke in keys.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries))
                strokes.Add(ParseStroke(stroke.Trim()));
            return strokes;
        }

        private static IReadOnlyList<string> ParseStroke(string stroke)
        {
            var parts = new List<string>();
            string rest = stroke;
            bool found = true;
            while (found)
            {
                found = false;
                foreach (string modifier in Modifiers)
                {
                    // Only a modifier followed by '+' and at least one more character: "Ctrl" alone is a key.
                    if (rest.Length > modifier.Length + 1 && rest.StartsWith(modifier + "+", StringComparison.OrdinalIgnoreCase))
                    {
                        parts.Add(rest.Substring(0, modifier.Length));
                        rest = rest.Substring(modifier.Length + 1);
                        found = true;
                        break;
                    }
                }
            }
            if (rest.Length > 0)
                parts.Add(rest);
            return parts;
        }

        /// <summary>The arrow keys and Enter as on the keyboard; the text of VS ("Up Arrow") stays in the data and the search.</summary>
        private static readonly Dictionary<string, string> Symbols = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Up Arrow"] = "\u2191",
            ["Down Arrow"] = "\u2193",
            ["Left Arrow"] = "\u2190",
            ["Right Arrow"] = "\u2192",
            ["Enter"] = "\u23CE", // the return symbol is on nearly every keyboard; Backspace and the rest are not
        };

        /// <summary>What a key cap shows: an arrow for the arrow keys, otherwise the key as VS names it.</summary>
        public static string Display(string key) => key != null && Symbols.TryGetValue(key, out string symbol) ? symbol : key;

        /// <summary>Keys without spaces and in lower case, to compare what someone types with what VS shows.</summary>
        public static string Normalize(string keys) => (keys ?? string.Empty).Replace(" ", "").ToLowerInvariant();
    }
}
