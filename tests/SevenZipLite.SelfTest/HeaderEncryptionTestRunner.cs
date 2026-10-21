namespace SevenZipLite.SelfTest;

/// <summary>
/// 专项验证 7z「加密文件头」（header encryption，<c>CreateArchive</c>/
/// <c>CreateArchiveFromDirectory</c>/<c>SevenZipArchiveEditor</c> 的 <c>encryptHeaders</c>
/// 参数，对应官方 7z 命令行的 <c>-mhe=on</c>）行为是否符合预期：
/// - 默认（<c>encryptHeaders: true</c>）：不传密码连 <see cref="SevenZipArchive.List"/> 都应该
///   失败（归档头本身就需要密码才能解析），这是本库刻意选择的、和官方 7z.exe 默认值相反的
///   默认行为，详见 <see cref="SevenZipCompressor.CreateArchive"/> 的参数注释。
/// - 显式传 <c>encryptHeaders: false</c>：应该退回"只加密内容、文件名仍然明文可见"的旧行为
///   （和官方 7z.exe 只传 <c>-p</c> 不加 <c>-mhe=on</c> 时完全一致）。
/// - zip 格式不支持这个能力：不管 <c>encryptHeaders</c> 传什么，文件名永远明文可见。
/// 和 <see cref="SelfTestRunner"/>/<see cref="ArchiveEditorTestRunner"/> 是正交的专项用例，
/// 由 <see cref="SelfTestRunner.RunAll"/> 统一调用、汇总进同一份结果列表。
/// </summary>
public static class HeaderEncryptionTestRunner
{
    public static IReadOnlyList<SelfTestResult> RunAll(string root)
    {
        return new List<SelfTestResult>
        {
            RunDefaultEncryptsHeaders(root),
            RunExplicitOptOutKeepsHeadersPlain(root),
            RunZipIgnoresEncryptHeaders(root),
            RunEditorDefaultEncryptsHeaders(root),
            RunEditorOptOutKeepsHeadersPlain(root),
        };
    }

    private static string Dir(string root, string name)
    {
        string d = Path.Combine(root, "heenc_" + name);
        Directory.CreateDirectory(d);
        return d;
    }

    private static List<SevenZipCompressor.Entry> SeedEntries(string work)
    {
        string srcDir = Path.Combine(work, "src");
        Directory.CreateDirectory(srcDir);
        string a = Path.Combine(srcDir, "secret-name.txt");
        File.WriteAllText(a, "top secret content");
        return new List<SevenZipCompressor.Entry> { new() { DiskPath = a, ArchivePath = "secret-name.txt" } };
    }

    private static SelfTestResult RunDefaultEncryptsHeaders(string root)
    {
        string name = "7z_default_encrypts_headers";
        var log = new List<string>();
        try
        {
            string work = Dir(root, name);
            var entries = SeedEntries(work);
            string archivePath = Path.Combine(work, "out.7z");

            // 不传 encryptHeaders，走默认值 true。
            SevenZipCompressor.CreateArchive(entries, archivePath, ArchiveFormat.SevenZip, password: "S3cr3t!");

            bool listWithoutPasswordThrew = false;
            try { SevenZipArchive.List(archivePath, ArchiveFormat.SevenZip); }
            catch (InvalidOperationException) { listWithoutPasswordThrew = true; }
            log.Add($"不传密码 List() 是否按预期失败（头部已加密）: {listWithoutPasswordThrew}");

            var listedWithPassword = SevenZipArchive.List(archivePath, ArchiveFormat.SevenZip, "S3cr3t!");
            bool listWithPasswordOk = listedWithPassword.Count == 1 && listedWithPassword[0].Path == "secret-name.txt";
            log.Add($"传正确密码 List() 是否成功: {listWithPasswordOk}");

            bool ok = listWithoutPasswordThrew && listWithPasswordOk;
            return new SelfTestResult(name, ok, ok ? null : "默认 encryptHeaders=true 的行为不符合预期", log);
        }
        catch (Exception ex)
        {
            log.Add(ex.ToString());
            return new SelfTestResult(name, false, ex.Message, log);
        }
    }

    private static SelfTestResult RunExplicitOptOutKeepsHeadersPlain(string root)
    {
        string name = "7z_opt_out_keeps_headers_plain";
        var log = new List<string>();
        try
        {
            string work = Dir(root, name);
            var entries = SeedEntries(work);
            string archivePath = Path.Combine(work, "out.7z");

            SevenZipCompressor.CreateArchive(entries, archivePath, ArchiveFormat.SevenZip,
                password: "S3cr3t!", encryptHeaders: false);

            // 显式关闭头部加密：不传密码应该也能看到文件名（旧行为，和官方 7z.exe 默认值一致）。
            var listedWithoutPassword = SevenZipArchive.List(archivePath, ArchiveFormat.SevenZip);
            bool ok = listedWithoutPassword.Count == 1 && listedWithoutPassword[0].Path == "secret-name.txt";
            log.Add($"encryptHeaders=false 时不传密码 List() 是否仍能看到文件名: {ok}");

            return new SelfTestResult(name, ok, ok ? null : "encryptHeaders=false 应该保留旧的\"仅加密内容\"行为", log);
        }
        catch (Exception ex)
        {
            log.Add(ex.ToString());
            return new SelfTestResult(name, false, ex.Message, log);
        }
    }

