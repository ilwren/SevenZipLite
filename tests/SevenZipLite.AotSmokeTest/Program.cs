using SevenZipLite;

// 端到端冒烟测试：创建带密码的 zip -> 列出条目 -> 解压 -> 校验内容。
// 在普通 JIT 模式下跑这个项目也完全有意义（本来就是真实的功能回归测试），但它存在的
// 主要目的是被 .github/workflows/release.yml 的 verify-aot job 以
// `dotnet publish -r linux-x64 -p:PublishAot=true` 的方式发布成原生可执行文件后再运行
// 一遍——只有这样才能验证 Native AOT 产物里的 COM 互操作/P-Invoke 字符串编组在真实
// 运行时下也是正确的，而不只是编译期 0 警告。
//
// End-to-end smoke test: create a password-protected zip -> list entries -> extract ->
// verify content. Running this under ordinary JIT also makes sense on its own (it's a
// genuine functional regression test), but its main purpose is to be published as a
// native executable via `dotnet publish -r linux-x64 -p:PublishAot=true` by the
// verify-aot job in .github/workflows/release.yml, then actually run — only running the
// real Native AOT output can verify that COM interop / P-Invoke string marshalling are
// correct at runtime, not just that the compiler produced 0 warnings.

var workDir = Path.Combine(Path.GetTempPath(), "aot-smoke-" + Guid.NewGuid());
Directory.CreateDirectory(workDir);

try
{
    const string originalContent = "Hello from Native AOT! 你好，原生 AOT。";
    var srcFile = Path.Combine(workDir, "hello.txt");
    File.WriteAllText(srcFile, originalContent);

    var zipPath = Path.Combine(workDir, "out.zip");
    SevenZipCompressor.CreateArchive(
        new[] { new SevenZipCompressor.Entry { DiskPath = srcFile, ArchivePath = "hello.txt" } },
        zipPath,
        ArchiveFormat.Zip,
        password: "aot-smoke-pw");

    Console.WriteLine($"[1/3] Created archive: {zipPath} ({new FileInfo(zipPath).Length} bytes)");

    var entries = SevenZipArchive.List(zipPath, ArchiveFormat.Zip);
    if (entries.Count != 1 || entries[0].Path != "hello.txt")
    {
        Console.Error.WriteLine($"FAIL: unexpected entry list (count={entries.Count})");
        return 1;
    }
    Console.WriteLine($"[2/3] Listed entries: {entries.Count} ({entries[0].Path}, {entries[0].Size} bytes)");

    var extractDir = Path.Combine(workDir, "extracted");
    SevenZipArchive.ExtractAll(zipPath, ArchiveFormat.Zip, extractDir, password: "aot-smoke-pw");
    var extractedContent = File.ReadAllText(Path.Combine(extractDir, "hello.txt"));

    if (extractedContent != originalContent)
    {
        Console.Error.WriteLine("FAIL: extracted content does not match original.");
        Console.Error.WriteLine($"  expected: {originalContent}");
        Console.Error.WriteLine($"  actual:   {extractedContent}");
        return 1;
    }
    Console.WriteLine($"[3/3] Extracted content matches original: {extractedContent}");

    // 额外验证 SevenZipArchiveEditor 的增量更新路径——它用到了一个之前任何 AOT 冒烟测试都
    // 没覆盖过的互操作手法（对同一个原生 IInArchive 指针做 Marshal.QueryInterface 换
    // IOutArchive），单靠编译期 <IsAotCompatible> 分析完全看不出这条路径在真正的 Native AOT
    // 产物里是否可行，必须像上面一样实际跑一遍。
    //
    // Also exercise SevenZipArchiveEditor's incremental-update path — it uses an interop
    // technique no previous AOT smoke test covered (Marshal.QueryInterface for IOutArchive
    // off the same native IInArchive pointer). Compile-time <IsAotCompatible> analysis alone
    // cannot tell whether this path actually works in a real Native AOT build; it must be
    // run for real, just like the steps above.
    var newFile = Path.Combine(workDir, "added.txt");
    File.WriteAllText(newFile, "added under AOT");
    using (var editor = SevenZipArchiveEditor.Open(zipPath, ArchiveFormat.Zip, password: "aot-smoke-pw"))
    {
        editor.AddFile(newFile, "added.txt");
        editor.Delete("hello.txt");
        editor.SaveChanges();
    }

    var afterEdit = SevenZipArchive.List(zipPath, ArchiveFormat.Zip);
    if (afterEdit.Count != 1 || afterEdit[0].Path != "added.txt")
    {
        Console.Error.WriteLine($"FAIL: SevenZipArchiveEditor.SaveChanges produced unexpected entries (count={afterEdit.Count})");
        return 1;
    }

    var extractDir2 = Path.Combine(workDir, "extracted-after-edit");
    SevenZipArchive.ExtractAll(zipPath, ArchiveFormat.Zip, extractDir2, password: "aot-smoke-pw");
    var addedContent = File.ReadAllText(Path.Combine(extractDir2, "added.txt"));
    if (addedContent != "added under AOT")
    {
        Console.Error.WriteLine("FAIL: content added via SevenZipArchiveEditor does not match after round-trip.");
        return 1;
    }
    Console.WriteLine("[4/4] SevenZipArchiveEditor incremental add/delete + SaveChanges verified under AOT");

    Console.WriteLine("AOT SMOKE TEST: PASS");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"AOT SMOKE TEST: FAIL ({ex.GetType().Name}: {ex.Message})");
    Console.Error.WriteLine(ex.StackTrace);
    return 1;
}
finally
{
    try { Directory.Delete(workDir, recursive: true); } catch { /* best-effort cleanup */ }
}
