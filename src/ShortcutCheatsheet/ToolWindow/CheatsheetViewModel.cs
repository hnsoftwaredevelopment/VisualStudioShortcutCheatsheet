using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Threading;
using ShortcutCheatsheet.Localization;
using ShortcutCheatsheet.Model;

namespace ShortcutCheatsheet.ToolWindow
{
    /// <summary>One stroke of a chord, and whether another follows: the comma goes after it, so a wrapped chord
    /// ends a line with "Ctrl R ," instead of starting the next one with a comma.</summary>
    internal sealed class StrokeView
    {
        public StrokeView(IReadOnlyList<string> keys, bool hasNext)
        {
            Keys = keys;
            HasNext = hasNext;
        }

        public IReadOnlyList<string> Keys { get; }
        public bool HasNext { get; }
    }
    /// <summary>One row in the list: a shortcut plus what the row needs to draw it.</summary>
    internal sealed class ShortcutItem
    {
        public ShortcutItem(Shortcut shortcut, Grouping grouping)
        {
            Shortcut = shortcut;
            var strokes = KeyChord.Parse(shortcut.Keys);
            Strokes = strokes.Select((stroke, i) => new StrokeView(stroke.Select(KeyChord.Display).ToList(), i < strokes.Count - 1)).ToList();
            (Outer, Inner) = ShortcutView.Groups(shortcut, grouping);
            ShowScope = grouping == Grouping.Keys;
            // The label says nothing new when the group heading or the category already is the extension.
            ShowExtension = shortcut.Extension != null && !shortcut.NotApplied && grouping != Grouping.Extension && shortcut.Category != shortcut.Extension;
        }

        public Shortcut Shortcut { get; }
        public IReadOnlyList<StrokeView> Strokes { get; }
        public string Outer { get; }
        public string Inner { get; }
        public string Name => Shortcut.Name;
        public string Extension => Shortcut.Extension;
        public bool IsCustom => Shortcut.IsCustom;
        public bool NotApplied => Shortcut.NotApplied;

        /// <summary>"Bookmark Studio wants these keys, but they are used by Build Solution.", for a binding VS did not apply.</summary>
        public string UsedByText => Shortcut.NotApplied
            ? Localizer.Format(Strings.UsedByFormat, Shortcut.Extension, UsedBy)
            : null;

