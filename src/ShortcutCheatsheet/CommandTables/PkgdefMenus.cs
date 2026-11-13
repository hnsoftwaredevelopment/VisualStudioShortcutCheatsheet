using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ShortcutCheatsheet.CommandTables
{
    /// <summary>One entry of [$RootKey$\Menus] in a .pkgdef: which package has a command table, and under what name.</summary>
    internal sealed class MenuResource
    {
        public MenuResource(Guid package, string satellite, string resourceName)
        {
            Package = package;
            Satellite = satellite;
            ResourceName = resourceName;
        }

        public Guid Package { get; }

        /// <summary>A separate resource DLL; empty when the table is in the package assembly (the usual case).</summary>
        public string Satellite { get; }

        /// <summary>"Menus.ctmenu" for most extensions; some use a number such as "1000".</summary>
        public string ResourceName { get; }
    }

    /// <summary>
    /// Reads the Menus section of a .pkgdef:
    /// <code>
    /// [$RootKey$\Menus]
    /// "{4c82e17d-927e-42d2-8460-b473ac7df316}"=", Menus.ctmenu, 1"
    /// </code>
    /// The value is "satellite DLL, resource name, version".
    /// </summary>
    internal static class PkgdefMenus
    {
        private static readonly Regex Section = new Regex(@"^\s*\[(?<key>[^\]]+)\]\s*$");
        private static readonly Regex Entry = new Regex(@"^\s*""\{?(?<guid>[0-9a-fA-F-]{36})\}?""\s*=\s*""(?<value>[^""]*)""");

        public static IReadOnlyList<MenuResource> Parse(string pkgdef)
        {
            var result = new List<MenuResource>();
            bool inMenus = false;
            foreach (string line in pkgdef.Split('\n'))
            {
                var section = Section.Match(line);
                if (section.Success)
                {
                    inMenus = section.Groups["key"].Value.Trim().Equals(@"$RootKey$\Menus", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inMenus)
                    continue;
                var entry = Entry.Match(line);
                if (!entry.Success)
                    continue;
                string[] parts = entry.Groups["value"].Value.Split(',');
                if (parts.Length < 2 || parts[1].Trim().Length == 0)
                    continue;
                result.Add(new MenuResource(new Guid(entry.Groups["guid"].Value), parts[0].Trim(), parts[1].Trim()));
            }
            return result;
        }
    }
}
