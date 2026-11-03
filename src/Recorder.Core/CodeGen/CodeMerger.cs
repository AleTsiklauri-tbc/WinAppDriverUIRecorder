using System.Text.RegularExpressions;

namespace Recorder.Core.CodeGen;

/// <summary>A generated member that was left out because the file already finds the same element under another name.</summary>
public sealed record SameLocator(string Generated, string Existing);

/// <summary>Why a file could not be read with confidence (spec 10.5). The app words each one (D-30 style: codes in Core).</summary>
public enum MergeProblem
{
    /// <summary>The profile has no merge patterns.</summary>
    NoPatterns,

    /// <summary>A merge pattern in profile.json is not a valid regular expression (the detail says why).</summary>
    InvalidPattern,

    /// <summary>A merge pattern took too long on the file.</summary>
    PatternTooSlow,

    /// <summary>The generated code itself could not be read with the profile's patterns.</summary>
    GeneratedUnreadable,

    UnbalancedBraces,

    NoClass,

    SeveralClasses,

    /// <summary>The class body's opening brace is not the last thing on its line.</summary>
    BodyNotOnItsOwnLine,

    /// <summary>The class's closing brace shares its line with other code.</summary>
    ClosingBraceNotOnItsOwnLine,

    /// <summary>Java text blocks or C# raw strings (""").</summary>
    TextBlocks,
}

/// <summary>What merging generated code into an existing file does (spec 10.5).</summary>
/// <param name="Problem">Why the file could not be read with confidence. Nothing is merged then, and <paramref name="NewMembers"/> holds everything generated.</param>
/// <param name="ProblemDetail">For <see cref="MergeProblem.InvalidPattern"/>: what is wrong with the pattern.</param>
/// <param name="Text">The file with the additions; the file as it was when nothing is new or on a problem.</param>
/// <param name="NameTaken">Generated members left out because the file has something of that name that finds another element.</param>
/// <param name="NewMembers">The additions as text, for "Copy new members to clipboard".</param>
public sealed record MergeResult(
    MergeProblem? Problem, string? ProblemDetail, string Text, IReadOnlyList<string> AddedImports, IReadOnlyList<string> AddedMembers,
    IReadOnlyList<string> AddedMethods, IReadOnlyList<SameLocator> SameLocators, IReadOnlyList<string> NameTaken, string NewMembers)
{
    public bool IsUnderstood => Problem is null;

    public bool HasChanges => AddedImports.Count + AddedMembers.Count + AddedMethods.Count > 0;

    /// <summary>Something to tell the user even when nothing is added: a locator kept under the file's name, or a name taken.</summary>
    public bool HasNotes => SameLocators.Count + NameTaken.Count > 0;
}

/// <summary>
/// Merges a generated class into an existing file, conservatively (spec 10.5): missing imports go after the last import,
/// locator members the file lacks (by what they find) after its last member, and methods it lacks (by name) before the
/// class's closing brace. Nothing else in the file changes, and its line breaks are kept.
/// </summary>
public static class CodeMerger
{
    public static MergeResult Merge(string existingText, string generatedText, MergeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(existingText);
        ArgumentNullException.ThrowIfNull(generatedText);
        ArgumentNullException.ThrowIfNull(settings);
        ClassReader? generated;
        ClassReader? existing;
        MergeProblem? problem;
        try
        {
            generated = ClassReader.Read(generatedText, settings, out problem);
            if (generated is null)
            {
                return Failed(problem == MergeProblem.NoPatterns ? problem.Value : MergeProblem.GeneratedUnreadable, null, existingText, generatedText);
            }

            existing = ClassReader.Read(existingText, settings, out problem);
        }
        catch (ArgumentException exception)
        {
            return Failed(MergeProblem.InvalidPattern, exception.Message, existingText, generatedText);
        }

        if (existing is null)
        {
            var everything = Text(generated, [.. generated.ImportLines.Select(i => generated.Lines[i].Trim())], Units(generated, UnitKind.Member), Units(generated, UnitKind.Method));
            return Failed(problem!.Value, null, existingText, everything);
        }

        return Merge(existing, generated, existingText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n");
    }

    private static MergeResult Merge(ClassReader existing, ClassReader generated, string lineBreak)
    {
        var existingMembers = Units(existing, UnitKind.Member);
        var byValue = existingMembers.GroupBy(u => u.Value!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Name!, StringComparer.Ordinal);
        var names = existing.Units.Where(u => u.Name is not null).Select(u => u.Name!).ToHashSet(StringComparer.Ordinal);
        var members = new List<ClassUnit>();
        var same = new List<SameLocator>();
        var taken = new List<string>();
        foreach (var unit in Units(generated, UnitKind.Member))
        {
            if (byValue.TryGetValue(unit.Value!, out var existingName))
            {
                if (existingName != unit.Name)
                {
                    same.Add(new SameLocator(unit.Name!, existingName));
                }
            }
            else if (names.Contains(unit.Name!))
            {
                taken.Add(unit.Name!);
            }
            else
            {
                members.Add(unit);
            }
        }

        var methods = Units(generated, UnitKind.Method).Where(u => !names.Contains(u.Name!)).ToList();
        var known = existing.ImportLines.Select(i => Squeeze(existing.Code[i])).ToHashSet(StringComparer.Ordinal);
        var imports = generated.ImportLines.Select(i => generated.Lines[i].Trim()).Where(line => !known.Contains(Squeeze(line))).Distinct().ToList();

        // Steps methods return their own class: merged into another class, they return that one.
        var rename = existing.ClassName != generated.ClassName ? new Regex(@"\b" + Regex.Escape(generated.ClassName) + @"\b", RegexOptions.None, TimeSpan.FromSeconds(1)) : null;
        string Renamed(string line) => rename is null ? line : rename.Replace(line, existing.ClassName);

        // Bottom up, so that earlier line numbers stay valid.
        var lines = existing.Lines.ToList();
        var indent = MemberIndent(existing);
        if (methods.Count > 0)
        {
            var block = new List<string>();
            var blankBefore = existing.CloseLine - 1 > existing.OpenLine && existing.Lines[existing.CloseLine - 1].Trim().Length > 0;
            foreach (var method in methods)
            {
                if (blankBefore || block.Count > 0)
                {
                    block.Add(string.Empty);
                }

                block.AddRange(Reindent(generated, method.First, method.Last, indent).Select(Renamed));
            }

            lines.InsertRange(existing.CloseLine, block);
        }

        if (members.Count > 0)
        {
            var (after, blank) = MemberAnchor(existing);
            var block = members.SelectMany(m => Reindent(generated, m.Start, m.Last, indent)).ToList();
            lines.InsertRange(after + 1, blank ? block.Prepend(string.Empty) : block);
        }

        if (imports.Count > 0)
        {
            if (existing.ImportLines.Count > 0)
            {
                lines.InsertRange(existing.ImportLines[^1] + 1, imports);
            }
            else if (existing.PackageLine is { } package)
            {
                lines.InsertRange(package + 1, imports.Prepend(string.Empty));
            }
            else
            {
                lines.InsertRange(0, imports.Append(string.Empty));
            }
        }

        var newMembers = Text(generated, imports, members, methods);
        return new MergeResult(
            null, null, string.Join(lineBreak, lines), imports, [.. members.Select(m => m.Name!)], [.. methods.Select(m => m.Name!)], same, taken,
            rename is null ? newMembers : string.Join('\n', newMembers.Split('\n').Select(Renamed)));
    }

    private static MergeResult Failed(MergeProblem problem, string? detail, string existingText, string newMembers) =>
        new(problem, detail, existingText, [], [], [], [], [], newMembers);

    private static List<ClassUnit> Units(ClassReader reader, UnitKind kind) => [.. reader.Units.Where(u => u.Kind == kind)];

    private static string Squeeze(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));

