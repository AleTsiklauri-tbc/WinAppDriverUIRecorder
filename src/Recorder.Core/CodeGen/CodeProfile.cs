using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Recorder.Core.Naming;

namespace Recorder.Core.CodeGen;

/// <summary>
/// The settings of a code profile, read from its <c>profile.json</c> (spec 10.1). Templates see them as <c>profile.*</c>.
/// </summary>
public sealed record ProfileSettings
{
    public string Name { get; init; } = string.Empty;

    /// <summary>"java" or "csharp": decides string literals, key names and keywords.</summary>
    public string Language { get; init; } = "java";

    public string FileExtension { get; init; } = ".java";

    /// <summary>The code view's syntax highlighting: "Java" or "C#".</summary>
    public string Highlighting { get; init; } = "Java";

    /// <summary>Package (namespace) of page classes.</summary>
    public string Package { get; init; } = string.Empty;

    /// <summary>Package of Steps classes; empty when the profile has no Steps layer.</summary>
    public string StepsPackage { get; init; } = string.Empty;

    public string TestPackage { get; init; } = string.Empty;

    public string PageClassSuffix { get; init; } = "Page";

    public string StepsClassSuffix { get; init; } = "Steps";

    public string TestClassSuffix { get; init; } = "Test";

    /// <summary>Prefixes removed from a form's AutomationId for its class name: frmLoanSearch → LoanSearchPage (spec 10.2).</summary>
    public IReadOnlyList<string> StripPageIdPrefixes { get; init; } = ["frm"];

    public string Indent { get; init; } = "    ";

    /// <summary>"camel" (Java: recordedFlow) or "pascal" (C#: RecordedFlow) for method names.</summary>
    public string MethodCase { get; init; } = "camel";

    /// <summary>How a page reaches an element: {page} is the page variable, {getter} and {property} the element's accessors.</summary>
    public string ElementAccess { get; init; } = "{page}.{getter}()";

