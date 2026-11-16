using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Threading;
using ShortcutCheatsheet.Model;

namespace ShortcutCheatsheet.ToolWindow
{
    /// <summary>One row in the list: a shortcut plus what the row needs to draw it.</summary>
    internal sealed class ShortcutItem
    {
        public ShortcutItem(Shortcut shortcut, Grouping grouping)
        {
            Shortcut = shortcut;
            Strokes = KeyChord.Parse(shortcut.Keys);
            (Outer, Inner) = ShortcutView.Groups(shortcut, grouping);
            ShowScope = grouping == Grouping.Keys;
            // The label says nothing new when the group heading or the category already is the extension.
            ShowExtension = shortcut.Extension != null && grouping != Grouping.Extension && shortcut.Category != shortcut.Extension;
        }

        public Shortcut Shortcut { get; }
        public IReadOnlyList<IReadOnlyList<string>> Strokes { get; }
        public string Outer { get; }
        public string Inner { get; }
        public string Name => Shortcut.Name;
        public string Extension => Shortcut.Extension;
        public bool IsCustom => Shortcut.IsCustom;
        public bool HasConflict => Shortcut.ConflictsWith != null;

        /// <summary>Without groups the scope is not visible elsewhere, so the row shows it.</summary>
        public bool ShowScope { get; }

        /// <summary>Show the extension label in the row.</summary>
        public bool ShowExtension { get; }

        /// <summary>The scope, shown in the row when the list has no groups.</summary>
        public string Scope => Shortcut.Scope;

        public string ToolTip
        {
            get
            {
                var lines = new List<string>();
                if (!string.IsNullOrEmpty(Shortcut.Command))
                    lines.Add(Shortcut.Command);
                lines.Add(Shortcut.Scope + ": " + Shortcut.Keys);
                if (Shortcut.Extension != null)
                    lines.Add("From " + Shortcut.Extension);
                if (Shortcut.IsCustom)
                    lines.Add("Added by you");
                if (HasConflict)
                    lines.Add("Same keys as: " + string.Join(", ", Shortcut.ConflictsWith));
                return string.Join("\n", lines);
            }
        }

        /// <summary>One line for the clipboard: "Ctrl+E, V	Duplicate	Text Editor".</summary>
        public string ToClipboardText() => Shortcut.Keys + "\t" + Shortcut.Name + "\t" + Shortcut.Scope;
    }

    /// <summary>State of the window: the sheet, the filter, the grouping, and the list that follows from them.</summary>
    internal sealed class CheatsheetViewModel : INotifyPropertyChanged
    {
        private readonly DispatcherTimer _searchDelay;
        private CheatSheet _sheet;
        private string _searchText = string.Empty;
        private Grouping _grouping = Grouping.Scope;
        private bool _onlyExtensions, _onlyCustom, _onlyConflicts, _expandGroups, _isBusy;
        private ListCollectionView _items;
        private string _summary = string.Empty;

        public CheatsheetViewModel()
        {
            // Rebuilding on every key press makes typing stutter with a thousand rows.
            _searchDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _searchDelay.Tick += (s, e) =>
            {
                _searchDelay.Stop();
                Rebuild();
            };
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public IReadOnlyList<KeyValuePair<Grouping, string>> Groupings { get; } = new[]
        {
            new KeyValuePair<Grouping, string>(Grouping.Scope, "Where it works"),
            new KeyValuePair<Grouping, string>(Grouping.Category, "Category"),
            new KeyValuePair<Grouping, string>(Grouping.Extension, "Extension"),
            new KeyValuePair<Grouping, string>(Grouping.Keys, "Keys (no groups)"),
        };

        public CheatSheet Sheet
        {
            get => _sheet;
            set { _sheet = value; Rebuild(); }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (Set(ref _searchText, value ?? string.Empty))
                {
                    _searchDelay.Stop();
                    _searchDelay.Start();
                }
            }
        }

        public Grouping Grouping
        {
            get => _grouping;
            set { if (Set(ref _grouping, value)) Rebuild(); }
        }

        public bool OnlyExtensions
        {
            get => _onlyExtensions;
            set { if (Set(ref _onlyExtensions, value)) Rebuild(); }
        }

        public bool OnlyCustom
        {
            get => _onlyCustom;
            set { if (Set(ref _onlyCustom, value)) Rebuild(); }
        }

        public bool OnlyConflicts
        {
            get => _onlyConflicts;
            set { if (Set(ref _onlyConflicts, value)) Rebuild(); }
        }

        /// <summary>Groups open by default only when the list is short or filtered; 70 open scopes is a wall.</summary>
        public bool ExpandGroups
        {
            get => _expandGroups;
            private set => Set(ref _expandGroups, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            set => Set(ref _isBusy, value);
        }

        public ListCollectionView Items
        {
            get => _items;
            private set => Set(ref _items, value);
        }

        public string Summary
        {
            get => _summary;
            set => Set(ref _summary, value);
        }

        public bool IsGrouped => _grouping != Grouping.Keys;

        /// <summary>The visible rows, in order: what an export of "what you see" would contain.</summary>
        public IEnumerable<Shortcut> Visible => _items?.Cast<ShortcutItem>().Select(i => i.Shortcut) ?? Enumerable.Empty<Shortcut>();

        private void Rebuild()
        {
            if (_sheet == null)
            {
                Items = null;
                return;
            }

            var filter = new ShortcutFilter { Text = _searchText, OnlyExtensions = _onlyExtensions, OnlyCustom = _onlyCustom, OnlyConflicts = _onlyConflicts };
            var shown = ShortcutView.Sort(_sheet.Shortcuts.Where(s => ShortcutView.Matches(s, filter)), _grouping)
                .Select(s => new ShortcutItem(s, _grouping))
                .ToList();

            var view = new ListCollectionView(shown);
            if (_grouping != Grouping.Keys)
            {
                view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ShortcutItem.Outer)));
                view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ShortcutItem.Inner)));
            }
            ExpandGroups = !filter.IsEmpty || shown.Count <= 150;
            Items = view;
            OnPropertyChanged(nameof(IsGrouped));
            Summary = Describe(shown.Count);
        }

        private string Describe(int shown)
        {
            var sheet = _sheet;
            int fromExtensions = sheet.Shortcuts.Count(s => s.Extension != null);
            int extensions = sheet.Shortcuts.Where(s => s.Extension != null).Select(s => s.Extension).Distinct().Count();
            int conflicts = sheet.Shortcuts.Count(s => s.ConflictsWith != null);
            int custom = sheet.Shortcuts.Count(s => s.IsCustom);
            string text = $"{sheet.Shortcuts.Count} shortcuts, {fromExtensions} from {extensions} extensions, {custom} added by you, {conflicts} with a conflict.";
            return shown == sheet.Shortcuts.Count ? text : text + $" Showing {shown}.";
        }

        private bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }

        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
