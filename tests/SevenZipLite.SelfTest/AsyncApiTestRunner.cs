namespace SevenZipLite.SelfTest;

/// <summary>
/// 覆盖异步 API（<see cref="SevenZipCompressor.CreateArchiveAsync"/> /
/// <see cref="SevenZipArchive.ListAsync"/> / <see cref="SevenZipArchive.ExtractAllAsync"/>）、
/// 进度汇报（<see cref="SevenZipProgress"/>）以及 <see cref="CancellationToken"/> 取消的专项用例。
/// 这些场景和 <see cref="SelfTestRunner"/> 里纯同步的格式×分卷×密码矩阵是正交的，所以单独成类，
/// 由 <see cref="SelfTestRunner.RunAll"/> 统一调用、汇总进同一份结果列表。
/// </summary>
public static class AsyncApiTestRunner
{
    public static IReadOnlyList<SelfTestResult> RunAll(
        List<SevenZipCompressor.Entry> entries, string root, string outDir)
    {
        return new List<SelfTestResult>
        {
            RunAsyncRoundTrip(entries, root, outDir, "async round-trip (7z)", ArchiveFormat.SevenZip),
            RunAsyncRoundTrip(entries, root, outDir, "async round-trip (tar.gz)", ArchiveFormat.TarGZip),
            RunProgressReporting(entries, root, outDir, "progress reporting (7z)", ArchiveFormat.SevenZip),
            RunProgressReporting(entries, root, outDir, "progress reporting (tar.gz)", ArchiveFormat.TarGZip),
            RunCancelDuringCreate(entries, root, "cancel during CreateArchive (7z)", ArchiveFormat.SevenZip),
            RunCancelDuringCreate(entries, root, "cancel during CreateArchive (tar.gz)", ArchiveFormat.TarGZip),
            RunCancelDuringExtract(entries, root, outDir, "cancel during ExtractAll (7z)", ArchiveFormat.SevenZip),
            RunCancelDuringExtract(entries, root, outDir, "cancel during ExtractAll (tar.gz)", ArchiveFormat.TarGZip),
        };
    }

    private static SelfTestResult RunAsyncRoundTrip(
        List<SevenZipCompressor.Entry> entries, string root, string outDir, string name, ArchiveFormat format)
    {
        var log = new List<string>();
        try
        {
            string archivePath = Path.Combine(root, name.Replace(' ', '_').Replace('(', '_').Replace(')', '_') + Ext(format));
            string extractDir = Path.Combine(outDir, name.Replace(' ', '_').Replace('(', '_').Replace(')', '_'));
            Directory.CreateDirectory(extractDir);

            // .GetAwaiter().GetResult() 而不是 async Task 方法本身：SelfTestRunner 这套用例
            // 框架是同步跑的（RunAll 返回 IReadOnlyList<T>，不是 Task），这里只是要验证
            // Task.Run 包装之后，在同一个调用方线程同步等待也能拿到正确结果，不代表这就是
            // 推荐的调用方式——真实调用方应该用 await。
            var volumes = SevenZipCompressor.CreateArchiveAsync(entries, archivePath, format)
                .GetAwaiter().GetResult();
            log.Add($"[async] 生成文件: {string.Join(", ", volumes.Select(Path.GetFileName))}");

            var listed = SevenZipArchive.ListAsync(archivePath, format).GetAwaiter().GetResult();
            log.Add($"[async] 列出条目数: {listed.Count}");
            if (listed.Count != entries.Count)
                return new SelfTestResult(name, false, $"期望 {entries.Count} 个条目，实际 {listed.Count}", log);

            SevenZipArchive.ExtractAllAsync(archivePath, format, extractDir).GetAwaiter().GetResult();
            bool ok = VerifyExtracted(entries, extractDir);
            return new SelfTestResult(name, ok, ok ? null : "解压后内容与原文件不一致", log);
        }
        catch (Exception ex)
        {
            log.Add(ex.ToString());
            return new SelfTestResult(name, false, ex.Message, log);
        }
    }

    private sealed class RecordingProgress : IProgress<SevenZipProgress>
    {
        public readonly List<SevenZipProgress> Reports = new();
        public void Report(SevenZipProgress value) => Reports.Add(value);
    }

