using System.Collections.Concurrent;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Recorder.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace Recorder.IntegrationTests.SampleApp;

/// <summary>Phase 3 observations: read patterns only; input is delivered through Windows.</summary>
[Trait("Category", "Input")]
public sealed class RecordingFindingsTests : IClassFixture<SampleAppFixture>
{
    private readonly SampleAppFixture _app;

    public RecordingFindingsTests(SampleAppFixture app) => _app = app;

    [Fact]
    public void Focus_events_and_values_survive_focus_out_and_controls_expose_semantic_state()
    {
        using var cursor = Desktop.KeepCursor();
        var automation = _app.Session.Automation;
        var events = new ConcurrentQueue<AutomationElement>();
        var handler = automation.RegisterFocusChangedEvent(element => events.Enqueue(element));
        try
        {
            var amount = _app.ById("txtAmount");
            var password = _app.ById("txtPassword");
            ClickAt(amount);
            Wait.Until(() => automation.FocusedElement()?.Properties.AutomationId.ValueOrDefault == "txtAmount",
                TimeSpan.FromSeconds(5), "amount focus");
            Desktop.PressChord(0x31);
            Desktop.PressChord(0x32);
            Wait.Until(() => amount.Patterns.Value.Pattern.Value.ValueOrDefault == "12", TimeSpan.FromSeconds(5), "typed value");
            ClickAt(password);
            Wait.Until(() => automation.FocusedElement()?.Properties.AutomationId.ValueOrDefault == "txtPassword",
                TimeSpan.FromSeconds(5), "password focus");
            amount.Patterns.Value.Pattern.Value.Value.ShouldBe("12");
            password.Properties.IsPassword.Value.ShouldBeTrue();
            Wait.Until(() => events.Count >= 2, TimeSpan.FromSeconds(5), "focus events");

            var check = _app.ById("chkInsured");
            ClickAt(check);
            Wait.Until(() => check.Patterns.Toggle.Pattern.ToggleState.Value == ToggleState.On,
                TimeSpan.FromSeconds(5), "checked state");
            var radio = _app.ById("rbFixed");
            radio.Patterns.SelectionItem.Pattern.IsSelected.Value.ShouldBeTrue();

            var combo = _app.ById("cmbCurrency");
            ClickAt(combo);
            Desktop.PressChord(0x47);
            Desktop.PressChord(Desktop.VkReturn);
            Wait.Until(() => combo.Patterns.Value.Pattern.Value.ValueOrDefault == "GEL", TimeSpan.FromSeconds(5), "combo value");

            var grid = _app.ById("dgvLoans");
            var cell = Wait.For(() => grid.FindFirstDescendant(c => c.ByName("Status Row 2, Not sorted.")),
                TimeSpan.FromSeconds(5), "third row Status cell");
            cell.Patterns.GridItem.Pattern.Row.Value.ShouldBe(2);
            TestContext.Current.TestOutputHelper?.WriteLine("Focus events observed; unfocused ValuePattern remains readable; Toggle, SelectionItem, combo Value and GridItem.Row are available.");
        }
        finally
        {
            automation.UnregisterFocusChangedEvent(handler);
        }
    }

    private void ClickAt(AutomationElement element)
    {
        var point = Desktop.CenterOf(element);
        _app.BringToFrontAt(point);
        Desktop.MoveCursor(point);
        Desktop.LeftClick();
    }
}
