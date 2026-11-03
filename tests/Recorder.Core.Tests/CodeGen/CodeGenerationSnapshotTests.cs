using System.Text;
using Recorder.Core.CodeGen;
using Shouldly;
using Xunit;

namespace Recorder.Core.Tests.CodeGen;

/// <summary>
/// Spec 13.2: the code each built-in profile generates, as reviewed snapshots in Snapshots/. A change to a template shows
/// up here as a failing test and a .received. file: rename it to .verified. once it is right.
/// </summary>
public sealed class CodeGenerationSnapshotTests
{
    public static TheoryData<string> Profiles => [ProfileIds.Java, ProfileIds.CSharp];

    [Theory]
    [MemberData(nameof(Profiles))]
    public void The_acceptance_flow(string profile) =>
        Snapshot.Match(Render(Generate(profile, SampleRecording.AcceptanceFlow(), "Loan flow")), "AcceptanceFlow." + profile);

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Every_step_kind(string profile) =>
        Snapshot.Match(Render(Generate(profile, SampleRecording.EveryStepKind(), "Every step kind")), "EveryStepKind." + profile);

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Every_step_kind_has_an_action_template(string profile)
    {
        var templates = ProfileLoader.BuiltIn().Single(p => p.Id == profile).Templates;
        Enum.GetNames<Recorder.Core.Model.StepKind>().ShouldAllBe(kind => templates.ContainsKey("action." + kind));
    }

    internal static GeneratedCode Generate(string profile, SampleRecording recording, string testName) =>
        new CodeGenerator(ProfileLoader.BuiltIn().Single(p => p.Id == profile)).Generate(recording.Catalog, recording.Steps, testName);

    private static string Render(GeneratedCode code)
    {
        code.Error.ShouldBeNull();
        var text = new StringBuilder();
        foreach (var file in code.Files)
        {
            file.Error.ShouldBeNull();
            text.Append("// ===== ").Append(file.FileName).Append(" (").Append(file.Kind).Append(") =====\n").Append(file.Text).Append('\n');
        }

        return text.ToString();
    }
}

internal static class ProfileIds
{
    public const string Java = "java-appium-pom";
    public const string CSharp = "csharp-appium";
}
