using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SevenZipLite;
using SevenZipLite.SelfTest;

namespace SevenZipLite.AvaloniaAndroidDemo.Views;

public partial class MainView : UserControl
{
    private string? _pickedRealFilePath;

    public MainView()
    {
        InitializeComponent();

        // 先读出上一次（很可能是以硬崩溃告终的那一次）留下的记录给用户看，再开始本次运行的记录。
        PreviousRunText.Text = DiagnosticLog.ReadPrevious();
        DiagnosticLog.StartNewRun();

        RefreshEnvInfo();
    }

    // ---------------- 上次运行记录 ----------------

    private void OnClearPreviousRunClicked(object? sender, RoutedEventArgs e)
    {
        DiagnosticLog.Clear();
        DiagnosticLog.StartNewRun();
        PreviousRunText.Text = "（已清空）";
    }

    // ---------------- 0) 环境诊断 ----------------

    private void OnRefreshEnvClicked(object? sender, RoutedEventArgs e) => RefreshEnvInfo();

    private void RefreshEnvInfo()
    {
        DiagnosticLog.Mark("[环境诊断] 开始");
        var sb = new StringBuilder();
        sb.AppendLine($".NET 版本: {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"系统: {RuntimeInformation.OSDescription}");
        sb.AppendLine($"OS 架构: {RuntimeInformation.OSArchitecture}　　进程架构: {RuntimeInformation.ProcessArchitecture}");

        // 经典的 Mono 探测手法：这个类型只在 Mono 运行时下存在。
        bool monoTypePresent = Type.GetType("Mono.Runtime") is not null;
        sb.AppendLine($"Type.GetType(\"Mono.Runtime\") 探测: {(monoTypePresent ? "存在 → 很可能是 Mono 运行时" : "不存在")}");

        // 扫描本进程已加载的原生库列表（/proc/self/maps 是 Linux/Android 都有的机制；
        // 桌面 Windows 上没有这个文件，读取失败是预期行为，不代表出错）。
        try
        {
            string maps = File.ReadAllText("/proc/self/maps");
            bool hasCoreClr = maps.Contains("libcoreclr.so", StringComparison.Ordinal);
            bool hasMonoSgen = maps.Contains("libmonosgen", StringComparison.Ordinal);
            sb.AppendLine($"/proc/self/maps 扫描: libcoreclr.so 已加载={hasCoreClr}；libmonosgen* 已加载={hasMonoSgen}");
            if (hasCoreClr && !hasMonoSgen)
                sb.AppendLine("=> 判断：当前很可能正运行在 CoreCLR 上（ComWrappers 应该可用）。");
            else if (hasMonoSgen && !hasCoreClr)
                sb.AppendLine("=> 判断：当前很可能正运行在 Mono 上 —— 调用 SevenZipLite 预期会抛 PlatformNotSupportedException。" +
                               "需要在 Android 宿主项目 .csproj 里设置 UseMonoRuntime=false（且 TargetFramework 为 net10.0-android 及以上）。");
            else
                sb.AppendLine("=> 判断：结果不明确（两者都有/都没有探测到，可能是当前平台这套判断逻辑不适用）。");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"/proc/self/maps 读取失败（在桌面 Windows 上这是正常的，不代表有问题）：{ex.Message}");
        }

        EnvInfoText.Text = sb.ToString();
        DiagnosticLog.Mark("[环境诊断] 完成: " + sb.ToString().Replace(Environment.NewLine, " | "));
    }

    // ---------------- 1) CreateArchive ----------------

