using Microsoft.Win32;

namespace Recorder.IntegrationTests.Infrastructure;

internal static class WebView2Runtime
{
    private const string ClientKey = @"Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

    public static bool IsInstalled()
    {
        using var machine = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\" + ClientKey);
        using var user = Registry.CurrentUser.OpenSubKey(@"Software\" + ClientKey);
        return machine?.GetValue("pv") is string || user?.GetValue("pv") is string;
    }
}
