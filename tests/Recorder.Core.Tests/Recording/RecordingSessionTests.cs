using Microsoft.Extensions.Logging.Abstractions;
using Recorder.Core.Automation;
using Recorder.Core.Model;
using Recorder.Core.Recording;
using Shouldly;
using Xunit;
using static Recorder.Core.Tests.Recording.Elements;

namespace Recorder.Core.Tests.Recording;

/// <summary>Spec 9.1–9.6 through the real session loop, with fake hooks and a fake UI Automation service.</summary>
public sealed class RecordingSessionTests
{
    private static readonly RecordingOptions Options = RecordingOptions.Default with
    {
        DoubleClickTime = TimeSpan.FromMilliseconds(500),
        StateDelay = TimeSpan.FromMilliseconds(10),
        BusyRetryInterval = TimeSpan.FromMilliseconds(10),
    };

    private readonly FakeUia _uia = new();
    private readonly FakeMouseHook _mouse = new();
    private readonly FakeKeyHook _keys = new();
    private readonly FakeTarget _target = new();
    private readonly FakeCursor _cursor = new();
    private readonly DraftList _list = new();

    public RecordingSessionTests() =>
        _uia.Elements.AddRange([Amount, Password, Currency, Gel, Insured, ShowMessage, PrintButton, ReportWindow, MainWindowElement]);

    [Fact]
    public async Task A_typed_value_is_recorded_before_the_click_that_leaves_the_input()
    {
        await using var session = await StartAsync();
        _uia.Focus!(Amount);
        _uia.Values[Amount.SessionKey] = "5000";

        _mouse.Click(ShowMessage);
        await session.StopAsync();

        _list.Kinds.ShouldBe([StepKind.SetText, StepKind.Click]);
        (_list.Steps[0].Target, _list.Steps[0].Text).ShouldBe((Amount, "5000"));
    }

    [Fact]
    public async Task Stopping_records_the_value_still_in_the_focused_input()
    {
        await using var session = await StartAsync();
        _uia.Focus!(Amount);
        _uia.Values[Amount.SessionKey] = "5000";

        await session.StopAsync();

        _list.Steps.Single().Text.ShouldBe("5000");
    }

    [Fact]
    public async Task Typing_into_a_password_field_becomes_a_secret_step_and_its_value_is_never_read()
    {
        await using var session = await StartAsync();
        _uia.Focus!(Password);
        _keys.Typed();

        _mouse.Click(ShowMessage);
        await session.StopAsync();

        _list.Kinds.ShouldBe([StepKind.SetText, StepKind.Click]);
        (_list.Steps[0].Target, _list.Steps[0].IsSecret, _list.Steps[0].Text).ShouldBe((Password, true, null));
        _uia.StateReads.ShouldNotContain(Password.SessionKey);
    }

    [Fact]
    public async Task Enter_and_Tab_in_a_password_field_follow_its_test_data_step()
    {
        await using var session = await StartAsync();
        _uia.Focus!(Password);
        _keys.Typed();

        _keys.Press(0x0D);
        _keys.Press(0x09);
        await session.StopAsync();

        _list.Kinds.ShouldBe([StepKind.SetText, StepKind.PressKeys, StepKind.PressKeys]);
        _list.Steps[0].IsSecret.ShouldBeTrue();
        _list.Steps.Skip(1).Select(step => (step.Text, step.Target)).ShouldBe([("Enter", Password), ("Tab", Password)]);
        _uia.StateReads.ShouldNotContain(Password.SessionKey);
    }

    [Fact]
    public async Task Without_the_keyboard_hook_a_visit_to_a_password_field_counts_as_typing_once()
    {
        _keys.InstallResult = false;
        await using var session = await StartAsync();
        session.SpecialKeysActive.ShouldBeFalse();
        _uia.Focus!(Password);

        _mouse.Click(ShowMessage, time: 1000);
        _mouse.Click(ShowMessage, time: 3000);
        await session.StopAsync();

        _list.Kinds.ShouldBe([StepKind.SetText, StepKind.Click, StepKind.Click]);
        _list.Steps[0].IsSecret.ShouldBeTrue();
    }

    [Fact]
    public async Task A_check_box_click_records_the_state_read_after_it()
    {
        _uia.Checked[Insured.SessionKey] = true;
        await using var session = await StartAsync();

        _mouse.Click(Insured);
        await session.StopAsync();

        (_list.Steps.Single().Kind, _list.Steps.Single().State).ShouldBe((StepKind.SetChecked, true));
    }

    [Fact]
    public async Task A_drop_down_item_is_recorded_as_a_selection_on_its_combo_box()
    {
        await using var session = await StartAsync();

        _mouse.Click(Currency, time: 1000);
        _mouse.Click(Gel, time: 3000);
        await session.StopAsync();

        (_list.Steps.Single().Kind, _list.Steps.Single().Target, _list.Steps.Single().Text).ShouldBe((StepKind.SelectOption, Currency, "GEL"));
    }

    [Fact]
    public async Task A_click_in_another_top_level_window_is_preceded_by_a_switch_to_it()
    {
        await using var session = await StartAsync();

        _mouse.Click(PrintButton);
        await session.StopAsync();

        _list.Kinds.ShouldBe([StepKind.SwitchWindow, StepKind.Click]);
        _list.Steps[0].Target.ShouldBe(ReportWindow);
    }

