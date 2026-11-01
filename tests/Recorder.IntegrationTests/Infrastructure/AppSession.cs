using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.UIA3;

namespace Recorder.IntegrationTests.Infrastructure;

/// <summary>A launched application plus the UIA3 automation used to drive it. Disposing closes it.</summary>
internal sealed class AppSession : IDisposable
{
    private readonly Process _process;

    private AppSession(Process process, Application application, UIA3Automation automation, Window mainWindow)
    {
        _process = process;
        Application = application;
        Automation = automation;
        MainWindow = mainWindow;
    }

    public Application Application { get; }

    public UIA3Automation Automation { get; }

    public Window MainWindow { get; }

    public ConditionFactory Find => Automation.ConditionFactory;

    public int ProcessId => _process.Id;

    public static AppSession Launch(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        startInfo.UseShellExecute = false;

        // Started here (not by FlaUI) so this object owns the process and can read its exit code.
        // FlaUI gets only the id: it disposes Process objects handed to Attach.
        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start " + startInfo.FileName);
        var automation = new UIA3Automation();
        var application = Application.Attach(process.Id);
        try
        {
            var window = application.GetMainWindow(automation, TimeSpan.FromSeconds(30))
                ?? throw new TimeoutException("The main window did not appear: " + startInfo.FileName);
            return new AppSession(process, application, automation, window);
        }
        catch
        {
            KillQuietly(process);
            application.Dispose();
            automation.Dispose();
            process.Dispose();
            throw;
        }
    }

    /// <summary>Closes the main window and waits for a clean exit. Returns the exit code.</summary>
    public int CloseAndWaitForExit(TimeSpan timeout)
    {
        Application.Close();
        if (!_process.WaitForExit(timeout))
        {
            throw new TimeoutException("The application did not exit after its main window was closed.");
        }

        return _process.ExitCode;
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                Application.Close();
                _process.WaitForExit(TimeSpan.FromSeconds(5));
            }
        }
        finally
        {
            // A modal window can block WM_CLOSE; make sure nothing is left running.
            KillQuietly(_process);
            Application.Dispose();
            Automation.Dispose();
            _process.Dispose();
        }
    }

    private static void KillQuietly(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(TimeSpan.FromSeconds(5));
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
    }
}
