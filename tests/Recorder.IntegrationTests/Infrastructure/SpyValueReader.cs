using System.Collections.Concurrent;
using FlaUI.Core.AutomationElements;
using Recorder.Core.Automation;

namespace Recorder.IntegrationTests.Infrastructure;

/// <summary>Reads values like the real reader and records whose value was read (by AutomationId).</summary>
internal sealed class SpyValueReader : IValueReader
{
    private readonly ValuePatternReader _inner = new();

    public ConcurrentQueue<string> ReadFrom { get; } = new();

    public string? ReadValue(AutomationElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        ReadFrom.Enqueue(element.Properties.AutomationId.ValueOrDefault ?? string.Empty);
        return _inner.ReadValue(element);
    }
}
