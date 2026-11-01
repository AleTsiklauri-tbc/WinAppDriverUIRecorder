using System.Text.RegularExpressions;

namespace Recorder.Core.Locators;

public sealed record NameVerdict(bool IsAccepted, Stability Stability, IReadOnlyList<LocatorNote> Notes)
{
    public static NameVerdict Rejected { get; } = new(false, Stability.Low, []);
}

/// <summary>Name (visible text) rules (spec 7.4).</summary>
public static partial class NameRules
{
    public const int MaxLength = 80;

    public static NameVerdict Evaluate(string? name, string controlType)
    {
        ArgumentNullException.ThrowIfNull(controlType);
        if (string.IsNullOrWhiteSpace(name)
            || name.Length > MaxLength
            || name.AsSpan().IndexOfAny('\r', '\n') >= 0
            || IsControlTypeName(name, controlType))
        {
            return NameVerdict.Rejected;
        }

        var notes = new List<LocatorNote>(2);
        var stability = Stability.Medium;
        if (LooksDynamic(name))
        {
            stability = Stability.Low;
            notes.Add(LocatorNote.LooksDynamic);
        }

        if (HasNonLatinLetters(name))
        {
            notes.Add(LocatorNote.UiText);
        }

        return new NameVerdict(true, stability, notes);
    }

    /// <summary>Dates, times, amounts, long digit runs and GUIDs: values that change while the label stays.</summary>
    public static bool LooksDynamic(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return DatePattern().IsMatch(text)
            || TimePattern().IsMatch(text)
            || AmountPattern().IsMatch(text)
            || CurrencyPattern().IsMatch(text)
            || LongDigits().IsMatch(text)
            || GuidPattern().IsMatch(text);
    }

    /// <summary>Letters outside the Latin script, such as Georgian or Cyrillic.</summary>
    public static bool HasNonLatinLetters(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (var c in text)
        {
            // Latin, Latin-1 and Latin Extended-A/B end at U+024F.
            if (c > '\u024F' && char.IsLetter(c))
            {
                return true;
            }
        }

        return false;
    }

    // "Edit" on an Edit, "Check box" on a CheckBox: the text says nothing about this control.
    private static bool IsControlTypeName(string name, string controlType)
    {
        var compact = name.Replace(" ", string.Empty, StringComparison.Ordinal);
        return string.Equals(compact, controlType, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"\b\d{1,4}[./-]\d{1,2}[./-]\d{1,4}\b")]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"\b\d{1,2}:\d{2}(?::\d{2})?\b")]
    private static partial Regex TimePattern();

    // 1,500.00 · 1 500,00 · 15.50
    [GeneratedRegex(@"\d[\d\u00A0 ,.']*[.,]\d{2}\b")]
    private static partial Regex AmountPattern();

    [GeneratedRegex(@"[$€£₾₽¥]\s?\d|\d\s?[$€£₾₽¥]|\b(?:GEL|USD|EUR|RUB|GBP)\s?\d|\d\s?(?:GEL|USD|EUR|RUB|GBP)\b")]
    private static partial Regex CurrencyPattern();

    [GeneratedRegex(@"\d{4,}")]
    private static partial Regex LongDigits();

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidPattern();
}

/// <summary>Class name rules (spec 7.1, 7.2).</summary>
public static partial class ClassNameRules
{
    /// <summary>
    /// Class names a framework generates per build or process: WinForms (WindowsForms10.*.app.0.&lt;hash&gt;), WPF
    /// (HwndWrapper[…]), MFC and ATL (Afx:…, ATL:…). Useless as locators on their own.
    /// </summary>
    public static bool IsGenerated(string? className) =>
        className is not null
        && (className.StartsWith("WindowsForms10.", StringComparison.Ordinal)
            || className.StartsWith("HwndWrapper[", StringComparison.Ordinal)
            || className.StartsWith("Afx:", StringComparison.Ordinal)
            || className.StartsWith("ATL:", StringComparison.Ordinal));

    /// <summary>A class name usable as a ClassName locator. Web content reports CSS class lists that change with state (F-12).</summary>
    public static bool CanBeLocator(string? className, string? frameworkId) =>
        !string.IsNullOrWhiteSpace(className)
        && !IsGenerated(className)
        && !Frameworks.IsWeb(frameworkId)
        && !GenericClasses.Contains(className)
        && !className.StartsWith("Chrome_", StringComparison.Ordinal);

    // Standard Windows and common-control classes: every dialog and form is full of them, so they identify nothing.
    private static readonly HashSet<string> GenericClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Button", "Edit", "Static", "ComboBox", "ComboLBox", "ListBox", "ScrollBar", "MDIClient", "#32770", "#32768", "#32769",
        "SysListView32", "SysTreeView32", "SysTabControl32", "SysHeader32", "SysDateTimePick32", "SysMonthCal32", "SysLink",
        "msctls_progress32", "msctls_trackbar32", "msctls_updown32", "msctls_statusbar32", "ToolbarWindow32", "ReBarWindow32",
        "tooltips_class32", "RichEdit20W", "RICHEDIT50W", "Intermediate D3D Window",
    };

    /// <summary>
    /// The stable start of a WinForms class name, for starts-with: "WindowsForms10.EDIT" from
    /// "WindowsForms10.EDIT.app.0.141b42a_r8_ad1", and "WindowsForms10.Window.8" (the ".8" is kept, F-2). Null otherwise.
    /// </summary>
    public static string? StablePrefix(string? className)
    {
        if (className is null || !className.StartsWith("WindowsForms10.", StringComparison.Ordinal))
        {
            return null;
        }

        var end = className.IndexOf(".app.", StringComparison.Ordinal);
        return end > "WindowsForms10.".Length ? className[..end] : null;
    }
}
