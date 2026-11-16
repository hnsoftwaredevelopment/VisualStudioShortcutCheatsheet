using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ShortcutCheatsheet.CommandTables
{
    /// <summary>A command as VS identifies it: command set GUID plus number.</summary>
    internal readonly struct CommandId : IEquatable<CommandId>
    {
        public CommandId(Guid guid, uint id)
        {
            Guid = guid;
            Id = id;
        }

        public Guid Guid { get; }
        public uint Id { get; }

        public bool Equals(CommandId other) => Guid == other.Guid && Id == other.Id;
        public override bool Equals(object obj) => obj is CommandId other && Equals(other);
        public override int GetHashCode() => Guid.GetHashCode() * 31 + (int)Id;
        public override string ToString() => "{" + Guid.ToString("D", CultureInfo.InvariantCulture) + "}:0x" + Id.ToString("X4", CultureInfo.InvariantCulture);
    }

    /// <summary>Modifier bits as stored in a command table.</summary>
    [Flags]
    internal enum KeyModifiers : byte
    {
        None = 0,
        Alt = 1,
        Shift = 2,
        Control = 4,
        Windows = 8,
    }

    /// <summary>One key press: a virtual-key code plus modifiers.</summary>
    internal readonly struct KeyStroke : IEquatable<KeyStroke>
    {
        public KeyStroke(byte key, KeyModifiers modifiers)
        {
            Key = key;
            Modifiers = modifiers;
        }

        /// <summary>Windows virtual-key code, such as 0x70 for F1 or 0x4B for K.</summary>
        public byte Key { get; }
        public KeyModifiers Modifiers { get; }
        public bool IsEmpty => Key == 0;

        public bool Equals(KeyStroke other) => Key == other.Key && Modifiers == other.Modifiers;
        public override bool Equals(object obj) => obj is KeyStroke other && Equals(other);
        public override int GetHashCode() => Key << 8 | (byte)Modifiers;
    }

    /// <summary>A command defined by this command table (not one it only places in a menu).</summary>
    internal sealed class CommandDefinition
    {
        public CommandDefinition(CommandId id, string symbolName, string buttonText, string canonicalName, string localizedCanonicalName)
        {
            Id = id;
            SymbolName = symbolName;
            ButtonText = buttonText;
            CanonicalName = canonicalName;
            LocalizedCanonicalName = localizedCanonicalName;
        }

        public CommandId Id { get; }

        /// <summary>The IDSymbol name from the .vsct, such as "cmdidFirst"; null when not stored.</summary>
        public string SymbolName { get; }
        public string ButtonText { get; }

        /// <summary>As written in the .vsct, often with a leading dot: ".Tools.FixtureFirst". Empty when not set.</summary>
        public string CanonicalName { get; }
        public string LocalizedCanonicalName { get; }
    }

    /// <summary>A default key binding that the command table brings along.</summary>
    internal sealed class KeyBindingDefinition
    {
        public KeyBindingDefinition(CommandId command, Guid editor, Guid emulation, KeyStroke first, KeyStroke second)
        {
            Command = command;
            Editor = editor;
            Emulation = emulation;
            First = first;
            Second = second;
        }

        public CommandId Command { get; }

        /// <summary>The scope: guidVSStd97 is Global, the text editor factory is "Text Editor", and so on.</summary>
        public Guid Editor { get; }
        public Guid Emulation { get; }
        public KeyStroke First { get; }

        /// <summary>The second stroke of a chord such as Ctrl+K, Ctrl+Q; empty for a single key.</summary>
        public KeyStroke Second { get; }
    }

    /// <summary>What one compiled command table contains, as far as this extension needs it.</summary>
    internal sealed class CommandTable
    {
        /// <summary>guidVSStd97: as editor of a key binding it means "Global".</summary>
        public static readonly Guid GlobalScope = new Guid("5efc7975-14bc-11cf-9b2b-00aa00573819");

        private readonly HashSet<CommandId> _defined;

        public CommandTable(IReadOnlyList<Guid> guids, IReadOnlyDictionary<Guid, string> guidNames,
            IReadOnlyList<CommandDefinition> commands, IReadOnlyList<KeyBindingDefinition> keyBindings)
        {
            Guids = guids;
            GuidNames = guidNames;
            Commands = commands;
            KeyBindings = keyBindings;
            _defined = new HashSet<CommandId>(commands.Select(c => c.Id));
        }

        /// <summary>The GUID table; indexes in the file are 1-based into this list.</summary>
        public IReadOnlyList<Guid> Guids { get; }

        /// <summary>GuidSymbol names from the .vsct, such as "guidFixtureCmdSet".</summary>
        public IReadOnlyDictionary<Guid, string> GuidNames { get; }
        public IReadOnlyList<CommandDefinition> Commands { get; }
        public IReadOnlyList<KeyBindingDefinition> KeyBindings { get; }

        /// <summary>True when this table defines the command; a command it only places or binds is not its own.</summary>
        public bool Defines(CommandId id) => _defined.Contains(id);
    }
}
