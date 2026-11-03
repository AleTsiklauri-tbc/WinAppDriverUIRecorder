using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;

namespace Recorder.App.Controls;

/// <summary>Binds an AvalonEdit editor's text and syntax highlighting, which are not dependency properties (view concern).</summary>
public static class CodeEditor
{
    public static readonly DependencyProperty CodeProperty = DependencyProperty.RegisterAttached(
        "Code", typeof(string), typeof(CodeEditor), new PropertyMetadata(string.Empty, OnCodeChanged));

    /// <summary>"Java" or "C#": an AvalonEdit highlighting definition.</summary>
    public static readonly DependencyProperty HighlightingProperty = DependencyProperty.RegisterAttached(
        "Highlighting", typeof(string), typeof(CodeEditor), new PropertyMetadata(null, OnHighlightingChanged));

    public static string GetCode(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (string)element.GetValue(CodeProperty);
    }

    public static void SetCode(DependencyObject element, string value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(CodeProperty, value);
    }

    public static string? GetHighlighting(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (string?)element.GetValue(HighlightingProperty);
    }

    public static void SetHighlighting(DependencyObject element, string? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(HighlightingProperty, value);
    }

    // Live updates must not jump: the code is regenerated while the user reads it, so the scroll position stays.
    private static void OnCodeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextEditor editor && editor.Text != (string?)e.NewValue)
        {
            var (vertical, horizontal) = (editor.VerticalOffset, editor.HorizontalOffset);
            editor.Text = (string?)e.NewValue ?? string.Empty;
            editor.ScrollToVerticalOffset(vertical);
            editor.ScrollToHorizontalOffset(horizontal);
        }
    }

    // AvalonEdit's own colorizer is never used: ours keeps the colours readable on the theme's background, and repaints
    // when the background changes with the Windows theme.
    private static void OnHighlightingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextEditor editor)
        {
            return;
        }

        var transformers = editor.TextArea.TextView.LineTransformers;
        foreach (var old in transformers.OfType<ReadableHighlightingColorizer>().ToList())
        {
            transformers.Remove(old);
        }

        if (e.NewValue is string name && HighlightingManager.Instance.GetDefinition(name) is { } definition)
        {
            transformers.Insert(0, new ReadableHighlightingColorizer(definition, () => (editor.Background as SolidColorBrush)?.Color ?? Colors.White));
        }

        if (e.OldValue is null)
        {
            DependencyPropertyDescriptor.FromProperty(Control.BackgroundProperty, typeof(TextEditor))
                .AddValueChanged(editor, (_, _) => editor.TextArea.TextView.Redraw());
        }
    }
}
