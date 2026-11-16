using System.Runtime.CompilerServices;

// Lets the test project reach internal types without making them public.
// Kept separate from AssemblyInfo.cs because a versioning tool may rewrite that file.
[assembly: InternalsVisibleTo("ShortcutCheatsheet.Tests")]
