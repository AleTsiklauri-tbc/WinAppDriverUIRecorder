using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Microsoft.Extensions.Logging.Abstractions;
using Recorder.Core.Automation;
using Recorder.Core.Locators;
using Recorder.Core.Model;
using Recorder.IntegrationTests.Infrastructure;
using Xunit;

namespace Recorder.IntegrationTests.Lms;

/// <summary>
/// Spike, not a regression test: how LMS looks to the recorder (ARCHITECTURE F-35, F-36). Read-only: no input, no value
/// read, every name reduced to its shape (letters a/A, digits 9), window titles never written. Runs only when
/// WINFORMSRECORDER_LMS_SURVEY names the report file and LMS (or WINFORMSRECORDER_LMS_SURVEY_PROCESS) runs in this Windows
/// session. Bounds are read through the recorder's cache request, live, and from a capture at the element's center as hover
/// makes it; at 150 % the capture's differ for parts served through MSAA (F-35).
/// </summary>
public sealed partial class LmsSurveyTests
{
    private const int MaxProbes = 150;

    [Fact]
    public async Task Survey_LMS_as_the_recorder_sees_it()
    {
        var output = Environment.GetEnvironmentVariable("WINFORMSRECORDER_LMS_SURVEY");
        Assert.SkipWhen(string.IsNullOrEmpty(output), "Set WINFORMSRECORDER_LMS_SURVEY to a report path to survey LMS.");
        var session = Process.GetCurrentProcess().SessionId;
        var name = Environment.GetEnvironmentVariable("WINFORMSRECORDER_LMS_SURVEY_PROCESS") is { Length: > 0 } other ? other : "LMS";
        var lms = Process.GetProcessesByName(name).FirstOrDefault(p => p.SessionId == session && p.MainWindowHandle != 0);
        Assert.SkipWhen(lms is null, name + " is not running in this Windows session.");
        var main = lms!.MainWindowHandle;
        var cancel = TestContext.Current.CancellationToken;
        Desktop.Raise(main);
        var report = new StringBuilder();

        using var automation = new UIA3Automation();
        var watch = Stopwatch.StartNew();
        var tree = await Task.Run(() => new TreeFetcher(automation).Fetch(main, cancel), cancel);
        var fetchMs = watch.ElapsedMilliseconds;
        var details = await Task.Run(() => Details(automation, main), cancel);
        PixelRect Live(UiNode n) => details.TryGetValue(n.RuntimeKey, out var info) && !info.Live.IsEmpty ? info.Live : n.Bounds;
        var root = Live(tree.Root);
        report.AppendLine(CultureInfo.InvariantCulture, $"{name} {lms.MainModule?.FileVersionInfo.FileVersion}; scale {Desktop.ScaleAt(root.Center):0.##}; main window {root.Width}x{root.Height} class {Family(tree.Root.ClassName)}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Tree (raw view, as TreeFetcher reads it): {tree.Count} elements in {fetchMs} ms");

        var best = tree.Nodes.ToDictionary(n => n, n => LocatorEngine.Analyze(LocatorSubject.From(n, lms.Id, LocatorScope.Window), tree, n, TimeSpan.Zero).Best);

        report.AppendLine().AppendLine("== Census: control type | class | own window | AutomationId kind | framework -> count");
        foreach (var group in tree.Nodes.GroupBy(n => $"{n.ControlType} | {Family(n.ClassName)} | {Own(n)} | {IdKind(n.AutomationId)} | {n.FrameworkId ?? "-"}").OrderByDescending(g => g.Count()))
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"{group.Count(),5}  {group.Key}");
        }

        report.AppendLine().AppendLine("== Best locator: own window? | strategy | kind | stability | unique -> count");
        foreach (var group in tree.Nodes.GroupBy(n => $"{Own(n)} | {Rate(best[n])}").OrderByDescending(g => g.Count()))
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"{group.Count(),5}  {group.Key}");
        }

        report.AppendLine().AppendLine("== Bounds through the recorder's cache request vs live: own window? | same or width ratio -> count");
        foreach (var group in tree.Nodes.Where(n => !n.Bounds.IsEmpty && details.ContainsKey(n.RuntimeKey))
                     .GroupBy(n => $"{Own(n)} | {(n.Bounds == Live(n) ? "same" : "differ, cached/live width " + Ratio(n.Bounds.Width, Live(n).Width))}")
                     .OrderByDescending(g => g.Count()))
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"{group.Count(),5}  {group.Key}");
        }

        report.AppendLine().AppendLine("== What the recorder captures at a control's live center (no input, ElementFromPoint only)");
        using var uia = new UiaService(UiaOptions.Default, new NoValueReader(), TimeProvider.System, NullLogger<UiaService>.Instance);
        foreach (var probe in Probes(tree, root, Live))
        {
            var result = await uia.CaptureAtAsync(Live(probe).Center, cancel);
            var line = $"{Describe(probe)}  ->  ";
            if (result.Value is not { } snapshot)
            {
                report.AppendLine(line + result.Status);
                continue;
            }

            var captured = tree.Find(snapshot.SessionKey);
            var relation = captured is null ? "not in the tree" : captured == probe ? "same" : captured.IsDescendantOf(probe) ? "an inner part" : probe.IsDescendantOf(captured) ? "an outer element" : "another element";
            var outline = captured is null ? "outline ?" : snapshot.Bounds == Live(captured) ? "outline right" : $"outline WRONG {snapshot.Bounds} vs {Live(captured)}";
            report.AppendLine(CultureInfo.InvariantCulture, $"{line}{snapshot.ControlType} id={Id(snapshot.AutomationId ?? string.Empty)} cls={Family(snapshot.ClassName ?? string.Empty)} ({relation}, {outline}) best={(captured is null ? "?" : Rate(best[captured]))}");
        }

        report.AppendLine().AppendLine("== Tree: type [localized] class id name own-window framework | patterns | msaa role/state/action | best locator");
        foreach (var node in tree.Nodes)
        {
            var depth = node.Ancestors().Count();
            var info = details.GetValueOrDefault(node.RuntimeKey);
            var bounds = node.Bounds == Live(node) ? string.Empty : " bounds≠live";
            report.Append(' ', depth * 2).AppendLine(CultureInfo.InvariantCulture, $"{Describe(node)} [{info?.Localized}] {Own(node)} {node.FrameworkId}{bounds} | {info?.Patterns} | {info?.Msaa} | {Rate(best[node])} {Masked(best[node])}");
        }

        await File.WriteAllTextAsync(output!, report.ToString(), Encoding.UTF8, cancel);
    }

    // Controls with their own window and a designer name, then one part of each kind inside them, all visible on screen.
    private static IEnumerable<UiNode> Probes(UiTree tree, PixelRect window, Func<UiNode, PixelRect> live)
    {
        bool Visible(UiNode n) => !n.IsOffscreen && !live(n).IsEmpty && window.Contains(live(n).Center);
        var controls = tree.Nodes.Where(n => n.NativeWindowHandle != 0 && IdKind(n.AutomationId) == "name" && Visible(n) && n != tree.Root).ToList();
        var parts = tree.Nodes.Where(n => n.NativeWindowHandle == 0 && Visible(n))
            .GroupBy(n => $"{n.ControlType}|{n.Parent?.AutomationId}|{n.Parent?.ControlType}").Select(g => g.First());
        return controls.Concat(parts).Take(MaxProbes);
    }

    // Live reads: no cache request is active on this thread.
    private static Dictionary<string, Info> Details(UIA3Automation automation, nint main)
    {
        var details = new Dictionary<string, Info>(StringComparer.Ordinal);
        var walker = automation.TreeWalkerFactory.GetRawViewWalker();
        void Walk(AutomationElement element)
        {
            try
            {
                var legacy = element.Patterns.LegacyIAccessible.PatternOrDefault;
                var msaa = legacy is null ? "-" : $"{legacy.Role.ValueOrDefault}/{legacy.State.ValueOrDefault}/{legacy.DefaultAction.ValueOrDefault}";
                var patterns = string.Join(",", element.GetSupportedPatterns().Select(p => p.Name.Replace("Pattern", string.Empty, StringComparison.Ordinal)).Where(p => p != "LegacyIAccessible").Order(StringComparer.Ordinal));
                var rectangle = element.Properties.BoundingRectangle.ValueOrDefault;
                var live = rectangle.IsEmpty ? default : new PixelRect(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
                details[RuntimeKey.Of(element.Properties.RuntimeId.ValueOrDefault ?? [])] = new Info(element.Properties.LocalizedControlType.ValueOrDefault ?? string.Empty, patterns, msaa, live);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // The element went away while the tree was walked.
            }

            for (var child = walker.GetFirstChild(element); child is not null; child = walker.GetNextSibling(child))
            {
                Walk(child);
            }
        }

        Walk(automation.FromHandle(main));
        return details;
    }

    private static string Own(UiNode node) => node.NativeWindowHandle != 0 ? "hwnd" : "part";

    private static string Ratio(int cached, int live) => live == 0 ? "-" : (cached / (double)live).ToString("0.00", CultureInfo.InvariantCulture);

    private static string Describe(UiNode node) =>
        $"{node.ControlType} cls={Family(node.ClassName)} id={Id(node.AutomationId)} name={(node.ControlType is "Window" or "TitleBar" ? "<title>" : Shape(node.Name))}";

    private static string Rate(LocatorCandidate? candidate) =>
        candidate is null ? "none" : $"{candidate.Strategy} | {candidate.Explanation.Kind} | {candidate.Stability} | {(candidate.IsChecked ? candidate.IsUnique ? "unique" : $"{candidate.DistinctMatchCount} matches" : "unchecked")}";

    // A locator's value with every quoted literal reduced to its shape; an AutomationId as it is.
    private static string Masked(LocatorCandidate? candidate) => candidate switch
    {
        null => string.Empty,
        { Strategy: LocatorStrategy.AccessibilityId } => Id(candidate.RenderedValue),
        { Strategy: LocatorStrategy.Name } => Shape(candidate.RenderedValue),
        _ => QuotedLiteral().Replace(candidate.RenderedValue, m => Shape(m.Groups[m.Groups[1].Success ? 1 : 2].Value).Replace('"', '\'')),
    };

    private static string IdKind(string id) => id.Length == 0 ? "empty" : id.All(char.IsAsciiDigit) ? "digits" : DesignerName().IsMatch(id) ? "name" : "other";

    private static string Id(string id) => IdKind(id) switch { "empty" => "-", "digits" => "<digits>", "name" => id, _ => Shape(id) };

    private static string Family(string className) => className.Length == 0 ? "-" : AppSuffix().Replace(className, string.Empty);

    private static string Shape(string text)
    {
        if (text.Length == 0)
        {
            return "\"\"";
        }

        var shape = new string([.. text.Select(c => char.IsDigit(c) ? '9' : char.IsLetter(c) ? char.IsUpper(c) ? 'A' : 'a' : c)]);
        return "\"" + (shape.Length > 50 ? shape[..50] + "…" : shape) + "\"";
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex DesignerName();

    [GeneratedRegex(@"\.app\.0\.[0-9a-f]+_r\d+_ad\d+$")]
    private static partial Regex AppSuffix();

    // XPathLiteral quotes with ' or, when the text has an apostrophe, with " (or concat() of both).
    [GeneratedRegex("'([^']*)'|\"([^\"]*)\"")]
    private static partial Regex QuotedLiteral();

    private sealed record Info(string Localized, string Patterns, string Msaa, PixelRect Live);

    // The survey never reads a value: LMS shows customer data in its fields.
    private sealed class NoValueReader : IValueReader
    {
        public string? ReadValue(AutomationElement element) => null;
    }
}
