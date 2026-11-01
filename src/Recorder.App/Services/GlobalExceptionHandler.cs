using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Recorder.App.Hosting;
using Recorder.App.Resources;
using Recorder.Core.Diagnostics;

namespace Recorder.App.Services;

/// <summary>
/// Last line of defence: log the exception, show a plain message with "Copy details", keep running.
/// Stack traces go to the log and the clipboard, never onto the screen.
/// </summary>
public sealed partial class GlobalExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly ToastService _toasts;
    private readonly string _version;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, ToastService toasts)
    {
        _logger = logger;
        _toasts = toasts;
        _version = AppLog.AppVersion;
    }

    public void Attach(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogUnhandled(_logger, "UI thread", e.Exception);
        e.Handled = true;
        Report(e.Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogUnhandled(_logger, "background task", e.Exception);
        e.SetObserved();
        Report(e.Exception);
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            LogFatal(_logger, exception);
        }
    }

    private void Report(Exception exception) =>
        _toasts.ShowError(ErrorReport.Create(exception, Strings.Error_Unexpected, DateTimeOffset.Now, _version));

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Unhandled exception on the {Source}")]
    private static partial void LogUnhandled(ILogger logger, string source, Exception exception);

    [LoggerMessage(EventId = 2, Level = LogLevel.Critical, Message = "Fatal unhandled exception; the process is terminating")]
    private static partial void LogFatal(ILogger logger, Exception exception);
}
