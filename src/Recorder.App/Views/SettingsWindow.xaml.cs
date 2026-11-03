using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Recorder.App.Controls;
using Recorder.App.ViewModels;

namespace Recorder.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();

    private SettingsViewModel? ViewModel => DataContext as SettingsViewModel;

    // A shortcut box takes the keys pressed in it (view concern). Tab still moves on and Esc still closes the window.
    private void OnShortcutKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ShortcutRowViewModel row })
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        if (ShortcutKeys.IsModifier(key) || (key is Key.Tab or Key.Escape && (modifiers & ~ModifierKeys.Shift) == ModifierKeys.None))
        {
            return;
        }

        e.Handled = true;
        row.Press(ShortcutKeys.Gesture(key, modifiers));
    }

    private void OnAddressLostFocus(object sender, KeyboardFocusChangedEventArgs e) => ViewModel?.CommitAddressCommand.Execute(null);

    private void OnUiaTimeoutLostFocus(object sender, KeyboardFocusChangedEventArgs e) => ViewModel?.CommitUiaTimeoutCommand.Execute(null);

    // What was typed last is kept even when the window is closed straight from the box.
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        ViewModel?.CommitAddressCommand.Execute(null);
        ViewModel?.CommitUiaTimeoutCommand.Execute(null);
    }
}
