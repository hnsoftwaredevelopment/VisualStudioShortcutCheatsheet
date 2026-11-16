using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Media.Imaging;

namespace ShortcutCheatsheet.Export
{
    /// <summary>Each page as a PNG. One page goes to the chosen name; more pages to "name-1.png", "name-2.png", ...</summary>
    internal static class PngExporter
    {
        /// <summary>Writes the files and returns their paths.</summary>
        public static IReadOnlyList<string> Write(ExportDocument document, RenderOptions options, string path)
        {
            var pages = SheetRenderer.RenderBitmaps(document, options);
            var paths = PagePaths(path, pages.Count);
            for (int i = 0; i < pages.Count; i++)
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(pages[i]));
                using (var file = File.Create(paths[i]))
                    encoder.Save(file);
            }
            return paths;
        }

        public static IReadOnlyList<string> PagePaths(string path, int count)
        {
            if (count == 1)
                return new[] { path };
            string folder = Path.GetDirectoryName(path) ?? string.Empty;
            string name = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            var paths = new List<string>();
            for (int i = 1; i <= count; i++)
                paths.Add(Path.Combine(folder, name + "-" + i.ToString(CultureInfo.InvariantCulture) + extension));
            return paths;
        }
    }

    /// <summary>The pages as images in a PDF; one image per page, filling it.</summary>
    internal static class PdfExporter
    {
        public static void Write(ExportDocument document, RenderOptions options, Stream output)
        {
            var pdf = new PdfWriter { Title = document.Title };
            foreach (var page in SheetRenderer.RenderBitmaps(document, options))
                pdf.AddImagePage(page, options.PagePoints.Width, options.PagePoints.Height);
            pdf.Save(output);
        }
    }

    /// <summary>
    /// A minimal PDF 1.4 writer for pages that are one RGB image each. Our own, because the usual libraries are
    /// AGPL (PDFsharp 6.5, iText) and this needs only a catalog, pages, images and a cross-reference table.
    /// </summary>
    internal sealed class PdfWriter
    {
        private readonly List<byte[]> _objects = new List<byte[]>(); // object n is _objects[n - 1]
        private readonly List<int> _pageIds = new List<int>();

        public string Title { get; set; }

        /// <summary>Adds a page of <paramref name="width"/> by <paramref name="height"/> points showing the bitmap.</summary>
        public void AddImagePage(BitmapSource bitmap, double width, double height)
        {
            byte[] rgb = ToRgb(bitmap);
            byte[] packed = Zlib(rgb);
            int image = Add(StreamObject(
                string.Format(CultureInfo.InvariantCulture,
                    "<< /Type /XObject /Subtype /Image /Width {0} /Height {1} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /Length {2} >>",
                    bitmap.PixelWidth, bitmap.PixelHeight, packed.Length),
                packed));

            string w = N(width), h = N(height);
            byte[] content = Encoding.ASCII.GetBytes("q " + w + " 0 0 " + h + " 0 0 cm /Im0 Do Q");
            int contents = Add(StreamObject("<< /Length " + content.Length + " >>", content));

            // The page's parent is the Pages object, which gets its number in Save; fill it in later.
            _pageIds.Add(Add(null));
            _pending.Add((_pageIds.Count - 1, w, h, image, contents));
        }

        private readonly List<(int Page, string W, string H, int Image, int Contents)> _pending = new List<(int, string, string, int, int)>();

        public void Save(Stream output)
        {
            int pagesId = _objects.Count + 1;
            foreach (var p in _pending)
            {
                _objects[_pageIds[p.Page] - 1] = Encoding.ASCII.GetBytes(string.Format(CultureInfo.InvariantCulture,
                    "<< /Type /Page /Parent {0} 0 R /MediaBox [0 0 {1} {2}] /Resources << /XObject << /Im0 {3} 0 R >> >> /Contents {4} 0 R >>",
                    pagesId, p.W, p.H, p.Image, p.Contents));
            }
            var kids = new StringBuilder();
            foreach (int id in _pageIds)
                kids.Append(id).Append(" 0 R ");
            Add(Encoding.ASCII.GetBytes("<< /Type /Pages /Kids [" + kids.ToString().TrimEnd() + "] /Count " + _pageIds.Count + " >>"));
            int catalog = Add(Encoding.ASCII.GetBytes("<< /Type /Catalog /Pages " + pagesId + " 0 R >>"));
            int info = Add(Encoding.ASCII.GetBytes("<< /Title " + TextString(Title ?? string.Empty) + " /Producer (Shortcut Cheatsheet) >>"));

            var offsets = new List<long>();
            Write(output, "%PDF-1.4\n");
            output.Write(new byte[] { (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' }, 0, 6); // marks the file as binary
            for (int i = 0; i < _objects.Count; i++)
            {
                offsets.Add(output.Position);
                Write(output, (i + 1).ToString(CultureInfo.InvariantCulture) + " 0 obj\n");
                output.Write(_objects[i], 0, _objects[i].Length);
                Write(output, "\nendobj\n");
            }
            long xref = output.Position;
            var table = new StringBuilder();
            table.Append("xref\n0 ").Append(_objects.Count + 1).Append('\n');
            table.Append("0000000000 65535 f \n"); // each entry is exactly 20 bytes
            foreach (long offset in offsets)
                table.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
            table.Append("trailer\n<< /Size ").Append(_objects.Count + 1).Append(" /Root ").Append(catalog).Append(" 0 R /Info ").Append(info).Append(" 0 R >>\n");
            table.Append("startxref\n").Append(xref.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF\n");
            Write(output, table.ToString());
        }

        private int Add(byte[] body)
        {
            _objects.Add(body);
            return _objects.Count;
        }

        private static byte[] StreamObject(string dictionary, byte[] data)
        {
            var result = new MemoryStream();
            Write(result, dictionary + "\nstream\n");
            result.Write(data, 0, data.Length);
            Write(result, "\nendstream");
            return result.ToArray();
        }

        private static void Write(Stream output, string text)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            output.Write(bytes, 0, bytes.Length);
        }

        private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        /// <summary>A PDF text string in UTF-16 with a BOM, so any title survives; parentheses and backslashes escaped.</summary>
        private static string TextString(string value)
        {
            var text = new StringBuilder("(");
            foreach (byte b in new byte[] { 0xFE, 0xFF })
                text.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
            foreach (byte b in Encoding.BigEndianUnicode.GetBytes(value))
            {
                if (b == '(' || b == ')' || b == '\\')
                    text.Append('\\').Append((char)b);
                else if (b < 32 || b > 126)
                    text.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
                else
                    text.Append((char)b);
            }
            return text.Append(')').ToString();
        }

        /// <summary>Pixels as RGB, top row first. Pbgra32 on a filled background has no transparency, so the alpha is dropped.</summary>
        internal static byte[] ToRgb(BitmapSource bitmap)
        {
            var source = bitmap;
            if (source.Format != System.Windows.Media.PixelFormats.Pbgra32 && source.Format != System.Windows.Media.PixelFormats.Bgra32 && source.Format != System.Windows.Media.PixelFormats.Bgr32)
                source = new FormatConvertedBitmap(bitmap, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            int width = source.PixelWidth, height = source.PixelHeight;
            var bgra = new byte[width * height * 4];
            source.CopyPixels(bgra, width * 4, 0);
            var rgb = new byte[width * height * 3];
            for (int i = 0, j = 0; i < bgra.Length; i += 4, j += 3)
            {
                rgb[j] = bgra[i + 2];
                rgb[j + 1] = bgra[i + 1];
                rgb[j + 2] = bgra[i];
            }
            return rgb;
        }

        /// <summary>FlateDecode wants zlib: a 2-byte header, raw deflate (what DeflateStream writes), and an Adler-32 checksum.</summary>
        internal static byte[] Zlib(byte[] data)
        {
            var output = new MemoryStream();
            output.WriteByte(0x78);
            output.WriteByte(0x9C);
            using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
                deflate.Write(data, 0, data.Length);
            uint a = 1, b = 0;
            foreach (byte value in data)
            {
                a = (a + value) % 65521;
                b = (b + a) % 65521;
            }
            uint adler = (b << 16) | a;
            output.WriteByte((byte)(adler >> 24));
            output.WriteByte((byte)(adler >> 16));
            output.WriteByte((byte)(adler >> 8));
            output.WriteByte((byte)adler);
            return output.ToArray();
        }
    }
}
