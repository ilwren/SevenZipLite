using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SevenZipLite;
using SevenZipLite.SelfTest;

namespace SevenZipLite.AvaloniaDemo;

public partial class MainWindow : Window
{
    private readonly List<string> _pickedFiles = new();
    private string? _lastArchivePath;
    private IReadOnlyList<string>? _lastVolumePaths;
    private ArchiveFormat _lastFormat;
    private string? _lastPassword;

    public MainWindow()
    {
        InitializeComponent();

        // 显示当前进程/RID/OS 信息，方便确认现在实际加载/验证的是哪一份原生库
        // （7zlite.dll 还是 lib7zlite.so，win-x64/linux-x64 中的哪一个；这里只是如实
        // 显示运行时探测到的 RuntimeIdentifier，不是只允许这两个值——如果有人用
        // `dotnet run -r win-x86` 之类方式在别的 RID 下跑起来，这里也会如实显示）——
        // 跟旧 WPF 版本的用途完全一样，只是不再局限于"win-x64 / win-x86"两个选项，
        // 因为 Avalonia 版本同时覆盖了 Linux（但本 Demo 正式支持并由 CI 以 Native AOT
        // 发布的目标只有 win-x64 / linux-x64，不含 win-x86——见 .csproj 里的说明）。
        //
        // Shows the current process/RID/OS info, to help confirm which native binary
        // is actually loaded/verified right now (7zlite.dll vs. lib7zlite.so, win-x64
        // or linux-x64; this just reports whatever RuntimeIdentifier is detected at
        // runtime, it isn't restricted to only those two values — if someone runs it
        // under a different RID via e.g. `dotnet run -r win-x86`, it'll report that
        // too) — same purpose as the old WPF version, just no longer limited to
        // "win-x64 / win-x86" only, since the Avalonia version also covers Linux (but
        // the only RIDs this demo officially supports and CI publishes as Native AOT
        // are win-x64 / linux-x64, not win-x86 — see the .csproj comments).
        string arch = Environment.Is64BitProcess ? "64-bit" : "32-bit";
        string rid = RuntimeInformation.RuntimeIdentifier;
        HeaderLabel.Text = $"SevenZipLite Avalonia 演示 —— 当前进程: {arch}，RuntimeIdentifier: {rid}，" +
                            $"OSDescription: {RuntimeInformation.OSDescription}";
    }

    // ==================== 1) 内置往返自检 ====================

