namespace Recorder.App.Services;

public interface IClipboardService
{
    /// <summary>Returns false instead of throwing when another process holds the clipboard.</summary>
    bool TrySetText(string text);
}
