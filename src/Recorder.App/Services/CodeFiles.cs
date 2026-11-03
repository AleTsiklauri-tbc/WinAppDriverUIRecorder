using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Recorder.App.Resources;
using Recorder.App.ViewModels;
using Recorder.App.Views;
using Recorder.Core.CodeGen;
using Recorder.Core.Persistence;

namespace Recorder.App.Services;

/// <summary>
/// Writes generated code (spec 10.4): Save as for one class, Export for all of them, and Merge into an existing file with a
/// side-by-side preview and a .bak copy written first (spec 10.5). New files are UTF-8 without BOM (spec 10.3); a merged
/// file keeps its byte order mark and line breaks. UI thread only, and never while recording: the dialogs are modal.
/// </summary>
public sealed partial class CodeFiles
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private readonly SettingsStore _settings;
    private readonly ToastService _toasts;
    private readonly IClipboardService _clipboard;
    private readonly ILogger<CodeFiles> _logger;

    public CodeFiles(SettingsStore settings, ToastService toasts, IClipboardService clipboard, ILogger<CodeFiles> logger) =>
        (_settings, _toasts, _clipboard, _logger) = (settings, toasts, clipboard, logger);

    public void SaveAs(Window owner, GeneratedFile file, CodeProfile profile)
    {
        ArgumentNullException.ThrowIfNull(file);
        var dialog = new SaveFileDialog
        {
            Filter = Filter(profile),
            DefaultExt = profile.Settings.FileExtension,
            AddExtension = true,
            FileName = file.FileName,
            InitialDirectory = Folder(),
        };
        if (dialog.ShowDialog(owner) == true && Write(() => File.WriteAllText(dialog.FileName, file.Text, Utf8), "save"))
        {
            Remember(dialog.FileName);
            _toasts.Show(Text.Format(Strings.Toast_Saved, Path.GetFileName(dialog.FileName)));
        }
    }

    /// <summary>Every class into one folder, one file each; asks before replacing files already there.</summary>
    public void Export(Window owner, IReadOnlyList<GeneratedFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var dialog = new OpenFolderDialog { Title = Strings.Code_Export_Title, InitialDirectory = Folder() };
        if (dialog.ShowDialog(owner) != true)
        {
            return;
        }

        var targets = files.Select(file => (file.Text, Path: Path.Combine(dialog.FolderName, file.FileName))).ToList();
        var replaced = targets.Count(target => File.Exists(target.Path));
        if (replaced > 0
            && MessageBox.Show(owner, Text.Format(Strings.Code_Export_Replace, replaced), Strings.Code_Export_Replace_Title, MessageBoxButton.OKCancel, MessageBoxImage.Warning)
                != MessageBoxResult.OK)
        {
            return;
        }

        if (Write(() => targets.ForEach(target => File.WriteAllText(target.Path, target.Text, Utf8)), "export"))
        {
            _settings.Update(settings => settings with { LastCodeFolder = dialog.FolderName });
            LogExported(_logger, targets.Count);
            _toasts.Show(Text.Format(Strings.Toast_Exported, targets.Count, Path.GetFileName(dialog.FolderName)));
        }
    }

    /// <summary>Merges one class into a file the user picks, after showing what changes (spec 10.5).</summary>
    public void Merge(Window owner, GeneratedFile file, CodeProfile profile)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(profile);
        var dialog = new OpenFileDialog { Filter = Filter(profile), InitialDirectory = Folder(), Title = Text.Format(Strings.Code_Merge_Title, file.ClassName) };
        if (dialog.ShowDialog(owner) != true)
        {
            return;
        }

        var path = dialog.FileName;
        var name = Path.GetFileName(path);
        if (!TryRead(path, out var existing, out var hasBom))
        {
            return;
        }

        Remember(path);
        var result = CodeMerger.Merge(existing, file.Text, profile.Settings.Merge);
        if (result.IsUnderstood && !result.HasChanges && !result.HasNotes)
        {
            _toasts.Show(Text.Format(Strings.Merge_NothingNew, name));
            return;
        }

        var preview = new MergePreviewViewModel(name, existing, result, _clipboard, _toasts);
        if (new MergePreviewWindow { Owner = owner, DataContext = preview }.ShowDialog() != true)
        {
            return;
        }

        // The backup first: if anything fails after it, the original is still there.
        var written = Write(() => File.Copy(path, path + ".bak", overwrite: true), "back up")
            && Write(() => File.WriteAllText(path, result.Text, hasBom ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: true) : Utf8), "merge");
        if (written)
        {
            LogMerged(_logger, result.AddedMembers.Count, result.AddedMethods.Count, result.AddedImports.Count);
            _toasts.Show(Text.Format(Strings.Toast_CodeMerged, name, name + ".bak"));
        }
    }

    private bool TryRead(string path, out string text, out bool hasBom)
    {
        (text, hasBom) = (string.Empty, false);
        try
        {
            var bytes = File.ReadAllBytes(path);
            hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            text = StrictUtf8.GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));
            return true;
        }
        catch (DecoderFallbackException)
        {
            // Rewriting it as UTF-8 would change every non-ASCII character in it.
            _toasts.Show(Text.Format(Strings.Merge_NotUtf8, Path.GetFileName(path)));
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogFailed(_logger, "read", exception.GetType().Name, exception.HResult);
            _toasts.Show(Text.Format(Strings.Error_CodeReadFailed, exception.Message));
            return false;
        }
    }

    private bool Write(Action write, string operation)
    {
        try
        {
            write();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogFailed(_logger, operation, exception.GetType().Name, exception.HResult);
            _toasts.Show(Text.Format(Strings.Error_CodeSaveFailed, exception.Message));
            return false;
        }
    }

    private static string Filter(CodeProfile profile) =>
        Text.Format(Strings.Code_FileFilter, profile.Settings.Highlighting, profile.Settings.FileExtension);

    private string Folder() =>
        _settings.Current.LastCodeFolder is { } folder && Directory.Exists(folder) ? folder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    private void Remember(string path) => _settings.Update(settings => settings with { LastCodeFolder = Path.GetDirectoryName(path) });

    // Counts and error types only: paths can name customers.
    [LoggerMessage(EventId = 910, Level = LogLevel.Information, Message = "Code exported ({Files} files)")]
    private static partial void LogExported(ILogger logger, int files);

    [LoggerMessage(EventId = 911, Level = LogLevel.Information, Message = "Code merged: {Members} members, {Methods} methods, {Imports} imports added")]
    private static partial void LogMerged(ILogger logger, int members, int methods, int imports);

    [LoggerMessage(EventId = 912, Level = LogLevel.Warning, Message = "Could not {Operation} a code file: {ErrorType} (HRESULT 0x{HResult:X8})")]
    private static partial void LogFailed(ILogger logger, string operation, string errorType, int hresult);
}
