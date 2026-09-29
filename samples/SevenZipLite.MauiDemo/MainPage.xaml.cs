using SevenZipLite;
using SevenZipLite.SelfTest;

namespace SevenZipLite.MauiDemo;

public partial class MainPage : ContentPage
{
    private readonly List<string> _pickedFiles = new();
    private string? _lastArchivePath;
    private IReadOnlyList<string>? _lastVolumePaths;
    private ArchiveFormat _lastFormat;
    private string? _lastPassword;

    public MainPage()
    {
        InitializeComponent();
        FormatPicker.SelectedIndex = 0;
        PerfFormatPicker.SelectedIndex = 0;
    }

    // ---------------- 1) 内置自检 ----------------

    private async void OnRunSelfTestClicked(object? sender, EventArgs e)
    {
        SelfTestButton.IsEnabled = false;
        SelfTestSpinner.IsVisible = true;
        SelfTestSpinner.IsRunning = true;
        SelfTestSummaryLabel.Text = "运行中……";
        SelfTestLogEditor.Text = "";

        // 用 App 私有缓存目录，Android 上无需申请任何存储权限即可读写。
        string root = Path.Combine(FileSystem.Current.CacheDirectory,
            "selftest_" + Guid.NewGuid().ToString("N"));

        try
        {
            var results = await Task.Run(() => SelfTestRunner.RunAll(root));

            int pass = results.Count(r => r.Pass);
            int fail = results.Count - pass;

            var log = new System.Text.StringBuilder();
            foreach (var r in results)
            {
                foreach (var line in r.Log) log.AppendLine($"  -> {line}");
                log.AppendLine(r.Pass ? $"[PASS] {r.Name}" : $"[FAIL] {r.Name}  {r.Detail}");
            }

            SelfTestLogEditor.Text = log.ToString();
            SelfTestSummaryLabel.Text = $"结果: {pass} 通过, {fail} 失败（工作目录: {root}）";
            SelfTestSummaryLabel.TextColor = fail == 0 ? Colors.Green : Colors.Red;
        }
        catch (Exception ex)
        {
            SelfTestSummaryLabel.Text = "自检运行本身抛出异常（说明原生库加载/互操作层有问题）：";
            SelfTestSummaryLabel.TextColor = Colors.Red;
            SelfTestLogEditor.Text = ex.ToString();
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* 忽略清理失败 */ }
            SelfTestButton.IsEnabled = true;
            SelfTestSpinner.IsVisible = false;
            SelfTestSpinner.IsRunning = false;
        }
    }

    // ---------------- 2) 手动体验 ----------------

    private async void OnPickFilesClicked(object? sender, EventArgs e)
    {
        try
        {
            var results = await FilePicker.Default.PickMultipleAsync();
            var files = results?.ToList() ?? new List<FileResult>();
            if (files.Count == 0) return;

            _pickedFiles.Clear();
            _pickedFiles.AddRange(files.Select(f => f.FullPath));
            PickedFilesLabel.Text = "已选择: " + string.Join(", ", files.Select(f => f.FileName));
            PackButton.IsEnabled = true;
            ExtractButton.IsEnabled = false;
            ManualLogLabel.Text = "";
        }
        catch (Exception ex)
        {
            ManualLogLabel.Text = "选择文件失败: " + ex.Message;
        }
    }

    private async void OnPackClicked(object? sender, EventArgs e)
    {
        if (_pickedFiles.Count == 0) return;

        string formatText = (string)FormatPicker.SelectedItem;
        var format = formatText switch
        {
            "7z" => ArchiveFormat.SevenZip,
            "zip" => ArchiveFormat.Zip,
            "tar" => ArchiveFormat.Tar,
            "tar.gz" => ArchiveFormat.TarGZip,
            _ => ArchiveFormat.SevenZip
        };
        string? password = string.IsNullOrWhiteSpace(PasswordEntry.Text) ? null : PasswordEntry.Text;
        long.TryParse(VolumeSizeEntry.Text, out long volumeSize);

        string ext = format switch
        {
            ArchiveFormat.SevenZip => ".7z",
            ArchiveFormat.Zip => ".zip",
            ArchiveFormat.Tar => ".tar",
            ArchiveFormat.TarGZip => ".tar.gz",
            _ => ""
        };

        string workDir = Path.Combine(FileSystem.Current.CacheDirectory, "manual_demo");
        Directory.CreateDirectory(workDir);
        string archivePath = Path.Combine(workDir, "demo" + ext);

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
            ManualLogLabel.Text =
                $"打包成功：{volumes.Count} 个文件，共 {totalSize} 字节\n" +
                string.Join("\n", volumes.Select(v => "  " + Path.GetFileName(v)));
            ExtractButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            ManualLogLabel.Text = "打包失败: " + ex;
        }
        finally
        {
            PackButton.IsEnabled = true;
        }
    }

    private async void OnExtractClicked(object? sender, EventArgs e)
    {
        if (_lastArchivePath is null || _lastVolumePaths is null) return;

        string extractDir = Path.Combine(FileSystem.Current.CacheDirectory, "manual_demo_extract_" + Guid.NewGuid().ToString("N"));

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

            ManualLogLabel.Text =
                $"列出条目数: {listed.Count}\n" +
                $"解压到: {extractDir}\n" +
                (allMatch
                    ? "校验结果: 全部文件字节级一致 ✅"
                    : "校验结果: 以下文件不一致 ❌ " + string.Join(", ", mismatches));
        }
        catch (Exception ex)
        {
            ManualLogLabel.Text = "解压/校验失败: " + ex;
        }
        finally
        {
            ExtractButton.IsEnabled = true;
        }
    }

    // ---------------- 3) 大文件吞吐量测试 ----------------

    private async void OnRunPerfTestClicked(object? sender, EventArgs e)
    {
        if (!long.TryParse(PerfSizeEntry.Text, out long sizeMB) || sizeMB <= 0)
        {
            PerfTestSummaryLabel.Text = "请输入一个正整数作为测试大小（单位 MB）。";
            PerfTestSummaryLabel.TextColor = Colors.Red;
            return;
        }

        string formatText = (string)PerfFormatPicker.SelectedItem;
        var format = formatText switch
        {
            "7z" => ArchiveFormat.SevenZip,
            "zip" => ArchiveFormat.Zip,
            "tar" => ArchiveFormat.Tar,
            "tar.gz" => ArchiveFormat.TarGZip,
            _ => ArchiveFormat.SevenZip
        };

        PerfTestButton.IsEnabled = false;
        PerfTestSpinner.IsVisible = true;
        PerfTestSpinner.IsRunning = true;
        PerfTestSummaryLabel.Text = "运行中……（大文件测试可能耗时数分钟，请耐心等待）";
        PerfTestLogEditor.Text = "";

        // 用 App 私有缓存目录，Android 上无需申请任何存储权限即可读写；
        // 手机存储通常比桌面紧张，测完（成功或失败）都会在 finally 里清理。
        string root = Path.Combine(FileSystem.Current.CacheDirectory,
            "perftest_" + Guid.NewGuid().ToString("N"));

        try
        {
            var result = await Task.Run(() => PerfTestRunner.Run(
                root, sizeMB * 1024L * 1024L, format,
                onProgress: msg =>
                {
                    // 来自后台线程，回到 UI 线程追加日志，实时显示当前处于哪个阶段。
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        PerfTestLogEditor.Text += $"  -> {msg}\n";
                    });
                }));

            PerfTestSummaryLabel.Text = result.Pass
                ? $"结果: 通过（原始 {result.OriginalBytes / 1024.0 / 1024.0:F1} MB -> " +
                  $"压缩 {result.CompressedBytes / 1024.0 / 1024.0:F1} MB, {result.CompressionRatioPercent:F1}%）\n" +
                  $"压缩: {result.CreateSeconds:F2}s / {result.CreateThroughputMBps:F1} MB/s　　" +
                  $"解压: {result.ExtractSeconds:F2}s / {result.ExtractThroughputMBps:F1} MB/s"
                : $"结果: 失败 —— {result.Detail}";
            PerfTestSummaryLabel.TextColor = result.Pass ? Colors.Green : Colors.Red;
        }
        catch (Exception ex)
        {
            PerfTestSummaryLabel.Text = "性能测试运行本身抛出异常：";
            PerfTestSummaryLabel.TextColor = Colors.Red;
            PerfTestLogEditor.Text = ex.ToString();
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { /* 忽略清理失败 */ }
            PerfTestButton.IsEnabled = true;
            PerfTestSpinner.IsVisible = false;
            PerfTestSpinner.IsRunning = false;
        }
    }

    // ---------------- 4) 多线程压力测试 ----------------

    private async void OnRunStressTestClicked(object? sender, EventArgs e)
    {
        if (!int.TryParse(StressFileCountEntry.Text, out int fileCount) || fileCount <= 0)
        {
            StressTestSummaryLabel.Text = "请输入一个正整数作为每轮文件数。";
            StressTestSummaryLabel.TextColor = Colors.Red;
            return;
        }
        if (!int.TryParse(StressIterationsEntry.Text, out int iterations) || iterations <= 0)
        {
            StressTestSummaryLabel.Text = "请输入一个正整数作为轮数。";
            StressTestSummaryLabel.TextColor = Colors.Red;
            return;
        }

        string formatText = (string)StressFormatPicker.SelectedItem;
        var format = formatText switch
        {
            "7z" => ArchiveFormat.SevenZip,
            "zip" => ArchiveFormat.Zip,
            "tar" => ArchiveFormat.Tar,
            _ => ArchiveFormat.Zip
        };

        StressTestButton.IsEnabled = false;
        StressTestSpinner.IsVisible = true;
        StressTestSpinner.IsRunning = true;
        StressTestSummaryLabel.Text = "运行中……";
        StressTestLogEditor.Text = "";

        string root = Path.Combine(FileSystem.Current.CacheDirectory,
            "stresstest_" + Guid.NewGuid().ToString("N"));

        try
        {
            bool ok = await Task.Run(() => MultiThreadStressRunner.Run(
                root, iterations, fileCount, format,
                log: msg => MainThread.BeginInvokeOnMainThread(() =>
                {
                    StressTestLogEditor.Text += $"  -> {msg}\n";
                })));

            StressTestSummaryLabel.Text = ok ? "结果: 全部通过" : "结果: 发现问题（见日志）";
            StressTestSummaryLabel.TextColor = ok ? Colors.Green : Colors.Red;
        }
        catch (Exception ex)
        {
            StressTestSummaryLabel.Text = "压力测试运行本身抛出异常：";
            StressTestSummaryLabel.TextColor = Colors.Red;
            StressTestLogEditor.Text = ex.ToString();
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { /* 忽略清理失败 */ }
            StressTestButton.IsEnabled = true;
            StressTestSpinner.IsVisible = false;
            StressTestSpinner.IsRunning = false;
        }
    }
}