    [Fact]
    public async Task Keys_record_the_pending_value_first_and_editing_keys_record_nothing()
    {
        await using var session = await StartAsync();
        _uia.Focus!(Amount);
        _uia.Values[Amount.SessionKey] = "50";
        _keys.Press(0x25);
        _keys.Press(0x0D);

        await session.StopAsync();

        _list.Kinds.ShouldBe([StepKind.SetText, StepKind.PressKeys]);
        (_list.Steps[0].Text, _list.Steps[1].Text, _list.Steps[1].Target).ShouldBe(("50", "Enter", Amount));
    }

    [Fact]
    public async Task Nothing_is_recorded_while_paused_including_what_was_typed_meanwhile()
    {
        await using var session = await StartAsync();
        await ChangeStatusAsync(session, session.Pause, RecordingStatus.Paused);
        _mouse.IsInstalled.ShouldBeFalse();
        _uia.Focus!(Amount);
        _uia.Values[Amount.SessionKey] = "123";

        await ChangeStatusAsync(session, session.Resume, RecordingStatus.Recording);
        _uia.Values[Amount.SessionKey] = "5000";
        _mouse.Click(ShowMessage);
        await session.StopAsync();

        _list.Kinds.ShouldBe([StepKind.SetText, StepKind.Click]);
        _list.Steps[0].Text.ShouldBe("5000");
    }

    [Fact]
    public async Task Clicks_off_the_target_and_drags_are_ignored()
    {
        await using var session = await StartAsync();

        _target.Over = false;
        _mouse.Click(ShowMessage);
        _target.Over = true;
        _mouse.Raise(MouseAction.LeftDown, ShowMessage.Bounds.Center);
        _mouse.Raise(MouseAction.LeftUp, new PixelPoint(ShowMessage.Bounds.Center.X + 40, ShowMessage.Bounds.Center.Y));
        await session.StopAsync();

        _list.Steps.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_click_on_a_busy_target_waits_for_it_and_is_flagged_for_checking()
    {
        _uia.BusyFor(2);
        await using var session = await StartAsync();

        _mouse.Click(ShowMessage);
        await session.StopAsync();

        var step = _list.Steps.Single();
        (step.Kind, step.Target).ShouldBe((StepKind.Click, ShowMessage));
        step.Warnings.ShouldBe([StepWarning.ElementChanged]);
    }

    [Fact]
    public async Task A_fresh_hover_snapshot_answers_a_click_without_looking_the_point_up_again()
    {
        _uia.Hovered = ShowMessage;
        _cursor.Resting = ShowMessage.Bounds.Center;
        var hovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = await StartAsync(beforeStart: recording => recording.HoverUpdated += (_, update) =>
        {
            if (update.Kind == HoverKind.Element)
            {
                hovered.TrySetResult();
            }
        });
        await hovered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        _mouse.Click(ShowMessage);
        await session.StopAsync();

        _list.Steps.Single().Target.ShouldBe(ShowMessage);
        _uia.Captures.ShouldBe(0);
    }

    [Fact]
    public async Task Assert_mode_keeps_the_next_click_from_the_app_and_adds_the_chosen_assertion()
    {
        _uia.Values[Amount.SessionKey] = "5000";
        var requested = new TaskCompletionSource<AssertTarget>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = await StartAsync();
        session.AssertRequested += (_, target) => requested.TrySetResult(target);

        session.ArmAssert();
        _mouse.Click(Amount);
        var target = await requested.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        session.IsAssertArmed.ShouldBeFalse();
        session.AddAssertion(StepKind.AssertValue, target.Element!, target.State!.Value, null);
        await session.StopAsync();

        (target.Element, target.State.Value).ShouldBe((Amount, "5000"));
        (_list.Steps.Single().Kind, _list.Steps.Single().Text).ShouldBe((StepKind.AssertValue, "5000"));
    }

    [Fact]
    public async Task A_hook_that_stops_seeing_input_while_the_cursor_keeps_moving_is_installed_again()
    {
        _mouse.IdleFor = TimeSpan.FromSeconds(10);
        _cursor.Moving = true;
        await using var session = await StartAsync(Options with { HookCheckInterval = TimeSpan.FromMilliseconds(20), HookLostAfter = TimeSpan.FromMilliseconds(50) });

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (_mouse.Reinstalls == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        _mouse.Reinstalls.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task The_recorders_own_shortcuts_and_Esc_in_assert_mode_are_reserved()
    {
        await using var session = await StartAsync(Options with { ReservedKeys = [new HotkeyGesture(HotkeyModifiers.None, 0x78, "F9")] });
        var context = _keys.Context!;

        context.IsReserved(HotkeyModifiers.None, 0x78).ShouldBeTrue();
        context.IsReserved(HotkeyModifiers.None, 0x1B).ShouldBeFalse();
        session.ArmAssert();
        context.IsReserved(HotkeyModifiers.None, 0x1B).ShouldBeTrue();
    }

    private async Task<RecordingSession> StartAsync(RecordingOptions? options = null, Action<RecordingSession>? beforeStart = null)
    {
        var session = new RecordingSession(_uia, _mouse, _keys, _target, _cursor, options ?? Options, TimeProvider.System, NullLoggerFactory.Instance);
        session.StepsChanged += (_, changes) =>
        {
            lock (_list)
            {
                _list.Apply(changes);
            }
        };
        beforeStart?.Invoke(session);
        (await session.StartAsync()).ShouldBeTrue();
        return session;
    }

    private static async Task ChangeStatusAsync(RecordingSession session, Action change, RecordingStatus expected)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.StatusChanged += (_, status) =>
        {
            if (status == expected)
            {
                reached.TrySetResult();
            }
        };
        change();
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }
}
