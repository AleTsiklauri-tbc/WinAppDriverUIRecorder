using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Recorder.Core.Locators;
using Recorder.Core.Model;

namespace Recorder.Core.Persistence;

/// <summary>A recording: the captured elements with their locators, and the steps.</summary>
/// <param name="PageClassNames">Page class names the user typed (spec 10.2), by page.</param>
public sealed record RecordingDocument(
    string? TargetProcessName, DateTimeOffset CreatedAt, DateTimeOffset ModifiedAt, IReadOnlyList<SavedElement> Elements, IReadOnlyList<RecordedStep> Steps,
    IReadOnlyDictionary<PageKey, string>? PageClassNames = null)
{
    /// <summary>What is in the catalog and the step list now.</summary>
    public static RecordingDocument From(ElementCatalog catalog, StepList steps, string? targetProcessName, DateTimeOffset createdAt, DateTimeOffset modifiedAt)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(steps);
        var elements = catalog.Elements
            .Select(e => new SavedElement(e.Id, e.Page, e.Snapshot, e.VariableName, e.UserRenamed, e.Locators, e.SelectedIndex, e.UserChoseLocator))
            .ToList();
        var classNames = catalog.Pages.Where(p => p.ClassName is not null).ToDictionary(p => p.Key, p => p.ClassName!);
        return new RecordingDocument(targetProcessName, createdAt, modifiedAt, elements, [.. steps.Items], classNames);
    }
}

public enum RecordingFileProblem
{
    /// <summary>Not JSON, or not a WinForms Recorder recording.</summary>
    NotARecording,

    /// <summary>Written by a newer version of the recorder.</summary>
    NewerVersion,
}

public sealed class RecordingFileException : Exception
{
    public RecordingFileException(RecordingFileProblem problem, Exception? innerException = null)
        : base("The file is not a recording this version can open: " + problem, innerException) => Problem = problem;

    public RecordingFileProblem Problem { get; }
}

/// <summary>Reads and writes *.wfrec.json recordings (spec 12): schema-versioned, human-readable, UTF-8 without BOM.</summary>
public static class RecordingStore
{
    public const int CurrentSchemaVersion = 1;

    public const string Extension = ".wfrec.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Writes the file atomically: a failed save never leaves a half-written recording behind.</summary>
    public static void Save(string path, RecordingDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".tmp";
        File.WriteAllText(temp, Serialize(document), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, path, overwrite: true);
    }

    /// <exception cref="RecordingFileException">The file is not a recording, or a newer version wrote it.</exception>
    public static RecordingDocument Load(string path) => Deserialize(File.ReadAllText(path, Encoding.UTF8));

    public static string Serialize(RecordingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        // A page known by its window title is kept numbered instead, without the title (D-70).
        var numbered = 0;
        var file = new RecordingFile
        {
            Format = RecordingFile.FormatName,
            SchemaVersion = CurrentSchemaVersion,
            TargetProcessName = document.TargetProcessName,
            CreatedAt = document.CreatedAt,
            ModifiedAt = document.ModifiedAt,
            Pages = [.. document.Elements.GroupBy(e => e.Page).Select(page =>
            {
                var key = page.Key.IsTitled || page.Key.IsNumbered ? PageKey.Numbered(++numbered) : page.Key;
                return new PageFile
                {
                    Id = key.Id,
                    DisplayName = key.DisplayName,
                    ClassName = document.PageClassNames?.GetValueOrDefault(page.Key),
                    Elements = [.. page.Select(ToFile)],
                };
            })],
            Steps = [.. document.Steps.Select(ToFile)],
        };
        return JsonSerializer.Serialize(file, JsonOptions);
    }

    public static RecordingDocument Deserialize(string json)
    {
        RecordingFile? file;
        try
        {
            file = JsonSerializer.Deserialize<RecordingFile>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new RecordingFileException(RecordingFileProblem.NotARecording, exception);
        }

        if (file is null || file.Format != RecordingFile.FormatName || file.SchemaVersion < 1)
        {
            throw new RecordingFileException(RecordingFileProblem.NotARecording);
        }

        if (file.SchemaVersion > CurrentSchemaVersion)
        {
            throw new RecordingFileException(RecordingFileProblem.NewerVersion);
        }

        var elements = file.Pages.SelectMany(page => page.Elements.Select(e => FromFile(e, new PageKey(page.Id, page.DisplayName), file.CreatedAt))).ToList();
        var steps = file.Steps.Select(FromFile).ToList();
        var classNames = file.Pages.Where(p => !string.IsNullOrEmpty(p.ClassName)).ToDictionary(p => new PageKey(p.Id, p.DisplayName), p => p.ClassName!);
        return new RecordingDocument(file.TargetProcessName, file.CreatedAt, file.ModifiedAt, elements, steps, classNames);
    }

    private static ElementFile ToFile(SavedElement element)
    {
        var s = element.Snapshot;
        return new ElementFile
        {
            Id = element.Id,
            VariableName = element.VariableName,
            UserRenamed = element.UserRenamed,
            ControlType = s.ControlType,
            AutomationId = s.AutomationId,

            // A window's Name is its title (D-70).
            Name = NameRules.IsTitled(s.ControlType) ? null : s.Name,
            ClassName = s.ClassName,
            FrameworkId = s.FrameworkId,
            IsPassword = s.IsPassword,
            LabelText = s.LabelText,
            WindowAutomationId = s.TopLevelWindow.AutomationId,
            WindowClassName = s.TopLevelWindow.ClassName,
            InPopup = s.IsDetachedPopup,
            Locators = element.Locators is { } report ? ToFile(report, element.SelectedIndex, element.UserChoseLocator) : null,
        };
    }

