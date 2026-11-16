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
        public const string TextEditorScope = "Text Editor";

        /// <summary>The text editor factory: as editor of a key binding it means the scope "Text Editor".</summary>
        private static readonly Guid TextEditor = new Guid("8B382828-6202-11d1-8870-0000F87579D2");

        public static CheatSheet Build(IEnumerable<RawBinding> bindings, ExtensionCommands origins, Customizations customizations,
            string visualStudio, DateTimeOffset generated)
        {
            origins = origins ?? ExtensionCommands.Empty;
            customizations = customizations ?? Customizations.None;

            var raw = bindings.ToList();
            var shortcuts = new List<Shortcut>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var binding in raw)
            {
                // DTE can list the same binding twice (once per scheme layer); keep one.
                if (!seen.Add(binding.CommandGuid + "/" + binding.CommandId + "/" + binding.Scope + "/" + Normalize(binding.Keys)))
                    continue;

                var id = new CommandId(binding.CommandGuid, binding.CommandId);
                origins.TryFind(id, out var extension, out var definition);
                // DTE has no name for some commands of VS itself; VS's own command table often does.
                if (definition == null && binding.CommandName.Length == 0)
                    origins.TryFindVisualStudio(id, out definition);

                var (canonical, category, name) = Describe(binding.CommandName, binding.LocalizedName, id, definition);
                shortcuts.Add(new Shortcut
                {
                    Scope = binding.Scope,
                    Keys = binding.Keys,
                    Name = name,
                    Category = CategoryFor(category, extension),
                    Command = canonical,
                    CommandGuid = binding.CommandGuid,
                    CommandId = binding.CommandId,
                    Extension = extension?.Name,
                    IsCustom = customizations.IsAdded(binding.CommandName, binding.Scope, binding.Keys),
                });
            }

            shortcuts.AddRange(NotApplied(shortcuts, origins, customizations));

            var sorted = shortcuts
                .OrderBy(s => s.Scope == GlobalScope ? 0 : 1)
                .ThenBy(s => s.Scope, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(s => s.Category, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(s => s.Keys, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var perExtension = sorted.Where(s => s.Extension != null && !s.NotApplied).GroupBy(s => s.Extension).ToDictionary(g => g.Key, g => g.Count());
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

        /// <summary>
        /// Default key bindings of extensions that VS did not apply because the keys were taken. VS 2026 does not
        /// allow two commands on one key in one scope: the binding that comes later is skipped without a message.
        /// Only reported when another command really holds these keys in that scope, and not when you removed the
        /// binding yourself: then the reason is known and there is something to do about it.
        /// </summary>
        private static IEnumerable<Shortcut> NotApplied(List<Shortcut> applied, ExtensionCommands origins, Customizations customizations)
        {
            var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (extension, binding) in origins.DefaultBindings)
            {
                string keys = KeyText.Format(binding.First, binding.Second);
                if (keys == null)
                    continue; // a key without a known name: better silent than wrong
                string scope = ScopeName(binding.Editor);
                if (scope == null)
                    continue; // an editor-specific scope whose name we do not know

                bool isApplied = applied.Any(s => s.CommandGuid == binding.Command.Guid && s.CommandId == binding.Command.Id
                    && s.Scope == scope && Normalize(s.Keys) == Normalize(keys));
                if (isApplied)
                    continue;

                var holders = applied.Where(s => s.Scope == scope && Normalize(s.Keys) == Normalize(keys)).ToList();
                if (holders.Count == 0)
                    continue; // not applied for another reason, for example you gave the command other keys

                origins.TryFind(binding.Command, out _, out var definition);
                if (definition == null)
                    origins.TryFindVisualStudio(binding.Command, out definition);
                var (canonical, category, name) = Describe(string.Empty, string.Empty, binding.Command, definition);
                // The command may have other keys that VS did apply: then DTE's name is known and wins.
                var known = applied.FirstOrDefault(s => s.CommandGuid == binding.Command.Guid && s.CommandId == binding.Command.Id && !string.IsNullOrEmpty(s.Command));
                if (known != null)
                    (canonical, category, name) = (known.Command, known.Category, known.Name);
                if (customizations.IsRemoved(canonical, scope, keys))
                    continue;
                if (!reported.Add(binding.Command + "/" + scope + "/" + Normalize(keys)))
                    continue;

                yield return new Shortcut
                {
                    Scope = scope,
                    Keys = keys,
                    Name = name,
                    Category = known != null ? category : CategoryFor(category, extension),
                    Command = canonical,
                    CommandGuid = binding.Command.Guid,
                    CommandId = binding.Command.Id,
                    Extension = extension.Name,
                    NotApplied = true,
                    UsedBy = holders.Select(h => h.Name).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList(),
                };
            }
        }

        /// <summary>The scope name DTE uses for an editor GUID of a key binding; null when we do not know it.</summary>
        internal static string ScopeName(Guid editor) =>
            editor == CommandTable.GlobalScope ? GlobalScope : editor == TextEditor ? TextEditorScope : null;

        /// <summary>Canonical name, category and readable name, from DTE and, when present, the command table.</summary>
        private static (string Canonical, string Category, string Name) Describe(string commandName, string localizedName, CommandId id, CommandDefinition definition)
        {
            string canonical = commandName.Length > 0 ? commandName : TrimDot(definition?.CanonicalName);
            string button = definition?.ButtonText;

            // Some tables put an identifier in the button text ("VsGraphics.MRE.Select"): read it as a name.
            if (canonical.Length == 0 && IsIdentifier(button))
            {
                canonical = button;
                button = null;
            }

            var (category, name) = CommandNames.Describe(canonical, button);
            if (string.IsNullOrEmpty(name))
                name = localizedName.Length > 0 ? localizedName : string.Format(CultureInfo.InvariantCulture, "{{{0}}}:0x{1:X4}", id.Guid, id.Id);
            return (canonical, category, name);
        }

        /// <summary>"Other" or "Context Menu" says nothing for an extension command; the extension does.</summary>
        private static string CategoryFor(string category, ExtensionInfo extension) =>
            extension != null && (category == CommandNames.NoNameCategory || category == CommandNames.ContextMenuCategory) ? extension.Name : category;

        private static bool IsIdentifier(string text) =>
            !string.IsNullOrEmpty(text) && text.IndexOf('.') > 0 && !text.Any(char.IsWhiteSpace);

        private static string Normalize(string keys) => (keys ?? string.Empty).Replace(" ", "").ToLowerInvariant();

        private static string TrimDot(string name) => string.IsNullOrEmpty(name) ? string.Empty : name.TrimStart('.');
    }
}
