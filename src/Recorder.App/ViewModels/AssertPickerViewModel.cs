using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Recorder.App.Resources;
using Recorder.Core.Model;
using Recorder.Core.Recording;

namespace Recorder.App.ViewModels;

/// <summary>One option in the assertion picker: "Value equals “5000”".</summary>
public sealed record AssertOption(AssertionChoice Choice, string Label);

/// <summary>
/// The small picker shown next to the cursor in Assert mode (spec 9.6): the assertions valid for the clicked element,
/// pre-filled with its current values. Enter confirms the highlighted one, Esc cancels.
/// </summary>
public sealed partial class AssertPickerViewModel : ObservableObject
{
    public AssertPickerViewModel(ElementSnapshot element, IReadOnlyList<AssertionChoice> choices)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(choices);
        Element = element;
        Title = Text.Format(Strings.Assert_Title, ElementLabel.For(element));
        Options = [.. choices.Select(choice => new AssertOption(choice, Label(choice)))];
        Selected = Options.Count > 0 ? Options[0] : null;
    }

    /// <summary>Raised when the picker should close; <see cref="Chosen"/> says what was picked.</summary>
    public event EventHandler? CloseRequested;

    public ElementSnapshot Element { get; }

    public string Title { get; }

    public IReadOnlyList<AssertOption> Options { get; }

    [ObservableProperty]
    public partial AssertOption? Selected { get; set; }

    public AssertionChoice? Chosen { get; private set; }

    public static string Label(AssertionChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        return choice.Kind switch
        {
            StepKind.AssertChecked => choice.State == true ? Strings.Assert_Checked : Strings.Assert_Unchecked,
            StepKind.AssertEnabled => choice.State == true ? Strings.Assert_Enabled : Strings.Assert_Disabled,
            StepKind.AssertText => Text.Format(Strings.Assert_TextEquals, choice.Text),
            StepKind.AssertValue => Text.Format(Strings.Assert_ValueEquals, choice.Text),
            _ => Strings.Assert_Visible,
        };
    }

    [RelayCommand]
    private void Confirm(AssertOption? option)
    {
        Chosen = (option ?? Selected)?.Choice;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel()
    {
        Chosen = null;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
