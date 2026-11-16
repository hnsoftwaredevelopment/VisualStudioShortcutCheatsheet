using System;
using System.Collections.Generic;
using System.Linq;
using ShortcutCheatsheet.Model;

namespace ShortcutCheatsheet.Export
{
    /// <summary>A heading with its shortcuts; Name is null for the flat list ("Keys (no groups)").</summary>
    internal sealed class ExportGroup
    {
        public ExportGroup(string name, IReadOnlyList<ExportSubgroup> subgroups)
        {
            Name = name;
            Subgroups = subgroups;
        }

        public string Name { get; }
        public IReadOnlyList<ExportSubgroup> Subgroups { get; }
        public int Count => Subgroups.Sum(s => s.Shortcuts.Count);
    }

    internal sealed class ExportSubgroup
    {
        public ExportSubgroup(string name, IReadOnlyList<Shortcut> shortcuts)
        {
            Name = name;
            Shortcuts = shortcuts;
        }

        public string Name { get; }
        public IReadOnlyList<Shortcut> Shortcuts { get; }
    }

    /// <summary>
    /// What an export contains: the shortcuts the window shows, with its filter and grouping. The data formats
    /// (JSON, XML, CSV) use <see cref="Sheet"/>; the readable formats use <see cref="Groups"/>.
    /// </summary>
    internal sealed class ExportDocument
    {
        public const string DefaultTitle = "Visual Studio shortcuts";

        public string Title { get; set; } = DefaultTitle;

        /// <summary>"Community 18.0 · 13 November 2026 · only extensions".</summary>
        public string Subtitle { get; set; }

        public CheatSheet Sheet { get; set; }
        public IReadOnlyList<ExportGroup> Groups { get; set; }

        public IEnumerable<Shortcut> Shortcuts => Groups.SelectMany(g => g.Subgroups).SelectMany(s => s.Shortcuts);

        /// <summary>
        /// Builds the document from a full sheet: filter, sort and group exactly like the window does.
        /// <paramref name="filterText"/> is a short description of the filter for the subtitle, or null.
        /// </summary>
        public static ExportDocument Create(CheatSheet sheet, ShortcutFilter filter, Grouping grouping, string filterText, DateTimeOffset now)
        {
            var visible = ShortcutView.Sort(sheet.Shortcuts.Where(s => ShortcutView.Matches(s, filter)), grouping);

            var groups = new List<ExportGroup>();
            if (grouping == Grouping.Keys)
            {
                groups.Add(new ExportGroup(null, new[] { new ExportSubgroup(null, visible) }));
            }
            else
            {
                // Sort has already put them in order; group without changing it.
                foreach (var outer in visible.GroupBy(s => ShortcutView.Groups(s, grouping).Outer))
                {
                    var subgroups = outer.GroupBy(s => ShortcutView.Groups(s, grouping).Inner)
                        .Select(inner => new ExportSubgroup(inner.Key, inner.ToList()))
                        .ToList();
                    groups.Add(new ExportGroup(outer.Key, subgroups));
                }
            }

            var parts = new List<string>();
            if (!string.IsNullOrEmpty(sheet.VisualStudio))
                parts.Add("Visual Studio " + sheet.VisualStudio);
            // In the language of the extension, not of Windows: "16 November 2026" in an English sheet.
            parts.Add(now.ToString("D", Localization.Localizer.Culture));
            if (!string.IsNullOrEmpty(filterText))
                parts.Add(filterText);

            return new ExportDocument
            {
                Subtitle = string.Join(" · ", parts),
                Groups = groups,
                Sheet = new CheatSheet
                {
                    FormatVersion = sheet.FormatVersion,
                    Generated = sheet.Generated,
                    VisualStudio = sheet.VisualStudio,
                    Shortcuts = visible,
                    Extensions = sheet.Extensions,
                    RemovedByYou = sheet.RemovedByYou,
                },
            };
        }
    }
}
