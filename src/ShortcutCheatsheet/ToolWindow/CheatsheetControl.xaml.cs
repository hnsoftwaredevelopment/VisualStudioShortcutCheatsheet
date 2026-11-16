using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ShortcutCheatsheet.ToolWindow
{
    /// <summary>The content of the tool window. Buttons are forwarded to the pane, which owns VS access.</summary>
    public partial class CheatsheetControl : UserControl
    {
        internal CheatsheetControl(CheatsheetViewModel model)
        {
            InitializeComponent();
            DataContext = model;
        }

        internal event EventHandler RefreshRequested;
        internal event EventHandler ExportRequested;

        private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        private void Export_Click(object sender, RoutedEventArgs e) => ExportRequested?.Invoke(this, EventArgs.Empty);

        /// <summary>Ctrl+C or Copy in the context menu: one line per selected shortcut, tab-separated for Excel.</summary>
        private void Copy_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var lines = List.SelectedItems.Cast<ShortcutItem>().Select(i => i.ToClipboardText()).ToList();
            if (lines.Count > 0)
                Clipboard.SetText(string.Join(Environment.NewLine, lines));
        }

        /// <summary>Puts the cursor in the search box when the window opens.</summary>
        internal void FocusSearch() => Search.Focus();
    }
}
