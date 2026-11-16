using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShortcutCheatsheet.Localization;
using ShortcutCheatsheet.Model;

namespace ShortcutCheatsheet.Export
{
    internal enum PaperSize
    {
        A4,
        Letter,
    }

    /// <summary>How the printed sheet looks. Sizes in WPF units (1/96 inch), landscape.</summary>
    internal sealed class RenderOptions
    {
        public PaperSize Paper { get; set; } = PaperSize.A4;
        public bool Dark { get; set; }
        public int Columns { get; set; } = 3;

        /// <summary>Pixels per WPF unit in the image: 2 gives 192 dpi, sharp on paper and on a high-DPI screen.</summary>
        public double Scale { get; set; } = 2;

        public Size PageSize => Paper == PaperSize.Letter ? new Size(1056, 816) : new Size(1123, 794);

        /// <summary>The page in PDF points (1/72 inch).</summary>
        public Size PagePoints => Paper == PaperSize.Letter ? new Size(792, 612) : new Size(842, 595);
    }

    /// <summary>
    /// Draws the cheat sheet on pages for PNG and PDF: columns, group headings, and the keys as key caps. Uses
    /// DrawingVisual and FormattedText, without the WPF layout system, so it runs without a window. Must run on an
    /// STA thread.
    /// </summary>
    /// <remarks>
    /// Bindings VS did not apply are left out: they do not work, and on paper there is no room to explain why.
    /// The data formats and HTML keep them.
    /// </remarks>
    internal static class SheetRenderer
    {
        private const double Margin = 36;
        private const double ColumnGap = 22;
        private const double CapHeight = 17;
        private const double CapPadding = 5;
        private const double CapMinWidth = 18;
        private const double PlusWidth = 9;
        private const double RowPadding = 3;

        private sealed class Theme
        {
            public Brush Background, Foreground, Muted, Line, Cap, CapLine, Accent;

            public static Theme Create(bool dark) => dark
                ? new Theme { Background = B(0x1F1F1F), Foreground = B(0xE8E8E8), Muted = B(0xA0A0A0), Line = B(0x3A3A3A), Cap = B(0x2D2D30), CapLine = B(0x5A5A5A), Accent = B(0xC586F0) }
                : new Theme { Background = B(0xFFFFFF), Foreground = B(0x1E1E1E), Muted = B(0x6B6B6B), Line = B(0xE3E3E3), Cap = B(0xF6F6F6), CapLine = B(0xB8B8B8), Accent = B(0x68217A) };

            private static Brush B(int rgb)
            {
                var brush = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
                brush.Freeze();
                return brush;
            }
        }

        /// <summary>Something placed in a column: a heading or a row. Measured once, drawn at its place.</summary>
        private sealed class Block
        {
            public double Height;
            public bool KeepWithNext;
            public Action<DrawingContext, double, double> Draw; // x, y
            public int Page, Column;
            public double Y;

            /// <summary>Group and subgroup it belongs to, for the "(continued)" line at the top of a column.</summary>
            public string Group, Subgroup;
            public bool IsGroupHeading, IsRow;

            /// <summary>Set by Place when the block starts a column in the middle of a group.</summary>
            public string Continued;
        }

        private const double ContinuedHeight = 18;

        /// <summary>The pages as visuals of <see cref="RenderOptions.PageSize"/>.</summary>
        public static IReadOnlyList<DrawingVisual> Render(ExportDocument document, RenderOptions options)
        {
            var theme = Theme.Create(options.Dark);
            var text = new TextFactory(options.Scale);
            var page = options.PageSize;
            double columnWidth = (page.Width - 2 * Margin - (options.Columns - 1) * ColumnGap) / options.Columns;

            var shortcuts = document.Shortcuts.Where(s => !s.NotApplied).ToList();
            double keysWidth = KeysWidth(shortcuts, text, columnWidth);
            var blocks = Measure(document, theme, text, columnWidth, keysWidth);

            double firstTop = Margin + 50, otherTop = Margin + 26, bottom = page.Height - Margin - 14;
            int pages = Place(blocks, options.Columns, firstTop, otherTop, bottom);

            var visuals = new List<DrawingVisual>();
            for (int p = 0; p < pages; p++)
            {
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawRectangle(theme.Background, null, new Rect(page));
                    DrawHeader(dc, document, theme, text, page, p, pages);
                    foreach (var block in blocks.Where(b => b.Page == p))
                    {
                        double x = Margin + block.Column * (columnWidth + ColumnGap);
                        if (block.Continued != null)
                            dc.DrawText(text.Create(block.Continued, 10, theme.Muted, italic: true, maxWidth: columnWidth), new Point(x, block.Y - ContinuedHeight + 2));
                        block.Draw(dc, x, block.Y);
                    }
                }
                visuals.Add(visual);
            }
            return visuals;
        }

