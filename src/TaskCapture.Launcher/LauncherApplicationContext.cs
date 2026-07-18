using System.Runtime.InteropServices;

namespace TaskCapture.Launcher;

internal sealed class LauncherApplicationContext : ApplicationContext
{
    private const int HotKeyId = 0x5443;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint VirtualKeyA = 0x41;

    private readonly NotifyIcon _trayIcon;
    private readonly LauncherForm _window;
    private readonly HotKeyWindow _hotKeyWindow;

    public LauncherApplicationContext()
    {
        var webUrl = Environment.GetEnvironmentVariable("TASK_CAPTURE_WEB_URL") ?? "http://localhost:5080";
        _window = new LauncherForm(webUrl);

        var menu = new ContextMenuStrip();
        menu.Items.Add("入力画面を開く", null, (_, _) => ShowCapture(useClipboard: false));
        menu.Items.Add("クリップボードから開く", null, (_, _) => ShowCapture(useClipboard: true));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => ExitApplication());

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Task Capture (Ctrl+Shift+A)",
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ShowCapture(useClipboard: false);

        _hotKeyWindow = new HotKeyWindow(() => ShowCapture(useClipboard: true));
        if (!RegisterHotKey(_hotKeyWindow.Handle, HotKeyId, ModControl | ModShift, VirtualKeyA))
        {
            _trayIcon.BalloonTipTitle = "Task Capture";
            _trayIcon.BalloonTipText = "Ctrl+Shift+A を登録できませんでした。tray メニューから起動できます。";
            _trayIcon.ShowBalloonTip(4_000);
        }
    }

    private async void ShowCapture(bool useClipboard)
    {
        string? clipboardText = null;
        if (useClipboard && Clipboard.ContainsText(TextDataFormat.UnicodeText))
        {
            clipboardText = Clipboard.GetText(TextDataFormat.UnicodeText);
        }

        try
        {
            await _window.ShowCaptureAsync(clipboardText);
        }
        catch (Exception ex)
        {
            _trayIcon.BalloonTipTitle = "Task Capture を開けません";
            _trayIcon.BalloonTipText = ex.Message.Length > 200 ? ex.Message[..200] : ex.Message;
            _trayIcon.ShowBalloonTip(5_000);
        }
    }

    private void ExitApplication()
    {
        UnregisterHotKey(_hotKeyWindow.Handle, HotKeyId);
        _hotKeyWindow.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _window.AllowExit = true;
        _window.Close();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon.Dispose();
            _window.Dispose();
            _hotKeyWindow.Dispose();
        }
        base.Dispose(disposing);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private sealed class HotKeyWindow : NativeWindow, IDisposable
    {
        private const int WmHotKey = 0x0312;
        private readonly Action _onHotKey;

        public HotKeyWindow(Action onHotKey)
        {
            _onHotKey = onHotKey;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmHotKey) _onHotKey();
            base.WndProc(ref message);
        }

        public void Dispose() => DestroyHandle();
    }
}
