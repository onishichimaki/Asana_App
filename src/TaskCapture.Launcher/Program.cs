namespace TaskCapture.Launcher;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var background = args.Contains("--background", StringComparer.OrdinalIgnoreCase);
        var clipboard = args.Contains("--clipboard", StringComparer.OrdinalIgnoreCase);
        Application.Run(new LauncherApplicationContext(!background || clipboard, clipboard));
    }
}
