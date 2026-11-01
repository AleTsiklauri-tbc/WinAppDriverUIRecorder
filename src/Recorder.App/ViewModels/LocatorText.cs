using Recorder.App.Overlay;
using Recorder.App.Resources;
using Recorder.Core.Locators;

namespace Recorder.App.ViewModels;

/// <summary>A locator's quality as shown everywhere: overlay colour, badge colour and the Elements tick.</summary>
public enum LocatorQuality
{
    Unchecked,
    Unique,
    Medium,
    None,
}

/// <summary>Turns the locator engine's codes into plain sentences (all text lives in Strings.resx, spec 11.10).</summary>
internal static class LocatorText
{
    public static string Badge(LocatorStrategy strategy) => strategy switch
    {
        LocatorStrategy.AccessibilityId => Strings.Locator_Badge_AccessibilityId,
        LocatorStrategy.Name => Strings.Locator_Badge_Name,
        LocatorStrategy.ClassName => Strings.Locator_Badge_ClassName,
        _ => Strings.Locator_Badge_XPath,
    };

    public static LocatorQuality Quality(LocatorCandidate? candidate) => candidate switch
    {
        null or { IsChecked: false } => LocatorQuality.Unchecked,
        { IsUnique: false } => LocatorQuality.None,
        { Stability: Core.Locators.Stability.High } => LocatorQuality.Unique,
        _ => LocatorQuality.Medium,
    };

    public static HighlightStyle Style(LocatorQuality quality) => quality switch
    {
        LocatorQuality.Unique => HighlightStyle.Unique,
        LocatorQuality.Medium => HighlightStyle.Medium,
        LocatorQuality.None => HighlightStyle.NoUnique,
        _ => HighlightStyle.Neutral,
    };

    public static string Matches(LocatorCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!candidate.IsChecked)
        {
            return Strings.Locator_NotChecked;
        }

        if (candidate.IsUnique)
        {
            return candidate.Scope == LocatorScope.Desktop ? Strings.Locator_OneMatchDesktop : Strings.Locator_OneMatch;
        }

        return candidate.DistinctMatchCount == 0 ? Strings.Locator_NoMatch : Text.Format(Strings.Locator_Matches, candidate.DistinctMatchCount);
    }

    public static string Stability(Stability stability) => stability switch
    {
        Core.Locators.Stability.High => Strings.Locator_Stability_High,
        Core.Locators.Stability.Medium => Strings.Locator_Stability_Medium,
        _ => Strings.Locator_Stability_Low,
    };

    /// <summary>One or two plain sentences: why this locator, and what a refinement added.</summary>
    public static string Explanation(LocatorCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var explanation = candidate.Explanation;
        var why = explanation.Refinement is Refinement.Anchor or Refinement.Index
            ? WhyFromAttribute(candidate.Spec.Last)
            : WhyFromKind(explanation);
        string? refined = explanation.Refinement switch
        {
            Refinement.ControlType => Text.Format(Strings.Locator_Refined_ControlType, explanation.MatchesBefore),
            Refinement.Anchor => Text.Format(Strings.Locator_Refined_Anchor, explanation.MatchesBefore, explanation.Anchor),
            Refinement.Attribute => Text.Format(Strings.Locator_Refined_Attribute, explanation.MatchesBefore),
            Refinement.Index => Text.Format(Strings.Locator_Refined_Index, explanation.MatchesBefore),
            _ => null,
        };
        return why is null ? refined ?? string.Empty : refined is null ? why : why + " " + refined;
    }

    public static string Note(LocatorNote note) => note switch
    {
        LocatorNote.NumericId => Strings.Locator_Note_NumericId,
        LocatorNote.DynamicIdSuffix => Strings.Locator_Note_DynamicIdSuffix,
        LocatorNote.LooksDynamic => Strings.Locator_Note_LooksDynamic,
        LocatorNote.UiText => Strings.Locator_Note_UiText,
        LocatorNote.PositionBased => Strings.Locator_Note_PositionBased,
        LocatorNote.PopupWindow => Strings.Locator_Note_PopupWindow,
        _ => Strings.Locator_Note_DesignerDefaultName,
    };

    /// <summary>The overlay chip's ending: "· unique", "· no unique locator", "· not checked".</summary>
    public static string ChipSuffix(LocatorReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var text = !report.IsChecked ? Strings.Chip_NotChecked
            : report.HasUniqueLocator ? Strings.Chip_Unique
            : Strings.Chip_NoUnique;
        return " · " + text;
    }

    private static string WhyFromKind(LocatorExplanation explanation) => explanation.Kind switch
    {
        LocatorKind.Id => Strings.Locator_Why_Id,
        LocatorKind.IdPrefix => Strings.Locator_Why_IdPrefix,
        LocatorKind.Name => Strings.Locator_Why_Name,
        LocatorKind.ClassName => Strings.Locator_Why_ClassName,
        LocatorKind.Anchored => Text.Format(Strings.Locator_Why_Anchored, explanation.Anchor),
        LocatorKind.Indexed => explanation.Anchor is null ? Strings.Locator_Why_IndexedWindow : Text.Format(Strings.Locator_Why_Indexed, explanation.Anchor),
        _ => Strings.Locator_Why_Absolute,
    };

    // After an anchor or a position was added, the reason is still the attribute the locator started from.
    private static string? WhyFromAttribute(LocatorStep last) => (last.Predicates.Count > 0 ? last.Predicates[0] : null) switch
    {
        AttrEquals { Attribute: AttributeName.Name } => Strings.Locator_Why_Name,
        AttrEquals { Attribute: AttributeName.AutomationId } => Strings.Locator_Why_Id,
        AttrStartsWith { Attribute: AttributeName.AutomationId } => Strings.Locator_Why_IdPrefix,
        { Attribute: AttributeName.ClassName } => Strings.Locator_Why_ClassName,
        _ => null,
    };
}
