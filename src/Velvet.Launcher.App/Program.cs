using Avalonia;
using Velvet.Launcher.Infrastructure;

namespace Velvet.Launcher.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        StartupRegistration.Ensure();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
