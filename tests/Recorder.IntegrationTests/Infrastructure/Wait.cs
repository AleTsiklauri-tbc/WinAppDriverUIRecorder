using System.Diagnostics;
using FlaUI.Core.AutomationElements;

namespace Recorder.IntegrationTests.Infrastructure;

internal static class Wait
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(200);

    /// <summary>Polls until the probe returns a value, or fails with a readable message.</summary>
    public static T For<T>(Func<T?> probe, TimeSpan timeout, string what)
        where T : class
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            var result = probe();
            if (result is not null)
            {
                return result;
            }

            if (stopwatch.Elapsed > timeout)
            {
                throw new TimeoutException($"Timed out after {timeout.TotalSeconds:0.#} s waiting for {what}.");
            }

            Thread.Sleep(Interval);
        }
    }

    public static void Until(Func<bool> condition, TimeSpan timeout, string what) =>
        For(() => condition() ? new object() : null, timeout, what);

    public static AutomationElement ForDescendant(AutomationElement root, string automationId, TimeSpan? timeout = null) =>
        For(() => root.FindFirstDescendant(c => c.ByAutomationId(automationId)), timeout ?? TimeSpan.FromSeconds(10), $"element '{automationId}'");
}
