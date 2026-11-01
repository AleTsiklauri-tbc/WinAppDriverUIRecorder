using System.Text;

namespace Recorder.Core.Naming;

/// <summary>Turns non-Latin text into Latin letters for code identifiers (spec 10.2). Pluggable per script.</summary>
public interface ITransliterator
{
    /// <summary>True when the character belongs to this transliterator's script.</summary>
    bool Covers(char c);

    /// <summary>The Latin replacement of one character this transliterator handles.</summary>
    string Transliterate(char c);
}

/// <summary>Georgian, national system without apostrophes (spec 10.2): ძებნა → dzebna.</summary>
public sealed class GeorgianTransliterator : ITransliterator
{
    private static readonly Dictionary<char, string> Table = new()
    {
        ['ა'] = "a", ['ბ'] = "b", ['გ'] = "g", ['დ'] = "d", ['ე'] = "e", ['ვ'] = "v", ['ზ'] = "z", ['თ'] = "t", ['ი'] = "i",
        ['კ'] = "k", ['ლ'] = "l", ['მ'] = "m", ['ნ'] = "n", ['ო'] = "o", ['პ'] = "p", ['ჟ'] = "zh", ['რ'] = "r", ['ს'] = "s",
        ['ტ'] = "t", ['უ'] = "u", ['ფ'] = "p", ['ქ'] = "k", ['ღ'] = "gh", ['ყ'] = "q", ['შ'] = "sh", ['ჩ'] = "ch", ['ც'] = "ts",
        ['ძ'] = "dz", ['წ'] = "ts", ['ჭ'] = "ch", ['ხ'] = "kh", ['ჯ'] = "j", ['ჰ'] = "h",
    };

    public bool Covers(char c) => c is (>= '\u10A0' and <= '\u10FF') or (>= '\u1C90' and <= '\u1CBF') or (>= '\u2D00' and <= '\u2D2F');

    // Mtavruli capitals (U+1C90…) are the Mkhedruli letters (U+10D0…) in upper case.
    public string Transliterate(char c)
    {
        var letter = c is >= '\u1C90' and <= '\u1CBF' ? (char)(c - 0x1C90 + 0x10D0) : c;
        return Table.GetValueOrDefault(letter, string.Empty);
    }
}

/// <summary>Russian, a common passport-style scheme: Поиск → Poisk.</summary>
public sealed class RussianTransliterator : ITransliterator
{
    private static readonly Dictionary<char, string> Table = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "yo", ['ж'] = "zh", ['з'] = "z",
        ['и'] = "i", ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n", ['о'] = "o", ['п'] = "p", ['р'] = "r",
        ['с'] = "s", ['т'] = "t", ['у'] = "u", ['ф'] = "f", ['х'] = "kh", ['ц'] = "ts", ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "shch",
        ['ъ'] = string.Empty, ['ы'] = "y", ['ь'] = string.Empty, ['э'] = "e", ['ю'] = "yu", ['я'] = "ya",
    };

    public bool Covers(char c) => c is >= '\u0400' and <= '\u04FF';

    public string Transliterate(char c)
    {
        var lower = char.ToLowerInvariant(c);
        var latin = Table.GetValueOrDefault(lower, string.Empty);
        return c != lower && latin.Length > 0 ? char.ToUpperInvariant(latin[0]) + latin[1..] : latin;
    }
}

public static class Transliteration
{
    public static IReadOnlyList<ITransliterator> BuiltIn { get; } = [new GeorgianTransliterator(), new RussianTransliterator()];

    /// <summary>Replaces every character a transliterator handles; leaves everything else as it is.</summary>
    public static string ToLatin(string text, IReadOnlyList<ITransliterator> transliterators)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(transliterators);
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            var transliterator = transliterators.FirstOrDefault(t => t.Covers(c));
            if (transliterator is null)
            {
                builder.Append(c);
            }
            else
            {
                builder.Append(transliterator.Transliterate(c));
            }
        }

        return builder.ToString();
    }
}
