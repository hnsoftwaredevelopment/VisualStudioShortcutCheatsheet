using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using ShortcutCheatsheet.Model;

namespace ShortcutCheatsheet.Export
{
    /// <summary>The cheat sheet as indented JSON (UTF-8, no BOM). Field names come from the DataContract on the model.</summary>
    internal static class JsonExporter
    {
        public static void Write(CheatSheet sheet, Stream output)
        {
            var serializer = new DataContractJsonSerializer(typeof(CheatSheet));
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(output, new UTF8Encoding(false), ownsStream: false, indent: true, indentChars: "  "))
            {
                serializer.WriteObject(writer, sheet);
                writer.Flush();
            }
        }

        public static string ToText(CheatSheet sheet)
        {
            using (var stream = new MemoryStream())
            {
                Write(sheet, stream);
                return new UTF8Encoding(false).GetString(stream.ToArray());
            }
        }
    }
}
