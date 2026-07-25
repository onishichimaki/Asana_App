namespace TaskCapture.Launcher;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var singleInstance = new Mutex(
            initiallyOwned: true,
            "Local\\TaskCapture.Launcher.Singleton",
            out var ownsMutex);
        if (!ownsMutex) return;

        ApplicationConfiguration.Initialize();
        var background = args.Contains("--background", StringComparer.OrdinalIgnoreCase);
        var clipboard = args.Contains("--clipboard", StringComparer.OrdinalIgnoreCase);
        Application.Run(new LauncherApplicationContext(!background || clipboard, clipboard));
    }
}
