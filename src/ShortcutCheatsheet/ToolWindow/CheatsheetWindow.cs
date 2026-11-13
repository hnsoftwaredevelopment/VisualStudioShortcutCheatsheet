using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace ShortcutCheatsheet.ToolWindow
{
    /// <summary>The tool window. For now only a placeholder; the cheat sheet follows in a later step.</summary>
    [Guid("68b99b08-124c-4da3-ace2-dcd8c36dbd5a")]
    public sealed class CheatsheetWindow : ToolWindowPane
    {
        public CheatsheetWindow() : base(null)
        {
            Caption = "Shortcut Cheatsheet";
            var text = new TextBlock
            {
                Text = "Shortcut Cheatsheet is loaded.",
                Margin = new Thickness(12),
                TextWrapping = TextWrapping.Wrap,
            };
            // Follow the VS theme, like every other tool window.
            text.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            var root = new Grid { Children = { text } };
            root.SetResourceReference(Panel.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
            Content = root;
        }
    }
}
