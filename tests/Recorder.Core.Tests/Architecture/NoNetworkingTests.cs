using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace Recorder.Core.Tests.Architecture;

/// <summary>
/// Hard constraint: no telemetry, update checks or crash reporters. The only networking allowed is the
/// optional WinAppDriver client in src/Recorder.Core/Verification.
/// </summary>
public partial class NoNetworkingTests
{
    private const string AllowedFolder = @"src\Recorder.Core\Verification\";

    private static readonly string[] ForbiddenTokens =
        ["HttpClient", "ApplicationInsights", "Telemetry", "WebRequest", "WebClient", "System.Net.Sockets"];

    private static readonly string[] SourceExtensions = [".cs", ".csproj", ".props", ".targets", ".xaml"];

    [Fact]
    public void Only_the_WinAppDriver_client_may_contain_networking_code()
    {
        var root = RepoRoot.Find();
        var offenders = Directory.EnumerateFiles(Path.Combine(root, "src"), "*", SearchOption.AllDirectories)
            .Where(file => SourceExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .Where(file => !IsBuildOutput(file) && !file.Contains(AllowedFolder, StringComparison.OrdinalIgnoreCase))
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (File: Path.GetRelativePath(root, file), Line: index + 1, Text: line)))
            .Where(hit => ForbiddenTokens.Any(token => hit.Text.Contains(token, StringComparison.Ordinal)))
            .Select(hit => $"{hit.File}:{hit.Line}: {hit.Text.Trim()}")
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void No_package_is_a_telemetry_crash_reporting_or_update_package()
    {
        var root = RepoRoot.Find();
        var packages = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".props", StringComparison.OrdinalIgnoreCase))
            .Where(file => !IsBuildOutput(file) && !file.Contains(@"\legacy\", StringComparison.OrdinalIgnoreCase))
            .SelectMany(file => PackageNameRegex().Matches(File.ReadAllText(file)).Select(match => match.Groups["name"].Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        packages.ShouldNotBeEmpty();
        packages.Where(name => ForbiddenPackageRegex().IsMatch(name)).ShouldBeEmpty();
    }

    [Fact]
    public void No_restored_dependency_is_a_telemetry_crash_reporting_or_update_package()
    {
        // Catches transitive dependencies too (e.g. a test framework that pulls in ApplicationInsights).
        var root = RepoRoot.Find();
        var assetFiles = new[] { "src", "tests" }
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(root, folder), "project.assets.json", SearchOption.AllDirectories))
            .ToList();
        assetFiles.ShouldNotBeEmpty("Restore the solution before running this test.");

        var offenders = assetFiles
            .SelectMany(file => LibraryNameRegex().Matches(File.ReadAllText(file)).Select(match => (File: Path.GetRelativePath(root, file), Library: match.Groups["name"].Value)))
            .Where(hit => ForbiddenPackageRegex().IsMatch(hit.Library))
            .Select(hit => $"{hit.File}: {hit.Library}")
            .Distinct()
            .ToList();

        offenders.ShouldBeEmpty();
    }

    private static bool IsBuildOutput(string file) =>
        file.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase)
        || file.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase)
        || file.Contains(@"\artifacts\", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("""Package(?:Reference|Version)\s+Include="(?<name>[^"]+)""")]
    private static partial Regex PackageNameRegex();

    // "libraries" keys in project.assets.json look like "Serilog/4.4.0".
    [GeneratedRegex("""^\s{4}"(?<name>[^"/]+)/[^"]+":\s*\{""", RegexOptions.Multiline)]
    private static partial Regex LibraryNameRegex();

    [GeneratedRegex("Insights|Telemetry|Sentry|AppCenter|Raygun|Bugsnag|Rollbar|Exceptionless|NewRelic|Datadog|Squirrel|Updater", RegexOptions.IgnoreCase)]
    private static partial Regex ForbiddenPackageRegex();
}
