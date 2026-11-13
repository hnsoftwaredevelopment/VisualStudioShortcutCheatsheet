using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using ShortcutCheatsheet.Collecting;
using ShortcutCheatsheet.Export;
using Task = System.Threading.Tasks.Task;

namespace ShortcutCheatsheet.ToolWindow
{
    /// <summary>
    /// The tool window. For now: a summary of what was collected and an export to JSON, to check the data.
    /// The cheat sheet view itself follows in step 4.
    /// </summary>
    [Guid("68b99b08-124c-4da3-ace2-dcd8c36dbd5a")]
    public sealed class CheatsheetWindow : ToolWindowPane
    {
        private readonly TextBlock _summary = new TextBlock { Margin = new Thickness(12, 8, 12, 8), TextWrapping = TextWrapping.Wrap };
        private readonly Button _refresh = new Button { Content = "Refresh", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 0, 8, 0) };
        private readonly Button _export = new Button { Content = "Export to JSON...", Padding = new Thickness(8, 1, 8, 1), IsEnabled = false };

        private AsyncPackage _package;
        private CancellationTokenSource _loading;
        private Collection _collection;

        public CheatsheetWindow() : base(null)
        {
            Caption = "Shortcut Cheatsheet";
            BitmapImageMoniker = KnownMonikers.Keyboard;

            _summary.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            _refresh.SetResourceReference(FrameworkElement.StyleProperty, VsResourceKeys.ButtonStyleKey);
            _export.SetResourceReference(FrameworkElement.StyleProperty, VsResourceKeys.ButtonStyleKey);
            _refresh.Click += (s, e) => StartLoading();
            _export.Click += (s, e) => { ThreadHelper.ThrowIfNotOnUIThread(); Export(); };

            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 8, 12, 0) };
            bar.Children.Add(_refresh);
            bar.Children.Add(_export);
            var root = new DockPanel { LastChildFill = true };
            root.SetResourceReference(Panel.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
            DockPanel.SetDock(bar, Dock.Top);
            root.Children.Add(bar);
            root.Children.Add(new ScrollViewer { Content = _summary, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            Content = root;
        }

        /// <summary>Called by VS after the pane is sited; Package is known here, not in the constructor.</summary>
        protected override void Initialize()
        {
            base.Initialize();
            _package = (AsyncPackage)Package;
            StartLoading();
        }

        private void StartLoading()
        {
            _loading?.Cancel();
            _loading = new CancellationTokenSource();
            var token = _loading.Token;
            _package.JoinableTaskFactory.RunAsync(() => LoadAsync(token)).FileAndForget("ShortcutCheatsheet/Load");
        }

        private async Task LoadAsync(CancellationToken token)
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync(token);
            _summary.Text = "Collecting shortcuts...";
            _export.IsEnabled = false;
            try
            {
                var collection = await CheatSheetCollector.CollectAsync(_package, token);
                await _package.JoinableTaskFactory.SwitchToMainThreadAsync(token);
                _collection = collection;
                _summary.Text = Describe(collection);
                _export.IsEnabled = true;
            }
            catch (OperationCanceledException)
            {
                // A newer load replaced this one.
            }
            catch (Exception ex)
            {
                await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                _summary.Text = "Collecting failed: " + ex.Message;
            }
        }

        private static string Describe(Collection c)
        {
            var sheet = c.Sheet;
            int fromExtensions = sheet.Shortcuts.Count(s => s.Extension != null);
            int conflicts = sheet.Shortcuts.Count(s => s.ConflictsWith != null);
            int custom = sheet.Shortcuts.Count(s => s.IsCustom);
            int scopes = sheet.Shortcuts.Select(s => s.Scope).Distinct().Count();
            string text =
                $"{sheet.Shortcuts.Count} shortcuts in {scopes} scopes, from {c.Commands} commands ({c.DteMilliseconds} ms).\n" +
                $"{fromExtensions} from {sheet.Extensions.Count(e => e.Shortcuts > 0)} extensions (extensions read in {c.OriginMilliseconds} ms).\n" +
                $"{custom} added by you, {sheet.RemovedByYou} removed by you.\n" +
                $"{conflicts} shortcuts share their keys with another command in the same scope.\n\n" +
                string.Join("\n", sheet.Extensions.Where(e => e.Shortcuts > 0 || e.Problem != null)
                    .Select(e => $"{e.Name} {e.Version}: {e.Shortcuts}" + (e.Problem == null ? "" : " (" + e.Problem + ")")));
            return c.Problem == null ? text : text + "\n\nExtension Manager not available, read the folders instead: " + c.Problem;
        }

        private void Export()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_collection == null)
                return;
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export shortcuts",
                FileName = "Shortcuts.json",
                Filter = "JSON (*.json)|*.json",
                DefaultExt = ".json",
            };
            if (dialog.ShowDialog() != true)
                return;
            try
            {
                using (var file = File.Create(dialog.FileName))
                    JsonExporter.Write(_collection.Sheet, file);
                _summary.Text = Describe(_collection) + "\n\nSaved to " + dialog.FileName;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _summary.Text = Describe(_collection) + "\n\nSaving failed: " + ex.Message;
            }
        }
    }
}
