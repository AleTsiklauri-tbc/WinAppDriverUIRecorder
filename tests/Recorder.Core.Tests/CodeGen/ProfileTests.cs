using Recorder.Core.CodeGen;
using Recorder.Core.Locators;
using Recorder.Core.Model;
using Shouldly;
using Xunit;

namespace Recorder.Core.Tests.CodeGen;

/// <summary>Spec 10.1: built-in and user profiles, template errors shown as messages, and the clipboard snippet.</summary>
public sealed class ProfileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "profiles-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void The_built_in_profiles_are_java_first_then_csharp()
    {
        var set = ProfileLoader.Load(null);

        set.Profiles.Select(p => p.Id).ShouldBe([ProfileIds.Java, ProfileIds.CSharp]);
        set.Problems.ShouldBeEmpty();
        set.Find("missing").Id.ShouldBe(ProfileIds.Java);
        set.Profiles.ShouldAllBe(p => p.IsBuiltIn && p.Template("page") != null && p.Template("snippet") != null && p.Template("test") != null);
    }

    [Fact]
    public void A_user_folder_overrides_a_built_in_file_by_file_and_a_new_one_is_added()
    {
        Directory.CreateDirectory(Path.Combine(_folder, ProfileIds.Java));
        File.WriteAllText(Path.Combine(_folder, ProfileIds.Java, "snippet.sbn"), "// {{ element.variable_name }}");
        Directory.CreateDirectory(Path.Combine(_folder, "mine"));
        File.WriteAllText(Path.Combine(_folder, "mine", "profile.json"), """{ "name": "Mine", "language": "java", /* comment */ }""");

        var set = ProfileLoader.Load(_folder);

        var java = set.Find(ProfileIds.Java);
        java.IsBuiltIn.ShouldBeFalse();
        java.Template("snippet").ShouldBe("// {{ element.variable_name }}");
        java.Template("page").ShouldBe(ProfileLoader.BuiltIn().Single(p => p.Id == ProfileIds.Java).Template("page"));
        java.Settings.Package.ShouldBe("tbc.automation.pages");
        set.Find("mine").DisplayName.ShouldBe("Mine");
    }

    [Fact]
    public void A_broken_profile_is_reported_and_the_others_still_load()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "broken"));
        File.WriteAllText(Path.Combine(_folder, "broken", "profile.json"), "{ \"name\": ");
        Directory.CreateDirectory(Path.Combine(_folder, "empty"));

        var set = ProfileLoader.Load(_folder);

        set.Profiles.Count.ShouldBe(2);
        set.Problems.Select(p => (p.ProfileId, p.File)).ShouldBe([("broken", "profile.json"), ("empty", "profile.json")]);
    }

    [Fact]
    public void Exporting_a_built_in_profile_writes_every_file_and_reads_back_the_same()
    {
        var java = ProfileLoader.BuiltIn().Single(p => p.Id == ProfileIds.Java);

        ProfileLoader.Export(java, Path.Combine(_folder, ProfileIds.Java));
        var copy = ProfileLoader.Load(_folder).Find(ProfileIds.Java);

        Directory.GetFiles(Path.Combine(_folder, ProfileIds.Java)).Length.ShouldBe(java.Templates.Count + 1);
        copy.Templates.ShouldBe(java.Templates);
        copy.Settings.Classes.ShouldBe(java.Settings.Classes);
    }

    [Theory]
    [InlineData("page", "{{ page.class_nam }}", "page.sbn line 1")]
    [InlineData("page", "line one\n{{ for x in page.elements }}{{ x.nope }}{{ end }}", "page.sbn line 2")]
    [InlineData("page", "{{ unknown_thing }}", "page.sbn line 1")]
    [InlineData("page", "{{ if }}", "page.sbn line 1")]
    [InlineData("page", "{{ include \"element\" }}", "element.sbn line 1")]
    [InlineData("page", "{{ include \"page\" }}", "page.sbn line 1")]
    [InlineData("page", "{{ for i in 1..1000000 }}x{{ end }}", "page.sbn line 1")]
    public void A_template_error_becomes_a_message_with_its_file_and_line(string template, string source, string where)
    {
        var templates = new Dictionary<string, string>(ProfileLoader.BuiltIn().Single(p => p.Id == ProfileIds.Java).Templates)
        {
            [template] = source,
            ["element"] = "{{ element.nam }}",
        };
        var profile = ProfileLoader.BuiltIn().Single(p => p.Id == ProfileIds.Java) with { Templates = templates };

        var file = new CodeGenerator(profile).Generate(SampleRecording.AcceptanceFlow().Catalog, [], "flow").Files[0];

        file.Error.ShouldNotBeNull();
        file.Error.ShouldStartWith(where);
        file.Text.ShouldBeEmpty();
    }

    [Fact]
    public void A_broken_action_template_fails_the_whole_generation_with_a_message()
    {
        var java = ProfileLoader.BuiltIn().Single(p => p.Id == ProfileIds.Java);
        var templates = new Dictionary<string, string>(java.Templates) { ["action.Click"] = "{{ target.nope }}" };
        var templatesWithout = new Dictionary<string, string>(java.Templates);
        templatesWithout.Remove("action.SetText");
        var recording = SampleRecording.AcceptanceFlow();

        new CodeGenerator(java with { Templates = templates }).Generate(recording.Catalog, recording.Steps, "flow").Error.ShouldStartWith("action.Click.sbn line 1");
        new CodeGenerator(java with { Templates = templatesWithout }).Generate(recording.Catalog, recording.Steps, "flow").Error
            .ShouldBe("action.SetText.sbn: the profile has no template action.SetText.sbn");
    }

    [Fact]
    public void The_java_snippet_is_the_d32_getter_line()
    {
        var java = new CodeGenerator(ProfileLoader.BuiltIn().Single(p => p.Id == ProfileIds.Java));
        var candidate = new LocatorCandidate(
            LocatorStrategy.Name, LocatorSpec.ForAttribute(AttributeName.Name, "ძებნა \"x\""), "ძებნა \"x\"", LocatorScope.Window, Stability.Medium, 70, 1, 1,
            true, [], new LocatorExplanation(LocatorKind.Name));

        var line = java.Snippet("dzebnaButton", TestSnapshots.Edit(controlType: "Button"), candidate);

        line.ShouldBe(JavaLocatorLine.Getter("dzebnaButton", candidate));
        line.ShouldBe("public WebElement getDzebnaButton(){return winDriver.findElementByName(\"ძებნა \\\"x\\\"\");}");
    }

    [Fact]
    public void The_csharp_snippet_is_a_property()
    {
        var csharp = new CodeGenerator(ProfileLoader.BuiltIn().Single(p => p.Id == ProfileIds.CSharp));

        csharp.Snippet("amountInput", TestSnapshots.Edit(), null).ShouldBe("public WindowsElement AmountInput => _driver.FindElementByAccessibilityId(\"txtAmount\");");
    }

    [Fact]
    public void Nothing_recorded_gives_page_classes_only()
    {
        var recording = SampleRecording.AcceptanceFlow();

        var code = new CodeGenerator(ProfileLoader.BuiltIn().Single(p => p.Id == ProfileIds.Java)).Generate(recording.Catalog, [], "flow");

        code.Files.Select(f => (f.Kind, f.FileName)).ShouldBe([
            (CodeFileKind.Page, "LoanPage.java"), (CodeFileKind.Page, "MainPage.java"), (CodeFileKind.Page, "WindowPage.java")]);
        new CodeGenerator(ProfileLoader.BuiltIn().Single(p => p.Id == ProfileIds.Java)).Generate(new ElementCatalog(), [], "flow").Files.ShouldBeEmpty();
    }
}
