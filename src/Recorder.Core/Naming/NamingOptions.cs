namespace Recorder.Core.Naming;

/// <summary>
/// Naming conventions for variables (spec 10.2). The defaults follow docs/target-style and decision D-6; the code
/// profiles of Phase 4 will supply them instead.
/// </summary>
public sealed record NamingOptions
{
    public static NamingOptions Default { get; } = new();

    /// <summary>Type prefixes removed from AutomationIds: txtAmount → amount.</summary>
    public IReadOnlyList<string> StripIdPrefixes { get; init; } = ["txt", "btn", "cmb", "cbo", "chk", "rb", "lbl", "dgv", "grd", "tab", "pnl", "frm"];

    /// <summary>Suffix added per control type (spec 10.1 example, plus types it does not list).</summary>
    public IReadOnlyDictionary<string, string> SuffixByControlType { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Edit"] = "Input", ["Button"] = "Button", ["SplitButton"] = "Button", ["ComboBox"] = "Dropdown", ["CheckBox"] = "Checkbox",
        ["RadioButton"] = "Radio", ["DataGrid"] = "Grid", ["Table"] = "Grid", ["TabItem"] = "Tab", ["Tab"] = "Tabs",
        ["Text"] = "Label", ["MenuItem"] = "MenuItem", ["List"] = "List", ["ListItem"] = "Item", ["Tree"] = "Tree",
        ["TreeItem"] = "Item", ["Hyperlink"] = "Link", ["Window"] = "Window", ["Pane"] = "Panel", ["Group"] = "Group",
        ["Header"] = "Header", ["HeaderItem"] = "Header", ["DataItem"] = "Row", ["Custom"] = "Element", ["Spinner"] = "Spinner",
        ["Image"] = "Image", ["Document"] = "Document", ["ToolBar"] = "Toolbar", ["Calendar"] = "Calendar", ["Slider"] = "Slider",
    };

    /// <summary>
    /// Endings that already say what the control is, so no suffix is added (D-6): an id such as clientNoEditBox keeps
    /// its own "EditBox" instead of becoming clientNoEditBoxInput.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> EquivalentSuffixes { get; init; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        ["Edit"] = ["Input", "EditBox", "TextBox", "Field", "Edit", "Box"],
        ["Button"] = ["Button", "Btn"],
        ["ComboBox"] = ["Dropdown", "ComboBox", "Combo", "Box"],
        ["CheckBox"] = ["Checkbox", "Check"],
        ["RadioButton"] = ["Radio", "RadioButton", "Option"],
        ["Table"] = ["Grid", "Table", "GridView"],
        ["DataGrid"] = ["Grid", "Table", "GridView"],
        ["Text"] = ["Label", "Text", "Caption"],
        ["MenuItem"] = ["MenuItem", "Menu", "Item"],
        ["Window"] = ["Window", "Dialog", "Form"],
        ["Pane"] = ["Panel", "Pane", "Control", "Container"],
    };

    /// <summary>Java and C# keywords: never a variable name on their own.</summary>
    public IReadOnlySet<string> Keywords { get; init; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "abstract", "assert", "boolean", "break", "byte", "case", "catch", "char", "class", "const", "continue", "default", "do",
        "double", "else", "enum", "extends", "final", "finally", "float", "for", "goto", "if", "implements", "import",
        "instanceof", "int", "interface", "long", "native", "new", "package", "private", "protected", "public", "return",
        "short", "static", "strictfp", "super", "switch", "synchronized", "this", "throw", "throws", "transient", "try", "void",
        "volatile", "while", "true", "false", "null", "var", "record", "yield", "as", "base", "bool", "checked", "decimal",
        "delegate", "event", "explicit", "extern", "fixed", "foreach", "implicit", "in", "internal", "is", "lock", "namespace",
        "object", "operator", "out", "override", "params", "readonly", "ref", "sbyte", "sealed", "sizeof", "stackalloc",
        "string", "struct", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
    };

    public IReadOnlyList<ITransliterator> Transliterators { get; init; } = Transliteration.BuiltIn;
}
