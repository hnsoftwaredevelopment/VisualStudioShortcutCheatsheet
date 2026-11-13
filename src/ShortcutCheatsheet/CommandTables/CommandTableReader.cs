using System;
using System.Collections.Generic;
using System.Text;

namespace ShortcutCheatsheet.CommandTables
{
    /// <summary>
    /// Reads an uncompressed command table (version 5). The format is not documented; what is known:
    /// <list type="bullet">
    /// <item>Header of 20 bytes: "CFCT", version, 0, offset of the GUID table, 0.</item>
    /// <item>Sections: 4-letter tag ("Symb", "Menu", "Grup", "Cmmd", "Bimp", "Plac", "Keys", "SymR") plus a count.
    /// Entries have different sizes per section and there is no byte length, so a section cannot be skipped
    /// without understanding it.</item>
    /// <item>GUIDs are stored once in the GUID table; entries refer to them with a 1-based 16-bit index.</item>
    /// </list>
    /// "Symb" always comes first and is read directly. "Cmmd" and "Keys" are found by their tag and accepted only
    /// when every entry is valid and the section ends exactly at the next tag or at the end of the data.
    /// </summary>
    internal static class CommandTableReader
    {
        private const int HeaderSize = 20;
        private const int CommandHeaderSize = 36;
        private const int KeyBindingSize = 16;
        private const int MaxCount = 100000;
        private const int MaxStringBytes = 64 * 1024;

        /// <summary>Reads a compressed or uncompressed table. Throws FormatException when it does not fit the format.</summary>
        public static CommandTable Read(byte[] cto)
        {
            byte[] data = CtoDecompressor.Decompress(cto);
            if (data.Length < HeaderSize + 8)
                throw new FormatException("The command table is truncated.");

            var guids = ReadGuidTable(data);
            var names = ReadSymbols(data, out var idNames);
            var commands = new List<CommandDefinition>();
            var seen = new HashSet<CommandId>();
            foreach (int offset in FindTag(data, "Cmmd"))
            {
                var found = TryReadCommands(data, offset, guids, idNames);
                if (found == null)
                    continue;
                foreach (var command in found)
                    if (seen.Add(command.Id))
                        commands.Add(command);
            }

            var keyBindings = new List<KeyBindingDefinition>();
            foreach (int offset in FindTag(data, "Keys"))
            {
                var found = TryReadKeyBindings(data, offset, guids);
                if (found != null)
                    keyBindings.AddRange(found);
            }

            return new CommandTable(guids, names, commands, keyBindings);
        }

        private static List<Guid> ReadGuidTable(byte[] data)
        {
            int offset = ReadInt32(data, 12);
            int count = ReadInt32(data, offset);
            if (count < 0 || count > MaxCount || (long)offset + 4 + 16L * count > data.Length)
                throw new FormatException("The GUID table of the command table is invalid.");
            var guids = new List<Guid>(count);
            for (int i = 0; i < count; i++)
                guids.Add(ReadGuid(data, offset + 4 + 16 * i));
            return guids;
        }

        /// <summary>"Symb": per GUID its name and the names of its IDs, as in the Symbols section of the .vsct.</summary>
        private static Dictionary<Guid, string> ReadSymbols(byte[] data, out Dictionary<CommandId, string> idNames)
        {
            if (!HasTag(data, HeaderSize, "Symb"))
                throw new FormatException("The command table has no Symb section.");
            var names = new Dictionary<Guid, string>();
            idNames = new Dictionary<CommandId, string>();
            int count = ReadCount(data, HeaderSize + 4);
            int p = HeaderSize + 8;
            for (int i = 0; i < count; i++)
            {
                var guid = ReadGuid(data, p);
                p += 16;
                names[guid] = ReadName(data, ref p);
                int ids = ReadCount(data, p);
                p += 4;
                for (int j = 0; j < ids; j++)
                {
                    uint value = (uint)ReadInt32(data, p);
                    p += 4;
                    idNames[new CommandId(guid, value)] = ReadName(data, ref p);
                }
            }
            return names;
        }

        /// <summary>
        /// "Cmmd" entry: GUID index (2), parent GUID index (2), ID (4), parent ID (4), 16 bytes of priority, icon and
        /// flags, type (2), 2 bytes, length of the strings in bytes (4), then the strings: button text, menu text,
        /// tooltip, command well text, canonical name and localized canonical name, each ended by a 0 character.
        /// </summary>
        private static List<CommandDefinition> TryReadCommands(byte[] data, int offset, List<Guid> guids, Dictionary<CommandId, string> idNames)
        {
            if (offset + 8 > data.Length)
                return null;
            int count = ReadInt32(data, offset + 4);
            if (count <= 0 || count > MaxCount)
                return null;
            var result = new List<CommandDefinition>(count);
            int p = offset + 8;
            for (int i = 0; i < count; i++)
            {
                if (p + CommandHeaderSize > data.Length)
                    return null;
                int guidIndex = ReadUInt16(data, p);
                uint id = (uint)ReadInt32(data, p + 4);
                int stringBytes = ReadInt32(data, p + 32);
                if (guidIndex == 0 || guidIndex > guids.Count || stringBytes < 0 || stringBytes > MaxStringBytes
                    || stringBytes % 2 != 0 || p + CommandHeaderSize + stringBytes > data.Length)
                    return null;
                string[] strings = Encoding.Unicode.GetString(data, p + CommandHeaderSize, stringBytes).Split('\0');
                var commandId = new CommandId(guids[guidIndex - 1], id);
                idNames.TryGetValue(commandId, out string symbol);
                result.Add(new CommandDefinition(commandId, symbol, At(strings, 0), At(strings, 4), At(strings, 5)));
                p += CommandHeaderSize + stringBytes;
            }
            return EndsAtSection(data, p) ? result : null;
        }

