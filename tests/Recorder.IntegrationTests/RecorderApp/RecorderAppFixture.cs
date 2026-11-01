using Recorder.IntegrationTests.Infrastructure;

namespace Recorder.IntegrationTests.RecorderApp;

/// <summary>One recorder instance with a throwaway data folder, shared by a test class.</summary>
public sealed class RecorderAppFixture : IDisposable
{
    public RecorderAppFixture()
    {
        DataHome = RecorderLauncher.NewDataHome();
        Session = RecorderLauncher.Launch(DataHome);
    }

    internal string DataHome { get; }

    internal AppSession Session { get; }

    public void Dispose()
    {
        Session.Dispose();
        try
        {
            Directory.Delete(DataHome, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: the temp folder is cleaned up by the OS eventually.
        }
    }
}
