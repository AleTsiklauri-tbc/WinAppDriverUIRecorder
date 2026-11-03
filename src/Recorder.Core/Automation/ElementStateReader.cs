using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Identifiers;
using FlaUI.UIA3;

namespace Recorder.Core.Automation;

/// <summary>An element's current state, read again after a click or for an assertion.</summary>
/// <param name="Name">Visible text (for an input, the label next to it, F-4), never the typed value.</param>
/// <param name="Value">ValuePattern value. Always null for a password field: it is never read.</param>
/// <param name="IsChecked">Check box or toggle state, or a radio button's selection; null when the element has neither.</param>
public sealed record ElementState(bool IsEnabled, bool IsOffscreen, bool IsPassword, string? Name, string? Value, bool? IsChecked);

/// <summary>
/// Reads <see cref="ElementState"/> with one cached call plus a live value read. Only properties are read: no pattern
/// method is ever invoked on the target (F-14). Worker thread only.
/// </summary>
internal sealed class ElementStateReader
{
    private readonly IValueReader _valueReader;
    private readonly CacheRequest _request;
    private readonly PropertyId _toggleState;
    private readonly PropertyId _isSelected;
    private readonly PropertyId? _hasValue;
    private readonly PropertyId? _hasToggle;
    private readonly PropertyId? _hasSelectionItem;

    public ElementStateReader(UIA3Automation automation, IValueReader valueReader)
    {
        _valueReader = valueReader;
        var p = automation.PropertyLibrary.Element;
        var patterns = automation.PatternLibrary;
        _toggleState = automation.PropertyLibrary.Toggle.ToggleState;
        _isSelected = automation.PropertyLibrary.SelectionItem.IsSelected;
        _hasValue = patterns.ValuePattern.AvailabilityProperty;
        _hasToggle = patterns.TogglePattern.AvailabilityProperty;
        _hasSelectionItem = patterns.SelectionItemPattern.AvailabilityProperty;
        _request = ElementSnapshotFactory.Request(ElementSnapshotFactory.NonNull(
            p.IsEnabled, p.IsOffscreen, p.IsPassword, p.Name, _toggleState, _isSelected, _hasValue, _hasToggle, _hasSelectionItem));
    }

    /// <summary>Null when the element no longer exists.</summary>
    public ElementState? Read(AutomationElement element, CancellationToken cancellationToken)
    {
        AutomationElement? updated;
        bool isEnabled, isOffscreen, isPassword, hasValue;
        string? name;
        bool? isChecked;
        using (_request.Activate())
        {
            updated = element.FrameworkAutomationElement.GetUpdatedCache();
            if (updated is null)
            {
                return null;
            }

            var props = updated.Properties;
            var framework = updated.FrameworkAutomationElement;
            isEnabled = props.IsEnabled.ValueOrDefault;
            isOffscreen = props.IsOffscreen.ValueOrDefault;
            isPassword = props.IsPassword.ValueOrDefault;
            name = props.Name.ValueOrDefault;
            hasValue = Has(framework, _hasValue);
            isChecked = Has(framework, _hasToggle) && framework.TryGetPropertyValue<ToggleState>(_toggleState, out var toggle)
                ? toggle switch { ToggleState.On => true, ToggleState.Off => false, _ => null }
                : Has(framework, _hasSelectionItem) && framework.TryGetPropertyValue<bool>(_isSelected, out var selected) ? selected : null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var value = !isPassword && hasValue ? _valueReader.ReadValue(updated) : null;
        return new ElementState(isEnabled, isOffscreen, isPassword, name, value, isChecked);
    }

    private static bool Has(FrameworkAutomationElementBase framework, PropertyId? availability) =>
        availability is not null && framework.TryGetPropertyValue<bool>(availability, out var available) && available;
}
