using System.Globalization;
using System.Text;
using Recorder.Core.Locators;
using Recorder.Core.Naming;

namespace Recorder.Core.CodeGen;

/// <summary>
/// One locator as a getter in the style of docs/target-style (decision D-5, java-client 7.6):
/// <c>public WebElement getAmountInput(){return winDriver.findElementByAccessibilityId("txtAmount");}</c>.
/// Phase 4's code profiles generalise this to templates.
/// </summary>
public static class JavaLocatorLine
{
    public static string Getter(string variableName, LocatorCandidate candidate)
    {
        ArgumentException.ThrowIfNullOrEmpty(variableName);
        ArgumentNullException.ThrowIfNull(candidate);
        return $"public WebElement {VariableNamer.Getter(variableName)}(){{return winDriver.{FindMethod(candidate.Strategy)}({JavaString.Literal(candidate.RenderedValue)});}}";
    }

    /// <summary>The java-client 7 WindowsDriver method for a strategy.</summary>
    public static string FindMethod(LocatorStrategy strategy) => strategy switch
    {
        LocatorStrategy.AccessibilityId => "findElementByAccessibilityId",
        LocatorStrategy.Name => "findElementByName",
        LocatorStrategy.ClassName => "findElementByClassName",
        _ => "findElementByXPath",
    };
}

/// <summary>Java string literals (spec 10.3): escapes what Java requires and keeps non-ASCII text as it is.</summary>
public static class JavaString
{
    public static string Literal(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                default:
                    if (char.IsControl(c))
                    {
                        builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        return builder.Append('"').ToString();
    }
}
