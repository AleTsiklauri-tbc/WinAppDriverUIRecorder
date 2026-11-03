using System.Globalization;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;
using Scriban.Syntax;

namespace Recorder.Core.CodeGen;

/// <summary>A template that failed: "page.sbn line 12: unknown variable 'element.nam'" (spec 10.1).</summary>
public sealed class TemplateException(string file, int line, string reason, Exception? innerException = null)
    : Exception(file + (line > 0 ? " line " + line.ToString(CultureInfo.InvariantCulture) : string.Empty) + ": " + reason, innerException)
{
    public string File { get; } = file;

    public int Line { get; } = line;

    public string Reason { get; } = reason;
}

/// <summary>
/// Renders a profile's Scriban templates. Variables and members are strict, so a typo is an error with its file and line
/// instead of empty output. Besides the model, templates can call:
/// <list type="bullet">
/// <item><c>literal(text)</c>: a string literal in the profile's language.</item>
/// <item><c>simple_name("a.b.C")</c>: "C".</item>
/// <item><c>indent_lines(text, prefix)</c>: every non-empty line of text, prefixed.</item>
/// </list>
/// <c>include "name"</c> renders another template of the profile.
/// </summary>
public sealed class TemplateRenderer
{
    private readonly CodeProfile _profile;
    private readonly Dictionary<string, Template> _parsed = new(StringComparer.Ordinal);

    // A recording renders an action template per step: the functions are made once, and included templates are parsed once
    // (Scriban caches them only per context).
    private readonly ScriptObject _functions = new();
    private readonly Dictionary<string, Template> _included = new(StringComparer.Ordinal);

    public TemplateRenderer(CodeProfile profile)
    {
        _profile = profile;
        _functions.Import("literal", new Func<string?, string>(text => CodeNames.Literal(text, _profile.Settings.Language)));
        _functions.Import("simple_name", new Func<string?, string>(SimpleName));
        _functions.Import("indent_lines", new Func<string?, string?, string>(IndentLines));
    }

    public bool Has(string name) => _profile.Templates.ContainsKey(name);

    /// <exception cref="TemplateException">The template is missing, does not parse or fails while rendering.</exception>
    public string Render(string name, object model)
    {
        var template = Parse(name);
        var globals = new ScriptObject();
        globals.Import(model, filter: null, renamer: null);
        var context = new TemplateContext
        {
            StrictVariables = true,
            EnableRelaxedMemberAccess = false,
            TemplateLoader = new ProfileTemplateLoader(this),
            NewLine = "\n",
            LoopLimit = 100_000,
        };
        foreach (var (path, included) in _included)
        {
            context.CachedTemplates[path] = included;
        }

        context.PushGlobal(_functions);
        context.PushGlobal(globals);
        try
        {
            var text = template.Render(context);
            foreach (var (path, included) in context.CachedTemplates)
            {
                _included.TryAdd(path, included);
            }

            return text;
        }
        catch (ScriptRuntimeException exception)
        {
            throw new TemplateException(FileOf(exception.Span, name), exception.Span.Start.Line + 1, exception.OriginalMessage, exception);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or FormatException or InvalidCastException or IndexOutOfRangeException)
        {
            // Thrown by a function a template called with values it cannot take.
            throw new TemplateException(name + ".sbn", 0, exception.Message, exception);
        }
    }

    public static string SimpleName(string? qualified) =>
        string.IsNullOrEmpty(qualified) ? string.Empty : qualified[(qualified.LastIndexOf('.') + 1)..];

    public static string IndentLines(string? text, string? prefix)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var lines = text.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
        return string.Join('\n', lines.Select(line => line.Length == 0 ? line : prefix + line));
    }

    internal string Source(string name) =>
        _profile.Template(name) ?? throw new TemplateException(name + ".sbn", 0, "the profile has no template " + name + ".sbn");

    private Template Parse(string name)
    {
        if (_parsed.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var template = Template.Parse(Source(name), name + ".sbn");
        if (template.HasErrors)
        {
            var error = template.Messages.First(message => message.Type == ParserMessageType.Error);
            throw new TemplateException(name + ".sbn", error.Span.Start.Line + 1, error.Message);
        }

        _parsed[name] = template;
        return template;
    }

    private static string FileOf(SourceSpan span, string fallback) =>
        string.IsNullOrEmpty(span.FileName) ? fallback + ".sbn" : Path.GetFileName(span.FileName);

    // include "element" loads element.sbn. Its final line break is dropped, so that it can stand inside a line.
    private sealed class ProfileTemplateLoader(TemplateRenderer renderer) : ITemplateLoader
    {
        public string GetPath(TemplateContext context, SourceSpan callerSpan, string templateName) =>
            templateName.EndsWith(".sbn", StringComparison.Ordinal) ? templateName : templateName + ".sbn";

        public string Load(TemplateContext context, SourceSpan callerSpan, string templatePath)
        {
            var source = renderer.Source(templatePath[..^".sbn".Length]);
            return source.EndsWith("\r\n", StringComparison.Ordinal) ? source[..^2]
                : source.EndsWith('\n') ? source[..^1]
                : source;
        }

        public ValueTask<string?> LoadAsync(TemplateContext context, SourceSpan callerSpan, string templatePath) =>
            ValueTask.FromResult<string?>(Load(context, callerSpan, templatePath));
    }
}
