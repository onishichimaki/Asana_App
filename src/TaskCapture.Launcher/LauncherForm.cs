using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace TaskCapture.Launcher;

internal sealed class LauncherForm : Form
{
    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    private readonly Uri _webUri;
    private bool _initialized;
    private string? _pendingClipboardText;

    public bool AllowExit { get; set; }

    public LauncherForm(string webUrl)
    {
        if (!Uri.TryCreate(webUrl, UriKind.Absolute, out var uri) || (uri.Scheme is not "http" and not "https"))
        {
            throw new InvalidOperationException("TASK_CAPTURE_WEB_URL must be an absolute http or https URL.");
        }

        _webUri = uri;
        Text = "Task Capture";
        Width = 520;
        Height = 620;
        MinimumSize = new Size(400, 500);
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        TopMost = true;
        FormBorderStyle = FormBorderStyle.Sizable;
        ControlBox = true;
        MinimizeBox = true;
        MaximizeBox = false;
        ShowIcon = false;
        Controls.Add(_webView);
        FormClosing += OnFormClosing;
    }

    public async Task ShowCaptureAsync(string? clipboardText)
    {
        _pendingClipboardText = string.IsNullOrWhiteSpace(clipboardText) ? null : clipboardText[..Math.Min(clipboardText.Length, 10_000)];
        if (!Visible) Show();
        WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();

        await EnsureWebViewAsync();
        var builder = new UriBuilder(_webUri) { Query = $"launcher=1&session={Guid.NewGuid():N}" };
        _webView.CoreWebView2.Navigate(builder.Uri.AbsoluteUri);
    }

    private async Task EnsureWebViewAsync()
    {
        if (_initialized) return;

        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskCapture",
            "WebView2");
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
        await _webView.EnsureCoreWebView2Async(environment);
        _webView.CoreWebView2.Settings.AreDevToolsEnabled =
            Environment.GetEnvironmentVariable("TASK_CAPTURE_LAUNCHER_DEVTOOLS") == "1";
        _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        _initialized = true;
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        try
        {
            using var document = JsonDocument.Parse(eventArgs.WebMessageAsJson);
            var type = document.RootElement.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
            if (type == "web-ready")
            {
                SendClipboardToWeb();
            }
            else if (type == "registration-complete")
            {
                Hide();
            }
        }
        catch (JsonException)
        {
            // Ignore messages that are not part of the small launcher bridge contract.
        }
    }

    private void SendClipboardToWeb()
    {
        if (string.IsNullOrWhiteSpace(_pendingClipboardText)) return;
        _webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "clipboard",
            text = _pendingClipboardText
        }));
        _pendingClipboardText = null;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (AllowExit) return;
        eventArgs.Cancel = true;
        Hide();
    }
}
