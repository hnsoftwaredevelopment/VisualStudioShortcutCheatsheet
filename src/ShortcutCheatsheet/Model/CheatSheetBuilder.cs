using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ShortcutCheatsheet.Collecting;
using ShortcutCheatsheet.CommandTables;
using ShortcutCheatsheet.Origin;

namespace ShortcutCheatsheet.Model
{
    /// <summary>
    /// Combines what DTE reports with the origin of each command and your own changes. No VS needed, so tested
    /// directly. Order: scope (Global first), category, name.
    /// </summary>
    internal static class CheatSheetBuilder
    {
        public const string GlobalScope = "Global";

        public static CheatSheet Build(IEnumerable<RawBinding> bindings, ExtensionCommands origins, Customizations customizations,
            string visualStudio, DateTimeOffset generated)
        {
            origins = origins ?? ExtensionCommands.Empty;
            customizations = customizations ?? Customizations.None;

            var shortcuts = new List<Shortcut>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in bindings)
            {
                // DTE can list the same binding twice (once per scheme layer); keep one.
                if (!seen.Add(raw.CommandGuid + "/" + raw.CommandId + "/" + raw.Scope + "/" + raw.Keys))
                    continue;

                origins.TryFind(new CommandId(raw.CommandGuid, raw.CommandId), out var extension, out var definition);
                string canonical = raw.CommandName.Length > 0 ? raw.CommandName : TrimDot(definition?.CanonicalName);
                var (category, name) = CommandNames.Describe(canonical, definition?.ButtonText);
                if (string.IsNullOrEmpty(name))
                    name = raw.LocalizedName.Length > 0 ? raw.LocalizedName : string.Format(CultureInfo.InvariantCulture, "{{{0}}}:0x{1:X4}", raw.CommandGuid, raw.CommandId);

                shortcuts.Add(new Shortcut
                {
                    Scope = raw.Scope,
                    Keys = raw.Keys,
                    Name = name,
                    Category = category,
                    Command = canonical,
                    CommandGuid = raw.CommandGuid,
                    CommandId = raw.CommandId,
                    Extension = extension?.Name,
                    IsCustom = customizations.IsAdded(raw.CommandName, raw.Scope, raw.Keys),
                });
            }

            MarkConflicts(shortcuts);

            var sorted = shortcuts
                .OrderBy(s => s.Scope == GlobalScope ? 0 : 1)
                .ThenBy(s => s.Scope, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(s => s.Category, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(s => s.Keys, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var perExtension = sorted.Where(s => s.Extension != null).GroupBy(s => s.Extension).ToDictionary(g => g.Key, g => g.Count());
            var extensions = origins.Extensions
                .Where(e => e.Commands > 0 || e.Problem != null)
                .Select(e => new ExtensionSummary
                {
                    Name = e.Name,
                    Version = e.Version,
                    Shortcuts = perExtension.TryGetValue(e.Name, out int n) ? n : 0,
                    Problem = e.Problem,
                })
                .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            return new CheatSheet
            {
                Generated = generated.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
                VisualStudio = visualStudio,
                Shortcuts = sorted,
                Extensions = extensions,
                RemovedByYou = customizations.Removed,
            };
        }

        /// <summary>Same keys in the same scope for different commands. Global and a specific scope do not conflict: the specific one wins there.</summary>
        private static void MarkConflicts(List<Shortcut> shortcuts)
        {
            foreach (var group in shortcuts.GroupBy(s => s.Scope + "\n" + s.Keys.Replace(" ", ""), StringComparer.OrdinalIgnoreCase))
            {
                var commands = group.Select(Label).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (commands.Count < 2)
                    continue;
                foreach (var shortcut in group)
                    shortcut.ConflictsWith = commands.Where(c => !string.Equals(c, Label(shortcut), StringComparison.OrdinalIgnoreCase)).ToList();
            }
        }

        private static string Label(Shortcut s) => string.IsNullOrEmpty(s.Command) ? s.Name : s.Command;

        private static string TrimDot(string name) => string.IsNullOrEmpty(name) ? string.Empty : name.TrimStart('.');
    }
}
