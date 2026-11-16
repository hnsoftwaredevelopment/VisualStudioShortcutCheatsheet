using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;

namespace ShortcutCheatsheet.Collecting
{
    /// <summary>
    /// Reads every key binding from DTE.Commands. That is the list VS itself uses: VS, extensions and your own
    /// changes, in the active keyboard scheme. DTE is COM and must be used on the UI thread.
    /// </summary>
    internal static class DteBindingReader
    {
        public static List<RawBinding> Read(DTE2 dte, out int commandCount)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var result = new List<RawBinding>();
            commandCount = 0;
            foreach (Command command in dte.Commands)
            {
                commandCount++;
                object[] bindings;
                try
                {
                    bindings = command.Bindings as object[];
                }
                catch (COMException)
                {
                    continue; // some commands refuse; they have no bindings we can show
                }
                if (bindings == null || bindings.Length == 0)
                    continue;

                string name = Name(command);
                string localized = LocalizedName(command);
                Guid.TryParse(GuidText(command), out Guid guid);
                uint id = (uint)command.ID;
                foreach (object binding in bindings)
                {
                    if (binding is string text && RawBinding.TrySplit(text, out string scope, out string keys))
                        result.Add(new RawBinding(name, localized, guid, id, scope, keys));
                }
            }
            return result;
        }

        // Some commands throw on a property; one helper per property keeps the threading analyzer able to follow.
        private static string Name(Command command)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try { return command.Name; } catch (COMException) { return null; }
        }

        private static string LocalizedName(Command command)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try { return command.LocalizedName; } catch (COMException) { return null; }
        }

        private static string GuidText(Command command)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try { return command.Guid; } catch (COMException) { return null; }
        }
    }
}