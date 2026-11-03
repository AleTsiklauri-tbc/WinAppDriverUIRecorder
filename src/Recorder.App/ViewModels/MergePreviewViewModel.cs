using CommunityToolkit.Mvvm.Input;
using Recorder.App.Resources;
using Recorder.App.Services;
using Recorder.Core.CodeGen;

namespace Recorder.App.ViewModels;

/// <summary>
/// The merge preview (spec 10.5): the file now and after the merge side by side, what is added and what is left out, and
/// Apply or Cancel. When the file cannot be read with confidence it says why and offers to copy the new members instead.
/// </summary>
public sealed partial class MergePreviewViewModel
{
    private readonly MergeResult _result;
    private readonly IClipboardService _clipboard;
    private readonly ToastService _toasts;

    public MergePreviewViewModel(string fileName, string original, MergeResult result, IClipboardService clipboard, ToastService toasts)
    {
        ArgumentNullException.ThrowIfNull(result);
        (_result, _clipboard, _toasts) = (result, clipboard, toasts);
        Title = Text.Format(Strings.Merge_Title, fileName);
        OldHeader = fileName;
        OldText = original;
        NewText = result.Text;
        Problem = result.Problem is { } problem ? Text.Format(Strings.Merge_CannotRead, fileName, Reason(problem, result.ProblemDetail)) : null;
        Summary = result.IsUnderstood
            ? result.HasChanges
                ? Text.Format(Strings.Merge_Summary, result.AddedMembers.Count, result.AddedMethods.Count, result.AddedImports.Count)
                : Text.Format(Strings.Merge_NothingNew, fileName)
            : null;
        Notes = [
            .. result.SameLocators.Select(same => Text.Format(Strings.Merge_SameLocator, same.Generated, same.Existing)),
            .. result.NameTaken.Select(name => Text.Format(Strings.Merge_NameTaken, name)),
        ];
    }

    public string Title { get; }

    public string OldHeader { get; }

    public string NewHeader { get; } = Strings.Merge_NewHeader;

    public string OldText { get; }

    public string NewText { get; }

    /// <summary>Why the file could not be merged into; null when it can be.</summary>
    public string? Problem { get; }

    public bool IsUnderstood => _result.IsUnderstood;

    public string? Summary { get; }

    /// <summary>Generated members left out, and why.</summary>
    public IReadOnlyList<string> Notes { get; }

    public bool CanApply => _result.IsUnderstood && _result.HasChanges;

    public bool CanCopyNewMembers => _result.NewMembers.Length > 0;

    [RelayCommand(CanExecute = nameof(CanCopyNewMembers))]
    private void CopyNewMembers() => _toasts.Show(_clipboard.TrySetText(_result.NewMembers) ? Strings.Toast_Copied : Strings.Error_ClipboardBusy);

    private static string Reason(MergeProblem problem, string? detail) => problem switch
    {
        MergeProblem.NoPatterns => Strings.Merge_Problem_NoPatterns,
        MergeProblem.InvalidPattern => Text.Format(Strings.Merge_Problem_InvalidPattern, detail),
        MergeProblem.PatternTooSlow => Strings.Merge_Problem_PatternTooSlow,
        MergeProblem.GeneratedUnreadable => Strings.Merge_Problem_GeneratedUnreadable,
        MergeProblem.UnbalancedBraces => Strings.Merge_Problem_UnbalancedBraces,
        MergeProblem.NoClass => Strings.Merge_Problem_NoClass,
        MergeProblem.SeveralClasses => Strings.Merge_Problem_SeveralClasses,
        MergeProblem.BodyNotOnItsOwnLine => Strings.Merge_Problem_BodyNotOnItsOwnLine,
        MergeProblem.ClosingBraceNotOnItsOwnLine => Strings.Merge_Problem_ClosingBraceNotOnItsOwnLine,
        _ => Strings.Merge_Problem_TextBlocks,
    };
}
