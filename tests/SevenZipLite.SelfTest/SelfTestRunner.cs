namespace SevenZipLite.SelfTest;

/// <summary>单个用例的结果。</summary>
public sealed record SelfTestResult(string Name, bool Pass, string? Detail, IReadOnlyList<string> Log);

/// <summary>
/// 覆盖 7z / zip / tar / tar.gz 四种格式 × 是否分卷 × 是否密码 的往返测试
/// （创建归档 -> 列出条目 -> 解压 -> 校验内容一致）。
/// 这套用例最初写在 SevenZipLite.Tests（Linux 控制台）里用来定位/验证原生库的一系列
/// 链接与互操作 bug；现在提炼成共享库，好让 SevenZipLite.MauiDemo 在真机/模拟器上
/// 跑同一套用例，用来证明"裁剪版 7-Zip + 纯 C# P/Invoke"方案在 Android 上同样成立，
/// 而不仅仅是在开发机的 Linux 宿主环境里成立。
/// </summary>
public static class SelfTestRunner
{
    /// <summary>
    /// 在 <paramref name="rootDir"/>（调用方需保证可写、建议用完即删）下运行全部用例。
    /// </summary>
    public static IReadOnlyList<SelfTestResult> RunAll(string rootDir)
    {
        string srcDir = Path.Combine(rootDir, "src");
        string outDir = Path.Combine(rootDir, "out");
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(outDir);
        Directory.CreateDirectory(Path.Combine(srcDir, "sub"));

        File.WriteAllText(Path.Combine(srcDir, "hello.txt"), "Hello, 7-Zip on Android! 你好，世界。🚀🔥");
        File.WriteAllText(Path.Combine(srcDir, "sub", "nested.txt"),
            string.Concat(Enumerable.Repeat("line data\n", 500)));
        var bigData = new byte[37 * 1024]; // 37KB，配合 8KB 卷大小可以跨越多个卷边界
        new Random(42).NextBytes(bigData);
        File.WriteAllBytes(Path.Combine(srcDir, "big.bin"), bigData);

        var entries = new List<SevenZipCompressor.Entry>
        {
            new() { DiskPath = Path.Combine(srcDir, "hello.txt"), ArchivePath = "hello.txt" },
            new() { DiskPath = Path.Combine(srcDir, "sub", "nested.txt"), ArchivePath = "sub/nested.txt" },
            new() { DiskPath = Path.Combine(srcDir, "big.bin"), ArchivePath = "big.bin" },
        };

        var results = new List<SelfTestResult>
        {
            RunCase(entries, rootDir, outDir, "7z basic", ArchiveFormat.SevenZip, null, 0),
            RunCase(entries, rootDir, outDir, "7z with password", ArchiveFormat.SevenZip, "S3cr3t!密码", 0),
            RunCase(entries, rootDir, outDir, "7z split volumes", ArchiveFormat.SevenZip, null, 8192),
            RunCase(entries, rootDir, outDir, "7z split + password", ArchiveFormat.SevenZip, "vol-pwd", 8192),

            RunCase(entries, rootDir, outDir, "zip basic", ArchiveFormat.Zip, null, 0),
            RunCase(entries, rootDir, outDir, "zip with password", ArchiveFormat.Zip, "Zip#Pass", 0),
            RunCase(entries, rootDir, outDir, "zip split volumes", ArchiveFormat.Zip, null, 8192),
            // 之前这里有一条 "zip basic (force Store method, 诊断用)" 诊断用例，是排查
            // 2026-09 "zip 默认方法(Deflate) 写入统一报 E_FAIL(0x80004005)" 那个 bug 时加的。
            // 排查过程：一度怀疑是项目自建裁剪版原生库（Format7zLite）被 MSVC Release
            // 链接器裁掉了 Deflate 的全局注册对象；但改用 100% 官方未裁剪源码编译出的
            // Format7zF 后 bug 依旧稳定复现，才用 native 层临时诊断日志定位到真正根因：
            // 7-Zip 的 Zip 写入器只要选中一个需要真正编解码器的压缩方法（Deflate 等，Store
            // 除外），就会走多线程分发路径，由 native 另起一条工作线程回调我们暴露给它的
            // 托管 IInStream/IArchiveUpdateCallback 实现；而 .NET 的新版"源生成 COM"互操作
            // 没有传统 COM 那套套间/跨线程编组机制，从 native 自建、未依附 CLR 的线程
            // 回调进来天然不安全，第一次跨线程调用就直接失败。修复方式见
            // SevenZipCompressor.ForceSingleThreadedCompression 的详细注释——通过标准
            // ISetProperties::SetProperties("mt", 1) 强制单线程压缩，在全部三个平台
            // （官方 Windows 7z.dll、Linux/Android 编译产物）上统一生效，不需要重新编译
            // 任何原生库。这条 "Store method override" 用例保留作为方法覆盖功能本身的
            // 常规回归测试（与上面这个已修复的 bug 已无关系）。
            RunCase(entries, rootDir, outDir, "zip with Store method override", ArchiveFormat.Zip, null, 0,
                ZipMethodOverride.Store),
            RunCase(entries, rootDir, outDir, "zip with explicit Deflate method override", ArchiveFormat.Zip, null, 0,
                ZipMethodOverride.Deflate),

            RunCase(entries, rootDir, outDir, "tar basic", ArchiveFormat.Tar, null, 0),
            RunCase(entries, rootDir, outDir, "tar split volumes", ArchiveFormat.Tar, null, 8192),

            RunCase(entries, rootDir, outDir, "tar.gz basic", ArchiveFormat.TarGZip, null, 0),
            RunCase(entries, rootDir, outDir, "tar.gz split volumes", ArchiveFormat.TarGZip, null, 8192),
        };

        return results;
    }

    private static SelfTestResult RunCase(
        List<SevenZipCompressor.Entry> entries, string root, string outDir,
        string name, ArchiveFormat format, string? password, long volumeSize,
        ZipMethodOverride? zipMethodOverride = null)
    {
        var log = new List<string>();
        try
        {
            string archivePath = Path.Combine(root, name.Replace(' ', '_') + Ext(format));
            string extractDir = Path.Combine(outDir, name.Replace(' ', '_'));
            Directory.CreateDirectory(extractDir);

            var volumes = SevenZipCompressor.CreateArchive(entries, archivePath, format, password, volumeSize, zipMethodOverride);
            log.Add($"生成文件: {string.Join(", ", volumes.Select(Path.GetFileName))}");

            string openTarget = volumeSize > 0 ? volumes[0] : archivePath;
            var listed = SevenZipArchive.List(openTarget, format);
            log.Add($"列出条目数: {listed.Count}");

            SevenZipArchive.ExtractAll(openTarget, format, extractDir, password);
            bool ok = VerifyExtracted(entries, extractDir);
            return new SelfTestResult(name, ok, ok ? null : "解压后内容与原文件不一致", log);
        }
        catch (Exception ex)
        {
            log.Add(ex.ToString());
            return new SelfTestResult(name, false, ex.Message, log);
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
