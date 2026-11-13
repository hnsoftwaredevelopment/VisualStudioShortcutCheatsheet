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
    /// Commands that are in no extension table are taken to be part of Visual Studio.
    /// </summary>
    internal sealed class ExtensionCommands
    {
        private readonly Dictionary<CommandId, (ExtensionInfo Extension, CommandDefinition Command)> _commands =
            new Dictionary<CommandId, (ExtensionInfo, CommandDefinition)>();

        private ExtensionCommands()
        {
        }

        public static readonly ExtensionCommands Empty = new ExtensionCommands();

        public IReadOnlyList<ExtensionInfo> Extensions { get; private set; } = Array.Empty<ExtensionInfo>();

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

        /// <summary>Reads the extension folders. Slow (disk), so call it on a background thread.</summary>
        public static ExtensionCommands Build(IEnumerable<ExtensionSource> sources, CancellationToken token = default)
        {
            var result = new ExtensionCommands();
            var extensions = new List<ExtensionInfo>();
            foreach (var source in sources.Where(s => !s.IsVisualStudio))
            {
                token.ThrowIfCancellationRequested();
                var manifest = ManifestInfo.Read(source.Folder);
                var extension = new ExtensionInfo(source.Owner ?? manifest?.Name ?? Path.GetFileName(source.Folder.TrimEnd('\\')),
                    manifest?.Version, source.Folder);
                extensions.Add(extension);
                result.AddTables(extension);
            }
            result.Extensions = extensions;
            return result;
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

            foreach (string pkgdef in pkgdefs)
            {
                string text;
                try
                {
                    text = File.ReadAllText(pkgdef);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    extension.Problem = ex.Message;
                    continue;
                }

                var menus = PkgdefMenus.Parse(text);
                if (menus.Count == 0)
                    continue;
                var assemblies = PkgdefPackages.Assemblies(text, extension.Folder);
                foreach (var menu in menus)
                {
                    string dll = !string.IsNullOrEmpty(menu.Satellite)
                        ? PkgdefPackages.ResolveFolder(menu.Satellite, extension.Folder)
                        : assemblies.TryGetValue(menu.Package, out string path) ? path : Path.ChangeExtension(pkgdef, ".dll");
                    if (dll == null || !File.Exists(dll))
                        continue;
                    try
                    {
                        byte[] bytes = CommandTableResource.FindInPackage(dll, menu.ResourceName);
                        if (bytes == null)
                            continue; // native resource or another layout: nothing to read here
                        Add(extension, CommandTableReader.Read(bytes));
                    }
                    catch (FormatException ex)
                    {
                        extension.Problem = Path.GetFileName(dll) + ": " + ex.Message;
                    }
                }
            }
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
        }
    }
}
