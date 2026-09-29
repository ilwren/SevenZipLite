using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace SevenZipLite.SelfTest;

/// <summary>大文件往返吞吐量测试的结果。</summary>
/// <param name="Pass">压缩+解压是否成功，且解压结果与原始文件按 SHA-256 校验字节级一致。</param>
/// <param name="Detail">失败时的简要原因；成功时为 null。</param>
/// <param name="Log">按时间顺序记录的阶段性日志（生成/压缩/解压/校验），可直接展示在 UI 里。</param>
/// <param name="OriginalBytes">合成测试文件的实际大小（字节）。</param>
/// <param name="CompressedBytes">压缩后归档的总大小（字节，分卷时是各卷之和）。</param>
/// <param name="CreateSeconds">压缩耗时（秒），不含生成测试数据的时间。</param>
/// <param name="ExtractSeconds">解压耗时（秒），不含 SHA-256 校验的时间。</param>
/// <param name="CreateThroughputMBps">压缩吞吐量，按"原始数据大小 / 压缩耗时"计算（MB/s，1MB=1024*1024字节）。</param>
/// <param name="ExtractThroughputMBps">解压吞吐量，按"原始数据大小 / 解压耗时"计算（MB/s，同上）。</param>
public sealed record PerfTestResult(
    bool Pass,
    string? Detail,
    IReadOnlyList<string> Log,
    long OriginalBytes,
    long CompressedBytes,
    double CreateSeconds,
    double ExtractSeconds,
    double CreateThroughputMBps,
    double ExtractThroughputMBps)
{
    /// <summary>压缩后大小 / 原始大小，百分比形式，仅用于日志展示。</summary>
    public double CompressionRatioPercent => OriginalBytes == 0 ? 0 : 100.0 * CompressedBytes / OriginalBytes;
}

/// <summary>
/// 大文件（设计目标：最大约 1GB）创建/解压吞吐量测试。
///
/// 这套用例刻意 <b>不</b> 放进 <see cref="SelfTestRunner.RunAll"/> 里常规跑——那套用例
/// 每种格式只用几十 KB 的合成小文件，目标是"覆盖 格式×分卷×密码 的正确性矩阵，几秒钟内跑完"；
/// 本测试的目标是"观察真实吞吐量"，体积可到 GB 级、耗时可到分钟级，必须由调用方
/// （Linux 控制台的 --perf 参数 / WPF 与 MAUI 界面上单独的"性能测试"按钮）显式触发。
///
/// 合成测试数据不是纯随机字节，也不是纯重复内容，而是按 4MB 为单位在"高度可压缩的重复文本
/// 片段"和"不可压缩的伪随机噪声片段"之间交替——用来大致模拟真实世界里"文档/日志混杂媒体
/// /已压缩数据"这类中等压缩比的大文件，比纯随机数据（最坏情况，压缩比≈100%）或纯重复数据
/// （最好情况，压缩比接近 0%）更有参考意义，同时生成成本很低（不需要真的读磁盘上的样本文件）。
///
/// 校验环节用流式 SHA-256（生成时边写边算一次，解压后对结果文件再算一次，只比较两个 32
/// 字节的摘要）而不是像 <see cref="SelfTestRunner"/> 里的小文件用例那样
/// <c>File.ReadAllBytes(...).SequenceEqual(...)</c>——后者会把整个大文件读进托管堆里比较两次，
/// 对 GB 级文件既慢又容易在内存较小的设备（尤其是 MAUI/Android 真机）上造成 OOM。
/// </summary>
public static class PerfTestRunner
{
    /// <summary>合成测试数据交替生成时使用的块大小。</summary>
    private const int ChunkSize = 4 * 1024 * 1024; // 4MB

