using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ShortcutCheatsheet.Collecting
{
    /// <summary>
    /// The shortcuts you added or removed yourself. VS 2026 keeps them in Unified Settings, in settings.json of the
    /// VS instance, under "environment.keyboardShortcuts.customizations":
    /// <code>
    /// { "scheme": "default", "command": "Edit.Duplicate", "shortcut": "Ctrl+D", "scope": "Text Editor",
    ///   "scopeId": "{...}", "isRemoval": false }
    /// </code>
    /// </summary>
    internal sealed class Customizations
    {
        public const string Moniker = "environment.keyboardShortcuts.customizations";
        public static readonly Customizations None = new Customizations(new HashSet<string>(), new HashSet<string>());

        private readonly HashSet<string> _added;
        private readonly HashSet<string> _removed;

        private Customizations(HashSet<string> added, HashSet<string> removed)
        {
            _added = added;
            _removed = removed;
        }

        public int Added => _added.Count;

        /// <summary>Shortcuts of VS or an extension that you removed; DTE no longer lists them.</summary>
        public int Removed => _removed.Count;

        public bool IsAdded(string command, string scope, string keys) => _added.Contains(Key(command, scope, keys));

        /// <summary>You removed this binding yourself: when VS does not apply it, that is your choice, not a clash.</summary>
        public bool IsRemoved(string command, string scope, string keys) => _removed.Contains(Key(command, scope, keys));

        /// <summary>Reads settings.json. The file starts with a comment, which Newtonsoft skips.</summary>
        public static Customizations Parse(string settingsJson)
        {
            if (string.IsNullOrWhiteSpace(settingsJson))
                return None;
            JObject root;
            try
            {
                using (var reader = new JsonTextReader(new System.IO.StringReader(settingsJson)))
                    root = JObject.Load(reader, new JsonLoadSettings { CommentHandling = CommentHandling.Ignore });
            }
            catch (JsonException)
            {
                return None;
            }
            if (!(root[Moniker] is JArray items))
                return None;

            var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                if (!(item is JObject entry))
                    continue;
                string command = (string)entry["command"], scope = (string)entry["scope"], keys = (string)entry["shortcut"];
                if (string.IsNullOrEmpty(command) || string.IsNullOrEmpty(keys))
                    continue;
                if ((bool?)entry["isRemoval"] == true)
                    removed.Add(Key(command, scope, keys));
                else
                    added.Add(Key(command, scope, keys));
            }
            return new Customizations(added, removed);
        }

        private static string Key(string command, string scope, string keys) => command + "\n" + (scope ?? "") + "\n" + Normalize(keys);

        /// <summary>"Ctrl+E,V" and "Ctrl+E, V" are the same chord.</summary>
        private static string Normalize(string keys) => keys.Replace(" ", "");
    }
}
