using System.Diagnostics;
using Recorder.Core.CodeGen;
using Recorder.Core.Model;
using Recorder.Core.Persistence;
using Shouldly;
using Xunit;

namespace Recorder.Core.Tests.CodeGen;

/// <summary>
/// Spec 11.1: visible feedback within 100 ms. The code pane generates again 150 ms after a change (spec 10.4) and autosave
/// keeps the recording 2 s after one (spec 12), both on the UI thread, so each stays inside that budget for a recording larger
/// than a real test: 8 pages of 20 fields and 250 steps. While the window is minimized for recording, the code is not
/// generated at all until it shows again.
/// </summary>
[Collection(nameof(TimingCollection))]
public sealed class LargeRecordingTimingTests
{
    private const int Runs = 20;
    private const int WarmUps = 40;
    private static readonly SampleRecording Recording = SampleRecording.Large(pages: 8, elementsPerPage: 20, steps: 250);

    [Fact]
    public void Code_for_a_large_recording_is_generated_within_100_ms()
    {
        var generator = new CodeGenerator(ProfileLoader.BuiltIn().Single(p => p.Id == "java-appium-pom"));
        generator.Generate(Recording.Catalog, Recording.Steps, "Large flow").Error.ShouldBeNull();

        var p95 = Time("Code generation", () => generator.Generate(Recording.Catalog, Recording.Steps, "Large flow"));

        p95.ShouldBeLessThan(100);
    }

    [Fact]
    public void A_large_recording_is_autosaved_within_100_ms()
    {
        var steps = new StepList();
        steps.Reset(Recording.Steps);
        var folder = Path.Combine(Path.GetTempPath(), "WinFormsRecorder.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var file = Path.Combine(folder, "large" + RecordingStore.Extension);

            var p95 = Time("Autosave", () => RecordingStore.Save(file, RecordingDocument.From(Recording.Catalog, steps, "LMS.exe", DateTimeOffset.Now, DateTimeOffset.Now)));

            p95.ShouldBeLessThan(100);
            RecordingStore.Load(file).Steps.Count.ShouldBe(250);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // The p95 in milliseconds, with the numbers logged. Measured as in the app after a few changes: warmed up until .NET has
    // optimised the code (tiered compilation compiles a method again after 30 calls, in the background).
    private static double Time(string what, Action action)
    {
        for (var i = 0; i < WarmUps; i++)
        {
            action();
        }

        Thread.Sleep(500);
        var timings = new List<double>(Runs);
        for (var i = 0; i < Runs; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            action();
            timings.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        timings.Sort();
        var p95 = timings[(int)Math.Ceiling(0.95 * timings.Count) - 1];
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{what} (160 elements, 250 steps): median {timings[timings.Count / 2]:0.0} ms, p95 {p95:0.0} ms, max {timings[^1]:0.0} ms ({Runs} runs)");
        return p95;
    }
}

/// <summary>Timings run on their own, after the other tests, which would otherwise compete for the CPU.</summary>
[CollectionDefinition(nameof(TimingCollection), DisableParallelization = true)]
public sealed class TimingCollection;
