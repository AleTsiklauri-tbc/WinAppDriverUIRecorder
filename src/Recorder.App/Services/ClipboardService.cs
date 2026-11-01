using System.Runtime.InteropServices;
using System.Windows;

namespace Recorder.App.Services;

public sealed class ClipboardService : IClipboardService
{
    public bool TrySetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        try
        {
            Clipboard.SetDataObject(text, copy: true);
            return true;
        }
        catch (ExternalException)
        {
            return false;
        }
    }
}