    private static SelfTestResult RunProgressReporting(
        List<SevenZipCompressor.Entry> entries, string root, string outDir, string name, ArchiveFormat format)
    {
        var log = new List<string>();
        try
        {
            string archivePath = Path.Combine(root, name.Replace(' ', '_').Replace('(', '_').Replace(')', '_') + Ext(format));
            string extractDir = Path.Combine(outDir, name.Replace(' ', '_').Replace('(', '_').Replace(')', '_'));
            Directory.CreateDirectory(extractDir);

            var createProgress = new RecordingProgress();
            SevenZipCompressor.CreateArchive(entries, archivePath, format, progress: createProgress);
            log.Add($"[progress] 压缩阶段收到 {createProgress.Reports.Count} 条进度汇报");

            if (createProgress.Reports.Count == 0)
                return new SelfTestResult(name, false, "压缩过程没有收到任何进度汇报", log);
            // tar.gz 分两阶段各汇报一轮 0~100%，所以不要求全局"只有一次 100%"；只要求
            // 每一条汇报自身的字段合法，并且序列里出现过 Percentage 达到 100 的那一刻
            // （代表至少有一个阶段完整跑完）。
            if (createProgress.Reports.Any(r => r.BytesTotal > 0 && r.BytesCompleted > r.BytesTotal))
                return new SelfTestResult(name, false, "出现 BytesCompleted > BytesTotal 的非法进度汇报", log);
            if (!createProgress.Reports.Any(r => r.Percentage >= 100))
                return new SelfTestResult(name, false, "压缩过程从未汇报到 100%", log);

            var extractProgress = new RecordingProgress();
            SevenZipArchive.ExtractAll(archivePath, format, extractDir, progress: extractProgress);
            log.Add($"[progress] 解压阶段收到 {extractProgress.Reports.Count} 条进度汇报");

            if (extractProgress.Reports.Count == 0)
                return new SelfTestResult(name, false, "解压过程没有收到任何进度汇报", log);
            if (!extractProgress.Reports.Any(r => r.Percentage >= 100))
                return new SelfTestResult(name, false, "解压过程从未汇报到 100%", log);

            bool ok = VerifyExtracted(entries, extractDir);
            return new SelfTestResult(name, ok, ok ? null : "解压后内容与原文件不一致", log);
        }
        catch (Exception ex)
        {
            log.Add(ex.ToString());
            return new SelfTestResult(name, false, ex.Message, log);
        }
    }

    private static SelfTestResult RunCancelDuringCreate(
        List<SevenZipCompressor.Entry> entries, string root, string name, ArchiveFormat format)
    {
        var log = new List<string>();
        try
        {
            string archivePath = Path.Combine(root, name.Replace(' ', '_').Replace('(', '_').Replace(')', '_') + Ext(format));
            // 预先取消：验证"在第一个条目开始前就已经取消"这种最简单、最必现的场景——
            // 粒度是"条目之间"，预取消能保证第一次 SetTotal/SetCompleted 回调就会命中
            // E_ABORT，不依赖任何时序竞争，用例因此是确定性的（不是概率性/flaky 的）。
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            try
            {
                SevenZipCompressor.CreateArchive(entries, archivePath, format, cancellationToken: cts.Token);
                log.Add("没有抛出任何异常");
                return new SelfTestResult(name, false, "预期抛出 OperationCanceledException，但没有抛出", log);
            }
            catch (OperationCanceledException)
            {
                log.Add("按预期抛出 OperationCanceledException");
                return new SelfTestResult(name, true, null, log);
            }
        }
        catch (Exception ex)
        {
            log.Add(ex.ToString());
            return new SelfTestResult(name, false, $"抛出了错误类型的异常: {ex.GetType().Name}: {ex.Message}", log);
        }
    }

    private static SelfTestResult RunCancelDuringExtract(
        List<SevenZipCompressor.Entry> entries, string root, string outDir, string name, ArchiveFormat format)
    {
        var log = new List<string>();
        try
        {
            string archivePath = Path.Combine(root, "cancel_extract_src_" + name.Replace(' ', '_').Replace('(', '_').Replace(')', '_') + Ext(format));
            string extractDir = Path.Combine(outDir, "cancel_extract_" + name.Replace(' ', '_').Replace('(', '_').Replace(')', '_'));
            Directory.CreateDirectory(extractDir);

            // 先用正常（不取消）的方式生成一个合法归档，专门用来测试"解压阶段"的取消，
            // 避免和上面 RunCancelDuringCreate 用例重复验证压缩阶段的逻辑。
            SevenZipCompressor.CreateArchive(entries, archivePath, format);

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            try
            {
                SevenZipArchive.ExtractAll(archivePath, format, extractDir, cancellationToken: cts.Token);
                log.Add("没有抛出任何异常");
                return new SelfTestResult(name, false, "预期抛出 OperationCanceledException，但没有抛出", log);
            }
            catch (OperationCanceledException)
            {
                log.Add("按预期抛出 OperationCanceledException");
                return new SelfTestResult(name, true, null, log);
            }
        }
        catch (Exception ex)
        {
            log.Add(ex.ToString());
            return new SelfTestResult(name, false, $"抛出了错误类型的异常: {ex.GetType().Name}: {ex.Message}", log);
        }
    }

    private static bool VerifyExtracted(List<SevenZipCompressor.Entry> entries, string extractDir)
    {
        foreach (var e in entries)
        {
            string expected = Path.Combine(extractDir, e.ArchivePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(expected)) return false;
            if (!File.ReadAllBytes(expected).SequenceEqual(File.ReadAllBytes(e.DiskPath))) return false;
        }
        return true;
    }

    private static string Ext(ArchiveFormat f) => f switch
    {
        ArchiveFormat.SevenZip => ".7z",
        ArchiveFormat.Zip => ".zip",
        ArchiveFormat.Tar => ".tar",
        ArchiveFormat.TarGZip => ".tar.gz",
        _ => throw new NotSupportedException()
    };
}
