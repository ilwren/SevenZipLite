using System;
using System.IO;
using System.Threading.Tasks;

namespace SevenZipLite.AvaloniaAndroidDemo;

/// <summary>
/// 一个极简的"面包屑"持久化日志：每做一步关键操作之前/之后都落盘一行，文件在下次启动前不清空。
/// <para>
/// 存在的意义：如果应用是被一个<b>完全没被任何 try/catch 接住的硬崩溃</b>干掉的（比如真的
/// 是原生层面的崩溃，而不是一个规规矩矩的托管异常），那么 UI 上显示异常信息这条路就完全走不通，
/// 唯一能知道"死在哪一步"的办法就是提前把进度写到磁盘上，下次启动时读出来看——这正是用户反馈
/// "崩溃闪退、日志太杂定位不到"这个场景最需要的东西：不用再去翻夹杂了大量系统噪音的 adb logcat，
/// 直接在 App 里看上一次具体卡在哪一步。
/// </para>
/// </summary>
internal static class DiagnosticLog
{
    private static string _logFilePath = string.Empty;
    private static readonly object Gate = new();

    public static void Init()
    {
        try
        {
            string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(baseDir)) baseDir = Path.GetTempPath();
            string dir = Path.Combine(baseDir, "SevenZipLiteAvaloniaAndroidDemo");
            Directory.CreateDirectory(dir);
            _logFilePath = Path.Combine(dir, "last_run_breadcrumb.log");
        }
        catch
        {
            // 诊断基础设施本身绝不能把 App 搞挂——实在连目录都建不出来就退化成"不记录"。
            _logFilePath = string.Empty;
        }
    }

    /// <summary>读取上一次启动遗留下来的记录（在 <see cref="StartNewRun"/> 清空之前调用）。</summary>
    public static string ReadPrevious()
    {
        try
        {
            return !string.IsNullOrEmpty(_logFilePath) && File.Exists(_logFilePath)
                ? File.ReadAllText(_logFilePath)
                : "（没有找到上一次运行的记录——可能是第一次启动，也可能是上次正常退出前主动清空过。）";
        }
        catch (Exception ex)
        {
            return $"（读取上一次记录失败：{ex.Message}）";
        }
    }

    public static void StartNewRun()
    {
        Mark($"=== 新的一次启动 {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===", overwrite: true);
    }

    public static void Mark(string message, bool overwrite = false)
    {
        try
        {
            if (string.IsNullOrEmpty(_logFilePath)) return;
            lock (Gate)
            {
                string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}";
                if (overwrite) File.WriteAllText(_logFilePath, line);
                else File.AppendAllText(_logFilePath, line);
            }
        }
        catch
        {
            // 同上：记录失败本身不能再抛出新异常。
        }
    }

    public static void Clear()
    {
        try
        {
            if (!string.IsNullOrEmpty(_logFilePath) && File.Exists(_logFilePath)) File.Delete(_logFilePath);
        }
        catch
        {
            // 忽略
        }
    }

    /// <summary>
    /// 挂一层全局兜底：正常情况下本工具每个按钮点击都已经自己 try/catch 了，走不到这里；
    /// 这两个钩子是防御性的最后一道网，用来捕获"连按钮处理函数的 try/catch 都没盖住"的情况
    /// （比如 Avalonia 渲染线程自己抛的异常、或者某个未被 Task.Run 正确包裹的后台异常）。
    /// </summary>
    public static void InstallGlobalExceptionHooks()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Mark("!!! AppDomain.UnhandledException (IsTerminating=" + args.IsTerminating + "): " + args.ExceptionObject);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Mark("!!! TaskScheduler.UnobservedTaskException: " + args.Exception);
            args.SetObserved();
        };
    }
}
