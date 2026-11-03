using Recorder.Core.Locators;
using Recorder.Core.Model;
using Recorder.Core.Naming;

namespace Recorder.Core.CodeGen;

/// <summary>
/// Turns recorded steps into Steps methods and into in-place statements. A method is one kind of action on one element
/// (one menu path, one key chord, one check state); its values are parameters, so every recorded value reuses it. Method
/// names follow the user's verbs (spec 15): click…, enter…, select…, moveTo…Tab, check…Checkbox.
/// </summary>
internal sealed class ActionFactory(ProfileSettings settings, ProfileModel profile, TemplateRenderer renderer, NamingOptions naming)
{
    private const int MaxNameInText = 40;

    // Controls whose Name is a caption or the label beside them. A Text, list item or window shows data (a client's name, a title).
    private static readonly HashSet<string> CaptionedTypes = new(StringComparer.Ordinal)
    {
        "Button", "SplitButton", "CheckBox", "RadioButton", "MenuItem", "TabItem", "Hyperlink", "Edit", "ComboBox", "Spinner", "Slider",
    };

    private readonly string _language = settings.Language;

    /// <summary>Identifies the method a step uses: same key, same method.</summary>
    public string Key(RecordedStep step, ElementCode? element)
    {
        ArgumentNullException.ThrowIfNull(step);
        return step.Kind switch
        {
            StepKind.SetChecked => $"{step.Kind}:{step.Target}:{step.State}",
            StepKind.PressKeys => $"{step.Kind}:{step.Target}:{step.Text}",
            StepKind.MenuPath => $"{step.Kind}:{step.Target}:{string.Join('/', step.MenuItems)}",
            _ when IsCell(step) => $"{step.Kind}:cell:{step.Target}:{(HasHeader(step) ? "header" : "index")}:{(UseGridSteps(step, element) ? "helper" : "direct")}",
            _ => $"{step.Kind}:{step.Target}",
        };
    }