        /// <summary>Renders the pages to bitmaps at <see cref="RenderOptions.Scale"/>.</summary>
        public static IReadOnlyList<BitmapSource> RenderBitmaps(ExportDocument document, RenderOptions options)
        {
            var size = options.PageSize;
            int width = (int)Math.Round(size.Width * options.Scale), height = (int)Math.Round(size.Height * options.Scale);
            return Render(document, options).Select(visual =>
            {
                var bitmap = new RenderTargetBitmap(width, height, 96 * options.Scale, 96 * options.Scale, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                bitmap.Freeze();
                return (BitmapSource)bitmap;
            }).ToList();
        }

        /// <summary>
        /// Wide enough for nine in ten chords on one line, between 30 and 45 percent of the column. Sizing for the
        /// widest chord would push every name far right for the sake of a few rows; those wrap instead.
        /// </summary>
        private static double KeysWidth(List<Shortcut> shortcuts, TextFactory text, double columnWidth)
        {
            var widths = shortcuts.Select(s => ChordWidth(KeyChord.Parse(s.Keys), text) + 8).OrderBy(w => w).ToList();
            double typical = widths.Count == 0 ? 0 : widths[(int)Math.Min(widths.Count - 1, Math.Ceiling(widths.Count * 0.9) - 1)];
            return Math.Max(columnWidth * 0.30, Math.Min(typical, columnWidth * 0.45));
        }

        private static double ChordWidth(IReadOnlyList<IReadOnlyList<string>> strokes, TextFactory text) =>
            strokes.Sum(s => StrokeWidth(s, text)) + Math.Max(0, strokes.Count - 1) * 9;

        private static double CapWidth(string key, TextFactory text) => Math.Max(CapMinWidth, text.Cap(KeyChord.Display(key), null).WidthIncludingTrailingWhitespace + 2 * CapPadding);

        private static double StrokeWidth(IReadOnlyList<string> stroke, TextFactory text) =>
            stroke.Sum(k => CapWidth(k, text)) + (stroke.Count - 1) * PlusWidth;

        private static List<Block> Measure(ExportDocument document, Theme theme, TextFactory text, double columnWidth, double keysWidth)
        {
            var blocks = new List<Block>();
            foreach (var group in document.Groups)
            {
                var subgroups = group.Subgroups.Select(g => (g.Name, Rows: g.Shortcuts.Where(s => !s.NotApplied).ToList())).Where(g => g.Rows.Count > 0).ToList();
                if (subgroups.Count == 0)
                    continue;

                if (group.Name != null)
                {
                    var heading = text.Create(group.Name, 14, theme.Accent, bold: true, maxWidth: columnWidth);
                    blocks.Add(new Block
                    {
                        Group = group.Name,
                        IsGroupHeading = true,
                        Height = heading.Height + 10,
                        KeepWithNext = true,
                        Draw = (dc, x, y) =>
                        {
                            dc.DrawText(heading, new Point(x, y + 6));
                            dc.DrawRectangle(theme.Accent, null, new Rect(x, y + 6 + heading.Height + 1, columnWidth, 1));
                        },
                    });
                }

                foreach (var (name, rows) in subgroups)
                {
                    if (name != null)
                    {
                        var heading = text.Create(name, 10.5, theme.Muted, italic: true, bold: true, maxWidth: columnWidth);
                        blocks.Add(new Block { Group = group.Name, Height = heading.Height + 5, KeepWithNext = true, Draw = (dc, x, y) => dc.DrawText(heading, new Point(x, y + 4)) });
                    }
                    foreach (var shortcut in rows)
                    {
                        var row = MeasureRow(shortcut, group.Name, theme, text, columnWidth, keysWidth);
                        row.Group = group.Name;
                        row.Subgroup = name;
                        row.IsRow = true;
                        blocks.Add(row);
                    }
                }
            }
            return blocks;
        }

        private static Block MeasureRow(Shortcut s, string groupName, Theme theme, TextFactory text, double columnWidth, double keysWidth)
        {
            // Key caps, wrapped per stroke when a chord does not fit on one line.
            var caps = new List<(string Key, double X, double Y, double Width)>();
            var signs = new List<(string Sign, double X, double Y)>();
            double cx = 0, cy = 0;
            var strokes = KeyChord.Parse(s.Keys);
            for (int i = 0; i < strokes.Count; i++)
            {
                double width = StrokeWidth(strokes[i], text) + (i < strokes.Count - 1 ? 6 : 0);
                if (cx > 0 && cx + width > keysWidth)
                {
                    cx = 0;
                    cy += CapHeight + 3;
                }
                for (int k = 0; k < strokes[i].Count; k++)
                {
                    double w = CapWidth(strokes[i][k], text);
                    caps.Add((strokes[i][k], cx, cy, w));
                    cx += w;
                    if (k < strokes[i].Count - 1)
                    {
                        signs.Add(("+", cx, cy));
                        cx += PlusWidth;
                    }
                }
                if (i < strokes.Count - 1)
                {
                    signs.Add((",", cx, cy));
                    cx += 9;
                }
            }
            double keysHeight = cy + CapHeight;

            // A stroke that reaches the name column pushes the name below the keys. Up to 4 units into the gap is fine.
            bool nameBelow = strokes.Any(st => StrokeWidth(st, text) > keysWidth + 4);
            double nameTop = nameBelow ? keysHeight + 2 : 0;
            double nameX = keysWidth + 8, nameWidth = columnWidth - nameX;
            var nameText = text.Create(s.Name ?? s.Command ?? string.Empty, 10.5, theme.Foreground, maxWidth: nameWidth);
            string notes = ExportText.Notes(s, groupName);
            var notesText = notes.Length > 0 ? text.Create(notes, 9, theme.Muted, maxWidth: nameWidth) : null;
            double textHeight = nameText.Height + (notesText?.Height ?? 0);
            double height = Math.Max(keysHeight, nameTop + textHeight) + 2 * RowPadding + 1;

            return new Block
            {
                Height = height,
                Draw = (dc, x, y) =>
                {
                    foreach (var cap in caps)
                        DrawCap(dc, cap.Key, x + cap.X, y + RowPadding + cap.Y, cap.Width, theme, text);
                    foreach (var sign in signs)
                    {
                        var t = text.Cap(sign.Sign, theme.Muted);
                        double w = sign.Sign == "+" ? PlusWidth : 9;
                        dc.DrawText(t, new Point(x + sign.X + (w - t.Width) / 2, y + RowPadding + sign.Y + (CapHeight - t.Height) / 2));
                    }
                    double ty = y + RowPadding + nameTop + (nameBelow ? 0 : Math.Max(0, (CapHeight - nameText.Height) / 2));
                    dc.DrawText(nameText, new Point(x + nameX, ty));
                    if (notesText != null)
                        dc.DrawText(notesText, new Point(x + nameX, ty + nameText.Height));
                    dc.DrawRectangle(theme.Line, null, new Rect(x, y + height - 1, columnWidth, 0.75));
                },
            };
        }

        /// <summary>A key cap: the border colour as the base, the face on top and one unit shorter, which gives a thicker bottom edge.</summary>
        private static void DrawCap(DrawingContext dc, string key, double x, double y, double width, Theme theme, TextFactory text)
        {
            dc.DrawRoundedRectangle(theme.CapLine, null, new Rect(x, y, width, CapHeight), 3, 3);
            dc.DrawRoundedRectangle(theme.Cap, null, new Rect(x + 0.75, y + 0.75, width - 1.5, CapHeight - 2.25), 2.5, 2.5);
            var label = text.Cap(KeyChord.Display(key), theme.Foreground);
            dc.DrawText(label, new Point(x + (width - label.WidthIncludingTrailingWhitespace) / 2, y + (CapHeight - 1.5 - label.Height) / 2));
        }

        /// <summary>Gives every block a page, column and y. A heading moves along with the block after it. Returns the page count.</summary>
        private static int Place(List<Block> blocks, int columns, double firstTop, double otherTop, double bottom)
        {
            int page = 0, column = 0;
            double y = firstTop;
            double Top() => page == 0 ? firstTop : otherTop;

            for (int i = 0; i < blocks.Count; i++)
            {
                double needed = 0;
                for (int j = i; j < blocks.Count; j++)
                {
                    needed += blocks[j].Height;
                    if (!blocks[j].KeepWithNext)
                        break;
                }
                if (y + needed > bottom && y > Top())
                {
                    column++;
                    if (column == columns)
                    {
                        column = 0;
                        page++;
                    }
                    y = Top();

                    // In the middle of a group: say which one, so the column can be read on its own.
                    var block = blocks[i];
                    if (!block.IsGroupHeading && block.Group != null)
                    {
                        block.Continued = Localizer.Format(Strings.ExportContinuedFormat,
                            block.IsRow && block.Subgroup != null ? block.Group + " · " + block.Subgroup : block.Group);
                        y += ContinuedHeight;
                    }
                }
                blocks[i].Page = page;
                blocks[i].Column = column;
                blocks[i].Y = y;
                y += blocks[i].Height;
            }
            return page + 1;
        }

        private static void DrawHeader(DrawingContext dc, ExportDocument document, Theme theme, TextFactory text, Size page, int index, int count)
        {
            double width = page.Width - 2 * Margin;
            if (index == 0)
            {
                var title = text.Create(document.Title, 20, theme.Foreground, bold: true, maxWidth: width);
                dc.DrawText(title, new Point(Margin, Margin - 6));
                if (!string.IsNullOrEmpty(document.Subtitle))
                    dc.DrawText(text.Create(document.Subtitle, 10.5, theme.Muted, maxWidth: width), new Point(Margin, Margin - 6 + title.Height));
            }
            else
            {
                dc.DrawText(text.Create(document.Title, 12, theme.Muted, bold: true, maxWidth: width), new Point(Margin, Margin - 6));
            }

            var footer = text.Create(string.Format(CultureInfo.InvariantCulture, "{0} / {1}", index + 1, count), 9, theme.Muted);
            dc.DrawText(footer, new Point(page.Width - Margin - footer.Width, page.Height - Margin + 4));
            dc.DrawText(text.Create(Localizer.Get(Strings.ExportFooter), 9, theme.Muted), new Point(Margin, page.Height - Margin + 4));
        }

        /// <summary>FormattedText with the same font everywhere.</summary>
        private sealed class TextFactory
        {
            private static readonly FontFamily Family = new FontFamily("Segoe UI");
            private readonly double _pixelsPerDip;

            public TextFactory(double pixelsPerDip) => _pixelsPerDip = pixelsPerDip;

            public FormattedText Create(string value, double size, Brush brush, bool bold = false, bool italic = false, double maxWidth = 0)
            {
                var typeface = new Typeface(Family, italic ? FontStyles.Italic : FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal);
                var text = new FormattedText(value ?? string.Empty, Localizer.Culture, FlowDirection.LeftToRight, typeface, size, brush ?? Brushes.Black, _pixelsPerDip);
                if (maxWidth > 0)
                {
                    text.MaxTextWidth = maxWidth;
                    text.Trimming = TextTrimming.None;
                }
                return text;
            }

            public FormattedText Cap(string value, Brush brush) => Create(value, 9.5, brush);
        }
    }
}
