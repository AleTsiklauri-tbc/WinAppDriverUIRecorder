using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace Recorder.Core.Automation;

/// <summary>Everything the UIA worker thread owns. Created and used only on that thread.</summary>
internal sealed class UiaContext : IDisposable
{
    public UiaContext(UiaOptions options, IValueReader valueReader, TimeProvider time)
    {
        // Every cross-process call gives up after the timeout instead of waiting for a hung target.
        Automation = new UIA3Automation
        {
            ConnectionTimeout = options.CallTimeout,
            TransactionTimeout = options.CallTimeout,
        };
        Snapshots = new ElementSnapshotFactory(Automation, valueReader, time);
    }

    public UIA3Automation Automation { get; }

    public ElementSnapshotFactory Snapshots { get; }

    /// <summary>Live elements seen in this session, by runtime id, so they can be inspected again.</summary>
    public ElementCache Elements { get; } = new(capacity: 4096);

    public void Dispose() => Automation.Dispose();
}

/// <summary>A small least-recently-used map from runtime-id key to live element.</summary>
internal sealed class ElementCache(int capacity)
{
    private readonly Dictionary<string, LinkedListNode<(string Key, AutomationElement Element)>> _map = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Key, AutomationElement Element)> _order = new();

    public int Count => _map.Count;

    public void Remember(string key, AutomationElement element)
    {
        if (key.Length == 0)
        {
            return;
        }

        if (_map.Remove(key, out var existing))
        {
            _order.Remove(existing);
        }

        _map[key] = _order.AddFirst((key, element));
        if (_map.Count > capacity)
        {
            var oldest = _order.Last!;
            _order.RemoveLast();
            _map.Remove(oldest.Value.Key);
        }
    }

    public AutomationElement? Find(string key)
    {
        if (!_map.TryGetValue(key, out var node))
        {
            return null;
        }

        _order.Remove(node);
        _order.AddFirst(node);
        return node.Value.Element;
    }
}
