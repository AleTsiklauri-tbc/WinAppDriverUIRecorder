using System.Globalization;

namespace Recorder.App.Resources;

/// <summary>Formats Strings.resx templates with the user's culture.</summary>
public static class Text
{
    public static string Format(string template, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, template, arguments);
}