    private async void OnRunSelfTestClicked(object? sender, RoutedEventArgs e)
    {
        SelfTestButton.IsEnabled = false;
        SelfTestSummaryLabel.Text = "运行中……";
        SelfTestLogBox.Text = "";

        string root = Path.Combine(Path.GetTempPath(), "sevenziplite_avalonia_selftest_" + Guid.NewGuid().ToString("N"));

        try
        {
            var results = await Task.Run(() => SelfTestRunner.RunAll(root));

            int pass = results.Count(r => r.Pass);
            int fail = results.Count - pass;

            var log = new StringBuilder();
            foreach (var r in results)
            {
                foreach (var line in r.Log) log.AppendLine($"  -> {line}");
                log.AppendLine(r.Pass ? $"[PASS] {r.Name}" : $"[FAIL] {r.Name}  {r.Detail}");
            }

            SelfTestLogBox.Text = log.ToString();
            SelfTestSummaryLabel.Text = $"结果: {pass} 通过, {fail} 失败（工作目录: {root}）";
            SelfTestSummaryLabel.Foreground = fail == 0 ? Brushes.Green : Brushes.Red;
        }
        catch (DllNotFoundException ex)
        {
            SelfTestSummaryLabel.Text = "找不到原生库（7zlite.dll / lib7zlite.so 尚未构建/拷贝到输出目录）。";
            SelfTestSummaryLabel.Foreground = Brushes.Red;
            SelfTestLogBox.Text = NativeLibMissingHint(ex);
        }
        catch (Exception ex)
        {
            SelfTestSummaryLabel.Text = "自检运行本身抛出异常（说明原生库加载/互操作层有问题）：";
            SelfTestSummaryLabel.Foreground = Brushes.Red;
            SelfTestLogBox.Text = ex.ToString();
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* 忽略清理失败 */ }
            SelfTestButton.IsEnabled = true;
        }
    }

    // ==================== 2) 手动压缩 / 解压体验 ====================

    private async void OnPickFilesClicked(object? sender, RoutedEventArgs e)
    {
        // WPF 的 Microsoft.Win32.OpenFileDialog 是同步、Windows 专属的；Avalonia 用
        // TopLevel.StorageProvider 这套跨平台异步 API（底层按 OS 分别对接 Win32 对话框 /
        // GTK 或 xdg-desktop-portal / macOS NSOpenPanel），纯托管实现，不涉及任何反射，
        // 对 Native AOT 友好。
        //
        // WPF's Microsoft.Win32.OpenFileDialog is synchronous and Windows-only; Avalonia
        // uses the cross-platform, async TopLevel.StorageProvider API instead (backed by
        // the native Win32 dialog / GTK or xdg-desktop-portal / macOS NSOpenPanel
        // depending on OS). It's pure managed code with no reflection involved, so it's
        // Native AOT friendly.
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择要压缩的文件（可多选）",
            AllowMultiple = true,
        });
        if (files.Count == 0) return;

        _pickedFiles.Clear();
        _pickedFiles.AddRange(files.Select(f => f.Path.LocalPath));
        PickedFilesLabel.Text = "已选择: " + string.Join(", ", _pickedFiles.Select(Path.GetFileName));
        PackButton.IsEnabled = true;
        ExtractButton.IsEnabled = false;
        ManualLogBox.Text = "";
    }

    private async void OnPackClicked(object? sender, RoutedEventArgs e)
    {
        if (_pickedFiles.Count == 0) return;

        var format = ((ComboBoxItem)FormatCombo.SelectedItem!).Content switch
        {
            "7z" => ArchiveFormat.SevenZip,
            "zip" => ArchiveFormat.Zip,
            "tar" => ArchiveFormat.Tar,
            "tar.gz" => ArchiveFormat.TarGZip,
            _ => ArchiveFormat.SevenZip
        };
        string? password = string.IsNullOrEmpty(PasswordBox.Text) ? null : PasswordBox.Text;
        long.TryParse(VolumeSizeBox.Text, out long volumeSize);

        string ext = format switch
        {
            ArchiveFormat.SevenZip => ".7z",
            ArchiveFormat.Zip => ".zip",
            ArchiveFormat.Tar => ".tar",
            ArchiveFormat.TarGZip => ".tar.gz",
            _ => ""
        };

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var saveFile = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "选择归档输出路径（分卷时这是不带卷号的基础文件名）",
            SuggestedFileName = "demo" + ext,
        });
        if (saveFile is null) return;
        string archivePath = saveFile.Path.LocalPath;

        var entries = _pickedFiles
            .Select(p => new SevenZipCompressor.Entry { DiskPath = p, ArchivePath = Path.GetFileName(p) })
            .ToList();

        try
        {
            PackButton.IsEnabled = false;
            var volumes = await Task.Run(() =>
                SevenZipCompressor.CreateArchive(entries, archivePath, format, password, volumeSize));

            _lastArchivePath = archivePath;
            _lastVolumePaths = volumes;
            _lastFormat = format;
            _lastPassword = password;

            long totalSize = volumes.Sum(v => new FileInfo(v).Length);
            ManualLogBox.Text =
                $"打包成功：{volumes.Count} 个文件，共 {totalSize} 字节\n" +
                string.Join("\n", volumes.Select(v => "  " + v));
            ExtractButton.IsEnabled = true;
        }
        catch (DllNotFoundException ex)
        {
            ManualLogBox.Text = NativeLibMissingHint(ex);
        }
        catch (Exception ex)
        {
            ManualLogBox.Text = "打包失败: " + ex;
        }
        finally
        {
            PackButton.IsEnabled = true;
        }
    }

    private async void OnExtractClicked(object? sender, RoutedEventArgs e)
    {
        if (_lastArchivePath is null || _lastVolumePaths is null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择解压目标文件夹",
            AllowMultiple = false,
        });
        if (folders.Count == 0) return;

        string extractDir = Path.Combine(folders[0].Path.LocalPath,
            "sevenziplite_extract_" + Guid.NewGuid().ToString("N"));

        try
        {
            ExtractButton.IsEnabled = false;
            string openTarget = _lastVolumePaths.Count > 1 ? _lastVolumePaths[0] : _lastArchivePath;

            var listed = await Task.Run(() => SevenZipArchive.List(openTarget, _lastFormat));

            Directory.CreateDirectory(extractDir);
            await Task.Run(() => SevenZipArchive.ExtractAll(openTarget, _lastFormat, extractDir, _lastPassword));

            bool allMatch = true;
            var mismatches = new List<string>();
            foreach (var diskPath in _pickedFiles)
            {
                string expected = Path.Combine(extractDir, Path.GetFileName(diskPath));
                bool ok = File.Exists(expected) &&
                          File.ReadAllBytes(expected).SequenceEqual(File.ReadAllBytes(diskPath));
                if (!ok) { allMatch = false; mismatches.Add(Path.GetFileName(diskPath)); }
            }

            ManualLogBox.Text =
                $"列出条目数: {listed.Count}\n" +
                $"解压到: {extractDir}\n" +
                (allMatch
                    ? "校验结果: 全部文件字节级一致 \u2705"
                    : "校验结果: 以下文件不一致 \u274c " + string.Join(", ", mismatches));
        }
        catch (DllNotFoundException ex)
        {
            ManualLogBox.Text = NativeLibMissingHint(ex);
        }
        catch (Exception ex)
        {
            ManualLogBox.Text = "解压/校验失败: " + ex;
        }
        finally
        {
            ExtractButton.IsEnabled = true;
        }
    }

    // ==================== 3) 大文件吞吐量测试 ====================

    private async void OnRunPerfTestClicked(object? sender, RoutedEventArgs e)
    {
        if (!long.TryParse(PerfSizeBox.Text, out long sizeMB) || sizeMB <= 0)
        {
            PerfTestSummaryLabel.Text = "请输入一个正整数作为测试大小（单位 MB）。";
            PerfTestSummaryLabel.Foreground = Brushes.Red;
            return;
        }

        var format = ((ComboBoxItem)PerfFormatCombo.SelectedItem!).Content switch
        {
            "7z" => ArchiveFormat.SevenZip,
            "zip" => ArchiveFormat.Zip,
            "tar" => ArchiveFormat.Tar,
            "tar.gz" => ArchiveFormat.TarGZip,
            _ => ArchiveFormat.SevenZip
        };

        PerfTestButton.IsEnabled = false;
        PerfTestSummaryLabel.Text = "运行中……（大文件测试可能耗时数分钟，请耐心等待）";
        PerfTestSummaryLabel.Foreground = Brushes.Black;
        PerfTestLogBox.Text = "";

        string root = Path.Combine(Path.GetTempPath(), "sevenziplite_avalonia_perf_" + Guid.NewGuid().ToString("N"));

        try
        {
            var result = await Task.Run(() => PerfTestRunner.Run(
                root, sizeMB * 1024L * 1024L, format,
                onProgress: msg =>
                {
                    // 来自后台线程，回到 UI 线程追加日志——Avalonia.Threading.Dispatcher.UIThread.Invoke
                    // 跟 WPF 的 Dispatcher.Invoke 是同一种"同步阻塞直到 UI 线程执行完"的语义，
                    // 直接等价替换，不需要改调用方式。
                    //
                    // Called from a background thread; hop back to the UI thread to append
                    // the log — Avalonia.Threading.Dispatcher.UIThread.Invoke has the same
                    // "block until the UI thread has executed it" semantics as WPF's
                    // Dispatcher.Invoke, so it's a direct drop-in replacement.
                    Dispatcher.UIThread.Invoke(() =>
                    {
                        PerfTestLogBox.Text += $"  -> {msg}\n";
                        PerfTestLogBox.CaretIndex = PerfTestLogBox.Text?.Length ?? 0;
                    });
                }));

            PerfTestSummaryLabel.Text = result.Pass
                ? $"结果: 通过（原始 {result.OriginalBytes / 1024.0 / 1024.0:F1} MB -> " +
                  $"压缩 {result.CompressedBytes / 1024.0 / 1024.0:F1} MB, {result.CompressionRatioPercent:F1}%）\n" +
                  $"压缩: {result.CreateSeconds:F2}s / {result.CreateThroughputMBps:F1} MB/s　　" +
                  $"解压: {result.ExtractSeconds:F2}s / {result.ExtractThroughputMBps:F1} MB/s"
                : $"结果: 失败 —— {result.Detail}";
            PerfTestSummaryLabel.Foreground = result.Pass ? Brushes.Green : Brushes.Red;
        }
        catch (DllNotFoundException ex)
        {
            PerfTestSummaryLabel.Text = "找不到原生库（7zlite.dll / lib7zlite.so 尚未构建/拷贝到输出目录）。";
            PerfTestSummaryLabel.Foreground = Brushes.Red;
            PerfTestLogBox.Text = NativeLibMissingHint(ex);
        }
        catch (Exception ex)
        {
            PerfTestSummaryLabel.Text = "性能测试运行本身抛出异常：";
            PerfTestSummaryLabel.Foreground = Brushes.Red;
            PerfTestLogBox.Text = ex.ToString();
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { /* 忽略清理失败 */ }
            PerfTestButton.IsEnabled = true;
        }
    }

    // ==================== 4) 多线程压力测试 ====================

    private async void OnRunStressTestClicked(object? sender, RoutedEventArgs e)
    {
        if (!int.TryParse(StressFileCountBox.Text, out int fileCount) || fileCount <= 0)
        {
            StressTestSummaryLabel.Text = "请输入一个正整数作为每轮文件数。";
            StressTestSummaryLabel.Foreground = Brushes.Red;
            return;
        }
        if (!int.TryParse(StressIterationsBox.Text, out int iterations) || iterations <= 0)
        {
            StressTestSummaryLabel.Text = "请输入一个正整数作为轮数。";
            StressTestSummaryLabel.Foreground = Brushes.Red;
            return;
        }

        var format = ((ComboBoxItem)StressFormatCombo.SelectedItem!).Content switch
        {
            "7z" => ArchiveFormat.SevenZip,
            "zip" => ArchiveFormat.Zip,
            "tar" => ArchiveFormat.Tar,
            _ => ArchiveFormat.Zip
        };

        StressTestButton.IsEnabled = false;
        StressTestSummaryLabel.Text = "运行中……";
        StressTestSummaryLabel.Foreground = Brushes.Black;
        StressTestLogBox.Text = "";

        string root = Path.Combine(Path.GetTempPath(), "sevenziplite_avalonia_stress_" + Guid.NewGuid().ToString("N"));

        try
        {
            bool ok = await Task.Run(() => MultiThreadStressRunner.Run(
                root, iterations, fileCount, format,
                log: msg => Dispatcher.UIThread.Invoke(() =>
                {
                    StressTestLogBox.Text += $"  -> {msg}\n";
                    StressTestLogBox.CaretIndex = StressTestLogBox.Text?.Length ?? 0;
                })));

            StressTestSummaryLabel.Text = ok ? "结果: 全部通过" : "结果: 发现问题（见日志）";
            StressTestSummaryLabel.Foreground = ok ? Brushes.Green : Brushes.Red;
        }
        catch (DllNotFoundException ex)
        {
            StressTestSummaryLabel.Text = "找不到原生库（7zlite.dll / lib7zlite.so 尚未构建/拷贝到输出目录）。";
            StressTestSummaryLabel.Foreground = Brushes.Red;
            StressTestLogBox.Text = NativeLibMissingHint(ex);
        }
        catch (Exception ex)
        {
            StressTestSummaryLabel.Text = "压力测试运行本身抛出异常：";
            StressTestSummaryLabel.Foreground = Brushes.Red;
            StressTestLogBox.Text = ex.ToString();
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { /* 忽略清理失败 */ }
            StressTestButton.IsEnabled = true;
        }
    }

    private static string NativeLibMissingHint(Exception ex)
    {
        bool isWindows = OperatingSystem.IsWindows();
        string arch = Environment.Is64BitProcess ? "x64" : "x86";
        return isWindows
            ? $"{ex}\n\n" +
              $"提示：找不到 native-libs\\win-{arch}\\7zlite.dll。这个文件就是 7-zip.org 官方发布的\n" +
              $"原版 7z.dll 改名而来，不需要自己编译：从你本机已安装的 7-Zip（通常在\n" +
              $"\"C:\\Program Files\\7-Zip\\7z.dll\"）复制一份，或从 https://7-zip.org 官网下载对应\n" +
              $"架构（{arch}）的安装包/独立压缩包提取 7z.dll，改名为 7zlite.dll 后放到仓库根目录的\n" +
              $"native-libs\\win-{arch}\\ 下（详见该目录的 README.md），重新生成/运行本项目即可。"
            : $"{ex}\n\n" +
              $"提示：找不到 native-libs/linux-x64/lib7zlite.so。详见仓库根目录 native-libs/README.md\n" +
              $"里关于如何从本机 7-Zip 的 p7zip 插件或自行编译裁剪版 7-Zip 原生库获取该文件的说明，\n" +
              $"放到 native-libs/linux-x64/ 下后重新生成/运行本项目即可。";
    }
}
