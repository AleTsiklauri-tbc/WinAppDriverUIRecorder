using System.Text;
using System.Text.RegularExpressions;

namespace Recorder.Core.CodeGen;

/// <summary>A declaration in a class body: a locator member, a method, a constructor, or anything else (fields…).</summary>
/// <param name="Value">For a member: what it finds (the pattern's "value" group), with runs of spaces made single.</param>
/// <param name="First">Index of its first line, leading comments and annotations included.</param>
/// <param name="Start">Index of the line the declaration itself starts on.</param>
/// <param name="Last">Index of its last line.</param>
internal sealed record ClassUnit(UnitKind Kind, string? Name, string? Value, int First, int Start, int Last);

internal enum UnitKind
{
    Member,
    Method,
    Constructor,
    Other,
}

/// <summary>
/// Reads one Java or C# class well enough to merge into it (spec 10.5): where its imports, members and methods are, and
/// where its body ends. Comments and string literals are masked before braces are counted. Anything unexpected makes it
/// give up with a reason instead of guessing.
/// </summary>
internal sealed class ClassReader
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private ClassReader(IReadOnlyList<string> lines, IReadOnlyList<string> code, IReadOnlyList<string> masked)
    {
        Lines = lines;
        Code = code;
        Masked = masked;
    }

    /// <summary>The file's lines as they are.</summary>
    public IReadOnlyList<string> Lines { get; }

    /// <summary>The lines with comments blanked out (strings kept).</summary>
    public IReadOnlyList<string> Code { get; }

    /// <summary>The lines with comments and the contents of string and character literals blanked out.</summary>
    public IReadOnlyList<string> Masked { get; }

    public string ClassName { get; private set; } = string.Empty;

    public int ClassLine { get; private set; }

    public int OpenLine { get; private set; }

    public int CloseLine { get; private set; }

    public IReadOnlyList<int> ImportLines { get; private set; } = [];

    public int? PackageLine { get; private set; }

    public IReadOnlyList<ClassUnit> Units { get; private set; } = [];

    /// <summary>Reads the class, or says why it cannot (<paramref name="problem"/>).</summary>
    /// <exception cref="ArgumentException">A merge pattern of the profile is not a valid regular expression.</exception>
    public static ClassReader? Read(string text, MergeSettings settings, out MergeProblem? problem)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(settings);
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        if (!Mask(string.Join('\n', lines), out var code, out var masked, out problem))
        {
            return null;
        }

        var reader = new ClassReader(lines, code.Split('\n'), masked.Split('\n'));
        try
        {
            problem = reader.ReadStructure(settings);
        }
        catch (RegexMatchTimeoutException)
        {
            problem = MergeProblem.PatternTooSlow;
        }

        return problem is null ? reader : null;
    }

    /// <summary>The leading spaces or tabs of a line.</summary>
    public static string IndentOf(string line) => line[..(line.Length - line.TrimStart(' ', '\t').Length)];

    private static Regex Pattern(string pattern) => new(pattern, RegexOptions.CultureInvariant, RegexTimeout);

    private MergeProblem? ReadStructure(MergeSettings settings)
    {
        if (settings.ClassDeclaration.Length == 0 || settings.Member.Length == 0 || settings.Method.Length == 0)
        {
            return MergeProblem.NoPatterns;
        }

        var depths = new int[Masked.Count + 1];
        for (var i = 0; i < Masked.Count; i++)
        {
            depths[i + 1] = depths[i] + Masked[i].Count(c => c == '{') - Masked[i].Count(c => c == '}');
        }

        if (depths[^1] != 0 || depths.Any(d => d < 0))
        {
            return MergeProblem.UnbalancedBraces;
        }

        var declaration = Pattern(settings.ClassDeclaration);
        var classes = Enumerable.Range(0, Masked.Count).Select(i => (Line: i, Match: declaration.Match(Code[i]))).Where(c => c.Match.Success).ToList();
        if (classes.Count == 0)
        {
            return MergeProblem.NoClass;
        }

        var outermost = classes.Min(c => depths[c.Line]);
        var candidates = classes.Where(c => depths[c.Line] == outermost).ToList();
        if (candidates.Count > 1)
        {
            return MergeProblem.SeveralClasses;
        }

        (ClassLine, ClassName) = (candidates[0].Line, candidates[0].Match.Groups["name"].Value);
        OpenLine = Enumerable.Range(ClassLine, Masked.Count - ClassLine).FirstOrDefault(i => Masked[i].Contains('{', StringComparison.Ordinal), -1);
        if (OpenLine < 0 || !Masked[OpenLine].TrimEnd().EndsWith('{'))
        {
            return MergeProblem.BodyNotOnItsOwnLine;
        }

        var bodyDepth = depths[OpenLine + 1];
        CloseLine = Enumerable.Range(OpenLine + 1, Masked.Count - OpenLine - 1).FirstOrDefault(i => depths[i + 1] < bodyDepth, -1);
        if (CloseLine < 0 || Masked[CloseLine].Trim() != "}")
        {
            return MergeProblem.ClosingBraceNotOnItsOwnLine;
        }

        var import = settings.Import.Length > 0 ? Pattern(settings.Import) : null;
        ImportLines = import is null ? [] : [.. Enumerable.Range(0, ClassLine).Where(i => import.IsMatch(Code[i]))];
        var package = settings.Package.Length > 0 ? Pattern(settings.Package) : null;
        PackageLine = package is null ? null : Enumerable.Range(0, ClassLine).Where(i => package.IsMatch(Code[i])).Select(i => (int?)i).FirstOrDefault();
        Units = ReadUnits(depths, bodyDepth, Pattern(settings.Member), Pattern(settings.Method));
        return null;
    }

    // Declarations at the top level of the class body: each ends on a line back at body depth that ends with ';' or '}'.
    private List<ClassUnit> ReadUnits(int[] depths, int bodyDepth, Regex member, Regex method)
    {
        var units = new List<ClassUnit>();
        var first = -1;
        for (var i = OpenLine + 1; i < CloseLine; i++)
        {
            if (first < 0 && Lines[i].Trim().Length == 0)
            {
                continue;
            }

            first = first < 0 ? i : first;
            var end = Masked[i].TrimEnd();
            if (depths[i + 1] == bodyDepth && (end.EndsWith(';') || end.EndsWith('}')))
            {
                units.Add(Classify(first, i, member, method));
                first = -1;
            }
        }

        return units;
    }

    private ClassUnit Classify(int first, int last, Regex member, Regex method)
    {
        // Leading comment lines (blank once masked), annotations and attributes are not part of the declaration.
        var start = first;
        while (start < last && (Masked[start].Trim().Length == 0 || Masked[start].TrimStart().StartsWith('@') || Masked[start].TrimStart().StartsWith('[')))
        {
            start++;
        }

        var declaration = Collapse(string.Join(' ', Code.Skip(start).Take(last - start + 1)));
        if (member.Match(declaration) is { Success: true } m)
        {
            return new ClassUnit(UnitKind.Member, m.Groups["name"].Value, Collapse(m.Groups["value"].Value), first, start, last);
        }

        if (method.Match(declaration) is { Success: true } f)
        {
            var name = f.Groups["name"].Value;
            return new ClassUnit(name == ClassName ? UnitKind.Constructor : UnitKind.Method, name, null, first, start, last);
        }

        return new ClassUnit(UnitKind.Other, null, null, first, start, last);
    }

    private static string Collapse(string text) => Regex.Replace(text.Trim(), @"\s+", " ", RegexOptions.None, RegexTimeout);

    // Blanks out comments (in code and masked) and literal contents (in masked only), keeping every line break and column.
    private static bool Mask(string text, out string code, out string masked, out MergeProblem? problem)
    {
        var c = new StringBuilder(text);
        var m = new StringBuilder(text);
        problem = null;
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '/' && i + 1 < text.Length && text[i + 1] is '/' or '*')
            {
                var block = text[i + 1] == '*';
                var end = block ? text.IndexOf("*/", i + 2, StringComparison.Ordinal) : text.IndexOf('\n', i);
                end = end < 0 ? text.Length : block ? end + 2 : end;
                Blank(c, i, end);
                Blank(m, i, end);
                i = end;
            }
            else if (text[i] == '"' && i + 2 < text.Length && text[i + 1] == '"' && text[i + 2] == '"')
            {
                problem = MergeProblem.TextBlocks;
                break;
            }
            else if (text[i] is '"' or '\'')
            {
                var verbatim = text[i] == '"' && i > 0 && (text[i - 1] == '@' || (text[i - 1] == '$' && i > 1 && text[i - 2] == '@'));
                var end = LiteralEnd(text, i, verbatim);
                Blank(m, i + 1, end - 1);
                i = end;
            }
            else
            {
                i++;
            }
        }

        (code, masked) = (c.ToString(), m.ToString());
        return problem is null;
    }

    // The index after the closing quote. Escapes: \x, or "" inside a C# verbatim string. A literal never spans lines here.
    private static int LiteralEnd(string text, int open, bool verbatim)
    {
        var quote = text[open];
        for (var i = open + 1; i < text.Length; i++)
        {
            if (!verbatim && text[i] == '\\')
            {
                i++;
            }
            else if (verbatim && text[i] == '"' && i + 1 < text.Length && text[i + 1] == '"')
            {
                i++;
            }
            else if (text[i] == quote || (!verbatim && text[i] == '\n'))
            {
                return i + 1;
            }
        }

        return text.Length;
    }

    private static void Blank(StringBuilder text, int from, int to)
    {
        for (var i = from; i < to; i++)
        {
            if (text[i] != '\n')
            {
                text[i] = ' ';
            }
        }
    }
}
