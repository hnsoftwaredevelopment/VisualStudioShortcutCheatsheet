using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace ShortcutCheatsheet.Tests
{
    internal static class TestPaths
    {
        /// <summary>
        /// The repository root. Found from the path of this source file, filled in by the compiler, because vstest
        /// does not always run the test DLL from the build folder.
        /// </summary>
        public static string RepositoryRoot => FindRoot();

        public static string Fixture(string relativePath) => Path.Combine(RepositoryRoot, "tests", "Fixtures", relativePath);

        private static string FindRoot([CallerFilePath] string sourceFile = "")
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFile));
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ShortcutCheatsheet.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("ShortcutCheatsheet.sln not found above the test sources.");
        }
    }
}