        /// <summary>
        /// "Keys" entry, 16 bytes: command GUID index (2), emulation GUID index (2), command ID (4), editor GUID index (2),
        /// 2 bytes, then key 1, modifiers 1, key 2, modifiers 2 (1 byte each).
        /// </summary>
        private static List<KeyBindingDefinition> TryReadKeyBindings(byte[] data, int offset, List<Guid> guids)
        {
            if (offset + 8 > data.Length)
                return null;
            int count = ReadInt32(data, offset + 4);
            if (count <= 0 || count > MaxCount || (long)offset + 8 + (long)KeyBindingSize * count > data.Length)
                return null;
            var result = new List<KeyBindingDefinition>(count);
            int p = offset + 8;
            for (int i = 0; i < count; i++, p += KeyBindingSize)
            {
                int commandGuid = ReadUInt16(data, p);
                int emulationGuid = ReadUInt16(data, p + 2);
                uint id = (uint)ReadInt32(data, p + 4);
                int editorGuid = ReadUInt16(data, p + 8);
                byte key1 = data[p + 12], key2 = data[p + 14];
                if (!IsIndex(commandGuid, guids) || !IsIndex(editorGuid, guids) || (emulationGuid != 0 && !IsIndex(emulationGuid, guids)) || key1 == 0)
                    return null;
                result.Add(new KeyBindingDefinition(
                    new CommandId(guids[commandGuid - 1], id),
                    guids[editorGuid - 1],
                    emulationGuid == 0 ? Guid.Empty : guids[emulationGuid - 1],
                    new KeyStroke(key1, (KeyModifiers)data[p + 13]),
                    new KeyStroke(key2, key2 == 0 ? KeyModifiers.None : (KeyModifiers)data[p + 15])));
            }
            return EndsAtSection(data, p) ? result : null;
        }

        private static IEnumerable<int> FindTag(byte[] data, string tag)
        {
            for (int i = HeaderSize; i <= data.Length - 8; i++)
                if (HasTag(data, i, tag))
                    yield return i;
        }

        private static bool HasTag(byte[] data, int offset, string tag)
        {
            if (offset < 0 || offset + 4 > data.Length)
                return false;
            for (int i = 0; i < 4; i++)
                if (data[offset + i] != tag[i])
                    return false;
            return true;
        }

        /// <summary>A section ends at the end of the data or where the next one starts: an upper-case letter and three letters.</summary>
        private static bool EndsAtSection(byte[] data, int p)
        {
            if (p == data.Length)
                return true;
            if (p + 8 > data.Length || !IsUpper(data[p]))
                return false;
            for (int i = 1; i < 4; i++)
                if (!IsUpper(data[p + i]) && !(data[p + i] >= 'a' && data[p + i] <= 'z'))
                    return false;
            return true;
        }

        private static bool IsUpper(byte b) => b >= 'A' && b <= 'Z';
        private static bool IsIndex(int index, List<Guid> guids) => index > 0 && index <= guids.Count;
        private static string At(string[] strings, int index) => index < strings.Length ? strings[index] : string.Empty;

        private static string ReadName(byte[] data, ref int p)
        {
            int length = ReadUInt16(data, p);
            p += 2;
            if (p + 2 * length > data.Length)
                throw new FormatException("A symbol name runs past the end of the command table.");
            string name = Encoding.Unicode.GetString(data, p, 2 * length);
            p += 2 * length;
            return name;
        }

        private static int ReadCount(byte[] data, int p)
        {
            int count = ReadInt32(data, p);
            if (count < 0 || count > MaxCount)
                throw new FormatException("A count in the command table is invalid.");
            return count;
        }

        private static Guid ReadGuid(byte[] data, int p)
        {
            if (p < 0 || p + 16 > data.Length)
                throw new FormatException("A GUID runs past the end of the command table.");
            var bytes = new byte[16];
            Buffer.BlockCopy(data, p, bytes, 0, 16);
            return new Guid(bytes);
        }

        private static int ReadInt32(byte[] data, int p)
        {
            if (p < 0 || p + 4 > data.Length)
                throw new FormatException("The command table is truncated.");
            return BitConverter.ToInt32(data, p);
        }

        private static int ReadUInt16(byte[] data, int p)
        {
            if (p < 0 || p + 2 > data.Length)
                throw new FormatException("The command table is truncated.");
            return BitConverter.ToUInt16(data, p);
        }
    }
}
