using Recorder.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace Recorder.IntegrationTests.RecorderApp;

/// <summary>
/// Spec 9.4 through the real keyboard hook: Enter and Tab pressed in a password field are recorded after its test-data step
/// (D-59), while the password itself is written nowhere. Uses the real mouse and keyboard (and puts the cursor back).
/// </summary>
[Trait("Category", "Input")]
public sealed class SpecialKeysEndToEndTests : IClassFixture<RecorderWithTargetFixture>
{
    private const string Secret = "Key-Test-9!";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly RecorderWithTargetFixture _fixture;

    public SpecialKeysEndToEndTests(RecorderWithTargetFixture fixture) => _fixture = fixture;

    private RecorderWindow Recorder => _fixture.Recorder;

    [Fact]
    public void Enter_and_Tab_in_a_password_field_are_recorded_and_the_password_is_not()
    {
        var password = _fixture.Sample.ById("txtPassword");
        using (Desktop.KeepCursor())
        {
            Recorder.Invoke("Toolbar.Record");
            Wait.Until(() => Recorder.NameOf("Status.State").StartsWith("Recording", StringComparison.Ordinal), Timeout, "recording to start");
            var point = Desktop.CenterOf(password);
            _fixture.Sample.BringToFrontAt(point);
            Desktop.MoveCursor(point);
            Thread.Sleep(450);
            Desktop.LeftClick();
            Thread.Sleep(350);

            Desktop.TypeText(Secret);
            Desktop.PressChord(Desktop.VkReturn);
            Thread.Sleep(300);
            Desktop.PressChord(0x09);
            Thread.Sleep(300);

            Desktop.PressChord(Desktop.VkF9);
            Recorder.WaitForName("Status.State", "Idle", TimeSpan.FromSeconds(20));
        }

        Wait.Until(() => Recorder.StepSentences().Count == 3, Timeout, "three steps");
        Recorder.StepSentences().ShouldBe(["Type the test value passwordInput into Password", "Press Enter in Password", "Press Tab in Password"]);
        foreach (var log in Directory.GetFiles(Path.Combine(_fixture.DataHome, "local", "logs"), "*.log"))
        {
            using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            reader.ReadToEnd().ShouldNotContain(Secret, customMessage: Path.GetFileName(log));
        }
    }
}
