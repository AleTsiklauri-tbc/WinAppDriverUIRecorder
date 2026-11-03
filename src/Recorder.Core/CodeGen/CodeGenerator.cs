using System.Text;
using Recorder.Core.Locators;
using Recorder.Core.Model;
using Recorder.Core.Naming;

namespace Recorder.Core.CodeGen;

public enum CodeFileKind
{
    Page,
    Steps,
    Test,
}

/// <summary>One generated class.</summary>
/// <param name="Error">Why its template failed, e.g. "page.sbn line 12: …"; the text is empty then.</param>
/// <param name="Page">The page a page or Steps class is for; null for the test.</param>
public sealed record GeneratedFile(CodeFileKind Kind, string ClassName, string FileName, string Text, string? Error, PageKey? Page = null);

/// <summary>Everything generated for a recording.</summary>
/// <param name="Error">A template failed before any class could be rendered (an action template); no files then.</param>
public sealed record GeneratedCode(IReadOnlyList<GeneratedFile> Files, string? Error)
{
    public static GeneratedCode Empty { get; } = new([], null);

    public IEnumerable<GeneratedFile> Of(CodeFileKind kind) => Files.Where(file => file.Kind == kind);
}

// The roots templates see (docs/TEMPLATES.md).

/// <summary>What page.sbn and steps.sbn see.</summary>
public sealed record PageTemplateModel(ProfileModel Profile, PageCode Page);

/// <summary>What test.sbn sees.</summary>
public sealed record TestTemplateModel(ProfileModel Profile, TestCode Test, IReadOnlyList<PageCode> Pages);

/// <summary>What snippet.sbn sees.</summary>
public sealed record ElementTemplateModel(ProfileModel Profile, ElementCode Element);

/// <summary>
/// Generates code with a profile (spec 10): a page class per page, a Steps class per page with actions when the profile has a
/// Steps layer, and a test. Template errors come back as messages, never as exceptions (spec 10.1).
/// </summary>
public sealed class CodeGenerator
{
    private readonly TemplateRenderer _renderer;
    private readonly CodeModelBuilder _builder;

    /// <param name="naming">Naming conventions for method names; by default the profile's own.</param>
    public CodeGenerator(CodeProfile profile, NamingOptions? naming = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Profile = profile;
        _renderer = new TemplateRenderer(profile);
        _builder = new CodeModelBuilder(profile, _renderer, naming ?? profile.Settings.Naming());
    }

    public CodeProfile Profile { get; }

    /// <param name="testName">The recording's name, e.g. its file name: "loan flow" → LoanFlowTest.loanFlow().</param>
    public GeneratedCode Generate(ElementCatalog catalog, IReadOnlyList<RecordedStep> steps, string testName)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(steps);
        CodeModel model;
        IReadOnlyList<PageKey> keys;
        try
        {
            model = _builder.Build(catalog, steps, testName, out keys);
        }
        catch (TemplateException exception)
        {
            return new GeneratedCode([], exception.Message);
        }

        var files = new List<GeneratedFile>();
        for (var i = 0; i < model.Pages.Count; i++)
        {
            var page = model.Pages[i];
            files.Add(Render(CodeFileKind.Page, "page", page.ClassName, new PageTemplateModel(model.Profile, page)) with { Page = keys[i] });
            if (Profile.Settings.HasStepsLayer && page.Actions.Count > 0)
            {
                files.Add(Render(CodeFileKind.Steps, "steps", page.StepsClassName, new PageTemplateModel(model.Profile, page)) with { Page = keys[i] });
            }
        }

        if (steps.Count > 0)
        {
            files.Add(Render(CodeFileKind.Test, "test", model.Test.ClassName, new TestTemplateModel(model.Profile, model.Test, model.Pages)));
        }

        return new GeneratedCode(files, null);
    }

    /// <summary>One locator as a line for the clipboard (snippet.sbn): what "Copy line" copies (D-32).</summary>
    /// <exception cref="TemplateException">snippet.sbn failed.</exception>
    public string Snippet(string variableName, ElementSnapshot snapshot, LocatorCandidate? candidate)
    {
        ArgumentException.ThrowIfNullOrEmpty(variableName);
        ArgumentNullException.ThrowIfNull(snapshot);
        var model = new ElementTemplateModel(_builder.Profile, _builder.Element(variableName, snapshot, candidate));
        return Tidy(_renderer.Render("snippet", model)).TrimEnd('\n');
    }

    /// <summary>
    /// Generated text as written: "\n" line breaks, no trailing spaces, at most one blank line in a row, one final line break.
    /// Templates can then use whitespace freely around their tags.
    /// </summary>
    public static string Tidy(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var builder = new StringBuilder(text.Length);
        var blank = 0;
        foreach (var raw in text.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0)
            {
                blank++;
                continue;
            }

            if (builder.Length > 0 && blank > 0)
            {
                builder.Append('\n');
            }

            builder.Append(line).Append('\n');
            blank = 0;
        }

        return builder.ToString();
    }

    private GeneratedFile Render(CodeFileKind kind, string template, string className, object model)
    {
        var fileName = className + Profile.Settings.FileExtension;
        try
        {
            return new GeneratedFile(kind, className, fileName, Tidy(_renderer.Render(template, model)), null);
        }
        catch (TemplateException exception)
        {
            return new GeneratedFile(kind, className, fileName, string.Empty, exception.Message);
        }
    }
}