    /// <summary>One find expression per strategy; {value} is the locator value as a string literal.</summary>
    public IReadOnlyDictionary<string, string> LocatorFormats { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The expression a test passes for a secret (D-9); {key} is the test-data key as a string literal.</summary>
    public string TestDataExpression { get; init; } = "{key}";

    /// <summary>Step kinds rendered as statements in the test rather than as Steps methods (assertions, window switches, comments).</summary>
    public IReadOnlyList<string> TestLevelKinds { get; init; } = [];

    /// <summary>Fully qualified names of the framework classes the templates use (base page, base test, log, config reader…).</summary>
    public IReadOnlyDictionary<string, string> Classes { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Regular expressions that let "Merge into existing file" understand a file (spec 10.5).</summary>
    public MergeSettings Merge { get; init; } = new();

    /// <summary>The cells the grid helper in <c>classes.gridSteps</c> can click (D-83). Null: every cell is clicked inside its grid.</summary>
    public GridStepsSettings? GridSteps { get; init; }

    /// <summary>Prefixes removed from an AutomationId for a variable name: txtAmount → amount (spec 10.2). Null: the defaults.</summary>
    public IReadOnlyList<string>? StripIdPrefixes { get; init; }

    /// <summary>Variable name suffixes by control type, on top of the defaults: Edit → Input (spec 10.1, D-6).</summary>
    public IReadOnlyDictionary<string, string>? ElementSuffixByControlType { get; init; }

    public bool HasStepsLayer => StepsPackage.Length > 0;

    /// <summary>How elements captured while this profile is active are named.</summary>
    public NamingOptions Naming()
    {
        var defaults = NamingOptions.Default;
        var suffixes = new Dictionary<string, string>(defaults.SuffixByControlType, StringComparer.Ordinal);
        foreach (var (controlType, suffix) in ElementSuffixByControlType ?? new Dictionary<string, string>())
        {
            suffixes[controlType] = suffix;
        }

        return defaults with { StripIdPrefixes = StripIdPrefixes ?? defaults.StripIdPrefixes, SuffixByControlType = suffixes };
    }
}

/// <summary>
/// Regular expressions that let "Merge into existing file" read a class (spec 10.5). Member and Method are matched against
/// one declaration of the class body, its line breaks and runs of spaces turned into single spaces and its leading comments
/// and annotations removed; Import, ClassDeclaration and Package against single lines.
/// </summary>
/// <param name="Member">
/// A locator member; group "name" is its name, group "value" identifies what it finds (the find call with its literal):
/// members with the same value find the same element.
/// </param>
/// <param name="Method">A method; group "name" is its name.</param>
/// <param name="Import">An import (using) line.</param>
/// <param name="ClassDeclaration">The class declaration line; group "name" is the class name.</param>
/// <param name="Package">The line new imports follow when the file has none (Java's package line); empty: the top of the file.</param>
public sealed record MergeSettings(string Member = "", string Method = "", string Import = "", string ClassDeclaration = "", string Package = "");

/// <summary>
/// The cells a framework's grid helper can click (D-83): with a header, in one grid, of some control types. LMSAutomationDemo's
/// <c>GridSteps.clickCell(row, header)</c> finds text cells by their header in the grid its GridPage finds (AutomationId "Grid").
/// </summary>
/// <param name="GridAutomationId">The AutomationId of the grid the helper works on.</param>
/// <param name="CellControlTypes">The control types of the cells it finds.</param>
public sealed record GridStepsSettings(string GridAutomationId = "", IReadOnlyList<string>? CellControlTypes = null);

/// <summary>A code profile: settings and Scriban templates by name ("page", "action.Click"…).</summary>
/// <param name="Id">The folder name, e.g. "java-appium-pom".</param>
/// <param name="Folder">The user folder it was read from; null for a built-in profile.</param>
public sealed record CodeProfile(string Id, ProfileSettings Settings, IReadOnlyDictionary<string, string> Templates, string? Folder)
{
    public const string DefaultId = "java-appium-pom";

    public bool IsBuiltIn => Folder is null;

    public string DisplayName => Settings.Name.Length > 0 ? Settings.Name : Id;

    public string? Template(string name) => Templates.GetValueOrDefault(name);
}

/// <summary>A profile that could not be read, with a plain reason.</summary>
public sealed record ProfileProblem(string ProfileId, string File, string Message);

public sealed record ProfileSet(IReadOnlyList<CodeProfile> Profiles, IReadOnlyList<ProfileProblem> Problems)
{
    /// <summary>The profile with this id, else the default, else the first.</summary>
    public CodeProfile Find(string? id) =>
        Profiles.FirstOrDefault(p => p.Id == id) ?? Profiles.FirstOrDefault(p => p.Id == CodeProfile.DefaultId) ?? Profiles[0];
}

/// <summary>
/// Reads code profiles: the built-in ones embedded in this assembly (from src/Recorder.Profiles), then the user's folders
/// under %APPDATA%\WinFormsRecorder\profiles. A folder with a built-in's name overrides it file by file: what the folder
/// lacks (profile.json, a template) comes from the built-in (spec 10.1, D-60).
/// </summary>
public static partial class ProfileLoader
{
    private const string ResourcePrefix = "Profiles/";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    public static ProfileSet Load(string? userProfilesDirectory)
    {
        var problems = new List<ProfileProblem>();
        var profiles = BuiltIn().ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        if (userProfilesDirectory is not null && Directory.Exists(userProfilesDirectory))
        {
            foreach (var folder in Directory.GetDirectories(userProfilesDirectory).Order(StringComparer.OrdinalIgnoreCase))
            {
                var id = Path.GetFileName(folder);
                try
                {
                    var builtIn = profiles.GetValueOrDefault(id) is { IsBuiltIn: true } original ? original : null;
                    var templates = new Dictionary<string, string>(builtIn?.Templates ?? new Dictionary<string, string>(), StringComparer.Ordinal);
                    foreach (var file in Directory.GetFiles(folder, "*.sbn"))
                    {
                        templates[Path.GetFileNameWithoutExtension(file)] = File.ReadAllText(file);
                    }

                    var json = Path.Combine(folder, "profile.json");
                    var settings = File.Exists(json) ? Parse(File.ReadAllText(json)) : builtIn?.Settings;
                    if (settings is null)
                    {
                        problems.Add(new ProfileProblem(id, "profile.json", "profile.json is missing."));
                        continue;
                    }

                    profiles[id] = new CodeProfile(id, settings, templates, folder);
                }
                catch (JsonException exception)
                {
                    problems.Add(new ProfileProblem(id, "profile.json", exception.Message));
                }
                catch (IOException exception)
                {
                    problems.Add(new ProfileProblem(id, folder, exception.Message));
                }
            }
        }

        return new ProfileSet([.. profiles.Values.OrderBy(p => p.Id == CodeProfile.DefaultId ? 0 : 1).ThenBy(p => p.Id, StringComparer.Ordinal)], problems);
    }

    /// <summary>The profiles that ship with the recorder.</summary>
    public static IReadOnlyList<CodeProfile> BuiltIn()
    {
        var assembly = typeof(ProfileLoader).Assembly;
        var files = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Select(name => (Resource: name, Path: name[ResourcePrefix.Length..].Replace('\\', '/')))
            .Where(file => file.Path.Contains('/', StringComparison.Ordinal))
            .GroupBy(file => file.Path[..file.Path.IndexOf('/', StringComparison.Ordinal)], StringComparer.Ordinal);
        var profiles = new List<CodeProfile>();
        foreach (var profile in files)
        {
            var texts = profile.ToDictionary(file => file.Path[(profile.Key.Length + 1)..], file => Read(assembly, file.Resource), StringComparer.Ordinal);
            var templates = texts.Where(t => t.Key.EndsWith(".sbn", StringComparison.Ordinal))
                .ToDictionary(t => t.Key[..^4], t => t.Value, StringComparer.Ordinal);
            profiles.Add(new CodeProfile(profile.Key, Parse(texts["profile.json"]), templates, null));
        }

        return profiles;
    }

    /// <summary>Writes a profile's files into a folder (to edit a copy of a built-in profile).</summary>
    public static void Export(CodeProfile profile, string folder)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Directory.CreateDirectory(folder);
        var assembly = typeof(ProfileLoader).Assembly;
        var prefix = ResourcePrefix + profile.Id;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.Replace('\\', '/').StartsWith(prefix + "/", StringComparison.Ordinal)))
        {
            File.WriteAllText(Path.Combine(folder, name.Replace('\\', '/')[(prefix.Length + 1)..]), Read(assembly, name), new System.Text.UTF8Encoding(false));
        }
    }

    /// <summary>
    /// Writes a complete copy of a profile into a new folder under another name: every template, and its profile.json with the
    /// name replaced (its comments kept).
    /// </summary>
    public static void Copy(CodeProfile profile, string folder, string name)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Directory.CreateDirectory(folder);
        var utf8 = new System.Text.UTF8Encoding(false);
        foreach (var (template, text) in profile.Templates)
        {
            File.WriteAllText(Path.Combine(folder, template + ".sbn"), text, utf8);
        }

        var own = profile.Folder is { } source ? Path.Combine(source, "profile.json") : null;
        var json = own is not null && File.Exists(own) ? File.ReadAllText(own) : BuiltInJson(profile.Id);
        json = NameProperty().Replace(json, "\"name\": " + JsonSerializer.Serialize(name), 1);
        File.WriteAllText(Path.Combine(folder, "profile.json"), json, utf8);
    }

    public static ProfileSettings Parse(string json) =>
        JsonSerializer.Deserialize<ProfileSettings>(json, JsonOptions) ?? throw new JsonException("profile.json is empty.");

    private static string BuiltInJson(string id)
    {
        var assembly = typeof(ProfileLoader).Assembly;
        var resource = assembly.GetManifestResourceNames().FirstOrDefault(n => n.Replace('\\', '/') == ResourcePrefix + id + "/profile.json")
            ?? throw new InvalidOperationException("No built-in profile.json for " + id);
        return Read(assembly, resource);
    }

    [System.Text.RegularExpressions.GeneratedRegex("\"name\"\\s*:\\s*\"(?:[^\"\\\\]|\\\\.)*\"")]
    private static partial System.Text.RegularExpressions.Regex NameProperty();

    private static string Read(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
