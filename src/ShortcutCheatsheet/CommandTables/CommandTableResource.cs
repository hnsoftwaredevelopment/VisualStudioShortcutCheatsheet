using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Resources;

namespace ShortcutCheatsheet.CommandTables
{
    /// <summary>
    /// Finds the compiled command table ("Menus.ctmenu") of a package, without loading its assembly.
    /// </summary>
    /// <remarks>
    /// The table is a byte array in a .resources file that is embedded in the assembly: in the package DLL itself
    /// (VSPackage.resources) or in a satellite DLL such as en-US\X.resources.dll (VSPackage.en-US.resources).
    /// <see cref="PEReader"/> reads the bytes of the DLL and <see cref="ResourceReader.GetResourceData"/> returns the
    /// raw value without creating objects, so no code of the extension runs.
    /// </remarks>
    internal static class CommandTableResource
    {
        public const string DefaultName = "Menus.ctmenu";

        /// <summary>The table bytes, from the assembly or one of its satellites; null when there is none.</summary>
        public static byte[] FindInPackage(string assemblyPath, string resourceName = DefaultName)
        {
            foreach (string file in CandidateFiles(assemblyPath))
            {
                byte[] table = FindInAssembly(file, resourceName);
                if (table != null)
                    return table;
            }
            return null;
        }

        /// <summary>The assembly, then its en-US satellite, then the other satellites.</summary>
        internal static IEnumerable<string> CandidateFiles(string assemblyPath)
        {
            yield return assemblyPath;
            string folder = Path.GetDirectoryName(assemblyPath);
            string satellite = Path.GetFileNameWithoutExtension(assemblyPath) + ".resources.dll";
            string english = Path.Combine(folder, "en-US", satellite);
            if (File.Exists(english))
                yield return english;
            IEnumerable<string> others;
            try
            {
                others = Directory.EnumerateDirectories(folder)
                    .Select(d => Path.Combine(d, satellite))
                    .Where(f => File.Exists(f) && !string.Equals(f, english, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                others = Enumerable.Empty<string>();
            }
            foreach (string file in others)
                yield return file;
        }

        /// <summary>The table bytes from one assembly; null when the file has no such resource or is not a .NET assembly.</summary>
        public static byte[] FindInAssembly(string path, string resourceName = DefaultName)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var pe = new PEReader(stream))
                {
                    if (!pe.HasMetadata || pe.PEHeaders.CorHeader == null)
                        return null;
                    var metadata = pe.GetMetadataReader();
                    int resourcesRva = pe.PEHeaders.CorHeader.ResourcesDirectory.RelativeVirtualAddress;
                    foreach (var handle in metadata.ManifestResources)
                    {
                        var resource = metadata.GetManifestResource(handle);
                        if (!resource.Implementation.IsNil)
                            continue; // stored in another file
                        if (!metadata.GetString(resource.Name).EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
                            continue;
                        byte[] resources = ReadEmbedded(pe, resourcesRva, resource.Offset);
                        byte[] table = resources == null ? null : FindInResources(resources, resourceName);
                        if (table != null)
                            return table;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is BadImageFormatException || ex is InvalidOperationException)
            {
            }
            return null;
        }

        /// <summary>An embedded resource: a 4-byte length followed by the data, at the resources directory plus the offset.</summary>
        private static byte[] ReadEmbedded(PEReader pe, int resourcesRva, long offset)
        {
            var block = pe.GetSectionData(resourcesRva + (int)offset);
            if (block.Length < 4)
                return null;
            var reader = block.GetReader();
            int length = reader.ReadInt32();
            if (length < 0 || length > reader.RemainingBytes)
                return null;
            return reader.ReadBytes(length);
        }

        /// <summary>A byte array from a .resources file; ResourceReader prefixes it with its length, which is removed here.</summary>
        internal static byte[] FindInResources(byte[] resources, string resourceName)
        {
            try
            {
                using (var reader = new ResourceReader(new MemoryStream(resources, writable: false)))
                {
                    reader.GetResourceData(resourceName, out string type, out byte[] data);
                    if (type != "ResourceTypeCode.ByteArray" || data == null || data.Length < 4)
                        return null;
                    int length = BitConverter.ToInt32(data, 0);
                    if (length < 0 || length > data.Length - 4)
                        return null;
                    var table = new byte[length];
                    Buffer.BlockCopy(data, 4, table, 0, length);
                    return table;
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is BadImageFormatException || ex is IOException || ex is FormatException)
            {
                return null; // not in this file, or not a .resources file
            }
        }
    }
}
