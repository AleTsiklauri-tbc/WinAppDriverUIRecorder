namespace Recorder.Core.CodeGen;

// What templates see (spec 10.1). Scriban exposes these properties in snake_case: ClassName → class_name.
// docs/TEMPLATES.md documents every member with examples.

/// <summary>The whole recording as code: its pages and its test.</summary>
public sealed record CodeModel(ProfileModel Profile, IReadOnlyList<PageCode> Pages, TestCode Test);

/// <summary>The profile's settings that templates need.</summary>
/// <param name="Classes">Fully qualified framework classes by role ("basePage", "log"…), as in profile.json.</param>
public sealed record ProfileModel(
    string Name, string Language, string Package, string StepsPackage, string TestPackage, string Indent, IReadOnlyDictionary<string, string> Classes);

/// <summary>One page (a form or dialog, D-8): its elements become a page class, its actions a Steps class.</summary>
/// <param name="Name">The base name, e.g. "Loan" for frmLoan.</param>
/// <param name="Uses">Which framework types the page's actions need, for imports.</param>
public sealed record PageCode(
    string Name, string ClassName, string StepsClassName, string Variable, string StepsVariable, string? WindowAutomationId,
    IReadOnlyList<ElementCode> Elements, IReadOnlyList<ActionCode> Actions, UsageCode Uses);

/// <summary>A captured element and its chosen locator.</summary>
/// <param name="Locator">The find expression from the profile's locator formats, e.g. winDriver.findElementByAccessibilityId("txtAmount").</param>
/// <param name="Literal">The locator value as a string literal of the profile's language.</param>
/// <param name="Scope">"Window", or "Desktop" for an element only a desktop session reaches (F-23, D-38).</param>
/// <param name="IsChecked">False when the locator could not be checked for uniqueness.</param>
public sealed record ElementCode(
    string VariableName, string Getter, string Property, string ControlType, string? AutomationId, string? Name, string Strategy, string Value,
    string Literal, string Locator, string Scope, bool IsUnique, bool IsChecked, string Stability);

/// <summary>A Steps method: one kind of action on one element, used by one or more recorded steps.</summary>
/// <param name="Body">The statements, rendered by action.&lt;Kind&gt;.sbn with the parameters as values.</param>
/// <param name="Description">A plain description for the log line, e.g. "Enter Amount".</param>
public sealed record ActionCode(
    string Kind, string Method, string Description, ElementCode? Element, IReadOnlyList<ParameterCode> Parameters, string Body, bool LogsValue);

public sealed record ParameterCode(string Type, string Name);

/// <summary>The test for the recording.</summary>
/// <param name="Steps">Pages whose Steps classes the test calls, in order of first use.</param>
/// <param name="Pages">Pages whose page objects the test's statements use, in order of first use.</param>
public sealed record TestCode(
    string ClassName, string Method, string Description, IReadOnlyList<TestBlock> Blocks, IReadOnlyList<PageCode> Steps, IReadOnlyList<PageCode> Pages,
    UsageCode Uses);

/// <summary>
/// A part of the test, in recording order. "chain": calls on one Steps object (fluent). "statement": code rendered in place,
/// with literal values (assertions, window switches; every step when the profile has no Steps layer). "comment": a comment,
/// rendered by action.Comment.sbn. "unknown": a step whose element was removed from the catalog; Comment says which.
/// </summary>
/// <param name="DeclareSteps">The first use of this page's Steps object: declare it here.</param>
/// <param name="DeclarePage">The first use of this page's page object in a statement: declare it here.</param>
/// <param name="Body">The statements of a "statement" or "comment" block.</param>
public sealed record TestBlock(
    string Kind, PageCode? Page, bool DeclareSteps, bool DeclarePage, IReadOnlyList<CallCode> Calls, string? Body, string? Comment);

/// <summary>A call of a Steps method with the recorded values as arguments.</summary>
public sealed record CallCode(ActionCode Action, IReadOnlyList<string> Arguments);

/// <summary>Framework types code uses, so templates can import exactly what is needed.</summary>
public sealed record UsageCode(bool Keys, bool Actions, bool GridSteps, bool Assert, bool TestData, bool Utility);

/// <summary>What an action template sees when it renders one action's statements.</summary>
/// <param name="Target">The element expression, e.g. loanPage.getAmountInput(); empty when the action has no element.</param>
/// <param name="Value">The value expression: a parameter name, or a literal.</param>
/// <param name="State">"true", "false" or null.</param>
/// <param name="Keys">The key expression for PressKeys, e.g. Keys.chord(Keys.CONTROL, "s").</param>
/// <param name="MenuItems">Find expressions for the menu items after the first one.</param>
/// <param name="Row">The grid row expression; null outside grids.</param>
/// <param name="Column">The grid column expression: the header as a string, or the column index for a column without a header (D-54).</param>
/// <param name="Cell">An expression giving the XPath of the cell below the grid, e.g. <c>".//*[@Name='Status Row 2' or starts-with(@Name,'Status Row 2,')]"</c> (F-6).</param>
/// <param name="Comment">One line of text for a Comment step.</param>
public sealed record ActionContext(
    string Kind, ElementCode? Element, string Target, string? Value, string? State, string? Keys, IReadOnlyList<string> MenuItems,
    string? Row, string? Column, string? Cell, string Description, string? Comment, ProfileModel Profile);
