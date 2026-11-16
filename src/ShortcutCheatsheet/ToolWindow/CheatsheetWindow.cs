using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using ShortcutCheatsheet.Collecting;
using ShortcutCheatsheet.Export;
using Task = System.Threading.Tasks.Task;

namespace ShortcutCheatsheet.ToolWindow
{
    /// <summary>
    /// View > Other Windows > Shortcut Cheatsheet. The pane collects (VS access) and exports; CheatsheetControl
    /// draws, CheatsheetViewModel filters and groups.
    /// </summary>
    [Guid("68b99b08-124c-4da3-ace2-dcd8c36dbd5a")]
    public sealed class CheatsheetWindow : ToolWindowPane
    {
        private readonly CheatsheetViewModel _model = new CheatsheetViewModel();
        private readonly CheatsheetControl _control;
        private AsyncPackage _package;
        private CancellationTokenSource _loading;

        public CheatsheetWindow() : base(null)
        {
            Caption = "Shortcut Cheatsheet";
            BitmapImageMoniker = ImageMonikers.Cheatsheet;
            _control = new CheatsheetControl(_model);
            _control.RefreshRequested += (s, e) => StartLoading();
            _control.ExportRequested += (s, e) => { ThreadHelper.ThrowIfNotOnUIThread(); Export(); };
            Content = _control;
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
            _model.IsBusy = true;
            try
            {
                var collection = await CheatSheetCollector.CollectAsync(_package, token);
                await _package.JoinableTaskFactory.SwitchToMainThreadAsync(token);
                Diagnostics.Log(collection);
                _model.Sheet = collection.Sheet;
                _control.FocusSearch();
            }
            catch (OperationCanceledException)
            {
                // A newer load replaced this one.
            }
            catch (Exception ex)
            {
                await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                ActivityLog.TryLogError("Shortcut Cheatsheet", "Collecting shortcuts failed: " + ex);
                _model.Summary = "The shortcuts could not be collected. Click Refresh to try again.";
            }
            finally
            {
                await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                _model.IsBusy = false;
            }
        }

        private void Export()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_model.Sheet == null)
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
                    JsonExporter.Write(_model.Sheet, file);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // The user chose the file, so this is something they can act on.
                _model.Summary = "Saving failed: " + ex.Message;
            }
        }
    }
}
