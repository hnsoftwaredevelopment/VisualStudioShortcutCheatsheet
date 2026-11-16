using System;
using System.Collections.Generic;
using System.IO;
using ShortcutCheatsheet.CommandTables;

namespace ShortcutCheatsheet.Origin
{
    /// <summary>The command tables of the packages in one .pkgdef: Menus entry, package DLL, resource, table.</summary>
    internal static class PackageTables
    {
        /// <summary>
        /// Every readable table. A package with a native (Win32) resource or a resource layout we do not know gives
        /// nothing; a table that does not fit the format adds a line to <paramref name="problems"/>.
        /// </summary>
        public static IEnumerable<CommandTable> Read(string pkgdef, string packageFolder, List<string> problems)
        {
            string text;
            try
            {
                text = File.ReadAllText(pkgdef);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                problems?.Add(Path.GetFileName(pkgdef) + ": " + ex.Message);
                yield break;
            }

            var menus = PkgdefMenus.Parse(text);
            if (menus.Count == 0)
                yield break;
            var assemblies = PkgdefPackages.Assemblies(text, packageFolder);
            foreach (var menu in menus)
            {
                string dll = !string.IsNullOrEmpty(menu.Satellite)
                    ? PkgdefPackages.ResolveFolder(menu.Satellite, packageFolder)
                    : assemblies.TryGetValue(menu.Package, out string path) ? path : Path.ChangeExtension(pkgdef, ".dll");
                if (dll == null || !File.Exists(dll))
                    continue;

                CommandTable table = null;
                try
                {
                    byte[] bytes = CommandTableResource.FindInPackage(dll, menu.ResourceName);
                    if (bytes != null)
                        table = CommandTableReader.Read(bytes);
                }
                catch (FormatException ex)
                {
                    problems?.Add(Path.GetFileName(dll) + ": " + ex.Message);
                }
                if (table != null)
                    yield return table;
            }
        }
    }
}
