using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Recorder.SampleApp;

/// <summary>
/// WebView2-hosted form with Angular/Kendo-style markup, like LMS insurance forms.
/// Its DOM is served by msedgewebview2.exe, not by this process. The page is local; nothing is downloaded.
/// </summary>
internal sealed class WebForm : Form
{
    private readonly WebView2 _webView = new() { Name = "InsuranceWebForm", Dock = DockStyle.Fill };

    public WebForm()
    {
        Name = "frmWeb";
        Text = "Web form";
        ClientSize = new Size(800, 500);
        Controls.Add(_webView);
        Load += OnLoad;
    }

    private async void OnLoad(object? sender, EventArgs e)
    {
        try
        {
            var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Recorder.SampleApp", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(null, userData);
            await _webView.EnsureCoreWebView2Async(environment);
            _webView.CoreWebView2.NavigateToString(LoadPage());
        }
        catch (WebView2RuntimeNotFoundException)
        {
            Controls.Remove(_webView);
            Controls.Add(new Label { Name = "lblNoRuntime", Text = "WebView2 runtime is not installed.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter });
        }
    }

    private static string LoadPage()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Recorder.SampleApp.insurance.html")
            ?? throw new InvalidOperationException("Embedded page is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
