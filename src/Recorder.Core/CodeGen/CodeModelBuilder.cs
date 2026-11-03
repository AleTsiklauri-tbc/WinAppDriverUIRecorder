using System.Text.RegularExpressions;
using Recorder.Core.Locators;
using Recorder.Core.Model;
using Recorder.Core.Naming;

namespace Recorder.Core.CodeGen;

/// <summary>
/// Builds the template model from the captured elements and the recorded steps. Pages come from the catalog (one per
/// form, D-8); each step becomes a call of a Steps method, or a statement in the test for test-level kinds (assertions,
/// window switches, comments) and for profiles without a Steps layer.
/// </summary>
internal sealed class CodeModelBuilder
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private readonly ProfileSettings _settings;
    private readonly ProfileModel _profile;
    private readonly ActionFactory _actions;
    private readonly HashSet<string> _testLevel;

    public CodeModelBuilder(CodeProfile profile, TemplateRenderer renderer, NamingOptions naming)
    {
        _settings = profile.Settings;
        _profile = new ProfileModel(
            _settings.Name, _settings.Language, _settings.Package, _settings.StepsPackage, _settings.TestPackage, _settings.Indent, _settings.Classes);
        _actions = new ActionFactory(_settings, _profile, renderer, naming);
        _testLevel = new HashSet<string>(_settings.TestLevelKinds, StringComparer.Ordinal);
    }

    public ProfileModel Profile => _profile;

    /// <param name="pageKeys">The page of each of the model's pages, in the same order.</param>
    public CodeModel Build(ElementCatalog catalog, IReadOnlyList<RecordedStep> steps, string testName, out IReadOnlyList<PageKey> pageKeys)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(steps);
        var pages = new List<PageDraft>();
        var classNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in catalog.Pages.Where(p => p.Elements.Count > 0))
        {
            // A typed class name is kept as typed, unless another page already has it.
            var wanted = page.ClassName is { } renamed ? StripSuffix(renamed) : CodeNames.PageName(page.Key, _settings.StripPageIdPrefixes);
            var name = CodeNames.Unique(wanted, classNames);
            var className = page.ClassName is { } typed && name == wanted ? typed : name + _settings.PageClassSuffix;
            pages.Add(new PageDraft(page, name, className, page.Elements.ToDictionary(e => e.Id, Element), _settings));
        }

        var byElement = pages.SelectMany(p => p.Elements.Keys.Select(id => (id, p))).ToDictionary(x => x.id, x => x.p);
        var secretKeys = steps.Where(s => s.TestDataKey is not null).Select(ActionFactory.Key).ToHashSet(StringComparer.Ordinal);
        var blocks = new List<TestBlock>();
        var declaredSteps = new HashSet<PageDraft>();
        var declaredPages = new HashSet<PageDraft>();
        foreach (var step in steps)
        {
            var page = step.Target is { } target ? byElement.GetValueOrDefault(target) : null;
            var element = page?.Elements[step.Target!.Value];
            if (step.Kind == StepKind.Comment)
            {
                blocks.Add(new TestBlock("comment", null, false, false, [], _actions.Statement(step, element, page?.Variable), step.Comment));
            }
            else if (page is null && step.Kind != StepKind.SwitchWindow && (step.Target is not null || step.Kind != StepKind.PressKeys))
            {
                blocks.Add(new TestBlock("unknown", null, false, false, [], null, ActionFactory.Describe(step, null) + ": its element is not in the Elements list"));
            }
            else if (page is null || !_settings.HasStepsLayer || _testLevel.Contains(step.Kind.ToString()))
            {
                // Window switches and keys pressed with no element need no page object.
                var body = _actions.Statement(step, element, page?.Variable);
                var declare = page is not null && Regex.IsMatch(body, @"\b" + Regex.Escape(page.Variable) + @"\b", RegexOptions.None, RegexTimeout) && declaredPages.Add(page);
                blocks.Add(new TestBlock("statement", page?.Code, false, declare, [], body, null));
            }
            else
            {
                var key = ActionFactory.Key(step);
                if (!page.Actions.TryGetValue(key, out var action))
                {
                    action = _actions.Method(step, element, page.Code, page.MethodNames, logsValue: !secretKeys.Contains(key) && step.Kind is StepKind.SetText or StepKind.SelectOption);
                    page.Actions.Add(key, action);
                }

                var call = new CallCode(action, _actions.Arguments(step));
                if (blocks.Count > 0 && blocks[^1] is { Kind: "chain" } chain && chain.Page == page.Code)
                {
                    blocks[^1] = chain with { Calls = [.. chain.Calls, call] };
                }
                else
                {
                    blocks.Add(new TestBlock("chain", page.Code, declaredSteps.Add(page), false, [call], null, null));
                }
            }
        }

        // Blocks were built while the pages' actions were still being collected: point them at the finished pages.
        var finished = pages.ToDictionary<PageDraft, PageCode, PageCode>(
            p => p.Code, p => p.Finish(Uses(p.Actions.Values.Select(a => a.Body))), ReferenceEqualityComparer.Instance);
        blocks = [.. blocks.Select(b => b.Page is null ? b : b with { Page = finished[b.Page] })];
        var stepsPages = blocks.Where(b => b.Kind == "chain").Select(b => b.Page!).Distinct().ToList();
        var pageObjects = blocks.Where(b => b.DeclarePage).Select(b => b.Page!).ToList();
        var testUses = Uses(blocks.Select(b => b.Body ?? string.Empty)) with { TestData = steps.Any(s => s.TestDataKey is not null) };
        var test = new TestCode(TestClassName(testName), MethodCase(CodeNames.Camel(testName)), testName, blocks, stepsPages, pageObjects, testUses);
        pageKeys = [.. pages.Select(p => p.Key)];
        return new CodeModel(_profile, [.. pages.Select(p => finished[p.Code])], test);
    }

    /// <summary>One element as the template model sees it, with the locator the user chose (or the best one).</summary>
    public ElementCode Element(string variableName, ElementSnapshot snapshot, LocatorCandidate? candidate)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        candidate ??= LocatorEngine.Unchecked(LocatorSubject.From(snapshot, snapshot.Scope), TimeSpan.Zero).Best;
        var strategy = candidate?.Strategy ?? LocatorStrategy.XPath;
        var value = candidate?.RenderedValue ?? "//" + snapshot.ControlType;
        var literal = CodeNames.Literal(value, _settings.Language);
        var format = _settings.LocatorFormats.GetValueOrDefault(strategy.ToString(), "{value}");

        // A window's Name, or its title bar's, is its title (D-61).
        var name = NameRules.IsTitled(snapshot.ControlType) ? null : snapshot.Name;
        return new ElementCode(
            variableName, VariableNamer.Getter(variableName), CodeNames.UpperFirst(variableName), snapshot.ControlType, snapshot.AutomationId, name,
            strategy.ToString(), value, literal, format.Replace("{value}", literal, StringComparison.Ordinal), (candidate?.Scope ?? snapshot.Scope).ToString(),
            candidate?.IsUnique ?? false, candidate?.IsChecked ?? false, (candidate?.Stability ?? Stability.Low).ToString());
    }

    // Uses of framework types, read from the generated statements so imports always match the code.
    private UsageCode Uses(IEnumerable<string> bodies)
    {
        var text = string.Join('\n', bodies);
        bool Has(string role, string fallback) =>
            Regex.IsMatch(text, @"(?<![\w.])" + Regex.Escape(TemplateRenderer.SimpleName(_settings.Classes.GetValueOrDefault(role, fallback))) + @"\b", RegexOptions.None, RegexTimeout);
        return new UsageCode(
            Keys: Has("keys", "Keys"),
            Actions: Has("actions", "Actions"),
            GridSteps: Has("gridSteps", "GridSteps"),
            Assert: Has("assert", "Assert"),
            TestData: false,
            Utility: Has("utility", "Utility"));
    }

    private ElementCode Element(PageElement element) =>
        Element(element.VariableName, element.Snapshot, element.Selected ?? element.Locators?.Best);

    private string StripSuffix(string className) =>
        className.Length > _settings.PageClassSuffix.Length && className.EndsWith(_settings.PageClassSuffix, StringComparison.Ordinal)
            ? className[..^_settings.PageClassSuffix.Length]
            : className;

    private string TestClassName(string testName)
    {
        var name = CodeNames.Pascal(testName);
        name = name.Length == 0 || char.IsAsciiDigit(name[0]) ? "Recorded" + name : name;
        return name.EndsWith(_settings.TestClassSuffix, StringComparison.Ordinal) ? name : name + _settings.TestClassSuffix;
    }

    private string MethodCase(string name)
    {
        var safe = name.Length == 0 || char.IsAsciiDigit(name[0]) ? "recorded" + CodeNames.UpperFirst(name) : name;
        return _settings.MethodCase == "pascal" ? CodeNames.UpperFirst(safe) : CodeNames.Safe(safe, _settings.Language);
    }

    /// <summary>A page while its actions are collected.</summary>
    private sealed class PageDraft
    {
        /// <param name="name">The page's base name, unique among the pages.</param>
        /// <param name="className">The class name: the user's when the base name is theirs, else derived.</param>
        public PageDraft(PageModel page, string name, string className, Dictionary<Guid, ElementCode> elements, ProfileSettings settings)
        {
            Key = page.Key;
            Elements = elements;
            Variable = CodeNames.LowerFirst(name) + settings.PageClassSuffix;
            Code = new PageCode(
                name, className, name + settings.StepsClassSuffix, Variable, CodeNames.LowerFirst(name) + settings.StepsClassSuffix,
                page.Key.Id.StartsWith("id:", StringComparison.Ordinal) ? page.Key.DisplayName : null,
                [.. elements.Values], [], new UsageCode(false, false, false, false, false, false));
        }

        public PageKey Key { get; }

        public Dictionary<Guid, ElementCode> Elements { get; }

        public string Variable { get; }

        public PageCode Code { get; }

        public Dictionary<string, ActionCode> Actions { get; } = new(StringComparer.Ordinal);

        public HashSet<string> MethodNames { get; } = new(StringComparer.Ordinal);

        public PageCode Finish(UsageCode uses) => Code with { Actions = [.. Actions.Values], Uses = uses };
    }
}
