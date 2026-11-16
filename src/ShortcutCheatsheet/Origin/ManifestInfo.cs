using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace ShortcutCheatsheet.Origin
{
    /// <summary>Name and version from extension.vsixmanifest, in the current (2011) and the old (2010) schema.</summary>
    internal sealed class ManifestInfo
    {
        private ManifestInfo(string name, string version, bool isSystemComponent)
        {
            Name = name;
            Version = version;
            IsSystemComponent = isSystemComponent;
        }

        public string Name { get; }
        public string Version { get; }

        /// <summary>
        /// Installation@SystemComponent="true": part of Visual Studio, installed with a workload (Container Tools, for
        /// example), even when it sits in Common7\IDE\Extensions like a third-party extension.
        /// </summary>
        public bool IsSystemComponent { get; }

        /// <summary>Null when the folder has no readable manifest.</summary>
        public static ManifestInfo Read(string folder)
        {
            string path = Path.Combine(folder, "extension.vsixmanifest");
            if (!File.Exists(path))
                return null;
            try
            {
                XDocument doc;
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                using (var reader = XmlReader.Create(path, settings))
                    doc = XDocument.Load(reader);

                // 2011: PackageManifest/Metadata/DisplayName and Identity@Version.
                var metadata = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "Metadata");
                if (metadata != null)
                {
                    string name = metadata.Elements().FirstOrDefault(e => e.Name.LocalName == "DisplayName")?.Value;
                    string version = (string)metadata.Elements().FirstOrDefault(e => e.Name.LocalName == "Identity")?.Attribute("Version");
                    var installation = doc.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "Installation");
                    bool system = string.Equals((string)installation?.Attribute("SystemComponent"), "true", StringComparison.OrdinalIgnoreCase);
                    return new ManifestInfo(name?.Trim(), version, system);
                }

                // 2010: Vsix/Identifier/Name and Version.
                var identifier = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "Identifier");
                if (identifier != null)
                {
                    return new ManifestInfo(
                        identifier.Elements().FirstOrDefault(e => e.Name.LocalName == "Name")?.Value?.Trim(),
                        identifier.Elements().FirstOrDefault(e => e.Name.LocalName == "Version")?.Value?.Trim(),
                        identifier.Elements().Any(e => e.Name.LocalName == "SystemComponent" && e.Value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)));
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is XmlException)
            {
            }
            return null;
        }
    }
}
