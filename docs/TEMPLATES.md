# Code templates

WinForms Recorder writes code through **profiles** (spec 10). A profile is a folder with a `profile.json` and
[Scriban](https://github.com/scriban/scriban/blob/master/doc/language.md) templates. Two are built in:

| Profile | Writes |
|---|---|
| `java-appium-pom` (default) | Java for LMSAutomationDemo: Page classes (locators), Steps classes (fluent actions), a TestNG test. Appium java-client 7.6 |
| `csharp-appium` | C# with Appium.WebDriver 4 and MSTest: page classes (properties) and a test that calls them directly |

The code pane shows the active profile's output and updates 150 ms after anything changes; while the window is minimized,
as during a recording, it waits until the window shows again. Choose the profile at the top right of the pane, or in
Settings › Code generation.

## Changing a profile

**Edit profile…** in the code pane copies the active profile to
`%APPDATA%\WinFormsRecorder\profiles\<profile>\` (unless it is there already) and opens that folder. Edit a template,
save, and the code pane shows the result at once; there is no restart.

**Duplicate profile** (Settings › Code generation) copies the active profile, every file of it, to a new folder
`<profile>-copy` named "… (copy)", makes the copy the active profile and opens its folder: a way to change a profile and
keep the original as it is. **Open profiles folder**, next to it, opens `%APPDATA%\WinFormsRecorder\profiles`.

- A folder with the same name as a built-in profile overrides it **file by file**: delete everything except the files
  you change, and the rest still comes from the built-in profile (and gets its fixes in later versions).
- A folder with another name is a new profile; it needs its own `profile.json`, and every template it lacks is an error
  when it is needed.
- A folder that cannot be read (broken JSON) is left out and named in a banner until it is fixed.
- To go back to the built-in profile, delete or rename the folder.

## Files of a profile

| File | Renders | Sees |
|---|---|---|
| `profile.json` | Settings (below) | — |
| `page.sbn` | One page class per page (form or dialog) | `profile`, `page` |
| `element.sbn` | One locator member, usually `include`d by `page.sbn` | `profile`, `page`, `element` |
| `steps.sbn` | One Steps class per page that has actions (profiles with a Steps layer) | `profile`, `page` |
| `test.sbn` | The test for the recording | `profile`, `test`, `pages` |
| `snippet.sbn` | The single line the locator card copies (Ctrl+C) | `profile`, `element` |
| `action.<Kind>.sbn` | The statements of one step kind, one file per kind | the action context (below) |

The 18 step kinds: `Click`, `DoubleClick`, `RightClick`, `SetText`, `ClearText`, `SelectOption`, `SetChecked`,
`SelectTab`, `MenuPath`, `PressKeys`, `GridCellClick`, `SwitchWindow`, `AssertVisible`, `AssertEnabled`, `AssertText`,
`AssertValue`, `AssertChecked`, `Comment`.

## profile.json

Comments (`//`, `/* */`) and trailing commas are allowed.

| Key | Meaning | Java profile |
|---|---|---|
| `name` | Shown in the profile picker | `Java · TestNG · Appium 7` |
| `language` | `java` or `csharp`: string literals, key names and keywords | `java` |
| `fileExtension` | Of saved and exported files | `.java` |
| `highlighting` | The code view's colours and the label "Copy *Java* line": `Java` or `C#` | `Java` |
| `package` | Package (namespace) of page classes | `tbc.automation.pages` |
| `stepsPackage` | Package of Steps classes; **empty means no Steps layer**: every step becomes a statement in the test | `tbc.automation.steps` |
| `testPackage` | Package of the test | `tbc.automation.tests` |
| `pageClassSuffix`, `stepsClassSuffix`, `testClassSuffix` | `Loan` → `LoanPage`, `LoanSteps`; the recording "Loan flow" → `LoanFlowTest` | `Page`, `Steps`, `Test` |
| `stripPageIdPrefixes` | Removed from a form's AutomationId for its page name: `frmLoanSearch` → `LoanSearch` | `["frm"]` |
| `indent` | One level of indentation, for templates that need it (`profile.indent`) | 4 spaces |
| `methodCase` | `camel` (`enterAmount`) or `pascal` (`EnterAmount`) | `camel` |
| `elementAccess` | How code reaches an element: `{page}` is the page variable, `{getter}` and `{property}` the element's accessors | `{page}.{getter}()` |
| `locatorFormats` | The find expression per strategy (`AccessibilityId`, `Name`, `ClassName`, `XPath`); `{value}` is the value as a string literal | `winDriver.findElementByAccessibilityId({value})` … |
| `testDataExpression` | What the test passes for a secret (a password field's value is never recorded); `{key}` is the key as a literal | `ConfigReader.getProperty({key})` |
| `testLevelKinds` | Step kinds written as statements in the test instead of Steps methods | the assertions and `SwitchWindow` |
| `gridSteps` | The cells the grid helper in `classes.gridSteps` can click: those with a header, of a type in `cellControlTypes`, in the grid whose AutomationId is `gridAutomationId`. Such a cell sets `use_grid_steps`; any other cell is clicked inside its own grid (ARCHITECTURE D-83). Absent: no cell uses the helper | `{ "gridAutomationId": "Grid", "cellControlTypes": ["Edit"] }` |
| `classes` | Fully qualified framework classes by role, for imports and `simple_name` | see below |
| `stripIdPrefixes` | Removed from AutomationIds for variable names (`txtAmount` → `amount`), for controls captured from now on | `txt`, `btn`, `cmb`… |
| `elementSuffixByControlType` | Variable name suffix per control type, on top of the built-in ones (`Edit` → `Input`) | see the file |
| `merge` | Patterns for "Merge into file…" (below) | |

The Java profile's `classes`: `windowsDriver`, `webElement`, `by`, `keys`, `actions`, `assert`, `test` (TestNG's annotation),
`basePage`, `baseTest`, `log`, `configReader`, `gridSteps`, `utility`. They were checked against LMSAutomationDemo on
2026-11-03 (ARCHITECTURE D-65). If the framework moves a class, changing its line here fixes every import.

## The template model

Scriban shows .NET names in snake_case: `ClassName` is `class_name`. Keys of `profile.classes` are written as in
profile.json: `profile.classes.basePage`. Lists are iterated with `for x in list` and indexed with `list[0]`; their length
is `array.size list`.

Variables and members are **strict**: a misspelt name is an error, not an empty string (see *Errors*).

### `profile`

| Member | Example |
|---|---|
| `name`, `language` | `Java · TestNG · Appium 7`, `java` |
| `package`, `steps_package`, `test_package` | `tbc.automation.pages` |
| `indent` | 4 spaces |
| `classes` | `profile.classes.basePage` = `tbc.automation.pages.BasePage` |

### `page` (page.sbn, steps.sbn, element.sbn; items of `pages`, `test.steps`, `test.pages`)

| Member | Example | Notes |
|---|---|---|
| `name` | `Loan` | Base name, unique among the pages |
| `class_name` | `LoanPage` | Edited in the code pane (Pages tab) |
| `steps_class_name` | `LoanSteps` | |
| `variable` | `loanPage` | The page object's variable in Steps classes and tests |
| `steps_variable` | `loanSteps` | |
| `window_automation_id` | `frmLoan` | Null for a page without a usable AutomationId |
| `elements` | list of `element` | In capture order |
| `actions` | list of `action` | The page's Steps methods, in order of first use |
| `uses` | `uses.keys`, `uses.actions`, `uses.grid_steps`, `uses.assert`, `uses.utility`, `uses.by` | Which framework classes the actions' code mentions, for imports |

A page is named after its form's AutomationId. A page without one is `Window` (`Dialog` for a Win32 dialog) until you
rename it: window titles are never used, because LMS shows the open customer in them.

### `element`

| Member | Example |
|---|---|
| `variable_name` | `amountInput` |
| `getter` | `getAmountInput` |
| `property` | `AmountInput` |
| `control_type` | `Edit` |
| `automation_id` | `txtAmount` (or null) |
| `name` | `Amount` (null for a window: its Name is its title) |
| `strategy` | `AccessibilityId`, `Name`, `ClassName` or `XPath` (the chosen locator, else the best one) |
| `value` | `txtAmount` |
| `literal` | `"txtAmount"`: the value as a string literal of the profile's language |
| `locator` | `winDriver.findElementByAccessibilityId("txtAmount")`: `locatorFormats` applied |
| `scope` | `Window`, or `Desktop` for an element only a desktop session reaches (a drop-down list) |
| `is_unique`, `is_checked` | Whether the locator was checked and finds exactly this element |
| `stability` | `High`, `Medium` or `Low` |

### `action` (items of `page.actions`)

| Member | Example |
|---|---|
| `kind` | `SetText` |
| `method` | `enterAmount` |
| `description` | `Enter Amount`: for log lines |
| `element` | the `element` it acts on (null for keys pressed with no element) |
| `parameters` | list of `{ type, name }`: `String amount`; grid cells `int rowIndex, String columnHeader` (or `int columnIndex`) |
| `body` | The statements, rendered by `action.<Kind>.sbn` with the parameters as values |
| `logs_value` | False for a secret: log the description only |

### `test` (test.sbn)

| Member | Example |
|---|---|
| `class_name`, `method`, `description` | `LoanFlowTest`, `loanFlow`, `Loan flow` (from the recording's file name) |
| `blocks` | The test's parts in recording order (below) |
| `steps` | Pages whose Steps classes the test uses, for imports |
| `pages` | Pages whose page objects the test's statements use, for imports |
| `uses` | As `page.uses`, for the test's statements; `uses.test_data` when a secret is passed |

`pages` (a root of test.sbn) lists every page.

### `block` (items of `test.blocks`)

| `kind` | Means | Members |
|---|---|---|
| `chain` | Consecutive calls on one page's Steps object | `page`, `declare_steps` (first use: declare the variable), `calls` (each `call.action` and `call.arguments`, the recorded values as code) |
| `statement` | Code written in place, with literal values: test-level kinds, and every step without a Steps layer | `page` (may be null), `declare_page` (first use: declare the page object), `body` |
| `comment` | A comment step, rendered by `action.Comment.sbn` | `body`, `comment` |
| `unknown` | A step whose element was removed from the Elements list | `comment` says which |

### The action context (`action.<Kind>.sbn`)

| Variable | In a Steps method | In a statement |
|---|---|---|
| `kind`, `description`, `profile` | as above | as above |
| `element` | the element, or null | the element, or null |
| `target` | `loanPage.getAmountInput()` (`elementAccess`), empty without an element | the same, with the test's page variable |
| `value` | the parameter name: `amount` | the literal: `"5000"`, or `ConfigReader.getProperty("passwordInput")` for a secret |
| `state` | `"true"`, `"false"` or null (SetChecked, AssertEnabled, AssertChecked) | same |
| `keys` | `Keys.chord(Keys.CONTROL, "s")` (PressKeys) | same |
| `menu_items` | find expressions for the menu items after the first (MenuPath) | same |
| `row`, `column` | `rowIndex`, `columnHeader` (grid cells only, else null) | `2`, `"Status"` |
| `cell` | an expression giving the cell's XPath below the grid (F-6) | a literal |
| `use_grid_steps` | true when the grid helper in `classes.gridSteps` can click this cell (`gridSteps` in profile.json); otherwise click `cell` inside `target` | same |
| `comment` | the comment, one line (Comment) | same |

### Functions

| Function | Does |
|---|---|
| `literal text` | A string literal of the profile's language: `literal "say \"hi\""` → `"say \"hi\""`. Non-ASCII text stays as it is |
| `simple_name "a.b.C"` | `C`: a class name from `profile.classes` |
| `indent_lines text prefix` | Every non-empty line of `text`, prefixed |
| `include "element"` | Renders `element.sbn` here. Its final line break is dropped, so it can stand inside a line |

All of Scriban's built-in functions work too (`array.add`, `array.sort`, `array.uniq`, `array.join`, `string.*`…). Watch
out for its reserved words: a variable may not be called `import`, `for`, `end`, `with`, `func`…

## Examples

`element.sbn` of the Java profile, the D-5 getter:

```scriban
public WebElement {{ element.getter }}(){return {{ element.locator }};}
```

`action.SetChecked.sbn`: click only when the state differs; a plain click when the state could not be read:

```scriban
{{~ if state ~}}
if ({{ if state == "true" }}!{{ end }}{{ target }}.isSelected()) {
    {{ target }}.click();
}
{{~ else ~}}
{{ target }}.click();
{{~ end ~}}
```

Sorted imports, as in `steps.sbn`:

```scriban
{{~
imports = [profile.classes.windowsDriver, profile.package + "." + page.class_name, profile.classes.log]
if page.uses.keys
  imports = imports | array.add profile.classes.keys
end
for qualified in imports | array.uniq | array.sort
~}}
import {{ qualified }};
{{~ end ~}}
```

## Whitespace

`{{~ … ~}}` on a line of its own removes that line entirely; `{{- … -}}` also removes the line breaks around it. The
recorder then tidies what a template wrote: line breaks become `\n`, trailing spaces go, runs of blank lines become one,
and the file ends with one line break. So a template can be laid out for reading.

## Errors

A template that fails shows its error **in place of the code**, in the form of spec 10.1. Real examples:

```text
page.sbn line 2: Cannot get member with name nam.
page.sbn line 2: The variable or function `unknown_thing` was not found
page.sbn line 2: Error while parsing if statement: Expecting <expression> instead of `}}` in: if <expression> ... end|else|else if
page.sbn line 2: Exceeding number of recursive depth limit `100` for node: `include "page"`
page.sbn line 2: Range expression exceeds LoopLimit `100000`.
action.SetText.sbn: the profile has no template action.SetText.sbn
```

Unknown variables, unknown members, a missing `action.<Kind>.sbn`, a template that includes itself and a runaway loop
are all errors, never a hang or a crash. An error in an action template stops the whole generation (its file and line
are named), because every class depends on it; an error in `page.sbn`, `steps.sbn` or `test.sbn` affects only its own
classes.

## Merge patterns

"Merge into file…" reads an existing class with the regular expressions under `merge` (spec 10.5). Member and method
patterns are matched against one declaration of the class body, with its leading comments and annotations removed and
its line breaks and runs of spaces turned into single spaces; the others against single lines.

| Key | Matches | Groups |
|---|---|---|
| `member` | A locator member: `public WebElement getAmountInput(){return winDriver.findElementByAccessibilityId("txtAmount");}`, on one line or three | `name`; `value`: the find call with its literal. Two members with the same value find the same element |
| `method` | A method declaration | `name` |
| `import` | An import line | |
| `classDeclaration` | The class's declaration line | `name` |
| `package` | The line new imports follow when the file has none (Java's `package`); empty: the top of the file | |

What the merge adds and what it leaves out is described in ARCHITECTURE D-66.
