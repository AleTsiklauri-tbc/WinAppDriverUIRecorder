using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Recorder.Core.Model;
using Recorder.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace Recorder.IntegrationTests.RecorderApp;

/// <summary>
/// Phase 2, end to end: the locator card, choosing a locator, Highlight matches, renaming, Ctrl+C, Verify and the
/// hover colours, with the real recorder and SampleApp. Each test captures its own control, so their order does not
/// matter. These move the cursor and send keys; the Ctrl+C test also uses the clipboard and puts its text back.
/// </summary>
[Trait("Category", "Input")]
public sealed class LocatorCardEndToEndTests : IClassFixture<RecorderWithTargetFixture>
{
    private const string PickingStatus = "Picking · click a control in Recorder.SampleApp.exe · Esc cancels";

    // HighlightPalette (spec 8.4): green = unique High, amber = Medium, red = no unique locator, blue = captured.
    private static readonly ScreenColor Green = new(0x10, 0x7C, 0x10);
    private static readonly ScreenColor Amber = new(0x9D, 0x5D, 0x00);
    private static readonly ScreenColor Red = new(0xC4, 0x2B, 0x1C);
    private static readonly ScreenColor Blue = new(0x00, 0x78, 0xD4);

    private readonly RecorderWithTargetFixture _fixture;

    public LocatorCardEndToEndTests(RecorderWithTargetFixture fixture) => _fixture = fixture;

    private RecorderWindow Recorder => _fixture.Recorder;

    private SampleAppFixture Sample => _fixture.Sample;

    [Fact]
    public void A_captured_control_shows_its_best_locator_its_variable_name_and_the_Java_line()
    {
        Capture(Sample.ById("txtAmount"), "amountInput");

        Recorder.WaitForName("Locator.Matches", "1 match in window ✓");
        Recorder.NameOf("Inspector.Title").ShouldBe("Edit · txtAmount");
        Recorder.NameOf("Locator.Badge").ShouldBe("ID");
        Recorder.ValueOf("Locator.Value").ShouldBe("txtAmount");
        Recorder.NameOf("Locator.Stability").ShouldBe("Stable");
        Recorder.ValueOf("Locator.JavaLine").ShouldBe("""public WebElement getAmountInput(){return winDriver.findElementByAccessibilityId("txtAmount");}""");
        Recorder.WaitForCapturedElement("amountInput").Properties.HelpText.Value.ShouldBe("amountInput, ID, Unique locator");
    }

    [Fact]
    public void Choosing_another_locator_updates_the_card_and_Highlight_matches_outlines_every_match()
    {
        Capture(Sample.ById("btnSaveGuarantor"), "saveGuarantorButton");
        Recorder.WaitForName("Locator.Matches", "1 match in window ✓");

        var nameLocator = Wait.For(() => FindChoice("Name Save"), TimeSpan.FromSeconds(5), "the Name locator among the other locators");
        nameLocator.Patterns.SelectionItem.Pattern.Select();

        try
        {
            Recorder.WaitForName("Locator.Matches", "2 matches ✗");
        }
        catch (TimeoutException timeout)
        {
            var choices = string.Join("; ", Recorder.Main.FindAllDescendants(c => c.ByAutomationId("Locator.Choose")).Select(choice => $"{choice.Properties.Name.ValueOrDefault}={choice.Patterns.SelectionItem.PatternOrDefault?.IsSelected.ValueOrDefault}"));
            throw new TimeoutException($"{timeout.Message} Card: {Recorder.NameOf("Locator.Badge")} \"{Recorder.ValueOf("Locator.Value")}\" {Recorder.NameOf("Locator.Matches")}; element {Recorder.ValueOf("Inspector.VariableName")}; choices [{choices}]; focus on \"{Recorder.Session.Automation.FocusedElement()?.Properties.AutomationId.ValueOrDefault}\".", timeout);
        }
        Recorder.NameOf("Locator.Badge").ShouldBe("Name");
        Recorder.ValueOf("Locator.Value").ShouldBe("Save");
        Recorder.ValueOf("Locator.JavaLine").ShouldBe("""public WebElement getSaveGuarantorButton(){return winDriver.findElementByName("Save");}""");

        // Both Save buttons get an outline and a red chip for 1.5 s.
        var customer = Desktop.BoundsOf(Sample.ById("btnSaveCustomer"));
        var guarantor = Desktop.BoundsOf(Sample.ById("btnSaveGuarantor"));
        var thickness = Desktop.HighlightThicknessAt(customer.Center);
        Recorder.Invoke("Locator.Highlight");
        Wait.Until(() => Recorder.IsOutlined(customer, thickness) && Recorder.IsOutlined(guarantor, thickness), TimeSpan.FromSeconds(5), "outlines around both Save buttons");
        var chips = Recorder.VisibleChips(thickness);
        chips.Count.ShouldBe(2);
        chips.Select(RecorderWindow.ColorOf).ShouldAllBe(color => color.IsNear(Red));
        Wait.Until(() => Recorder.VisibleHighlightWindows().Count == 0, TimeSpan.FromSeconds(5), "the outlines to disappear");
    }

