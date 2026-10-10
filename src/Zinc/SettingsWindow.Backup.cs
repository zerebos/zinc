using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Zinc.Settings;
using Zinc.Terminal;

namespace Zinc;

/// <summary>
/// About's "Settings backup" row: Export settings (config, color schemes, shaders, the session: one zip) and Import
/// settings (asks first; Core <see cref="SettingsBackup"/> saves what's there into backups\ before writing). The
/// config watcher applies an import; its session waits for the next start.
/// </summary>
internal sealed partial class SettingsWindow
{
    // The last export/import's outcome: kept across renders (an import's config reload re-renders the page).
    private string? _backupStatus;
    private string? _backupStatusFile;

    private static string BackupVersion => TerminalSession.Version.Replace("zinc ", "");

    private string ConfigFolder => Path.GetDirectoryName(Path.GetFullPath(_config.Path))!;

    private void BackupRow()
    {
        _page.Children.Add(Row("Settings backup",
            "Your settings, color schemes, shaders and open windows in one zip file, to keep or to take to another PC.",
            ButtonRow(("Export settings", () => _ = ExportSettings()), ("Import settings", () => _ = ImportSettings()))));
        if (_backupStatus != null)
        {
            var note = Note(_backupStatus);
            _page.Children.Add(note);
            if (_backupStatusFile is { } file)
            {
                _page.Children.Add(new HyperlinkButton
                {
                    Content = "Open folder",
                    Padding = new Thickness(2, 2, 0, 0),
                    Command = new RelayCommand(() => Reveal(file)),
                });
            }
        }
    }

    private void ShowBackupStatus(string text, string? file = null)
    {
        _backupStatus = text;
        _backupStatusFile = file;
        Render();
    }

    private async Task ExportSettings()
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = $"Zinc settings {DateTime.Now:yyyy-MM-dd}" };
            picker.FileTypeChoices.Add("Zip file", [".zip"]);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            if (await picker.PickSaveFileAsync() is not { } file)
            {
                return;
            }

            App.Instance.SaveSessionNow(); // the windows as they are now
            int count;
            using (var stream = new FileStream(file.Path, FileMode.Create, FileAccess.Write))
            {
                count = SettingsBackup.Write(stream, ConfigFolder, App.Instance.SessionPath, BackupVersion, DateTimeOffset.Now);
            }

            // Includes, shaders and shader folders the config names elsewhere: copy them yourself.
            var leftOut = SettingsBackup.LeftOut(_config);
            Log.Write($"backup: exported {count} files to {file.Path}; left out {leftOut.Count}: {string.Join(", ", leftOut)}");
            ShowBackupStatus($"Exported {Files(count)} to {file.Path}." + (leftOut.Count == 0 ? "" :
                $" Your settings also use files outside Zinc's settings folder, which aren't in it: {string.Join(", ", leftOut)}. " +
                "Copy them yourself to take them to another PC."), file.Path);
        }
        catch (Exception ex)
        {
            Log.Write($"backup: export failed: {ex}");
            ShowBackupStatus($"Couldn't export your settings ({ex.Message.TrimEnd('.', ' ')}). Try saving to another folder.");
        }
    }

    private async Task ImportSettings()
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".zip");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            if (await picker.PickSingleFileAsync() is not { } file)
            {
                return;
            }

            using var stream = new MemoryStream(await File.ReadAllBytesAsync(file.Path));
            var (contents, problem) = SettingsBackup.Read(stream);
            if (contents == null)
            {
                await new ContentDialog
                {
                    Title = "Can't import this file",
                    Content = problem,
                    CloseButtonText = "Close",
                    XamlRoot = _page.XamlRoot,
                }.ShowAsync();
                return;
            }

            var confirm = new ContentDialog
            {
                Title = "Import settings?",
                Content = new TextBlock { Text = ImportSummary(contents, App.Instance.SessionPath != null), TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = "Import settings",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = _page.XamlRoot,
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            stream.Position = 0;
            var result = SettingsBackup.Import(stream, ConfigFolder, App.Instance.SessionPath, BackupVersion, DateTimeOffset.Now);
            Log.Write($"backup: imported {result.Files} files from {file.Path}{(result.Session ? " and a session for the next start" : "")}, " + $"skipped {result.Skipped}; before: {result.SafetyCopy}");
            ShowBackupStatus(ImportDone(result), result.SafetyCopy);
        }
        catch (ImportFailedException ex)
        {
            // Stopped partway... some files are the backup's, the rest are not imported
            Log.Write($"backup: import failed after {ex.Written} files: {ex}");
            ShowBackupStatus($"The import stopped partway: {ex.Message}. Your settings from before are in " + $"{Path.GetFileName(ex.SafetyCopy)}: import that file to go back.", ex.SafetyCopy);
        }
        catch (Exception ex)
        {
            Log.Write($"backup: import failed: {ex}");
            ShowBackupStatus($"Couldn't import the settings ({ex.Message.TrimEnd('.', ' ')}). Nothing was changed: check the file can be opened, and try again.");
        }
    }

    private static string Files(int count) => count == 1 ? "1 file" : $"{count} files";

    /// <summary>What an import did, for the row's status.</summary>
    private static string ImportDone(ImportResult result)
    {
        // TODO: we should do proper i18n with pluralization at some point...
        var done = result.Files > 0
            ? $"Imported {Files(result.Files)}." + (result.Session ? " The windows from the backup open the next time Zinc starts." : "")
            : result.Session ? "Imported the windows from the backup: they open the next time Zinc starts." : "Imported nothing: the backup has no settings.";
        var skipped = result.Skipped > 0
            ? $" {Files(result.Skipped)} in it {(result.Skipped == 1 ? "wasn't" : "weren't")} restored, as Zinc doesn't use {(result.Skipped == 1 ? "it" : "them")} (from a newer version, or not settings)."
            : "";
        return $"{done}{skipped} Your settings from before are in {Path.GetFileName(result.SafetyCopy)}.";
    }

    /// <summary>What an import will do, for the question before it.</summary>
    private static string ImportSummary(BackupContents contents, bool keepsSessions)
    {
        var from = contents.Made is { } made
            ? $"This backup was made on {made.LocalDateTime:d}{(contents.Version != null ? $" by Zinc {contents.Version}" : "")}. "
            : "";
        var parts = new List<string>();
        if (contents.Count("themes") is > 0 and var schemes)
        {
            parts.Add(schemes == 1 ? "1 color scheme" : $"{schemes} color schemes");
        }

        if (contents.Count("shaders") is > 0 and var shaders)
        {
            parts.Add(shaders == 1 ? "1 shader" : $"{shaders} shaders");
        }

        var adds = parts.Count > 0
            ? $" It adds {JoinAnd(parts)}, replacing yours with the same names; the others stay."
            : "";
        var windows = contents.HasSession && keepsSessions ? " The windows in it open the next time Zinc starts." : "";
        return $"{from}It replaces your settings. They're saved first, in the backups folder next to config.toml.{adds}{windows}";
    }

    private static string JoinAnd(List<string> parts) => parts.Count switch
    {
        1 => parts[0],
        2 => $"{parts[0]} and {parts[1]}",
        _ => string.Join(", ", parts[..^1]) + " and " + parts[^1],
    };
}