    private static SelfTestResult RunZipIgnoresEncryptHeaders(string root)
    {
        string name = "zip_ignores_encrypt_headers";
        var log = new List<string>();
        try
        {
            string work = Dir(root, name);
            var entries = SeedEntries(work);
            string archivePath = Path.Combine(work, "out.zip");

            // zip 格式本身不支持头部加密（标准 zip 的中央目录文件名永远明文），即使
            // encryptHeaders 默认 true，对 zip 也应该是无效果的 no-op。
            SevenZipCompressor.CreateArchive(entries, archivePath, ArchiveFormat.Zip, password: "S3cr3t!");

            var listedWithoutPassword = SevenZipArchive.List(archivePath, ArchiveFormat.Zip);
            bool ok = listedWithoutPassword.Count == 1 && listedWithoutPassword[0].Path == "secret-name.txt";
            log.Add($"zip 格式即使给了密码，不传密码 List() 是否仍能看到文件名（格式本身不支持头部加密）: {ok}");

            return new SelfTestResult(name, ok, ok ? null : "zip 格式不应受 encryptHeaders 影响", log);
        }
        catch (Exception ex)
        {
            log.Add(ex.ToString());
            return new SelfTestResult(name, false, ex.Message, log);
        }
    }

    private static SelfTestResult RunEditorDefaultEncryptsHeaders(string root)
    {
        string name = "editor_default_encrypts_headers";
        var log = new List<string>();
        try
        {
            string work = Dir(root, name);
            var entries = SeedEntries(work);
            string archivePath = Path.Combine(work, "seed.7z");
            SevenZipCompressor.CreateArchive(entries, archivePath, ArchiveFormat.SevenZip, password: "Edit0r!");

            string newFile = Path.Combine(work, "another.txt");
            File.WriteAllText(newFile, "more secrets");

            using (var editor = SevenZipArchiveEditor.Open(archivePath, ArchiveFormat.SevenZip, "Edit0r!"))
            {
                editor.AddFile(newFile, "another.txt");
                editor.SaveChanges(); // 默认 encryptHeaders=true
            }

            bool listWithoutPasswordThrew = false;
            try { SevenZipArchive.List(archivePath, ArchiveFormat.SevenZip); }
            catch (InvalidOperationException) { listWithoutPasswordThrew = true; }
            log.Add($"SevenZipArchiveEditor.SaveChanges 后不传密码 List() 是否按预期失败: {listWithoutPasswordThrew}");

            var listedWithPassword = SevenZipArchive.List(archivePath, ArchiveFormat.SevenZip, "Edit0r!");
            bool contentOk = listedWithPassword.Select(i => i.Path).OrderBy(p => p, StringComparer.Ordinal)
                .SequenceEqual(new[] { "another.txt", "secret-name.txt" });
            log.Add($"传密码后条目正确: {contentOk}");

            bool ok = listWithoutPasswordThrew && contentOk;
            return new SelfTestResult(name, ok, ok ? null : "SevenZipArchiveEditor 默认 encryptHeaders=true 的行为不符合预期", log);
        }
        catch (Exception ex)
        {
            log.Add(ex.ToString());
            return new SelfTestResult(name, false, ex.Message, log);
        }
    }

    private static SelfTestResult RunEditorOptOutKeepsHeadersPlain(string root)
    {
        string name = "editor_opt_out_keeps_headers_plain";
        var log = new List<string>();
        try
        {
            string work = Dir(root, name);
            var entries = SeedEntries(work);
            string archivePath = Path.Combine(work, "seed.7z");
            SevenZipCompressor.CreateArchive(entries, archivePath, ArchiveFormat.SevenZip,
                password: "Edit0r!", encryptHeaders: false);

            using (var editor = SevenZipArchiveEditor.Open(archivePath, ArchiveFormat.SevenZip, "Edit0r!", encryptHeaders: false))
            {
                editor.Delete("secret-name.txt"); // 随便触发一次 SaveChanges，内容不重要
                string newFile = Path.Combine(work, "x.txt");
                File.WriteAllText(newFile, "x");
                editor.AddFile(newFile, "x.txt");
                editor.SaveChanges();
            }

            var listedWithoutPassword = SevenZipArchive.List(archivePath, ArchiveFormat.SevenZip);
            bool ok = listedWithoutPassword.Count == 1 && listedWithoutPassword[0].Path == "x.txt";
            log.Add($"editor encryptHeaders=false 时 SaveChanges 后不传密码 List() 是否仍能看到文件名: {ok}");

            return new SelfTestResult(name, ok, ok ? null : "SevenZipArchiveEditor 的 encryptHeaders=false 没有正确生效", log);
        }
        catch (Exception ex)
        {
            log.Add(ex.ToString());
            return new SelfTestResult(name, false, ex.Message, log);
        }
    }
}