    [Fact]
    public void F2_in_the_Elements_list_renames_the_variable_and_the_Java_line_follows()
    {
        var item = Capture(Sample.ById("chkInsured"), "insuredCheckbox");
        using (Desktop.KeepCursor())
        {
            Focus(item);
            Desktop.PressChord(Desktop.VkF2);

            var nameBox = Wait.ForDescendant(Recorder.Main, "Inspector.VariableName");
            Wait.Until(() => nameBox.Properties.HasKeyboardFocus.ValueOrDefault, TimeSpan.FromSeconds(5), "the variable name box to get the focus");
            nameBox.Patterns.Value.Pattern.SetValue("insuredFlag");
            Desktop.PressChord(Desktop.VkReturn);
        }

        Recorder.WaitForCapturedElement("insuredFlag");
        Recorder.FindCapturedElement("insuredCheckbox").ShouldBeNull();
        Recorder.WaitForValue("Locator.JavaLine", """public WebElement getInsuredFlag(){return winDriver.findElementByAccessibilityId("chkInsured");}""");
    }

    [Fact]
    public void Ctrl_C_copies_the_Java_line_of_the_selected_element()
    {
        var item = Capture(Sample.ById("cmbCurrency"), "currencyDropdown");
        const string JavaLine = """public WebElement getCurrencyDropdown(){return winDriver.findElementByAccessibilityId("cmbCurrency");}""";
        Recorder.WaitForValue("Locator.JavaLine", JavaLine);
        var before = ClipboardText.Read();
        try
        {
            ClipboardText.Write("before Ctrl+C");
            using (Desktop.KeepCursor())
            {
                Focus(item);
                Desktop.PressChord(Desktop.VkControl, Desktop.VkC);
            }

            Wait.Until(() => ClipboardText.Read() == JavaLine, TimeSpan.FromSeconds(5), "the Java line on the clipboard");
        }
        finally
        {
            ClipboardText.Write(before ?? string.Empty);
        }
    }

    [Fact]
    [Trait("Requires", "WinAppDriver")]
    public async Task Verify_asks_WinAppDriver_at_the_address_typed_into_the_panel()
    {
        Assert.SkipUnless(await WinAppDriverSession.IsAvailableAsync(), "WinAppDriver is not running at " + WinAppDriverSession.ConfiguredEndpoint.Display);
        var address = WinAppDriverSession.ConfiguredEndpoint.Display;
        Capture(Sample.ById("btnOpenDialog"), "openDialogButton");
        Recorder.WaitForName("Locator.Matches", "1 match in window ✓");

        // The first click opens the panel with the saved address (the default port 4723); then the user types theirs.
        Recorder.Invoke("Locator.Verify");
        Wait.ForDescendant(Recorder.Main, "Locator.Address").Patterns.Value.Pattern.SetValue(address);
        Recorder.Invoke("Locator.Verify");

        Recorder.WaitForName("Locator.VerifyStatus", $"Checked with WinAppDriver at {address}.", TimeSpan.FromSeconds(120));
        Recorder.NameOf("Locator.Verified").ShouldMatch(@"^WinAppDriver: 1 match · \d+ ms$");
        Recorder.NameOf("Status.WinAppDriver").ShouldBe("WinAppDriver reachable");
        File.ReadAllText(Path.Combine(_fixture.DataHome, "roaming", "settings.json")).ShouldContain(address);
    }

