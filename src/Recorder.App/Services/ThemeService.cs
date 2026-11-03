using System.Windows;
using Recorder.Core.Persistence;

namespace Recorder.App.Services;

/// <summary>
/// Light, dark, or as Windows is set (spec 11.7, 12): the WPF Fluent theme follows the setting at start and whenever it
/// changes. UI thread only.
/// </summary>
public sealed class ThemeService
{
    private ThemeChoice _applied = ThemeChoice.System;

    public ThemeService(SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Apply(settings.Current.Theme);
        settings.Changed += (_, current) => Apply(current.Theme);
    }

    // Settings also change on other threads (the target tracker remembers the app); only the theme's own change acts.
    private void Apply(ThemeChoice choice)
    {
        if (choice == _applied || Application.Current is not { } app)
        {
            return;
        }

        if (!app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.BeginInvoke(() => Apply(choice));
            return;
        }

#pragma warning disable WPF0001 // ThemeMode is still "for evaluation" in C# on .NET 10, and the only way to switch the Fluent theme (D-77).
        app.ThemeMode = choice switch
        {
            ThemeChoice.Light => ThemeMode.Light,
            ThemeChoice.Dark => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
#pragma warning restore WPF0001
        _applied = choice;
    }
}
