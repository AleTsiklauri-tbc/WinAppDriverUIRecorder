using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace Recorder.Core.Tests.Architecture;

/// <summary>
/// Spec 3: the recorder ships as one self-contained exe for Windows x64, because corporate PCs may not allow installing
/// .NET (D-82). <c>./build.ps1 publish</c> also fails when anything but the exe lands in the publish folder.
/// </summary>
public class PublishProfileTests
{
    [Fact]
    public void The_publish_profile_makes_one_self_contained_exe_for_x64()
    {
        var root = RepoRoot.Find();
        var profile = XDocument.Load(Path.Combine(root, "src", "Recorder.App", "Properties", "PublishProfiles", "win-x64.pubxml"));
        string? Property(string name) => profile.Descendants(name).SingleOrDefault()?.Value;

        Property("RuntimeIdentifier").ShouldBe("win-x64");
        Property("SelfContained").ShouldBe("true");
        Property("PublishSingleFile").ShouldBe("true");
        Property("IncludeNativeLibrariesForSelfExtract").ShouldBe("true", "WPF's native DLLs would sit next to the exe");
        XDocument.Load(Path.Combine(root, "src", "Directory.Build.props")).Descendants("DebugType").Single().Value
            .ShouldBe("embedded", "a .pdb file would sit next to the exe");
    }
}
