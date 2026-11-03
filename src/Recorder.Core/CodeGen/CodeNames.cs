using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Recorder.Core.Model;
using Recorder.Core.Naming;

namespace Recorder.Core.CodeGen;

/// <summary>Class, method and parameter names for generated code (spec 10.2), and string literals per language (spec 10.3).</summary>
public static partial class CodeNames
{
    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue", "decimal",
        "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null",
        "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte",
        "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
        "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
    };

    /// <summary>Words of any text, transliterated (Georgian, Russian) and split at case changes and separators.</summary>
    public static IReadOnlyList<string> Words(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : [.. WordPattern().Matches(Transliteration.ToLatin(text, Transliteration.BuiltIn)).Select(m => m.Value)];

    public static string Pascal(IEnumerable<string> words) =>
        string.Concat(words.Select(w => w.Length > 1 && w.All(char.IsAsciiLetterUpper) ? w[0] + w[1..].ToLowerInvariant() : UpperFirst(w)));

    public static string Pascal(string? text) => Pascal(Words(text));

    public static string Camel(string? text) => LowerFirst(Pascal(text));

    public static string UpperFirst(string word) => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];

    public static string LowerFirst(string word) => word.Length == 0 ? word : char.ToLowerInvariant(word[0]) + word[1..];

    /// <summary>
    /// A page's base name (spec 10.2): its form's AutomationId without "frm", as PascalCase. Never its window title, which in
    /// LMS shows customers (AGENTS.md, D-61): a page without a usable AutomationId is "Dialog" for a Win32 dialog, else "Window".
    /// </summary>
    public static string PageName(PageKey page, IReadOnlyList<string> stripPrefixes)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(stripPrefixes);
        if (!page.Id.StartsWith("id:", StringComparison.Ordinal))
        {
            return page.Id == "class:#32770" ? "Dialog" : "Window";
        }

        var source = page.DisplayName;
        foreach (var prefix in stripPrefixes)
        {
            if (source.Length > prefix.Length && source.StartsWith(prefix, StringComparison.Ordinal) && char.IsAsciiLetterUpper(source[prefix.Length]))
            {
                source = source[prefix.Length..];
                break;
            }
        }

        var name = Pascal(source);
        return name.Length == 0 || char.IsAsciiDigit(name[0]) ? "Window" + name : name;
    }

    /// <summary>A variable's name without its control-type suffix: amountInput → amount, insuredCheckbox → insured.</summary>
    public static string BaseOf(string variableName, string controlType, NamingOptions naming)
    {
        ArgumentNullException.ThrowIfNull(variableName);
        ArgumentNullException.ThrowIfNull(naming);
        var endings = naming.EquivalentSuffixes.GetValueOrDefault(controlType, []).Append(naming.SuffixByControlType.GetValueOrDefault(controlType, string.Empty));
        foreach (var ending in endings.Where(e => e.Length > 0).OrderByDescending(e => e.Length))
        {
            if (variableName.Length > ending.Length && variableName.EndsWith(ending, StringComparison.OrdinalIgnoreCase))
            {
                return variableName[..^ending.Length];
            }
        }

        return variableName;
    }

    /// <summary>A name that is not a keyword of the language.</summary>
    public static string Safe(string name, string language) =>
        IsKeyword(name, language) ? name + "Value" : name;

    public static bool IsKeyword(string name, string language) =>
        language == "csharp" ? CSharpKeywords.Contains(name) : NamingOptions.Default.Keywords.Contains(name);

    /// <summary>A string literal: Java and C# escape the same characters; non-ASCII text stays as it is (spec 10.3).</summary>
    public static string Literal(string? value, string language) =>
        language == "csharp" ? CSharpLiteral(value ?? string.Empty) : JavaString.Literal(value ?? string.Empty);

    /// <summary>The integer as code.</summary>
    public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>The name made unique among <paramref name="taken"/> with 2, 3…, then taken.</summary>
    public static string Unique(string name, ISet<string> taken)
    {
        ArgumentNullException.ThrowIfNull(taken);
        var unique = name;
        for (var n = 2; taken.Contains(unique); n++)
        {
            unique = name + Number(n);
        }

        taken.Add(unique);
        return unique;
    }

    private static string CSharpLiteral(string value)
    {
        var builder = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            builder.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\0' => "\\0",
                _ when char.IsControl(c) => "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture),
                _ => c.ToString(),
            });
        }

        return builder.Append('"').ToString();
    }

    // Camel-case humps, acronyms and digit runs: clientNoEditBox → client|No|Edit|Box.
    [GeneratedRegex("[A-Z]?[a-z]+|[A-Z]+(?![a-z])|[0-9]+")]
    private static partial Regex WordPattern();
}

/// <summary>Key chords as code (spec 9.4 PressKeys): "Ctrl+S" → Keys.chord(Keys.CONTROL, "s") in Java.</summary>
public static class KeyCode
{
    private static readonly Dictionary<string, (string Java, string CSharp)> Named = new(StringComparer.Ordinal)
    {
        ["Enter"] = ("ENTER", "Enter"), ["Tab"] = ("TAB", "Tab"), ["Esc"] = ("ESCAPE", "Escape"), ["Delete"] = ("DELETE", "Delete"),
        ["Left"] = ("LEFT", "Left"), ["Up"] = ("UP", "Up"), ["Right"] = ("RIGHT", "Right"), ["Down"] = ("DOWN", "Down"),
        ["Ctrl"] = ("CONTROL", "Control"), ["Shift"] = ("SHIFT", "Shift"), ["Alt"] = ("ALT", "Alt"),
    };

    public static string Expression(string chord, string language)
    {
        ArgumentException.ThrowIfNullOrEmpty(chord);
        var parts = chord.Split('+').Select(part => Part(part, language)).ToList();
        if (parts.Count == 1)
        {
            return parts[0];
        }

        return language == "csharp" ? string.Join(" + ", parts) + " + Keys.Null" : "Keys.chord(" + string.Join(", ", parts) + ")";
    }

    /// <summary>The chord as words for method names: "Ctrl+S" → "CtrlS".</summary>
    public static string MethodPart(string chord) => string.Concat(chord.Split('+').Select(CodeNames.UpperFirst));

    private static string Part(string part, string language)
    {
        if (Named.TryGetValue(part, out var named))
        {
            return language == "csharp" ? "Keys." + named.CSharp : "Keys." + named.Java;
        }

        if (part.Length > 1 && part[0] == 'F' && int.TryParse(part.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            return "Keys.F" + CodeNames.Number(number);
        }

        return CodeNames.Literal(part.ToLowerInvariant(), language);
    }
}
