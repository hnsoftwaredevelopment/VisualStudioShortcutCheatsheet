using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ExtensionCommon.VisualStudio;
using ShortcutCheatsheet.CommandTables;

namespace ShortcutCheatsheet.Origin
{
    /// <summary>An installed extension, as far as the cheat sheet needs it.</summary>
    internal sealed class ExtensionInfo
    {
        public ExtensionInfo(string name, string version, string folder)
        {
            Name = name;
            Version = version;
            Folder = folder;
        }

        public string Name { get; }
        public string Version { get; }
        public string Folder { get; }
        public int Commands { get; internal set; }
        public int DefaultKeyBindings { get; internal set; }

        /// <summary>Why a command table could not be read; null when all went well.</summary>
        public string Problem { get; internal set; }
    }

    /// <summary>
    /// Which extension defines which command: from the command table of every package of every enabled extension.
    /// Commands that are in no extension table are taken to be part of Visual Studio. Optionally also the tables of
    /// VS itself, only for names: DTE has no name for some of its own commands.
    /// </summary>
    internal sealed class ExtensionCommands
    {
        private readonly Dictionary<CommandId, (ExtensionInfo Extension, CommandDefinition Command)> _commands =
            new Dictionary<CommandId, (ExtensionInfo, CommandDefinition)>();
        private IReadOnlyDictionary<CommandId, CommandDefinition> _visualStudio = new Dictionary<CommandId, CommandDefinition>();

        private ExtensionCommands()
        {
        }

        public static readonly ExtensionCommands Empty = new ExtensionCommands();

        private readonly List<(ExtensionInfo Extension, KeyBindingDefinition Binding)> _defaultBindings =
            new List<(ExtensionInfo, KeyBindingDefinition)>();

        public IReadOnlyList<ExtensionInfo> Extensions { get; private set; } = Array.Empty<ExtensionInfo>();

        /// <summary>The key bindings extensions bring along by default; VS skips the ones whose keys are taken.</summary>
        public IReadOnlyList<(ExtensionInfo Extension, KeyBindingDefinition Binding)> DefaultBindings => _defaultBindings;

        /// <summary>For tests: an index from command tables that are already read.</summary>
        internal static ExtensionCommands FromTables(IEnumerable<(ExtensionInfo Extension, CommandTable Table)> tables)
        {
            var result = new ExtensionCommands();
            var extensions = new List<ExtensionInfo>();
            foreach (var (extension, table) in tables)
            {
                if (!extensions.Contains(extension))
                    extensions.Add(extension);
                result.Add(extension, table);
            }
            result.Extensions = extensions;
            return result;
        }

        /// <summary>The extension that defines the command; false for a command of Visual Studio.</summary>
        public bool TryFind(CommandId id, out ExtensionInfo extension, out CommandDefinition command)
        {
            if (_commands.TryGetValue(id, out var found))
            {
                extension = found.Extension;
                command = found.Command;
                return true;
            }
            extension = null;
            command = null;
            return false;
        }

        /// <summary>A command of Visual Studio itself, from its own command tables (read with <see cref="WithVisualStudioNames"/>).</summary>
        public bool TryFindVisualStudio(CommandId id, out CommandDefinition command) => _visualStudio.TryGetValue(id, out command);

        /// <summary>Reads the extension folders. Slow (disk), so call it on a background thread.</summary>
        public static ExtensionCommands Build(IEnumerable<ExtensionSource> sources, CancellationToken token = default)
        {
            var result = new ExtensionCommands();
            var extensions = new List<ExtensionInfo>();
            foreach (var source in sources.Where(s => !s.IsVisualStudio))
            {
                token.ThrowIfCancellationRequested();
                var manifest = ManifestInfo.Read(source.Folder);
                if (manifest?.IsSystemComponent == true)
                    continue; // installed with VS (a workload), so its commands count as VS
                var extension = new ExtensionInfo(source.Owner ?? manifest?.Name ?? Path.GetFileName(source.Folder.TrimEnd('\\')),
                    manifest?.Version, source.Folder);
                extensions.Add(extension);
                result.AddTables(extension);
            }
            result.Extensions = extensions;
            return result;
        }

        /// <summary>Adds the names of VS's own commands. The tables are read once per IDE folder and kept: about 4 seconds.</summary>
        public ExtensionCommands WithVisualStudioNames(string ideFolder, CancellationToken token = default)
        {
            _visualStudio = VisualStudioNames.Get(ideFolder, token);
            return this;
        }

        private void AddTables(ExtensionInfo extension)
        {
            string[] pkgdefs;
            try
            {
                pkgdefs = Directory.GetFiles(extension.Folder, "*.pkgdef", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                extension.Problem = ex.Message;
                return;
            }

            var problems = new List<string>();
            foreach (string pkgdef in pkgdefs)
                foreach (var table in PackageTables.Read(pkgdef, extension.Folder, problems))
                    Add(extension, table);
            if (problems.Count > 0)
                extension.Problem = string.Join("; ", problems);
        }

        private void Add(ExtensionInfo extension, CommandTable table)
        {
            foreach (var command in table.Commands)
            {
                // First come, first served: two extensions defining the same command is not something VS allows either.
                if (!_commands.ContainsKey(command.Id))
                {
                    _commands[command.Id] = (extension, command);
                    extension.Commands++;
                }
            }
            extension.DefaultKeyBindings += table.KeyBindings.Count;
            foreach (var binding in table.KeyBindings)
                _defaultBindings.Add((extension, binding));
        }

        /// <summary>Command tables of VS itself, cached per IDE folder for the lifetime of the process.</summary>
        private static class VisualStudioNames
        {
            private static readonly object Gate = new object();
            private static string _folder;
            private static IReadOnlyDictionary<CommandId, CommandDefinition> _names;

            public static IReadOnlyDictionary<CommandId, CommandDefinition> Get(string ideFolder, CancellationToken token)
            {
                lock (Gate)
                {
                    if (_names != null && string.Equals(_folder, ideFolder, StringComparison.OrdinalIgnoreCase))
                        return _names;
                }

                var names = new Dictionary<CommandId, CommandDefinition>();
                if (!string.IsNullOrEmpty(ideFolder) && Directory.Exists(ideFolder))
                {
                    IEnumerable<string> pkgdefs;
                    try
                    {
                        pkgdefs = Directory.EnumerateFiles(ideFolder, "*.pkgdef", SearchOption.AllDirectories).ToList();
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        pkgdefs = Enumerable.Empty<string>();
                    }
                    foreach (string pkgdef in pkgdefs)
                    {
                        token.ThrowIfCancellationRequested();
                        foreach (var table in PackageTables.Read(pkgdef, Path.GetDirectoryName(pkgdef), null))
                            foreach (var command in table.Commands)
                                if (!names.ContainsKey(command.Id))
                                    names[command.Id] = command;
                    }
                }

                lock (Gate)
                {
                    _folder = ideFolder;
                    _names = names;
                }
                return names;
            }
        }
    }
}
