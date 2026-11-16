using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace ShortcutCheatsheet.Origin
{
    /// <summary>
    /// Which DLL belongs to a package, from [$RootKey$\Packages\{guid}] in a .pkgdef:
    /// <code>
    /// [$RootKey$\Packages\{acf2e531-...}]
    /// "CodeBase"="$PackageFolder$\ShortcutCheatsheet.dll"
    /// "Assembly"="ShortcutCheatsheet, Version=..., Culture=neutral, PublicKeyToken=null"
    /// </code>
    /// CodeBase is a path; Assembly is a name, which is looked for in the extension folder.
    /// </summary>
    internal static class PkgdefPackages
    {
        private static readonly Regex Section = new Regex(@"^\s*\[\$RootKey\$\\Packages\\\{(?<guid>[0-9a-fA-F-]{36})\}\]\s*$", RegexOptions.IgnoreCase);
        private static readonly Regex AnySection = new Regex(@"^\s*\[");
        private static readonly Regex Value = new Regex(@"^\s*""(?<name>CodeBase|Assembly)""\s*=\s*""(?<value>[^""]*)""", RegexOptions.IgnoreCase);

        public static Dictionary<Guid, string> Assemblies(string pkgdef, string packageFolder)
        {
            var result = new Dictionary<Guid, string>();
            Guid? current = null;
            foreach (string line in pkgdef.Split('\n'))
            {
                var section = Section.Match(line);
                if (section.Success)
                {
                    current = new Guid(section.Groups["guid"].Value);
                    continue;
                }
                if (AnySection.IsMatch(line))
                {
                    current = null;
                    continue;
                }
                if (current == null)
                    continue;
                var value = Value.Match(line);
                if (!value.Success)
                    continue;
                string path = value.Groups["name"].Value.Equals("CodeBase", StringComparison.OrdinalIgnoreCase)
                    ? ResolveFolder(value.Groups["value"].Value, packageFolder)
                    : Path.Combine(packageFolder, value.Groups["value"].Value.Split(',')[0].Trim() + ".dll");
                // CodeBase wins: it is the exact file.
                if (!result.ContainsKey(current.Value) || value.Groups["name"].Value.Equals("CodeBase", StringComparison.OrdinalIgnoreCase))
                    result[current.Value] = path;
            }
            return result;
        }

        /// <summary>Replaces $PackageFolder$ (case-insensitive) and returns null for paths outside the extension.</summary>
        internal static string ResolveFolder(string value, string packageFolder)
        {
            if (string.IsNullOrEmpty(value))
                return null;
            string resolved = Regex.Replace(value, @"\$PackageFolder\$", packageFolder.TrimEnd('\\'), RegexOptions.IgnoreCase);
            if (resolved.IndexOf('$') >= 0)
                return null; // another token such as $RootFolder$: not part of this extension
            return Path.IsPathRooted(resolved) ? resolved : Path.Combine(packageFolder, resolved);
        }
    }
}