    private async void OnTestCreateArchiveClicked(object? sender, RoutedEventArgs e)
    {
        TestCreateArchiveButton.IsEnabled = false;
        TestCreateArchiveResult.Text = "运行中…";
        DiagnosticLog.Mark("[1-CreateArchive] 开始");
        try
        {
            string workDir = Path.Combine(Path.GetTempPath(), "szl_demo_1_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDir);
            string srcFile = Path.Combine(workDir, "hello.txt");
            await File.WriteAllTextAsync(srcFile, "hello from SevenZipLite diagnostic tool");
            DiagnosticLog.Mark("[1-CreateArchive] 源文件已就绪，即将调用 SevenZipCompressor.CreateArchive");

            string archivePath = Path.Combine(workDir, "out.7z");
            var entries = new List<SevenZipCompressor.Entry>
            {
                new() { DiskPath = srcFile, ArchivePath = "hello.txt" }
            };

            var volumes = await Task.Run(() => SevenZipCompressor.CreateArchive(entries, archivePath, ArchiveFormat.SevenZip));

            DiagnosticLog.Mark("[1-CreateArchive] 调用返回，未抛出异常");
            TestCreateArchiveResult.Text =
                $"✅ 成功：生成 {volumes.Count} 个文件，大小 {new FileInfo(volumes[0]).Length} 字节\n路径：{volumes[0]}";

            try { Directory.Delete(workDir, recursive: true); } catch { /* 忽略清理失败 */ }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Mark("[1-CreateArchive] 抛出异常: " + ex.GetType().FullName + ": " + ex.Message);
            TestCreateArchiveResult.Text = "❌ 抛出异常：\n" + ex;
        }
        finally
        {
            TestCreateArchiveButton.IsEnabled = true;
        }
    }

    // ---------------- 2) CreateArchiveFromDirectory（同步） ----------------

    private async void OnTestCreateFromDirClicked(object? sender, RoutedEventArgs e)
    {
        TestCreateFromDirButton.IsEnabled = false;
        TestCreateFromDirResult.Text = "运行中…";
        DiagnosticLog.Mark("[2-CreateArchiveFromDirectory] 开始");
        string workDir = Path.Combine(Path.GetTempPath(), "szl_demo_2_" + Guid.NewGuid().ToString("N"));
        try
        {
            string srcDir = Path.Combine(workDir, "src");
            Directory.CreateDirectory(srcDir);
            await File.WriteAllTextAsync(Path.Combine(srcDir, "a.txt"), "aaaa");
            await File.WriteAllTextAsync(Path.Combine(srcDir, "b.txt"), "bbbb");
            DiagnosticLog.Mark("[2-CreateArchiveFromDirectory] 源目录已就绪，即将调用（同步）");

            string archivePath = Path.Combine(workDir, "out.zip");
            var volumes = await Task.Run(() =>
                SevenZipCompressor.CreateArchiveFromDirectory(srcDir, archivePath, ArchiveFormat.Zip));

            DiagnosticLog.Mark("[2-CreateArchiveFromDirectory] 调用返回，未抛出异常");
            TestCreateFromDirResult.Text =
                $"✅ 成功：生成 {volumes.Count} 个文件，大小 {new FileInfo(volumes[0]).Length} 字节";
        }
        catch (Exception ex)
        {
            DiagnosticLog.Mark("[2-CreateArchiveFromDirectory] 抛出异常: " + ex.GetType().FullName + ": " + ex.Message);
            TestCreateFromDirResult.Text = "❌ 抛出异常：\n" + ex;
        }
        finally
        {
            try { if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true); } catch { }
            TestCreateFromDirButton.IsEnabled = true;
        }
    }

    // ---------------- 3) CreateArchiveFromDirectoryAsync（异步，复刻最初报错的调用路径） ----------------

