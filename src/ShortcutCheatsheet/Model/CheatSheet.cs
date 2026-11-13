using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace ShortcutCheatsheet.Model
{
    /// <summary>One shortcut on the cheat sheet. Also the shape of the JSON and XML export.</summary>
    [DataContract(Name = "shortcut", Namespace = CheatSheet.XmlNamespace)]
    internal sealed class Shortcut
    {
        /// <summary>Where it works: "Global", "Text Editor", ...</summary>
        [DataMember(Name = "scope", Order = 1)] public string Scope { get; set; }

        /// <summary>As VS shows it: "Ctrl+E, V".</summary>
        [DataMember(Name = "keys", Order = 2)] public string Keys { get; set; }

        /// <summary>Readable name: "Duplicate".</summary>
        [DataMember(Name = "name", Order = 3)] public string Name { get; set; }

        /// <summary>Group within the scope: "Edit", "Debug", or the extension's own prefix.</summary>
        [DataMember(Name = "category", Order = 4)] public string Category { get; set; }

        /// <summary>Canonical command name: "Edit.Duplicate". Empty for commands VS knows only by GUID and ID.</summary>
        [DataMember(Name = "command", Order = 5, EmitDefaultValue = false)] public string Command { get; set; }

        [DataMember(Name = "commandGuid", Order = 6)] public Guid CommandGuid { get; set; }
        [DataMember(Name = "commandId", Order = 7)] public uint CommandId { get; set; }

        /// <summary>The extension that adds the command; null for Visual Studio itself.</summary>
        [DataMember(Name = "extension", Order = 8, EmitDefaultValue = false)] public string Extension { get; set; }

        /// <summary>You added this shortcut yourself (Tools > Options > Keyboard).</summary>
        [DataMember(Name = "custom", Order = 9, EmitDefaultValue = false)] public bool IsCustom { get; set; }

        /// <summary>Other commands with the same keys in the same scope.</summary>
        [DataMember(Name = "conflictsWith", Order = 10, EmitDefaultValue = false)] public List<string> ConflictsWith { get; set; }
    }

    /// <summary>A summary line per extension.</summary>
    [DataContract(Name = "extension", Namespace = CheatSheet.XmlNamespace)]
    internal sealed class ExtensionSummary
    {
        [DataMember(Name = "name", Order = 1)] public string Name { get; set; }
        [DataMember(Name = "version", Order = 2, EmitDefaultValue = false)] public string Version { get; set; }
        [DataMember(Name = "shortcuts", Order = 3)] public int Shortcuts { get; set; }
        [DataMember(Name = "problem", Order = 4, EmitDefaultValue = false)] public string Problem { get; set; }
    }

    /// <summary>Everything that goes into a cheat sheet.</summary>
    [DataContract(Name = "cheatSheet", Namespace = XmlNamespace)]
    internal sealed class CheatSheet
    {
        public const string XmlNamespace = "https://github.com/hnsoftwaredevelopment/VisualStudioShortcutCheatsheet";

        /// <summary>Version of this file layout; raise it when a field changes meaning.</summary>
        [DataMember(Name = "formatVersion", Order = 1)] public int FormatVersion { get; set; } = 1;
        [DataMember(Name = "generated", Order = 2)] public string Generated { get; set; }
        [DataMember(Name = "visualStudio", Order = 3)] public string VisualStudio { get; set; }
        [DataMember(Name = "shortcuts", Order = 4)] public List<Shortcut> Shortcuts { get; set; } = new List<Shortcut>();
        [DataMember(Name = "extensions", Order = 5)] public List<ExtensionSummary> Extensions { get; set; } = new List<ExtensionSummary>();
        [DataMember(Name = "removedByYou", Order = 6)] public int RemovedByYou { get; set; }
    }
}
