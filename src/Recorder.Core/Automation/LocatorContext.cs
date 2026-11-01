using FlaUI.UIA3;
using Recorder.Core.Locators;

namespace Recorder.Core.Automation;

/// <summary>What the locator worker thread owns. Created and used only on that thread.</summary>
internal sealed class LocatorContext : IDisposable
{
    private readonly TimeProvider _time;
    private (nint Root, UiTree Tree, long FetchedAt)? _last;

    public LocatorContext(UiaOptions options, TimeProvider time)
    {
        _time = time;
        Automation = new UIA3Automation
        {
            ConnectionTimeout = options.CallTimeout,
            TransactionTimeout = options.CallTimeout,
        };
        Trees = new TreeFetcher(Automation);
    }

    public UIA3Automation Automation { get; }

    public TreeFetcher Trees { get; }

    /// <summary>
    /// The tree of <paramref name="root"/>. Hovering may reuse one read moments ago if it still contains the element;
    /// everything else reads afresh, so counts reflect the screen as it is now.
    /// </summary>
    public UiTree TreeFor(nint root, string targetKey, TimeSpan reuseFor, CancellationToken cancellationToken)
    {
        if (reuseFor > TimeSpan.Zero
            && _last is { } last
            && last.Root == root
            && _time.GetElapsedTime(last.FetchedAt) <= reuseFor
            && last.Tree.Find(targetKey) is not null)
        {
            return last.Tree;
        }

        var tree = Trees.Fetch(root, cancellationToken);
        _last = (root, tree, _time.GetTimestamp());
        return tree;
    }

    public void Dispose() => Automation.Dispose();
}
