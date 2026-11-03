using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Recorder.App.Resources;
using Recorder.App.ViewModels;
using Recorder.App.Views;

namespace Recorder.App.Services;

/// <summary>The sections of the Settings window, in spec 12's order.</summary>
public enum SettingsSection
{
    General,
    Shortcuts,
    Recording,
    Code,
    WinAppDriver,
    Advanced,
}

/// <summary>
/// Opens the Settings window (spec 12) at a section. Never while recording: the window is modal (spec 11.1). UI thread only.
/// </summary>
/// <remarks>Resolves what it shows when asked, so that services which offer "Settings" (banners) can depend on it.</remarks>
public sealed class SettingsDialog(IServiceProvider services)
{
    public void Show(SettingsSection section = SettingsSection.General)
    {
        if (services.GetRequiredService<RecordingCoordinator>().IsActive)
        {
            services.GetRequiredService<ToastService>().Show(Strings.Toast_SettingsWhileRecording);
            return;
        }

        var viewModel = services.GetRequiredService<SettingsViewModel>();
        viewModel.Section = section;
        try
        {
            new SettingsWindow { Owner = Application.Current?.MainWindow, DataContext = viewModel }.ShowDialog();
        }
        finally
        {
            viewModel.Detach();
        }
    }
}
