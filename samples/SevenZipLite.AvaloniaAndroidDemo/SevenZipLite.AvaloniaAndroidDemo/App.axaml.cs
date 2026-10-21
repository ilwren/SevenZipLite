using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SevenZipLite.AvaloniaAndroidDemo.Views;

namespace SevenZipLite.AvaloniaAndroidDemo;

public partial class App : Application
{
    public override void Initialize()
    {
        // 必须在加载任何 XAML/做任何其他事情之前第一时间初始化诊断日志 + 挂好全局兜底
        // 异常钩子：如果真的发生了"连 try/catch 都没来得及接住就整个进程死掉"的硬崩溃
        // （可能是 Avalonia 自身渲染层的问题，也可能是某个没被我们覆盖到的调用路径），
        // 这里好歹能在死之前把异常内容落盘，下次启动时 MainView 能读出来显示给你看，
        // 不用非得去翻连着一堆系统噪音的 adb logcat。
        DiagnosticLog.Init();
        DiagnosticLog.InstallGlobalExceptionHooks();
        DiagnosticLog.Mark("App.Initialize() 开始");

        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        DiagnosticLog.Mark("App.OnFrameworkInitializationCompleted() 开始，ApplicationLifetime = "
            + ApplicationLifetime?.GetType().FullName);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 桌面端（本地跑一遍先做"是不是 Avalonia/SevenZipLite 本身的问题"的快速排除，
            // 不用等真机/模拟器）用一个最朴素的 Window 直接包住共享的 MainView，不单独
            // 建 MainWindow.axaml 文件，减少一份要维护的 XAML。
            desktop.MainWindow = new Window
            {
                Title = "SevenZipLite Avalonia 崩溃复现工具（桌面端）",
                Width = 900,
                Height = 800,
                Content = new MainView()
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            // Android（以及其他"单视图"宿主，比如浏览器）走这条分支。
            singleViewPlatform.MainView = new MainView();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