    /// <summary>
    /// 运行一次大文件往返测试：生成合成数据 -> 计时压缩 -> 计时解压 -> 流式 SHA-256 校验。
    /// </summary>
    /// <param name="rootDir">工作目录，调用方保证可写；本方法只在这个目录下创建/删除自己用到的几个文件，
    /// 不会删除目录本身（分卷输出、临时数据文件均在此目录内，命名带 "perf_" 前缀，不与其他用例冲突）。</param>
    /// <param name="sizeBytes">合成测试文件的目标大小（字节）。设计目标上限约 1GB（1024L*1024*1024）；
    /// 更大的值理论上也能跑，但没有特别验证过，且耗时会显著增加。</param>
    /// <param name="format">归档格式，默认 7z（LZMA2，最能体现真实压缩 CPU 开销）。</param>
    /// <param name="password">可选密码（仅 7z/zip 生效，tar/tar.gz 会被忽略）。</param>
    /// <param name="zipMethodOverride">仅在 format=Zip 时有意义，用于对比 Store（应接近纯 IO 吞吐量）
    /// 与 Deflate（真正走编解码器）两种场景的速度差异。</param>
    /// <param name="keepFiles">true 时保留生成的源文件/归档/解压结果供人工检查；默认测完即删，
    /// 避免在存储空间有限的设备（尤其是手机）上残留 GB 级文件。</param>
    /// <param name="onProgress">可选的进度回调，每个阶段结束时调用一次，供 UI 实时显示当前状态。</param>
    public static PerfTestResult Run(
        string rootDir,
        long sizeBytes,
        ArchiveFormat format = ArchiveFormat.SevenZip,
        string? password = null,
        ZipMethodOverride? zipMethodOverride = null,
        bool keepFiles = false,
        Action<string>? onProgress = null)
    {
        var log = new List<string>();
        void Report(string msg)
        {
            log.Add(msg);
            onProgress?.Invoke(msg);
        }

        Directory.CreateDirectory(rootDir);
        string srcPath = Path.Combine(rootDir, "perf_payload.bin");
        string archivePath = Path.Combine(rootDir, "perf_archive" + Ext(format));
        string extractDir = Path.Combine(rootDir, "perf_extract");

        try
        {
            Report($"生成合成测试文件（目标大小 {FormatSize(sizeBytes)}，" +
                   "按 4MB 块交替混合可压缩重复片段与不可压缩随机噪声，模拟真实世界中等压缩比的大文件）……");
            string srcHash = GeneratePayload(srcPath, sizeBytes);
            long actualSize = new FileInfo(srcPath).Length;
            Report($"生成完成：实际大小 {FormatSize(actualSize)}，SHA-256={srcHash}");

            var entries = new List<SevenZipCompressor.Entry>
            {
                new() { DiskPath = srcPath, ArchivePath = "perf_payload.bin" },
            };

            Report($"开始压缩计时（格式={format}{(zipMethodOverride is { } m ? $"，方法覆盖={m}" : "")}" +
                   $"{(password is null ? "" : "，已加密")}）……");
            var createSw = Stopwatch.StartNew();
            var volumes = SevenZipCompressor.CreateArchive(entries, archivePath, format, password, 0, zipMethodOverride);
            createSw.Stop();

            long compressedBytes = volumes.Sum(v => new FileInfo(v).Length);
            double createSeconds = createSw.Elapsed.TotalSeconds;
            double createMBps = ThroughputMBps(actualSize, createSeconds);
            double ratio = actualSize == 0 ? 0 : 100.0 * compressedBytes / actualSize;
            Report($"压缩完成：耗时 {createSeconds:F2}s，压缩后 {FormatSize(compressedBytes)}" +
                   $"（压缩比 {ratio:F1}%），吞吐量 {createMBps:F1} MB/s（按原始数据大小 / 压缩耗时计算）");

            Directory.CreateDirectory(extractDir);
            string openTarget = volumes.Count > 1 ? volumes[0] : archivePath;
            Report("开始解压计时……");
            var extractSw = Stopwatch.StartNew();
            SevenZipArchive.ExtractAll(openTarget, format, extractDir, password);
            extractSw.Stop();

            double extractSeconds = extractSw.Elapsed.TotalSeconds;
            double extractMBps = ThroughputMBps(actualSize, extractSeconds);
            Report($"解压完成：耗时 {extractSeconds:F2}s，吞吐量 {extractMBps:F1} MB/s（按原始数据大小 / 解压耗时计算）");

            Report("流式 SHA-256 校验解压结果与原始文件是否一致（不整体加载进内存）……");
            string extractedPath = Path.Combine(extractDir, "perf_payload.bin");
            string? extractedHash = File.Exists(extractedPath) ? ComputeSha256(extractedPath) : null;
            bool ok = extractedHash is not null && extractedHash == srcHash;
            Report(ok
                ? "校验通过：解压结果与原始文件字节级一致。"
                : $"校验失败：SHA-256 不匹配（期望 {srcHash}，实际 {extractedHash ?? "<文件不存在>"}）。");

            return new PerfTestResult(
                ok, ok ? null : "解压后内容与原始文件不一致（SHA-256 不匹配）", log,
                actualSize, compressedBytes, createSeconds, extractSeconds, createMBps, extractMBps);
        }
        catch (Exception ex)
        {
            log.Add(ex.ToString());
            return new PerfTestResult(false, ex.Message, log, 0, 0, 0, 0, 0, 0);
        }
        finally
        {
            if (!keepFiles)
            {
                try { if (File.Exists(srcPath)) File.Delete(srcPath); } catch { /* 忽略清理失败 */ }
                try { if (Directory.Exists(extractDir)) Directory.Delete(extractDir, recursive: true); } catch { /* 忽略清理失败 */ }
                try
                {
                    // 归档可能分卷（archivePath 本身只是不带卷号后缀的基础文件名），
                    // 把 rootDir 下所有以它为前缀的文件都清掉。
                    string prefix = Path.GetFileName(archivePath);
                    foreach (var f in Directory.GetFiles(rootDir, prefix + "*"))
                        try { File.Delete(f); } catch { /* 忽略清理失败 */ }
                }
                catch { /* 忽略清理失败 */ }
            }
        }
    }

