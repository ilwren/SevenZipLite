using System.Security.Cryptography;

namespace SevenZipLite.SelfTest;

/// <summary>
/// 多线程压缩/解压的往返压力测试：反复创建一个包含若干个文件的归档、解压、逐文件
/// SHA-256 校验，用来给"多线程压缩路径（<c>mt&gt;1</c>）是否安全"这件事提供比
/// <see cref="SelfTestRunner"/> 里 13 个用例（都只跑 1 轮、文件很少）更强的信心——
/// 历史上就是这套测试（当时还是一次性脚本）在增大文件数/线程数后复现了
/// <see cref="SevenZipLite.Callbacks.UpdateCallback"/> 的共享字段竞态 bug，具体根因
/// 见 <c>SevenZipCompressor.cs</c> 里 <c>SetUInt32Properties</c> 方法上的详细注释。
///
/// 这是一个独立于 13 项正确性自检（<see cref="SelfTestRunner.RunAll"/>）之外的可选
/// 诊断工具，不混入默认自检流程（跑起来比较慢，且目的不同：这里关心的是"多轮/高并发下
/// 会不会出现偶发的竞态"，而不是"单次操作的结果是否正确"）。三个 Demo 入口（Linux 控制台
/// <c>SevenZipLite.Tests</c>、WPF、MAUI）都应该提供触发它的独立入口，但不应该让它默认跟随
/// 应用启动自动运行。
/// </summary>
public static class MultiThreadStressRunner
{
    /// <param name="rootDir">调用方保证可写、跑完会被清空删除的临时目录。</param>
    /// <param name="iterations">重复创建+解压+校验的轮数。</param>
    /// <param name="fileCount">每一轮归档里包含的文件数。</param>
    /// <param name="format">要压力测试的归档格式。</param>
    /// <param name="password">可选密码（仅 7z/zip 生效）。</param>
    /// <param name="log">进度/结果输出，调用方可以接到控制台、UI 日志控件等任意地方。</param>
    public static bool Run(
        string rootDir,
        int iterations,
        int fileCount,
        ArchiveFormat format = ArchiveFormat.Zip,
        string? password = null,
        Action<string>? log = null)
    {
        log ??= _ => { };
        Directory.CreateDirectory(rootDir);
        string srcDir = Path.Combine(rootDir, "src");
        Directory.CreateDirectory(srcDir);

        var rng = new Random(999);
        var entries = new List<SevenZipCompressor.Entry>();
        var hashes = new Dictionary<string, string>();
        for (int i = 0; i < fileCount; i++)
        {
            string p = Path.Combine(srcDir, $"file_{i}.bin");
            byte[] data = new byte[rng.Next(50_000, 2_000_000)];
            rng.NextBytes(data);
            File.WriteAllBytes(p, data);
            hashes[$"f{i}.bin"] = Convert.ToHexString(SHA256.HashData(data));
            entries.Add(new SevenZipCompressor.Entry { DiskPath = p, ArchivePath = $"f{i}.bin" });
        }

        bool allOk = true;
        for (int iter = 0; iter < iterations; iter++)
        {
            string ext = format switch { ArchiveFormat.SevenZip => ".7z", ArchiveFormat.Tar => ".tar", ArchiveFormat.TarGZip => ".tar.gz", _ => ".zip" };
            string archivePath = Path.Combine(rootDir, $"stress_{iter}{ext}");
            string extractDir = Path.Combine(rootDir, $"extract_{iter}");
            try
            {
                var methodOverride = format == ArchiveFormat.Zip ? ZipMethodOverride.Deflate : (ZipMethodOverride?)null;
                var volumes = SevenZipCompressor.CreateArchive(entries, archivePath, format, password, 0, methodOverride);
                Directory.CreateDirectory(extractDir);
                SevenZipArchive.ExtractAll(archivePath, format, extractDir, password);

                foreach (var kv in hashes)
                {
                    string extracted = Path.Combine(extractDir, kv.Key);
                    if (!File.Exists(extracted))
                    {
                        log($"[stress iter {iter}] 缺少文件 {kv.Key}");
                        allOk = false;
                        continue;
                    }
                    string h = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(extracted)));
                    if (h != kv.Value)
                    {
                        log($"[stress iter {iter}] {kv.Key} 哈希不一致");
                        allOk = false;
                    }
                }
                log($"[stress iter {iter}] OK ({fileCount} 文件, {volumes.Count} 卷)");
            }
            catch (Exception ex)
            {
                log($"[stress iter {iter}] 异常: {ex.Message}");
                allOk = false;
            }
            finally
            {
                try { if (File.Exists(archivePath)) File.Delete(archivePath); } catch { /* 忽略 */ }
                try { if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true); } catch { /* 忽略 */ }
            }
        }

        try { Directory.Delete(rootDir, true); } catch { /* 忽略 */ }
        return allOk;
    }
}
