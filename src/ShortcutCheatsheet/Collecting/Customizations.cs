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
        public static readonly Customizations None = new Customizations(new HashSet<string>(), 0);

        private readonly HashSet<string> _added;

        private Customizations(HashSet<string> added, int removed)
        {
            _added = added;
            Removed = removed;
        }

        public int Added => _added.Count;

        /// <summary>Shortcuts of VS or an extension that you removed; DTE no longer lists them.</summary>
        public int Removed { get; }

        public bool IsAdded(string command, string scope, string keys) => _added.Contains(Key(command, scope, keys));

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
            int removed = 0;
            foreach (var item in items)
            {
                if (!(item is JObject entry))
                    continue;
                if ((bool?)entry["isRemoval"] == true)
                {
                    removed++;
                    continue;
                }
                string command = (string)entry["command"], scope = (string)entry["scope"], keys = (string)entry["shortcut"];
                if (!string.IsNullOrEmpty(command) && !string.IsNullOrEmpty(keys))
                    added.Add(Key(command, scope, keys));
            }
            return new Customizations(added, removed);
        }

        private static string Key(string command, string scope, string keys) => command + "\n" + (scope ?? "") + "\n" + Normalize(keys);

        /// <summary>"Ctrl+E,V" and "Ctrl+E, V" are the same chord.</summary>
        private static string Normalize(string keys) => keys.Replace(" ", "");
    }
}
