using SevenZipLite;
using SevenZipLite.SelfTest;

string root = Path.Combine(Path.GetTempPath(), "sevenziplite_test_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

Console.WriteLine("=== SevenZipLite 往返测试 (Linux x86_64 宿主机, 复用与 Android 交叉编译完全相同的 C# 互操作层) ===");
Console.WriteLine($"临时目录: {root}\n");

var results = SelfTestRunner.RunAll(root);

int pass = 0, fail = 0;
foreach (var r in results)
{
    foreach (var line in r.Log) Console.WriteLine($"  -> {line}");
    Console.WriteLine(r.Pass ? $"[PASS] {r.Name}" : $"[FAIL] {r.Name}  {r.Detail}");
    if (r.Pass) pass++; else fail++;
}

Console.WriteLine();
Console.WriteLine($"=== 结果: {pass} 通过, {fail} 失败 ===");

// 可选：大文件吞吐量测试。上面的往返自检每种格式只用几十 KB 的合成数据，目的是
// "快、覆盖 格式×分卷×密码 的正确性矩阵"，不适合用来观察真实吞吐量，所以这里
// 单独做成需要显式传参才会跑的选项：
//   dotnet run -c Release -- --perf              （默认 256MB / 7z）
//   dotnet run -c Release -- --perf 512           （512MB / 7z）
//   dotnet run -c Release -- --perf 1024 zip      （1024MB / zip，注意会比较慢）
// 可选格式: 7z（默认）/ zip / tar / targz
bool perfFailed = false;
int perfIdx = Array.IndexOf(args, "--perf");
if (perfIdx >= 0)
{
    long sizeMB = 256;
    if (perfIdx + 1 < args.Length && long.TryParse(args[perfIdx + 1], out var parsedMB))
        sizeMB = parsedMB;
    string formatArg = perfIdx + 2 < args.Length ? args[perfIdx + 2] : "7z";
    var format = formatArg.ToLowerInvariant() switch
    {
        "zip" => ArchiveFormat.Zip,
        "tar" => ArchiveFormat.Tar,
        "targz" or "tar.gz" => ArchiveFormat.TarGZip,
        _ => ArchiveFormat.SevenZip
    };

    Console.WriteLine();
    Console.WriteLine($"=== 大文件吞吐量测试：目标 {sizeMB} MB，格式 {format} ===");
    string perfRoot = Path.Combine(Path.GetTempPath(), "sevenziplite_perf_" + Guid.NewGuid().ToString("N"));
    var perfResult = PerfTestRunner.Run(perfRoot, sizeMB * 1024L * 1024L, format,
        onProgress: msg => Console.WriteLine($"  -> {msg}"));

    Console.WriteLine(perfResult.Pass
        ? $"[PASS] 大文件吞吐量测试：原始 {perfResult.OriginalBytes / 1024.0 / 1024.0:F1} MB -> " +
          $"压缩 {perfResult.CompressedBytes / 1024.0 / 1024.0:F1} MB（{perfResult.CompressionRatioPercent:F1}%），" +
          $"压缩 {perfResult.CreateSeconds:F2}s / {perfResult.CreateThroughputMBps:F1} MB/s，" +
          $"解压 {perfResult.ExtractSeconds:F2}s / {perfResult.ExtractThroughputMBps:F1} MB/s"
        : $"[FAIL] 大文件吞吐量测试：{perfResult.Detail}");
    perfFailed = !perfResult.Pass;

    try { Directory.Delete(perfRoot, recursive: true); } catch { /* 忽略清理失败 */ }
}

// 可选：多线程压缩/解压压力测试（不属于 13 项正确性自检，需要显式传参才会跑）：
//   dotnet run -c Release -- --stress
// 用来在"多轮 x 较多文件"的场景下给多线程压缩路径的稳定性提供比默认自检更强的信心，
// 详见 SevenZipLite.SelfTest.MultiThreadStressRunner 类型注释。
if (Array.IndexOf(args, "--stress") >= 0)
{
    bool RunStress(string label, ArchiveFormat fmt, string? pwd) => MultiThreadStressRunner.Run(
        Path.Combine(Path.GetTempPath(), "sevenziplite_stress_" + Guid.NewGuid().ToString("N")),
        iterations: 15, fileCount: 20, fmt, pwd,
        log: msg => Console.WriteLine($"  -> {msg}"));

    Console.WriteLine();
    Console.WriteLine("=== 多线程压力测试：20 个文件 x 15 轮 (zip) ===");
    bool stressOk = RunStress("zip", ArchiveFormat.Zip, null);
    Console.WriteLine(stressOk ? "[PASS] zip 压力测试全部通过" : "[FAIL] zip 压力测试发现问题");

    Console.WriteLine();
    Console.WriteLine("=== 多线程压力测试：20 个文件 x 15 轮 (7z + 密码) ===");
    bool stressOk7z = RunStress("7z", ArchiveFormat.SevenZip, "P@ss");
    Console.WriteLine(stressOk7z ? "[PASS] 7z 压力测试全部通过" : "[FAIL] 7z 压力测试发现问题");

    Console.WriteLine();
    Console.WriteLine("=== 多线程压力测试：20 个文件 x 15 轮 (tar) ===");
    bool stressOkTar = RunStress("tar", ArchiveFormat.Tar, null);
    Console.WriteLine(stressOkTar ? "[PASS] tar 压力测试全部通过" : "[FAIL] tar 压力测试发现问题");

    return (fail == 0 && !perfFailed && stressOk && stressOk7z && stressOkTar) ? 0 : 1;
}

return fail == 0 && !perfFailed ? 0 : 1;
