using System.Globalization;
using System.Text.RegularExpressions;

namespace Recorder.Core.Locators;

/// <summary>What is known about an element when judging its AutomationId.</summary>
/// <param name="WindowHandles">The element's own window handle and its ancestors'. An id that contains one is a handle.</param>
/// <param name="IsNonClient">Part of a title bar or system menu, where WinAppDriver reports different ids (Findings F-22).</param>
public readonly record struct IdFacts(IReadOnlyCollection<nint> WindowHandles, int ProcessId, string? FrameworkId, bool IsNonClient);

public enum IdRejection
{
    None,
    Empty,
    WindowHandle,
    ProcessId,
    GuidValue,
    NonClient,
}

/// <param name="StablePrefix">For an id ending in a changing number: the part before the number, for a starts-with locator.</param>
public sealed record IdVerdict(IdRejection Rejection, Stability Stability, IReadOnlyList<LocatorNote> Notes, string? StablePrefix)
{
    public bool IsAccepted => Rejection == IdRejection.None;

    /// <summary>Accepted and stable: usable as an anchor for other locators.</summary>
    public bool IsHigh => IsAccepted && Stability == Stability.High;

    public static IdVerdict Rejected(IdRejection reason) => new(reason, Stability.Low, [], null);
}

/// <summary>AutomationId rules (spec 7.3).</summary>
public static partial class IdRules
{
    /// <summary>
    /// WinForms gives an unnamed control its window handle, in decimal, as AutomationId (Findings F-3).
    /// The handle changes on every run, so such an id must never be used.
    /// </summary>
    public static bool IsWindowHandle(string? automationId, nint nativeWindowHandle) =>
        nativeWindowHandle != 0
        && !string.IsNullOrEmpty(automationId)
        && automationId == ((long)nativeWindowHandle).ToString(CultureInfo.InvariantCulture);

    /// <summary>An id worth showing in labels: present and not a window handle.</summary>
    public static bool IsUsable(string? automationId, nint nativeWindowHandle) =>
        !string.IsNullOrWhiteSpace(automationId) && !IsWindowHandle(automationId, nativeWindowHandle);

    public static IdVerdict Evaluate(string? automationId, IdFacts facts)
    {
        if (string.IsNullOrWhiteSpace(automationId))
        {
            return IdVerdict.Rejected(IdRejection.Empty);
        }

        if (facts.IsNonClient)
        {
            return IdVerdict.Rejected(IdRejection.NonClient);
        }

        if (ContainsWindowHandle(automationId, facts.WindowHandles))
        {
            return IdVerdict.Rejected(IdRejection.WindowHandle);
        }

        if (facts.ProcessId > 0 && ContainsNumber(automationId, facts.ProcessId.ToString(CultureInfo.InvariantCulture), minimumDigits: 3))
        {
            return IdVerdict.Rejected(IdRejection.ProcessId);
        }

        if (GuidLike().IsMatch(automationId))
        {
            return IdVerdict.Rejected(IdRejection.GuidValue);
        }

        if (AllDigits().IsMatch(automationId))
        {
            return new IdVerdict(IdRejection.None, Stability.Low, [LocatorNote.NumericId], null);
        }

        if (DesignerDefault().IsMatch(automationId))
        {
            return new IdVerdict(IdRejection.None, Stability.High, [LocatorNote.DesignerDefaultName], null);
        }

        var suffix = LongNumberSuffix().Match(automationId);
        if (!suffix.Success && Frameworks.IsWeb(facts.FrameworkId))
        {
            // Web pages number their widgets at run time (datepicker-3), so even a short number after a separator changes.
            suffix = WebCounter().Match(automationId);
        }

        return suffix.Success
            ? new IdVerdict(IdRejection.None, Stability.Low, [LocatorNote.DynamicIdSuffix], suffix.Groups["prefix"].Value)
            : new IdVerdict(IdRejection.None, Stability.High, [], null);
    }

    private static bool ContainsWindowHandle(string automationId, IReadOnlyCollection<nint> handles)
    {
        foreach (var handle in handles)
        {
            if (handle == 0)
            {
                continue;
            }

            var value = (long)handle;
            if (ContainsNumber(automationId, value.ToString(CultureInfo.InvariantCulture), minimumDigits: 1))
            {
                return true;
            }

            var hex = value.ToString("X", CultureInfo.InvariantCulture);
            foreach (Match run in HexRun().Matches(automationId))
            {
                if (run.Value.Length >= 4 && string.Equals(run.Value.TrimStart('0'), hex, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // A whole run of digits equal to the number: "frm_4242" contains 4242, "button12" does not contain 1.
    private static bool ContainsNumber(string text, string number, int minimumDigits)
    {
        if (number.Length < minimumDigits)
        {
            return false;
        }

        foreach (Match run in DigitRun().Matches(text))
        {
            if (run.Value.TrimStart('0') == number.TrimStart('0'))
            {
                return true;
            }
        }

        return false;
    }

    // Ported from the legacy recorder (GenerateXPath.CheckAndFixNoneStaticValue): a GUID anywhere in the value.
    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidLike();

    [GeneratedRegex("^[0-9]+$")]
    private static partial Regex AllDigits();

    [GeneratedRegex("[0-9]+")]
    private static partial Regex DigitRun();

    [GeneratedRegex("[0-9A-Fa-f]+")]
    private static partial Regex HexRun();

    // Names the WinForms and DevExpress designers generate: a camelCase type name plus a short number
    // (textBox12, button3, dataGridView1, simpleButton2, comboBoxEdit1). Compiled in, so stable.
    [GeneratedRegex("^(?=[a-z])[A-Za-z]*?(?i:box|button|label|panel|view|control|edit|strip|item|page|picker|bar|list|grid|column|group|tree|container|calendar|browser|menu|splitter|layout|link|check|radio|combo|text)[0-9]{1,3}$")]
    private static partial Regex DesignerDefault();

    // The legacy idea (^([^\d]*)[_\.\-\d]+$), narrowed: only four or more trailing digits count as generated,
    // so designer names such as textBox12 stay exact. row_48213 → "row_", item.000123 → "item.".
    [GeneratedRegex(@"^(?<prefix>(?=.*\p{L}).*[^0-9])(?<digits>[0-9]{4,})$")]
    private static partial Regex LongNumberSuffix();

    [GeneratedRegex(@"^(?<prefix>(?=.*\p{L}).*[-_:.])(?<digits>[0-9]+)$")]
    private static partial Regex WebCounter();
}

/// <summary>UI Automation framework ids the rules care about.</summary>
public static class Frameworks
{
    public const string WinForms = "WinForm";

    /// <summary>Web content (WebView2 reports "Chrome"). Its ids and class names are produced at run time.</summary>
    public static bool IsWeb(string? frameworkId) =>
        frameworkId is "Chrome" or "MicrosoftEdge" or "InternetExplorer";
}