    [Fact]
    public void In_pick_mode_the_chip_colour_tells_how_good_the_best_locator_is()
    {
        Capture(Sample.ById("rbFixed"), "fixedRadio");
        var georgian = Wait.For(() => Sample.Loan.FindFirstChild(c => c.ByControlType(ControlType.Button).And(c.ByName("ძებნა"))), TimeSpan.FromSeconds(5), "the ძებნა button");
        (PixelRect Bounds, ScreenColor Color, string What)[] probes =
        [
            (Desktop.BoundsOf(Sample.ById("btnSaveCustomer")), Green, "a control with a unique ID"),
            (Desktop.BoundsOf(georgian), Amber, "a control found by its text"),
            (Desktop.BoundsOf(Sample.ById("rbFixed")), Blue, "a control that is already captured"),
        ];

        using (Desktop.KeepCursor())
        {
            Desktop.Raise(Sample.MainWindowHandle);
            Desktop.MoveCursor(probes[0].Bounds.Center);
            Recorder.PickButton.Patterns.Toggle.Pattern.Toggle();
            try
            {
                Recorder.WaitForName("Status.State", PickingStatus);
                Sample.BringToFrontAt(probes[0].Bounds.Center);
                foreach (var (bounds, color, what) in probes)
                {
                    Desktop.MoveCursor(bounds.Center);
                    WaitForChip(bounds, color, what);
                }
            }
            finally
            {
                // Leave pick mode even when a check failed, or the next test would inspect whatever the cursor is on.
                Desktop.PressChord(Desktop.VkEscape);
                Recorder.WaitForName("Status.State", "Idle");
            }
        }
    }

    /// <summary>Captures a control with the global shortcut and waits until the Inspector shows it.</summary>
    private AutomationElement Capture(AutomationElement control, string variableName)
    {
        using (Desktop.KeepCursor())
        {
            Desktop.Raise(Sample.MainWindowHandle);
            Desktop.MoveCursor(Desktop.CenterOf(control));
            Desktop.PressChord(Desktop.VkControl, Desktop.VkShift, Desktop.VkX);
            var item = Recorder.WaitForCapturedElement(variableName);
            Recorder.WaitForValue("Inspector.VariableName", variableName);
            return item;
        }
    }

    private void WaitForChip(PixelRect bounds, ScreenColor color, string what)
    {
        var thickness = Desktop.HighlightThicknessAt(bounds.Center);
        try
        {
            Wait.Until(
                () => Recorder.IsOutlined(bounds, thickness) && Recorder.VisibleChips(thickness) is [var chip] && RecorderWindow.ColorOf(chip).IsNear(color),
                TimeSpan.FromSeconds(5),
                $"a {color} chip on {what} {bounds}");
        }
        catch (TimeoutException timeout)
        {
            var windows = string.Join("; ", Recorder.VisibleHighlightWindows().Select(window => window.Bounds));
            var chips = string.Join("; ", Recorder.VisibleChips(thickness).Select(chip => $"{chip} {RecorderWindow.ColorOf(chip)}"));
            var owner = Desktop.ProcessAtPoint(bounds.Center) switch
            {
                var id when id == Recorder.Session.ProcessId => "the recorder",
                var id when id == Sample.Session.ProcessId => "the SampleApp",
                var id => "process " + id,
            };
            throw new TimeoutException($"{timeout.Message} Saw windows [{windows}], chips [{chips}]; the window under the cursor belongs to {owner}; status \"{Recorder.NameOf("Status.State")}\".", timeout);
        }
    }

    // A real click: it activates the recorder, so the next keys go to it.
    private void Focus(AutomationElement item)
    {
        Desktop.Raise(Recorder.MainWindowHandle);
        Desktop.MoveCursor(Desktop.CenterOf(item));
        Desktop.LeftClick();
        Wait.Until(() => item.Properties.HasKeyboardFocus.ValueOrDefault, TimeSpan.FromSeconds(5), "the Elements entry to get the focus");
    }

    private AutomationElement? FindChoice(string accessibleName) =>
        Recorder.Main.FindAllDescendants(c => c.ByAutomationId("Locator.Choose"))
            .FirstOrDefault(choice => choice.Properties.Name.ValueOrDefault == accessibleName);
}
