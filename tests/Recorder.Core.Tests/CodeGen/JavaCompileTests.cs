using Recorder.Tests.Java;
using Shouldly;
using Xunit;

namespace Recorder.Core.Tests.CodeGen;

/// <summary>
/// Spec 14, Phase 4 acceptance: the Java generated for the recorded flow compiles with javac against the stubs of the
/// target framework and the real java-client 7.6.0 jars. Skipped when no JDK or jars are found (check-java.ps1).
/// </summary>
[Trait("Requires", "JDK")]
public sealed class JavaCompileTests
{
    [Fact]
    public void The_code_generated_for_the_acceptance_flow_compiles() =>
        Compiles(CodeGenerationSnapshotTests.Generate(ProfileIds.Java, SampleRecording.AcceptanceFlow(), "Loan flow"));

    [Fact]
    public void The_code_generated_for_every_step_kind_compiles() =>
        Compiles(CodeGenerationSnapshotTests.Generate(ProfileIds.Java, SampleRecording.EveryStepKind(), "Every step kind"));

    private static void Compiles(Recorder.Core.CodeGen.GeneratedCode code)
    {
        code.Error.ShouldBeNull();
        var (exitCode, output) = JavaCompileCheck.Run(code.Files, RepoRoot.Find());
        Assert.SkipWhen(exitCode is JavaCompileCheck.NoJdk or JavaCompileCheck.NoJars, output);
        exitCode.ShouldBe(JavaCompileCheck.Compiled, output);
    }
}
