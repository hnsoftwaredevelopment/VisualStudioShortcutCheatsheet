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

        /// <summary>Keys without spaces and in lower case, to compare what someone types with what VS shows.</summary>
        public static string Normalize(string keys) => (keys ?? string.Empty).Replace(" ", "").ToLowerInvariant();
    }
}