        private string UsedBy => string.Join(", ", Shortcut.UsedBy ?? new List<string>());

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
                    lines.Add(Localizer.Format(Strings.ToolTipFromFormat, Shortcut.Extension));
                if (Shortcut.IsCustom)
                    lines.Add(Localizer.Get(Strings.ToolTipAddedByYou));
                if (NotApplied)
                    lines.Add(Localizer.Format(Strings.ToolTipNotAppliedFormat, UsedBy));
                return string.Join("\n", lines);
            }
        }

        /// <summary>One line for the clipboard: "Ctrl+E, V	Duplicate	Text Editor".</summary>
        public string ToClipboardText() => Shortcut.Keys + "\t" + Shortcut.Name + "\t" + Shortcut.Scope;
    }

    /// <summary>An entry of the grouping list. The label changes with the language, the selected value stays.</summary>
    internal sealed class GroupingOption : INotifyPropertyChanged
    {
        private string _value;

        public GroupingOption(Grouping key)
        {
            Key = key;
            _value = LocalizedEnumConverter<Grouping>.DisplayName(key);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public Grouping Key { get; }

        public string Value => _value;

        public void Refresh()
        {
            _value = LocalizedEnumConverter<Grouping>.DisplayName(Key);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
    }

    /// <summary>State of the window: the sheet, the filter, the grouping, and the list that follows from them.</summary>
    internal sealed class CheatsheetViewModel : INotifyPropertyChanged
    {
        private readonly DispatcherTimer _searchDelay;
        private CheatSheet _sheet;
        private string _searchText = string.Empty;
        private Grouping _grouping = Grouping.Scope;
        private bool _onlyExtensions, _onlyCustom, _onlyNotApplied, _expandGroups, _isBusy;
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

        // Same items with new labels after a language change: a new list would make the ComboBox lose its selection.
        public IReadOnlyList<GroupingOption> Groupings { get; } =
            System.Enum.GetValues(typeof(Grouping)).Cast<Grouping>().Select(g => new GroupingOption(g)).ToList();

        private LocalizedTexts _texts = new LocalizedTexts();

        /// <summary>The texts of the window, for {Binding Texts[Name]}.</summary>
        public LocalizedTexts Texts
        {
            get => _texts;
            private set => Set(ref _texts, value);
        }

        /// <summary>After a language change: new texts everywhere, including rows, tooltips and the summary.</summary>
        public void Relocalize()
        {
            Texts = new LocalizedTexts();
            foreach (var option in Groupings)
                option.Refresh();
            Rebuild();
        }

        public CheatSheet Sheet
        {
            get => _sheet;
            set
            {
                _sheet = value;
                // A hidden filter must not stay on, or the list would be empty for no visible reason.
                if (!HasCustom) _onlyCustom = false;
                if (!HasNotApplied) _onlyNotApplied = false;
                OnPropertyChanged(nameof(HasCustom));
                OnPropertyChanged(nameof(HasNotApplied));
                OnPropertyChanged(nameof(OnlyCustom));
                OnPropertyChanged(nameof(OnlyNotApplied));
                Rebuild();
            }
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

        public bool OnlyNotApplied
        {
            get => _onlyNotApplied;
            set { if (Set(ref _onlyNotApplied, value)) Rebuild(); }
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

        /// <summary>A filter only shows when it can find something: no own shortcuts means no "Only added by you".</summary>
        public bool HasCustom => _sheet?.Shortcuts.Any(s => s.IsCustom) == true;

        public bool HasNotApplied => _sheet?.Shortcuts.Any(s => s.NotApplied) == true;

        /// <summary>The visible rows, in order: what an export of "what you see" would contain.</summary>
        public IEnumerable<Shortcut> Visible => _items?.Cast<ShortcutItem>().Select(i => i.Shortcut) ?? Enumerable.Empty<Shortcut>();

        private void Rebuild()
        {
            if (_sheet == null)
            {
                Items = null;
                return;
            }

            var filter = Filter;
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

        public ShortcutFilter Filter => new ShortcutFilter { Text = _searchText, OnlyExtensions = _onlyExtensions, OnlyCustom = _onlyCustom, OnlyNotApplied = _onlyNotApplied };

        /// <summary>The window as an export: what it shows, in its grouping. Null before the sheet is loaded.</summary>
        public Export.ExportDocument CreateExport() =>
            _sheet == null ? null : Export.ExportDocument.Create(_sheet, Filter, _grouping, DescribeFilter(), DateTimeOffset.Now);

        /// <summary>The filter in a few words for the export's subtitle: "search 'debug' · only extensions".</summary>
        private string DescribeFilter()
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(_searchText)) parts.Add(Localizer.Format(Strings.FilterSearchFormat, _searchText.Trim()));
            if (_onlyExtensions) parts.Add(Localizer.Get(Strings.FilterOnlyExtensions));
            if (_onlyCustom) parts.Add(Localizer.Get(Strings.FilterOnlyCustom));
            if (_onlyNotApplied) parts.Add(Localizer.Get(Strings.FilterOnlyNotApplied));
            return parts.Count == 0 ? null : string.Join(" · ", parts);
        }

        private string Describe(int shown)
        {
            // Not-applied rows are on the sheet but do not work: they are not counted as shortcuts.
            var working = _sheet.Shortcuts.Where(s => !s.NotApplied).ToList();
            int fromExtensions = working.Count(s => s.Extension != null);
            int extensions = working.Where(s => s.Extension != null).Select(s => s.Extension).Distinct().Count();
            int custom = working.Count(s => s.IsCustom);
            int notApplied = _sheet.Shortcuts.Count - working.Count;
            // Leave out what is zero: "0 added by you" is noise.
            var parts = new List<string> { working.Count == 1 ? Localizer.Get(Strings.SummaryShortcutsOne) : Localizer.Format(Strings.SummaryShortcutsFormat, working.Count) };
            if (fromExtensions > 0) parts.Add(Localizer.Format(Strings.SummaryFromExtensionsFormat, fromExtensions, extensions));
            if (custom > 0) parts.Add(Localizer.Format(Strings.SummaryAddedByYouFormat, custom));
            if (notApplied > 0) parts.Add(Localizer.Format(Strings.SummaryNotAppliedFormat, notApplied));
            string text = Localizer.Format(Strings.SummaryFormat, string.Join(Localizer.Get(Strings.SummaryListSeparator), parts));
            return shown == _sheet.Shortcuts.Count ? text : text + " " + Localizer.Format(Strings.SummaryShowingFormat, shown);
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