    private static SavedElement FromFile(ElementFile e, PageKey page, DateTimeOffset createdAt)
    {
        // No live element: no bounds, process, handle, ancestors or runtime id until it is captured again.
        var snapshot = new ElementSnapshot(
            e.ControlType, e.AutomationId, e.Name, e.ClassName, e.FrameworkId, e.IsPassword, IsEnabled: true, IsOffscreen: false, default, ProcessId: 0,
            NativeWindowHandle: 0, new WindowInfo(0, null, e.WindowAutomationId, e.WindowClassName), [], [], Value: null, e.LabelText, [], createdAt,
            IsDetachedPopup: e.InPopup);
        var report = e.Locators is { } locators ? FromFile(locators) : null;
        var selected = report is null ? -1 : Math.Clamp(e.Locators!.SelectedIndex, -1, report.Candidates.Count - 1);
        return new SavedElement(e.Id, page, snapshot, e.VariableName, e.UserRenamed, report, selected, e.Locators?.UserChose == true);
    }

    private static LocatorsFile ToFile(LocatorReport report, int selectedIndex, bool userChose) => new()
    {
        SelectedIndex = selectedIndex,
        UserChose = userChose,
        IsChecked = report.IsChecked,
        Scope = report.Scope,
        TreeSize = report.TreeSize,
        Candidates = [.. report.Candidates.Select(c => new CandidateFile
        {
            Strategy = c.Strategy,
            Value = c.RenderedValue,
            Scope = c.Scope,
            Stability = c.Stability,
            Score = c.Score,
            MatchCount = c.MatchCount,
            DistinctMatchCount = c.DistinctMatchCount,
            IsUnique = c.IsUnique,
            Notes = [.. c.Notes],
            Kind = c.Explanation.Kind,
            Refinement = c.Explanation.Refinement,
            Anchor = c.Explanation.Anchor,
            MatchesBefore = c.Explanation.MatchesBefore,
            Spec = ToFile(c.Spec),
        })],
    };

    private static LocatorReport FromFile(LocatorsFile file)
    {
        var candidates = file.Candidates.Select(c => new LocatorCandidate(
            c.Strategy, FromFile(c.Spec), c.Value, c.Scope, c.Stability, c.Score, c.MatchCount, c.DistinctMatchCount, c.IsUnique, [.. c.Notes],
            new LocatorExplanation(c.Kind, c.Refinement, c.Anchor, c.MatchesBefore))).ToList();
        var selected = candidates.Count == 0 ? -1 : Math.Clamp(file.SelectedIndex, 0, candidates.Count - 1);
        return new LocatorReport(candidates, selected, file.IsChecked, file.Scope, file.TreeSize, TimeSpan.Zero);
    }

    private static SpecFile ToFile(LocatorSpec spec) => new()
    {
        ResultIndex = spec.ResultIndex,
        Steps = [.. spec.Steps.Select(step => new SpecStepFile
        {
            Axis = step.Axis,
            ControlType = step.ControlType,
            Index = step.Index,
            Predicates = [.. step.Predicates.Select(ToFile)],
        })],
    };

    private static LocatorSpec FromFile(SpecFile file) =>
        new([.. file.Steps.Select(step => new LocatorStep(step.Axis, step.ControlType, [.. step.Predicates.Select(FromFile)], step.Index))], file.ResultIndex);

    private static PredicateFile ToFile(Predicate predicate) => predicate switch
    {
        AttrEquals p => new PredicateFile { Operator = PredicateOperator.Exact, Attribute = p.Attribute, Value = p.Value },
        AttrStartsWith p => new PredicateFile { Operator = PredicateOperator.StartsWith, Attribute = p.Attribute, Value = p.Prefix },
        AttrContains p => new PredicateFile { Operator = PredicateOperator.Contains, Attribute = p.Attribute, Value = p.Part },
        _ => throw new ArgumentOutOfRangeException(nameof(predicate), predicate.GetType().Name, "Unknown predicate."),
    };

    private static Predicate FromFile(PredicateFile file) => file.Operator switch
    {
        PredicateOperator.StartsWith => new AttrStartsWith(file.Attribute, file.Value),
        PredicateOperator.Contains => new AttrContains(file.Attribute, file.Value),
        _ => new AttrEquals(file.Attribute, file.Value),
    };

    private static StepFile ToFile(RecordedStep step) => new()
    {
        Id = step.Id,
        At = step.At,
        Kind = step.Kind,
        Target = step.Target,
        Text = step.Text,
        TestDataKey = step.TestDataKey,
        State = step.State,
        RowIndex = step.RowIndex,
        ColumnIndex = step.ColumnIndex,
        ColumnHeader = step.ColumnHeader,
        MenuItems = step.MenuItems.Count > 0 ? [.. step.MenuItems] : null,
        Comment = step.Comment,
        Scroll = step.Scroll,
        Warnings = step.Warnings.Count > 0 ? [.. step.Warnings] : null,
    };

    private static RecordedStep FromFile(StepFile file) => new()
    {
        Id = file.Id,
        At = file.At,
        Kind = file.Kind,
        Target = file.Target,
        Text = file.Text,
        TestDataKey = file.TestDataKey,
        State = file.State,
        RowIndex = file.RowIndex,
        ColumnIndex = file.ColumnIndex,
        ColumnHeader = file.ColumnHeader,
        MenuItems = file.MenuItems ?? [],
        Comment = file.Comment,
        Scroll = file.Scroll,
        Warnings = file.Warnings ?? [],
    };
}
