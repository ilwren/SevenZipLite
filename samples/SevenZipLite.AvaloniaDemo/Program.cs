using Avalonia;

namespace SevenZipLite.AvaloniaDemo;

internal static class Program
{
    // Avalonia 应用的入口点跟 WPF 不一样：WPF 靠 App.xaml 的 StartupUri 隐式启动，
    // Avalonia 需要显式构建 AppBuilder 并调用 StartWithClassicDesktopLifetime——这是
    // Avalonia 官方桌面应用模板的标准写法，不是本项目的特殊处理。
    //
    // Avalonia's entry point differs from WPF: WPF starts implicitly via App.xaml's
    // StartupUri, while Avalonia requires explicitly building an AppBuilder and calling
    // StartWithClassicDesktopLifetime — this is the standard pattern from Avalonia's
    // official desktop app template, not anything specific to this project.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
