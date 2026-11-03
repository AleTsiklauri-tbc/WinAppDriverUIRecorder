using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Recorder.App.Overlay;
using Recorder.App.Services;
using Recorder.App.ViewModels;
using Recorder.App.Views;
using Recorder.Core.Automation;
using Recorder.Core.Diagnostics;
using Recorder.Core.Model;
using Recorder.Core.Persistence;
using Recorder.Core.Recording;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Recorder.App.Hosting;

/// <summary>Composition root: services, configuration and logging.</summary>
internal static class AppHost
{
    public static IHost Build(AppPaths paths)
    {
        // No default sources or providers: nothing is read from the environment and
        // nothing is logged anywhere except the local rolling file below.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            ApplicationName = "WinFormsRecorder",
            ContentRootPath = AppContext.BaseDirectory,
        });

        var levelSwitch = new LoggingLevelSwitch(LogEventLevel.Information);
        builder.Services.AddSerilog(CreateLogger(LogOptions.For(paths), levelSwitch), dispose: true);
        builder.Services.Configure<ConsoleLifetimeOptions>(options => options.SuppressStatusMessages = true);
        builder.Services.AddSingleton(levelSwitch);
        builder.Services.AddSingleton(paths);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<SettingsStore>();
        builder.Services.AddSingleton<AutosaveService>();

        // Capture engine (Recorder.Core)
        builder.Services.AddSingleton(services => new UiaOptions(TimeSpan.FromMilliseconds(services.GetRequiredService<SettingsStore>().Current.UiaTimeoutMilliseconds)));
        builder.Services.AddSingleton<IValueReader, ValuePatternReader>();
        builder.Services.AddSingleton<IUiaService, UiaService>();
        builder.Services.AddSingleton(LocatorOptions.Default);
        builder.Services.AddSingleton<ILocatorService, LocatorService>();
        builder.Services.AddSingleton<ITargetEnvironment, SystemTargetEnvironment>();
        builder.Services.AddSingleton<TargetTracker>();
        builder.Services.AddSingleton<ITargetFilter>(services => services.GetRequiredService<TargetTracker>());
        builder.Services.AddSingleton<ICursor, SystemCursor>();
        builder.Services.AddSingleton(HoverOptions.Default);
        builder.Services.AddSingleton<HoverTracker>();
        builder.Services.AddSingleton<IMouseHook, MouseHookService>();
        builder.Services.AddSingleton(_ => new ElementCatalog());
        builder.Services.AddSingleton<ISpecialKeyHook, SpecialKeyHookService>();
        builder.Services.AddSingleton<StepList>();
        builder.Services.AddSingleton<EditHistory>();

        // App services and view models
        builder.Services.AddSingleton<IClipboardService, ClipboardService>();
        builder.Services.AddSingleton<ToastService>();
        builder.Services.AddSingleton<BannerService>();
        builder.Services.AddSingleton<HotkeyService>();
        builder.Services.AddSingleton<Overlays>();
        builder.Services.AddSingleton<ElementLocating>();
        builder.Services.AddSingleton<WinAppDriverService>();
        builder.Services.AddSingleton<CaptureCoordinator>();
        builder.Services.AddSingleton<StepRecorder>();
        builder.Services.AddSingleton<AssertController>();
        builder.Services.AddSingleton<RecordingCoordinator>();
        builder.Services.AddSingleton<RecordingFiles>();
        builder.Services.AddSingleton<Autosaver>();
        builder.Services.AddSingleton<CodeProfiles>();
        builder.Services.AddSingleton<CodeFiles>();
        builder.Services.AddSingleton<SettingsDialog>();
        builder.Services.AddSingleton<Shortcuts>();
        builder.Services.AddSingleton<ThemeService>();
        builder.Services.AddSingleton<GlobalExceptionHandler>();
        builder.Services.AddSingleton<FirstRunViewModel>();
        builder.Services.AddSingleton<TargetSelectorViewModel>();
        builder.Services.AddSingleton<ElementsViewModel>();
        builder.Services.AddSingleton<LocatorsViewModel>();
        builder.Services.AddSingleton<InspectorViewModel>();
        builder.Services.AddSingleton<StepsViewModel>();
        builder.Services.AddSingleton<RecordingViewModel>();
        builder.Services.AddSingleton<CodeViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddSingleton<CheatSheetViewModel>();
        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        return builder.Build();
    }

    /// <summary>Detailed logs (spec 12, Advanced) switch the file log to Debug: timings and statuses only (AGENTS.md).</summary>
    public static void FollowLogSetting(IServiceProvider services)
    {
        var settings = services.GetRequiredService<SettingsStore>();
        var levelSwitch = services.GetRequiredService<LoggingLevelSwitch>();
        void Apply(AppSettings current) => levelSwitch.MinimumLevel = current.DetailedLogs ? LogEventLevel.Debug : LogEventLevel.Information;
        Apply(settings.Current);
        settings.Changed += (_, current) => Apply(current);
    }

    private static Serilog.Core.Logger CreateLogger(LogOptions options, LoggingLevelSwitch levelSwitch) =>
        new LoggerConfiguration()
            .MinimumLevel.ControlledBy(levelSwitch)
            .Enrich.FromLogContext()
            .WriteTo.File(
                options.FilePathPattern,
                formatProvider: CultureInfo.InvariantCulture,
                outputTemplate: options.OutputTemplate,
                fileSizeLimitBytes: options.FileSizeLimitBytes,
                rollingInterval: RollingInterval.Day,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: null,
                retainedFileTimeLimit: options.RetainFor)
            .CreateLogger();
}