    private async void OnTestCreateFromDirAsyncClicked(object? sender, RoutedEventArgs e)
    {
        TestCreateFromDirAsyncButton.IsEnabled = false;
        TestCreateFromDirAsyncResult.Text = "运行中…";
        DiagnosticLog.Mark("[3-CreateArchiveFromDirectoryAsync] 开始");
        string workDir = Path.Combine(Path.GetTempPath(), "szl_demo_3_" + Guid.NewGuid().ToString("N"));
        try
        {
            string srcDir = Path.Combine(workDir, "src");
            Directory.CreateDirectory(srcDir);
            await File.WriteAllTextAsync(Path.Combine(srcDir, "a.txt"), "aaaa");
            DiagnosticLog.Mark("[3-CreateArchiveFromDirectoryAsync] 源目录已就绪，即将调用 CreateArchiveFromDirectoryAsync");

            string archivePath = Path.Combine(workDir, "out.7z");
            var volumes = await SevenZipCompressor.CreateArchiveFromDirectoryAsync(srcDir, archivePath, ArchiveFormat.SevenZip);

            DiagnosticLog.Mark("[3-CreateArchiveFromDirectoryAsync] 调用返回，未抛出异常");
            TestCreateFromDirAsyncResult.Text =
                $"✅ 成功：生成 {volumes.Count} 个文件，大小 {new FileInfo(volumes[0]).Length} 字节";
        }
        catch (Exception ex)
        {
            DiagnosticLog.Mark("[3-CreateArchiveFromDirectoryAsync] 抛出异常: " + ex.GetType().FullName + ": " + ex.Message);
            TestCreateFromDirAsyncResult.Text = "❌ 抛出异常：\n" + ex;
        }
        finally
        {
            try { if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true); } catch { }
            TestCreateFromDirAsyncButton.IsEnabled = true;
        }
    }

    // ---------------- 4) 完整自检 ----------------

    private async void OnRunSelfTestClicked(object? sender, RoutedEventArgs e)
    {
        RunSelfTestButton.IsEnabled = false;
        SelfTestResult.Text = "运行中…（可能需要几十秒）";
        DiagnosticLog.Mark("[4-SelfTestRunner] 开始");

        string root = Path.Combine(Path.GetTempPath(), "szl_demo_selftest_" + Guid.NewGuid().ToString("N"));
        try
        {
            var results = await Task.Run(() => SelfTestRunner.RunAll(root));

            int pass = results.Count(r => r.Pass);
            int fail = results.Count - pass;

            var log = new StringBuilder();
            log.AppendLine($"结果: {pass} 通过, {fail} 失败");
            foreach (var r in results)
            {
                if (!r.Pass)
                {
                    log.AppendLine($"[FAIL] {r.Name}  {r.Detail}");
                    foreach (var line in r.Log) log.AppendLine($"    -> {line}");
                }
            }
            if (fail == 0) log.AppendLine("（全部通过，详细日志省略；如需逐条查看可以告诉我再加回来。）");

            DiagnosticLog.Mark($"[4-SelfTestRunner] 完成: {pass} 通过, {fail} 失败");
            SelfTestResult.Text = log.ToString();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Mark("[4-SelfTestRunner] 运行本身抛出异常: " + ex.GetType().FullName + ": " + ex.Message);
            SelfTestResult.Text = "❌ 自检运行本身抛出异常（说明原生库加载/互操作层有问题）：\n" + ex;
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
            RunSelfTestButton.IsEnabled = true;
        }
    }

    // ---------------- 5) 用真实文件测试 ----------------

    private async void OnPickRealFileClicked(object? sender, RoutedEventArgs e)
    {
        DiagnosticLog.Mark("[5-选择真实文件] 开始");
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
        {
            PickedFilePathText.Text = "无法获取 StorageProvider（当前宿主环境不支持）。";
            return;
        }

        try
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择一个真实文件用于测试",
                AllowMultiple = false
            });

            if (files.Count == 0)
            {
                PickedFilePathText.Text = "（未选择文件）";
                return;
            }

            var file = files[0];
            _pickedRealFilePath = file.TryGetLocalPath();

            var sb = new StringBuilder();
            sb.AppendLine($"Avalonia 内部 Uri: {file.Path}");
            sb.AppendLine($"文件名: {file.Name}");
            sb.AppendLine($"TryGetLocalPath() 结果: {_pickedRealFilePath ?? "(null)"}");
            if (_pickedRealFilePath is null)
            {
                sb.AppendLine();
                sb.AppendLine("⚠️ 这是一个 content:// 风格的虚拟路径，TryGetLocalPath() 拿不到真实文件系统路径。");
                sb.AppendLine("SevenZipCompressor.Entry.DiskPath 要求的是能被 File.OpenRead 直接打开的真实路径——");
                sb.AppendLine("如果你自己项目里是直接把文件选择器返回的路径/Uri 字符串传给 DiskPath，这里大概率");
                sb.AppendLine("就是真正的崩溃原因（和 ComWrappers/运行时完全无关）。正确做法是先用");
                sb.AppendLine("file.OpenReadAsync() 把内容拷贝到一个真实的临时文件，再把那个临时文件路径传给 DiskPath。");
            }

            PickedFilePathText.Text = sb.ToString();
            DiagnosticLog.Mark("[5-选择真实文件] 结果: " + sb.ToString().Replace(Environment.NewLine, " | "));
            TestPickedFileButton.IsEnabled = _pickedRealFilePath is not null;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Mark("[5-选择真实文件] 抛出异常: " + ex);
            PickedFilePathText.Text = "❌ 选择文件时抛出异常：\n" + ex;
        }
    }

    private async void OnTestPickedFileClicked(object? sender, RoutedEventArgs e)
    {
        if (_pickedRealFilePath is null) return;

        TestPickedFileButton.IsEnabled = false;
        TestPickedFileResult.Text = "运行中…";
        DiagnosticLog.Mark("[5-用真实文件测试压缩] 开始: " + _pickedRealFilePath);
        try
        {
            string workDir = Path.Combine(Path.GetTempPath(), "szl_demo_5_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDir);
            string archivePath = Path.Combine(workDir, "out.7z");

            var entries = new List<SevenZipCompressor.Entry>
            {
                new() { DiskPath = _pickedRealFilePath, ArchivePath = Path.GetFileName(_pickedRealFilePath) }
            };

            var volumes = await Task.Run(() => SevenZipCompressor.CreateArchive(entries, archivePath, ArchiveFormat.SevenZip));

            DiagnosticLog.Mark("[5-用真实文件测试压缩] 调用返回，未抛出异常");
            TestPickedFileResult.Text =
                $"✅ 成功：生成 {volumes.Count} 个文件，大小 {new FileInfo(volumes[0]).Length} 字节";

            try { Directory.Delete(workDir, recursive: true); } catch { }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Mark("[5-用真实文件测试压缩] 抛出异常: " + ex.GetType().FullName + ": " + ex.Message);
            TestPickedFileResult.Text = "❌ 抛出异常：\n" + ex;
        }
        finally
        {
            TestPickedFileButton.IsEnabled = true;
        }
    }
}