    private static double ThroughputMBps(long bytes, double seconds) =>
        seconds > 0 ? bytes / 1024.0 / 1024.0 / seconds : 0;

    /// <summary>
    /// 流式生成合成测试文件：按 <see cref="ChunkSize"/> 为单位在磁盘上写入，
    /// 偶数块填充固定重复文本（高度可压缩），奇数块填充固定种子的伪随机字节（不可压缩），
    /// 全程只占用一个 4MB 缓冲区，不会因为文件很大就把整个内容留在托管堆里。
    /// 边写边用 <see cref="IncrementalHash"/> 累加 SHA-256，写完直接拿到摘要，
    /// 不需要事后再完整读一遍源文件去算哈希。
    /// </summary>
    /// <returns>源文件内容的 SHA-256（大写十六进制）。</returns>
    private static string GeneratePayload(string path, long sizeBytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 1 << 20, FileOptions.SequentialScan);

        var rng = new Random(12345); // 固定种子：同一目标大小每次生成的内容都一样，便于复现问题
        var buffer = new byte[ChunkSize];
        byte[] pattern = Encoding.UTF8.GetBytes(
            "SevenZipLite large-file throughput benchmark payload -- repeated pattern segment. 7z/zip/tar 吞吐量测试用重复片段。 ");

        long written = 0;
        int chunkIndex = 0;
        while (written < sizeBytes)
        {
            int thisChunkSize = (int)Math.Min(ChunkSize, sizeBytes - written);
            var span = buffer.AsSpan(0, thisChunkSize);

            if (chunkIndex % 2 == 0)
            {
                for (int i = 0; i < thisChunkSize; i++)
                    span[i] = pattern[i % pattern.Length];
            }
            else
            {
                rng.NextBytes(span);
            }

            fs.Write(buffer, 0, thisChunkSize);
            hash.AppendData(buffer, 0, thisChunkSize);

            written += thisChunkSize;
            chunkIndex++;
        }

        fs.Flush(flushToDisk: true);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    /// <summary>用 <see cref="SHA256.ComputeHash(Stream)"/> 流式读取计算哈希，不整体加载进内存。</summary>
    private static string ComputeSha256(string path)
    {
        using var sha256 = SHA256.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha256.ComputeHash(fs));
    }

    private static string FormatSize(long bytes)
    {
        double mb = bytes / 1024.0 / 1024.0;
        return mb >= 1024 ? $"{mb / 1024.0:F2} GB" : $"{mb:F1} MB";
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
