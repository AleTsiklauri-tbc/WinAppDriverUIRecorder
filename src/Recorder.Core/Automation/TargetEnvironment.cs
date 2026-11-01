using Recorder.Core.Interop;
using Recorder.Core.Model;

namespace Recorder.Core.Automation;

/// <summary>The operating-system facts the target tracker needs, behind a seam for tests.</summary>
public interface ITargetEnvironment
{
    bool IsRecorderElevated { get; }

    IReadOnlyList<AppWindow> GetAppWindows();

    ElevationState ElevationOf(int processId);

    /// <summary>Starts watching a process; the watch reports its exit even if the id is reused later.</summary>
    IProcessWatch Watch(int processId);

    int ProcessAtPoint(PixelPoint point);

    int ProcessOfWindow(nint windowHandle);
}

public interface IProcessWatch : IDisposable
{
    bool HasExited { get; }
}

public sealed class SystemTargetEnvironment : ITargetEnvironment
{
    public bool IsRecorderElevated => Environment.IsPrivilegedProcess;

    public IReadOnlyList<AppWindow> GetAppWindows() => WindowEnumerator.GetAppWindows();

    public ElevationState ElevationOf(int processId) => Win32.ElevationOf(processId);

    public IProcessWatch Watch(int processId) => new ProcessWatch(processId);

    public int ProcessAtPoint(PixelPoint point) => Win32.ProcessAtPoint(point);

    public int ProcessOfWindow(nint windowHandle) => Win32.ProcessOfWindow(windowHandle);

    // An open process handle keeps the process id from being reused, so "exited" is never a false negative.
    private sealed class ProcessWatch : IProcessWatch
    {
        private readonly int _processId;
        private nint _handle;

        public ProcessWatch(int processId)
        {
            _processId = processId;
            _handle = NativeMethods.OpenProcess(NativeMethods.SYNCHRONIZE | NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)processId);
        }

        public bool HasExited => _handle != 0
            ? NativeMethods.WaitForSingleObject(_handle, 0) == NativeMethods.WAIT_OBJECT_0
            : WindowEnumerator.ProcessName(_processId) is null;

        public void Dispose()
        {
            if (_handle != 0)
            {
                NativeMethods.CloseHandle(_handle);
                _handle = 0;
            }
        }
    }
}
