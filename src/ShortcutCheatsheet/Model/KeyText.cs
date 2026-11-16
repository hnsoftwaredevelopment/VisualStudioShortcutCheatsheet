using System;
using System.Collections.Generic;
using System.Text;
using ShortcutCheatsheet.CommandTables;

namespace ShortcutCheatsheet.Model
{
    /// <summary>
    /// Writes a key binding from a command table (virtual-key codes and modifier bits) the way DTE shows it:
    /// "Ctrl+Shift+B", "Ctrl+K, Ctrl+Q", "Shift+Alt+Up Arrow". Needed to see whether VS applied an extension's
    /// default key binding.
    /// </summary>
    /// <remarks>
    /// The punctuation keys (VK_OEM_*) depend on the keyboard layout; the names here are those of the US layout,
    /// which DTE showed on Herbert's machine ("Ctrl+=", "Ctrl+\"). A key without a known name gives null, and such
    /// a binding is left out rather than reported wrongly.
    /// </remarks>
    internal static class KeyText
    {
        private static readonly Dictionary<byte, string> Names = new Dictionary<byte, string>
        {
            [0x03] = "Break", [0x08] = "Bkspce", [0x09] = "Tab", [0x0D] = "Enter", [0x13] = "Pause", [0x1B] = "Esc",
            [0x20] = "Space", [0x21] = "PgUp", [0x22] = "PgDn", [0x23] = "End", [0x24] = "Home",
            [0x25] = "Left Arrow", [0x26] = "Up Arrow", [0x27] = "Right Arrow", [0x28] = "Down Arrow",
            [0x2D] = "Ins", [0x2E] = "Del",
            [0x6A] = "Num *", [0x6B] = "Num +", [0x6D] = "Num -", [0x6E] = "Num .", [0x6F] = "Num /",
            [0xBA] = ";", [0xBB] = "=", [0xBC] = ",", [0xBD] = "-", [0xBE] = ".", [0xBF] = "/", [0xC0] = "`",
            [0xDB] = "[", [0xDC] = "\\", [0xDD] = "]", [0xDE] = "'",
        };

        /// <summary>"Ctrl+K, Ctrl+Q"; null when a key has no known name.</summary>
        public static string Format(KeyStroke first, KeyStroke second)
        {
            string a = Format(first);
            if (a == null)
                return null;
            if (second.IsEmpty)
                return a;
            string b = Format(second);
            return b == null ? null : a + ", " + b;
        }

        /// <summary>One stroke: modifiers in the order DTE uses (Ctrl, Shift, Alt, Win), then the key.</summary>
        public static string Format(KeyStroke stroke)
        {
            string key = KeyName(stroke.Key);
            if (key == null)
                return null;
            var sb = new StringBuilder();
            if ((stroke.Modifiers & KeyModifiers.Control) != 0) sb.Append("Ctrl+");
            if ((stroke.Modifiers & KeyModifiers.Shift) != 0) sb.Append("Shift+");
            if ((stroke.Modifiers & KeyModifiers.Alt) != 0) sb.Append("Alt+");
            if ((stroke.Modifiers & KeyModifiers.Windows) != 0) sb.Append("Win+");
            return sb.Append(key).ToString();
        }

        private static string KeyName(byte vk)
        {
            if (vk >= 0x30 && vk <= 0x39 || vk >= 0x41 && vk <= 0x5A)
                return ((char)vk).ToString(); // digits and letters are their own character
            if (vk >= 0x60 && vk <= 0x69)
                return "Num " + (vk - 0x60);
            if (vk >= 0x70 && vk <= 0x87)
                return "F" + (vk - 0x6F);
            return Names.TryGetValue(vk, out string name) ? name : null;
        }
    }
}
