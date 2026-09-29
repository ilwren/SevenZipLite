using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using SevenZipLite;
using SevenZipLite.SelfTest;

namespace SevenZipLite.WpfDemo;

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

        // 显示当前进程位数（win-x64 / win-x86），方便确认现在实际加载/验证的是
        // NativeLibs\win-x64\7zlite.dll 还是 NativeLibs\win-x86\7zlite.dll——
        // 在 Visual Studio 里切换"解决方案平台"为 x64/x86 后重新运行，这里的文字会
        // 相应变化，用来交叉验证两个 RID 都能跑通。
        string arch = Environment.Is64BitProcess ? "x64 (win-x64)" : "x86 (win-x86)";
        string rid = RuntimeInformation.RuntimeIdentifier;
        HeaderLabel.Text = $"SevenZipLite WPF 演示 —— 当前进程: {arch}，RuntimeIdentifier: {rid}，" +
                            $"OSDescription: {RuntimeInformation.OSDescription}";
    }

    // ==================== 1) 内置往返自检 ====================

    private async void OnRunSelfTestClicked(object sender, RoutedEventArgs e)
    {
        SelfTestButton.IsEnabled = false;
        SelfTestSummaryLabel.Text = "运行中……";
        SelfTestLogBox.Text = "";

        string root = Path.Combine(Path.GetTempPath(), "sevenziplite_wpf_selftest_" + Guid.NewGuid().ToString("N"));

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
            SelfTestSummaryLabel.Foreground = fail == 0
                ? System.Windows.Media.Brushes.Green
                : System.Windows.Media.Brushes.Red;
        }
        catch (DllNotFoundException ex)
        {
            SelfTestSummaryLabel.Text = "找不到 7zlite.dll（原生库尚未构建/拷贝到输出目录）。";
            SelfTestSummaryLabel.Foreground = System.Windows.Media.Brushes.Red;
            SelfTestLogBox.Text = NativeLibMissingHint(ex);
        }
        catch (Exception ex)
        {
            SelfTestSummaryLabel.Text = "自检运行本身抛出异常（说明原生库加载/互操作层有问题）：";
            SelfTestSummaryLabel.Foreground = System.Windows.Media.Brushes.Red;
            SelfTestLogBox.Text = ex.ToString();
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* 忽略清理失败 */ }
            SelfTestButton.IsEnabled = true;
        }
    }

    // ==================== 2) 手动压缩 / 解压体验 ====================

    private void OnPickFilesClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Title = "选择要压缩的文件（可多选）",
        };
        if (dialog.ShowDialog() != true) return;

        _pickedFiles.Clear();
        _pickedFiles.AddRange(dialog.FileNames);
        PickedFilesLabel.Text = "已选择: " + string.Join(", ", dialog.FileNames.Select(Path.GetFileName));
        PackButton.IsEnabled = true;
        ExtractButton.IsEnabled = false;
        ManualLogBox.Text = "";
    }

    private async void OnPackClicked(object sender, RoutedEventArgs e)
    {
        if (_pickedFiles.Count == 0) return;

        var format = ((System.Windows.Controls.ComboBoxItem)FormatCombo.SelectedItem).Content switch
        {
            "7z" => ArchiveFormat.SevenZip,
            "zip" => ArchiveFormat.Zip,
            "tar" => ArchiveFormat.Tar,
            "tar.gz" => ArchiveFormat.TarGZip,
            _ => ArchiveFormat.SevenZip
        };
        string? password = string.IsNullOrEmpty(PasswordBox.Password) ? null : PasswordBox.Password;
        long.TryParse(VolumeSizeBox.Text, out long volumeSize);

        string ext = format switch
        {
            ArchiveFormat.SevenZip => ".7z",
            ArchiveFormat.Zip => ".zip",
            ArchiveFormat.Tar => ".tar",
            ArchiveFormat.TarGZip => ".tar.gz",
            _ => ""
        };

        var saveDialog = new SaveFileDialog
        {
            Title = "选择归档输出路径（分卷时这是不带卷号的基础文件名）",
            FileName = "demo" + ext,
            Filter = "全部文件|*.*",
        };
        if (saveDialog.ShowDialog() != true) return;
        string archivePath = saveDialog.FileName;

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

    private async void OnExtractClicked(object sender, RoutedEventArgs e)
    {
        if (_lastArchivePath is null || _lastVolumePaths is null) return;

        var folderDialog = new OpenFolderDialog
        {
            Title = "选择解压目标文件夹",
        };
        if (folderDialog.ShowDialog() != true) return;

        string extractDir = Path.Combine(folderDialog.FolderName,
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

    private async void OnRunPerfTestClicked(object sender, RoutedEventArgs e)
    {
        if (!long.TryParse(PerfSizeBox.Text, out long sizeMB) || sizeMB <= 0)
        {
            PerfTestSummaryLabel.Text = "请输入一个正整数作为测试大小（单位 MB）。";
            PerfTestSummaryLabel.Foreground = System.Windows.Media.Brushes.Red;
            return;
        }

        var format = ((System.Windows.Controls.ComboBoxItem)PerfFormatCombo.SelectedItem).Content switch
        {
            "7z" => ArchiveFormat.SevenZip,
            "zip" => ArchiveFormat.Zip,
            "tar" => ArchiveFormat.Tar,
            "tar.gz" => ArchiveFormat.TarGZip,
            _ => ArchiveFormat.SevenZip
        };

        PerfTestButton.IsEnabled = false;
        PerfTestSummaryLabel.Text = "运行中……（大文件测试可能耗时数分钟，请耐心等待）";
        PerfTestSummaryLabel.Foreground = System.Windows.Media.Brushes.Black;
        PerfTestLogBox.Text = "";

        string root = Path.Combine(Path.GetTempPath(), "sevenziplite_wpf_perf_" + Guid.NewGuid().ToString("N"));
        var log = new StringBuilder();

        try
        {
            var result = await Task.Run(() => PerfTestRunner.Run(
                root, sizeMB * 1024L * 1024L, format,
                onProgress: msg =>
                {
                    // 来自后台线程，回到 UI 线程追加日志，实时显示当前处于哪个阶段。
                    Dispatcher.Invoke(() =>
                    {
                        PerfTestLogBox.AppendText($"  -> {msg}\n");
                        PerfTestLogBox.ScrollToEnd();
                    });
                }));

            PerfTestSummaryLabel.Text = result.Pass
                ? $"结果: 通过（原始 {result.OriginalBytes / 1024.0 / 1024.0:F1} MB -> " +
                  $"压缩 {result.CompressedBytes / 1024.0 / 1024.0:F1} MB, {result.CompressionRatioPercent:F1}%）\n" +
                  $"压缩: {result.CreateSeconds:F2}s / {result.CreateThroughputMBps:F1} MB/s　　" +
                  $"解压: {result.ExtractSeconds:F2}s / {result.ExtractThroughputMBps:F1} MB/s"
                : $"结果: 失败 —— {result.Detail}";
            PerfTestSummaryLabel.Foreground = result.Pass
                ? System.Windows.Media.Brushes.Green
                : System.Windows.Media.Brushes.Red;
        }
        catch (DllNotFoundException ex)
        {
            PerfTestSummaryLabel.Text = "找不到 7zlite.dll（原生库尚未构建/拷贝到输出目录）。";
            PerfTestSummaryLabel.Foreground = System.Windows.Media.Brushes.Red;
            PerfTestLogBox.Text = NativeLibMissingHint(ex);
        }
        catch (Exception ex)
        {
            PerfTestSummaryLabel.Text = "性能测试运行本身抛出异常：";
            PerfTestSummaryLabel.Foreground = System.Windows.Media.Brushes.Red;
            PerfTestLogBox.Text = ex.ToString();
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { /* 忽略清理失败 */ }
            PerfTestButton.IsEnabled = true;
        }
    }

    // ==================== 4) 多线程压力测试 ====================

    private async void OnRunStressTestClicked(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(StressFileCountBox.Text, out int fileCount) || fileCount <= 0)
        {
            StressTestSummaryLabel.Text = "请输入一个正整数作为每轮文件数。";
            StressTestSummaryLabel.Foreground = System.Windows.Media.Brushes.Red;
            return;
        }
        if (!int.TryParse(StressIterationsBox.Text, out int iterations) || iterations <= 0)
        {
            StressTestSummaryLabel.Text = "请输入一个正整数作为轮数。";
            StressTestSummaryLabel.Foreground = System.Windows.Media.Brushes.Red;
            return;
        }

        var format = ((System.Windows.Controls.ComboBoxItem)StressFormatCombo.SelectedItem).Content switch
        {
            "7z" => ArchiveFormat.SevenZip,
            "zip" => ArchiveFormat.Zip,
            "tar" => ArchiveFormat.Tar,
            _ => ArchiveFormat.Zip
        };

        StressTestButton.IsEnabled = false;
        StressTestSummaryLabel.Text = "运行中……";
        StressTestSummaryLabel.Foreground = System.Windows.Media.Brushes.Black;
        StressTestLogBox.Text = "";

        string root = Path.Combine(Path.GetTempPath(), "sevenziplite_wpf_stress_" + Guid.NewGuid().ToString("N"));

        try
        {
            bool ok = await Task.Run(() => MultiThreadStressRunner.Run(
                root, iterations, fileCount, format,
                log: msg => Dispatcher.Invoke(() =>
                {
                    StressTestLogBox.AppendText($"  -> {msg}\n");
                    StressTestLogBox.ScrollToEnd();
                })));

            StressTestSummaryLabel.Text = ok ? "结果: 全部通过" : "结果: 发现问题（见日志）";
            StressTestSummaryLabel.Foreground = ok
                ? System.Windows.Media.Brushes.Green
                : System.Windows.Media.Brushes.Red;
        }
        catch (DllNotFoundException ex)
        {
            StressTestSummaryLabel.Text = "找不到 7zlite.dll（原生库尚未构建/拷贝到输出目录）。";
            StressTestSummaryLabel.Foreground = System.Windows.Media.Brushes.Red;
            StressTestLogBox.Text = NativeLibMissingHint(ex);
        }
        catch (Exception ex)
        {
            StressTestSummaryLabel.Text = "压力测试运行本身抛出异常：";
            StressTestSummaryLabel.Foreground = System.Windows.Media.Brushes.Red;
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
        string arch = Environment.Is64BitProcess ? "x64" : "x86";
        return
            $"{ex}\n\n" +
            $"提示：找不到 native-libs\\win-{arch}\\7zlite.dll。这个文件就是 7-zip.org 官方发布的\n" +
            $"原版 7z.dll 改名而来，不需要自己编译：从你本机已安装的 7-Zip（通常在\n" +
            $"\"C:\\Program Files\\7-Zip\\7z.dll\"）复制一份，或从 https://7-zip.org 官网下载对应\n" +
            $"架构（{arch}）的安装包/独立压缩包提取 7z.dll，改名为 7zlite.dll 后放到仓库根目录的\n" +
            $"native-libs\\win-{arch}\\ 下（详见该目录的 README.md），重新生成/运行本项目即可。";
    }
}
