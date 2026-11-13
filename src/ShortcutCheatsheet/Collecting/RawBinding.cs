using System;

namespace ShortcutCheatsheet.Collecting
{
    /// <summary>One key binding as DTE reports it: "Text Editor::Ctrl+E, V" for the command Edit.Duplicate.</summary>
    internal sealed class RawBinding
    {
        public RawBinding(string commandName, string localizedName, Guid commandGuid, uint commandId, string scope, string keys)
        {
            CommandName = commandName ?? string.Empty;
            LocalizedName = localizedName ?? string.Empty;
            CommandGuid = commandGuid;
            CommandId = commandId;
            Scope = scope;
            Keys = keys;
        }

        /// <summary>Canonical name such as "Edit.Duplicate"; empty for commands VS only knows by GUID and ID.</summary>
        public string CommandName { get; }
        public string LocalizedName { get; }
        public Guid CommandGuid { get; }
        public uint CommandId { get; }

        /// <summary>Where the shortcut works: "Global", "Text Editor", "Solution Explorer", ...</summary>
        public string Scope { get; }

        /// <summary>The keys as VS shows them: "Ctrl+E, V".</summary>
        public string Keys { get; }

        /// <summary>Splits "Scope::Keys". False for text without "::" or with an empty part.</summary>
        public static bool TrySplit(string binding, out string scope, out string keys)
        {
            scope = keys = null;
            if (string.IsNullOrEmpty(binding))
                return false;
            int separator = binding.IndexOf("::", StringComparison.Ordinal);
            if (separator <= 0 || separator + 2 >= binding.Length)
                return false;
            scope = binding.Substring(0, separator).Trim();
            keys = binding.Substring(separator + 2).Trim();
            return scope.Length > 0 && keys.Length > 0;
        }
    }
}