    /// <summary>A Steps method for the step's kind and element, with its body rendered with parameters as values.</summary>
    public ActionCode Method(RecordedStep step, ElementCode? element, PageCode page, ISet<string> taken, bool logsValue)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(page);
        var baseName = element is null ? string.Empty : CodeNames.BaseOf(element.VariableName, element.ControlType, naming);
        var parameters = Parameters(step, baseName);
        var name = CodeNames.Unique(Case(MethodName(step, element, baseName)), taken);
        var isCell = IsCell(step);
        var context = Context(
            step, element, page.Variable, value: isCell || parameters.Count != 1 ? null : parameters[0].Name, row: isCell ? parameters[0].Name : null,
            column: isCell ? parameters[1].Name : null, cell: isCell ? CellExpression(step, parameters: true) : null);
        return new ActionCode(step.Kind.ToString(), name, Describe(step, element), element, parameters, renderer.Render("action." + step.Kind, context), logsValue);
    }

    /// <summary>The arguments a test passes to the step's method: the recorded values.</summary>
    public IReadOnlyList<string> Arguments(RecordedStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return step.Kind switch
        {
            StepKind.SetText or StepKind.SelectOption => [Value(step)!],
            _ when IsCell(step) => [Row(step)!, Column(step)!],
            _ => [],
        };
    }

    /// <summary>The step as statements in place, with its recorded values (assertions, window switches, profiles without Steps).</summary>
    public string Statement(RecordedStep step, ElementCode? element, string? pageVariable)
    {
        ArgumentNullException.ThrowIfNull(step);
        var cell = IsCell(step) ? CellExpression(step, parameters: false) : null;
        var context = Context(step, element, pageVariable ?? string.Empty, Value(step), Row(step), Column(step), cell);
        return renderer.Render("action." + step.Kind, context);
    }

    /// <summary>A plain description of what the step does, for log lines, assertion messages and comments.</summary>
    /// <remarks>
    /// Uses the element's Name only when it is a short caption or label; otherwise the variable name, so that no data shown on
    /// screen (a client in a label, a window title) ends up in the code (D-61).
    /// </remarks>
    public static string Describe(RecordedStep step, ElementCode? element)
    {
        ArgumentNullException.ThrowIfNull(step);
        var what = element is null ? "the control"
            : CaptionedTypes.Contains(element.ControlType) && element.Name is { Length: > 0 and <= MaxNameInText } name && !name.Contains('\n', StringComparison.Ordinal)
                ? name
                : element.VariableName;
        var cell = IsCell(step) ? "a cell of " + what : what;
        return step.Kind switch
        {
            StepKind.Click => "Click " + what,
            StepKind.DoubleClick => "Double-click " + cell,
            StepKind.RightClick => "Right-click " + cell,
            StepKind.SetText => "Enter " + what,
            StepKind.ClearText => "Clear " + what,
            StepKind.SelectOption => "Select " + what,
            StepKind.SetChecked when element?.ControlType == "RadioButton" => "Select " + what,
            StepKind.SetChecked => (step.State switch { true => "Check ", false => "Uncheck ", null => "Click " }) + what,
            StepKind.SelectTab => "Open the " + what + " tab",
            StepKind.MenuPath => "Click " + string.Join(" > ", step.MenuItems),
            StepKind.PressKeys => "Press " + step.Text + (element is null ? string.Empty : " in " + what),
            StepKind.GridCellClick => "Click " + cell,
            StepKind.SwitchWindow => "Switch to " + what,
            StepKind.AssertVisible => what + " is visible",
            StepKind.AssertEnabled => what + (step.State == false ? " is disabled" : " is enabled"),
            StepKind.AssertText => "Text of " + what,
            StepKind.AssertValue => "Value of " + what,
            StepKind.AssertChecked => what + (step.State == false ? " is unchecked" : " is checked"),
            _ => "Comment",
        };
    }

    public string Access(string pageVariable, ElementCode element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return settings.ElementAccess.Replace("{page}", pageVariable, StringComparison.Ordinal)
            .Replace("{getter}", element.Getter, StringComparison.Ordinal)
            .Replace("{property}", element.Property, StringComparison.Ordinal);
    }

    private static bool IsCell(RecordedStep step) =>
        step.RowIndex is not null && step.Kind is StepKind.GridCellClick or StepKind.DoubleClick or StepKind.RightClick;

    private static bool HasHeader(RecordedStep step) => !string.IsNullOrEmpty(step.ColumnHeader);

    // The framework's grid helper clicks only the cells it can find (D-83): GridSteps.clickCell finds a text cell by its header,
    // in the one grid its GridPage looks up. Any other cell (a check box, a column without a header, another grid) is clicked
    // inside its own grid.
    private bool UseGridSteps(RecordedStep step, ElementCode? element) =>
        IsCell(step) && HasHeader(step) && settings.GridSteps is { } helper
        && string.Equals(element?.AutomationId, helper.GridAutomationId, StringComparison.Ordinal)
        && step.CellControlType is { } type && (helper.CellControlTypes ?? []).Contains(type, StringComparer.Ordinal);

    private ActionContext Context(RecordedStep step, ElementCode? element, string pageVariable, string? value, string? row, string? column, string? cell)
    {
        var target = element is null ? string.Empty : Access(pageVariable, element);
        var keys = step.Kind == StepKind.PressKeys && step.Text is { Length: > 0 } chord ? KeyCode.Expression(chord, _language) : null;
        var menu = step.Kind == StepKind.MenuPath ? step.MenuItems.Skip(1).Select(MenuItemLocator).ToList() : [];
        var state = step.State is { } s ? (s ? "true" : "false") : null;
        return new ActionContext(
            step.Kind.ToString(), element, target, value, state, keys, menu, row, column, cell, UseGridSteps(step, element), Describe(step, element),
            CommentOf(step, element), profile);
    }

    // The value as code: a literal, or the test-data expression for a secret (D-9).
    private string? Value(RecordedStep step) => step switch
    {
        { Kind: StepKind.SetText, TestDataKey: { } key } =>
            settings.TestDataExpression.Replace("{key}", CodeNames.Literal(key, _language), StringComparison.Ordinal),
        { Kind: StepKind.SetText or StepKind.SelectOption or StepKind.AssertText or StepKind.AssertValue } => CodeNames.Literal(step.Text, _language),
        _ => null,
    };

    private static string? Row(RecordedStep step) => IsCell(step) ? CodeNames.Number(step.RowIndex!.Value) : null;

    // The header, or for a column without one (the LMS check box column) its index (D-54).
    private string? Column(RecordedStep step) =>
        !IsCell(step) ? null : HasHeader(step) ? CodeNames.Literal(step.ColumnHeader, _language) : CodeNames.Number(step.ColumnIndex ?? 0);

    // A text cell is named "<Header> Row <n>, Not sorted.", a check box cell "<Header> Row <n>" (F-6); "Row 2" must not find "Row 20".
    private string CellExpression(RecordedStep step, bool parameters)
    {
        if (!parameters)
        {
            var name = (step.ColumnHeader ?? string.Empty) + " Row " + CodeNames.Number(step.RowIndex!.Value);
            return CodeNames.Literal($".//*[@Name={XPathLiteral.Quote(name)} or starts-with(@Name,{XPathLiteral.Quote(name + ",")})]", _language);
        }

        var cellName = (HasHeader(step) ? "columnHeader + " : string.Empty) + L(" Row ") + " + rowIndex";
        return $"{L(".//*[@Name='")} + {cellName} + {L("' or starts-with(@Name,'")} + {cellName} + {L(",')]")}";
    }

    private string L(string text) => CodeNames.Literal(text, _language);

    private static string? CommentOf(RecordedStep step, ElementCode? element)
    {
        var text = step switch
        {
            { Kind: StepKind.Comment, Scroll: { } notches } =>
                $"Scrolled {(notches < 0 ? "down" : "up")} {Math.Abs(notches)} notch{(Math.Abs(notches) == 1 ? string.Empty : "es")} in {element?.VariableName ?? "the app"}",
            _ => step.Comment,
        };

        // One line, and nothing that ends a block comment.
        return text?.ReplaceLineEndings(" ").Replace("*/", "* /", StringComparison.Ordinal).Trim();
    }

    private string MenuItemLocator(string name)
    {
        var xpath = "//MenuItem[@Name=" + XPathLiteral.Quote(name) + "]";
        var format = settings.LocatorFormats.GetValueOrDefault(nameof(LocatorStrategy.XPath), "{value}");
        return format.Replace("{value}", CodeNames.Literal(xpath, _language), StringComparison.Ordinal);
    }

    private List<ParameterCode> Parameters(RecordedStep step, string baseName)
    {
        var text = _language == "csharp" ? "string" : "String";
        if (IsCell(step))
        {
            return [new ParameterCode("int", "rowIndex"), HasHeader(step) ? new ParameterCode(text, "columnHeader") : new ParameterCode("int", "columnIndex")];
        }

        return step.Kind is StepKind.SetText or StepKind.SelectOption
            ? [new ParameterCode(text, CodeNames.Safe(CodeNames.Camel(baseName.Length > 0 ? baseName : "value"), _language))]
            : [];
    }

    private static string MethodName(RecordedStep step, ElementCode? element, string baseName)
    {
        var full = CodeNames.Pascal(element?.VariableName ?? "Control");
        var named = CodeNames.Pascal(baseName.Length > 0 ? baseName : element?.VariableName ?? "Control");
        return step.Kind switch
        {
            StepKind.SetText => "enter" + named,
            StepKind.ClearText => "clear" + named,
            StepKind.SelectOption => "select" + named,
            StepKind.SetChecked when element?.ControlType == "RadioButton" => "select" + full,
            StepKind.SetChecked => (step.State switch { true => "check", false => "uncheck", null => "click" }) + full,
            StepKind.SelectTab => "moveTo" + full,
            StepKind.MenuPath => "click" + string.Concat(step.MenuItems.Select(CodeNames.Pascal)),
            StepKind.PressKeys => "press" + KeyCode.MethodPart(step.Text ?? "Key") + (element is null ? string.Empty : "In" + named),
            StepKind.GridCellClick => "click" + full + "Cell",
            StepKind.DoubleClick when IsCell(step) => "doubleClick" + full + "Cell",
            StepKind.RightClick when IsCell(step) => "rightClick" + full + "Cell",
            StepKind.DoubleClick => "doubleClick" + full,
            StepKind.RightClick => "rightClick" + full,
            _ => "click" + full,
        };
    }

    private string Case(string name) => settings.MethodCase == "pascal" ? CodeNames.UpperFirst(name) : name;
}