    // After the last member; without members, after the last field or constructor before the first method (a blank line first).
    private static (int After, bool Blank) MemberAnchor(ClassReader existing)
    {
        if (existing.Units.LastOrDefault(u => u.Kind == UnitKind.Member) is { } member)
        {
            return (member.Last, false);
        }

        var firstMethod = existing.Units.FirstOrDefault(u => u.Kind == UnitKind.Method)?.First ?? existing.CloseLine;
        return existing.Units.LastOrDefault(u => u.Last < firstMethod) is { } before ? (before.Last, true) : (existing.OpenLine, false);
    }

    private static string MemberIndent(ClassReader existing)
    {
        var unit = existing.Units.LastOrDefault(u => u.Kind == UnitKind.Member) ?? (existing.Units.Count > 0 ? existing.Units[0] : null);
        if (unit is not null)
        {
            return ClassReader.IndentOf(existing.Lines[unit.Start]);
        }

        var line = ClassReader.IndentOf(existing.Lines[existing.ClassLine]);
        return line + (line.Contains('\t', StringComparison.Ordinal) ? "\t" : "    ");
    }

    // The lines with the generated class-level indentation replaced by the file's (tabs if the file uses them).
    private static IEnumerable<string> Reindent(ClassReader source, int first, int last, string indent)
    {
        var lines = source.Lines.Skip(first).Take(last - first + 1).ToList();
        var baseLength = lines.Where(l => l.Trim().Length > 0).Select(l => ClassReader.IndentOf(l).Length).DefaultIfEmpty(0).Min();
        var tabs = indent.Contains('\t', StringComparison.Ordinal);
        foreach (var line in lines)
        {
            if (line.Trim().Length == 0)
            {
                yield return string.Empty;
                continue;
            }

            var own = ClassReader.IndentOf(line)[Math.Min(baseLength, ClassReader.IndentOf(line).Length)..];
            yield return indent + (tabs ? own.Replace("    ", "\t", StringComparison.Ordinal) : own) + line.TrimStart(' ', '\t');
        }
    }

    // Imports, then members, then methods (a blank line between groups and between methods), indented as generated.
    private static string Text(ClassReader generated, List<string> imports, List<ClassUnit> members, List<ClassUnit> methods)
    {
        var groups = new List<string>();
        if (imports.Count > 0)
        {
            groups.Add(string.Join('\n', imports));
        }

        if (members.Count > 0)
        {
            groups.Add(string.Join('\n', members.Select(m => string.Join('\n', generated.Lines.Skip(m.Start).Take(m.Last - m.Start + 1)))));
        }

        groups.AddRange(methods.Select(m => string.Join('\n', generated.Lines.Skip(m.First).Take(m.Last - m.First + 1))));
        return groups.Count == 0 ? string.Empty : string.Join("\n\n", groups) + "\n";
    }
}
