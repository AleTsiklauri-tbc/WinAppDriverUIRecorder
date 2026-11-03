using System.Text;
using System.Text.RegularExpressions;
using Recorder.Core.Locators;
using Recorder.Core.Model;

namespace Recorder.Core.Naming;

/// <summary>
/// Suggests variable names for captured elements (spec 10.2): AutomationId (minus type prefixes) → label text → Name →
/// control type and a number; then transliterate, drop invalid characters, camelCase, add the control-type suffix,
/// avoid keywords and de-duplicate within the page.
/// </summary>
public static partial class VariableNamer
{
    public static string Suggest(ElementSnapshot snapshot, IEnumerable<string> takenInPage, NamingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(takenInPage);
        options ??= NamingOptions.Default;
        var taken = new HashSet<string>(takenInPage, StringComparer.OrdinalIgnoreCase);
        var suffix = options.SuffixByControlType.GetValueOrDefault(snapshot.ControlType, string.Empty);
        foreach (var source in Sources(snapshot, options))
        {
            var words = Words(Transliteration.ToLatin(source, options.Transliterators));
            if (words.Count == 0)
            {
                continue;
            }

            var name = CamelCase(words);
            if (suffix.Length > 0 && !EndsWithSuffix(name, snapshot.ControlType, suffix, options))
            {
                name += suffix;
            }

            if (char.IsAsciiDigit(name[0]))
            {
                name = LowerFirst(suffix.Length > 0 ? suffix : "element") + UpperFirst(name);
            }

            if (options.Keywords.Contains(name))
            {
                name += "Element";
            }

            return Unique(name, taken);
        }

        // Nothing to go by: the control type and a number, e.g. input1.
        var fallback = LowerFirst(suffix.Length > 0 ? suffix : "element");
        var n = 1;
        while (taken.Contains(fallback + n))
        {
            n++;
        }

        return fallback + n;
    }

    /// <summary>The Java getter for a variable: dzebnaButton → getDzebnaButton.</summary>
    public static string Getter(string variableName)
    {
        ArgumentException.ThrowIfNullOrEmpty(variableName);
        return "get" + UpperFirst(variableName);
    }

    /// <summary>
    /// A class name the user typed: kept as typed when it is an identifier (first letter made upper case), otherwise its
    /// words in PascalCase ("loan details page" → LoanDetailsPage); null when nothing usable is left.
    /// </summary>
    public static string? ClassName(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed))
        {
            return null;
        }

        var latin = Transliteration.ToLatin(typed.Trim(), Transliteration.BuiltIn);
        var clean = Clean(latin);
        if (clean is null)
        {
            return null;
        }

        var name = clean == latin ? clean : string.Concat(Words(latin).Select(UpperFirst));
        return name.Length == 0 || char.IsAsciiDigit(name[0]) ? null : UpperFirst(name);
    }

    /// <summary>A name the user typed, reduced to a valid identifier; null when nothing usable is left.</summary>
    public static string? Clean(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed))
        {
            return null;
        }

        var builder = new StringBuilder(typed.Length);
        foreach (var c in typed.Trim())
        {
            if (char.IsAsciiLetterOrDigit(c) || c == '_')
            {
                builder.Append(c);
            }
        }

        var name = builder.ToString();
        return name.Length == 0 || char.IsAsciiDigit(name[0]) ? null : name;
    }

    private static IEnumerable<string> Sources(ElementSnapshot snapshot, NamingOptions options)
    {
        var handles = snapshot.Ancestors.Select(a => a.NativeWindowHandle).Prepend(snapshot.NativeWindowHandle).ToList();
        var nonClient = snapshot.Ancestors.Any(a => a.ControlType == "TitleBar");
        var id = IdRules.Evaluate(snapshot.AutomationId, new IdFacts(handles, snapshot.ProcessId, snapshot.FrameworkId, nonClient));
        if (id.IsAccepted && !id.Notes.Contains(LocatorNote.NumericId))
        {
            yield return StripPrefix(id.StablePrefix ?? snapshot.AutomationId!, options.StripIdPrefixes);
        }

        if (!string.IsNullOrWhiteSpace(snapshot.LabelText) && !NameRules.LooksDynamic(snapshot.LabelText))
        {
            yield return snapshot.LabelText;
        }

        // Never a window's title (NameRules, D-70).
        var name = NameRules.Evaluate(snapshot.Name, snapshot.ControlType);
        if (name.IsAccepted && name.Stability != Stability.Low)
        {
            yield return snapshot.Name!;
        }
    }

    // txtAmount → Amount, btn_save → save; "textBox12" keeps its "tex".
    private static string StripPrefix(string id, IReadOnlyList<string> prefixes)
    {
        foreach (var prefix in prefixes)
        {
            if (id.Length > prefix.Length && id.StartsWith(prefix, StringComparison.Ordinal))
            {
                var next = id[prefix.Length];
                if (char.IsAsciiLetterUpper(next) || next is '_' or '-' or '.')
                {
                    return id[prefix.Length..].TrimStart('_', '-', '.');
                }
            }
        }

        return id;
    }

    private static List<string> Words(string latin) => [.. WordPattern().Matches(latin).Select(match => match.Value)];

    private static string CamelCase(List<string> words)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < words.Count; i++)
        {
            var word = words[i];
            var allUpper = word.Length > 1 && word.All(char.IsAsciiLetterUpper);
            if (i == 0)
            {
                builder.Append(allUpper ? word.ToLowerInvariant() : LowerFirst(word));
            }
            else
            {
                builder.Append(allUpper ? word[0] + word[1..].ToLowerInvariant() : UpperFirst(word));
            }
        }

        return builder.ToString();
    }

    private static bool EndsWithSuffix(string name, string controlType, string suffix, NamingOptions options)
    {
        var withoutNumber = name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        var endings = options.EquivalentSuffixes.GetValueOrDefault(controlType, []).Append(suffix);
        return endings.Any(ending => withoutNumber.EndsWith(ending, StringComparison.OrdinalIgnoreCase));
    }

    private static string Unique(string name, HashSet<string> taken)
    {
        if (!taken.Contains(name))
        {
            return name;
        }

        var n = 2;
        while (taken.Contains(name + n))
        {
            n++;
        }

        return name + n;
    }

    private static string LowerFirst(string word) => word.Length == 0 ? word : char.ToLowerInvariant(word[0]) + word[1..];

    private static string UpperFirst(string word) => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];

    // Camel-case humps, acronyms and digit runs: clientNoEditBox → client|No|Edit|Box, HTMLParser → HTML|Parser.
    [GeneratedRegex("[A-Z]?[a-z]+|[A-Z]+(?![a-z])|[0-9]+")]
    private static partial Regex WordPattern();
}
