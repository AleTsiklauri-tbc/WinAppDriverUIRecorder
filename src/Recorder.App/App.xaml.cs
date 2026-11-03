using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Recorder.App.Hosting;
using Recorder.App.Services;
using Recorder.App.Views;
using Recorder.Core.Automation;
using Recorder.Core.Persistence;

namespace Recorder.App;

public partial class App : Application
{
    private IHost? _host;

    private ILogger Logger => _host!.Services.GetRequiredService<ILogger<App>>();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = AppHost.Build(AppPaths.ForCurrentUser());
        _host.Services.GetRequiredService<GlobalExceptionHandler>().Attach(this);
        await _host.StartAsync().ConfigureAwait(true);
        AppLog.Started(Logger);

        // Before any window: the theme, the log detail, and the texts that name the shortcuts ({StaticResource Shortcuts}).
        _host.Services.GetRequiredService<ThemeService>();
        AppHost.FollowLogSetting(_host.Services);
        Resources["Shortcuts"] = _host.Services.GetRequiredService<Shortcuts>();

        var window = _host.Services.GetRequiredService<MainWindow>();
        window.Closed += OnMainWindowClosed;
        MainWindow = window;
        window.Show();

        // The capture coordinator attaches the global shortcuts to the window; recording registers its own after it.
        _host.Services.GetRequiredService<CaptureCoordinator>().Initialize(window);
        _host.Services.GetRequiredService<RecordingCoordinator>().Initialize();
        _host.Services.GetRequiredService<TargetTracker>().Start();
        _host.Services.GetRequiredService<Autosaver>().Start();
    }

    private async void OnMainWindowClosed(object? sender, EventArgs e)
    {
        try
        {
            if (_host is { } host)
            {
                _host = null;
                var logger = host.Services.GetRequiredService<ILogger<App>>();
                AppLog.Stopped(logger);

                // The overlay windows and shortcuts belong to the UI thread, so the coordinators are disposed here, on that thread.
                // A recording in progress ends first (its hooks), autosave keeps what is unsaved, then capture ends (the shortcuts and overlays).
                await host.Services.GetRequiredService<RecordingCoordinator>().DisposeAsync().ConfigureAwait(true);
                host.Services.GetRequiredService<Autosaver>().Flush();
                await host.Services.GetRequiredService<CaptureCoordinator>().DisposeAsync().ConfigureAwait(true);
                await host.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                await ((IAsyncDisposable)host).DisposeAsync().ConfigureAwait(true);
            }
        }
        finally
        {
            // Whatever failed above, the process must end: a recorder without a window would still hold the global shortcut.
            Shutdown();
        }
    }
}
