using FlaUI.Core.AutomationElements;

namespace Recorder.Core.Automation;

/// <summary>
/// Reads an element's ValuePattern value. A separate seam so tests can prove password fields are never read.
/// </summary>
public interface IValueReader
{
    string? ReadValue(AutomationElement element);
}

public sealed class ValuePatternReader : IValueReader
{
    public string? ReadValue(AutomationElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.Patterns.Value.PatternOrDefault?.Value.ValueOrDefault;
    }
}
