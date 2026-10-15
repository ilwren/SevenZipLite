namespace SevenZipLite.SelfTest;

/// <summary>
/// 覆盖新增的三块 API 增强的专项用例：
/// 1) <see cref="SevenZipCompressor.CreateArchiveFromDirectory"/> 整目录压缩便捷方法
///    （includeBaseDirectory 两种取值 + filter 回调）；
/// 2) <see cref="SevenZipArchiveEditor"/>（using 管理的增量增删条目会话）——开档/新增/删除/
///    替换/批量删除/保存/重新打开验证/不保存直接 Dispose 应当丢弃改动/Create() 从零新建
///    几条关键路径；
/// 3) 顺带验证 <see cref="SevenZipArchiveEditor"/> 对不支持的格式（tar/tar.gz）正确拒绝。
/// 和 <see cref="SelfTestRunner"/> 里纯同步的格式×分卷×密码矩阵是正交的，单独成类，由
/// <see cref="SelfTestRunner.RunAll"/> 统一调用、汇总进同一份结果列表。
/// </summary>
public static class ArchiveEditorTestRunner
{
    public static IReadOnlyList<SelfTestResult> RunAll(string root)
    {
        return new List<SelfTestResult>
        {
            RunCreateFromDirectoryIncludeBase(root),
            RunCreateFromDirectoryFlatten(root),
            RunCreateFromDirectoryFilter(root),
            RunCreateFromDirectoryEmptyThrows(root),

            RunEditorAddDeleteSaveRoundTrip(root, ArchiveFormat.SevenZip),
            RunEditorAddDeleteSaveRoundTrip(root, ArchiveFormat.Zip),
            RunEditorWithPassword(root),
            RunEditorReplaceSemantics(root),
            RunEditorBulkDeletePredicate(root),
            RunEditorExtractAllReflectsPendingState(root),
            RunEditorDisposeWithoutSaveDiscardsChanges(root),
            RunEditorCreateFromScratch(root),
            RunEditorOpenMissingFileThrows(root),
            RunEditorUnsupportedFormatThrows(root),
        };
    }

    private static string Dir(string root, string name)
    {
        string d = Path.Combine(root, "editor_" + name);
        Directory.CreateDirectory(d);
        return d;
    }

    // ---------------- CreateArchiveFromDirectory ----------------

    private static SelfTestResult RunCreateFromDirectoryIncludeBase(string root)
    {
        string name = "fromdir_include_base";
        var log = new List<string>();
        try
        {
            string work = Dir(root, name);
            string srcDir = Path.Combine(work, "photos");
            Directory.CreateDirectory(Path.Combine(srcDir, "sub"));
            File.WriteAllText(Path.Combine(srcDir, "a.txt"), "A");
            File.WriteAllText(Path.Combine(srcDir, "sub", "b.txt"), "B");

            string archivePath = Path.Combine(work, "out.7z");
            SevenZipCompressor.CreateArchiveFromDirectory(srcDir, archivePath, ArchiveFormat.SevenZip,
                includeBaseDirectory: true);

            var listed = SevenZipArchive.List(archivePath, ArchiveFormat.SevenZip);
            var paths = listed.Where(i => !i.IsDir).Select(i => i.Path).OrderBy(p => p, StringComparer.Ordinal).ToList();
            log.Add($"条目: {string.Join(", ", paths)}");

            bool ok = paths.SequenceEqual(new[] { "photos/a.txt", "photos/sub/b.txt" });
            return new SelfTestResult(name, ok, ok ? null : "includeBaseDirectory=true 时归档内路径不符合预期", log);
        }
        catch (Exception ex)
        {
            log.Add(ex.ToString());
            return new SelfTestResult(name, false, ex.Message, log);
        }
    }

    private static SelfTestResult RunCreateFromDirectoryFlatten(string root)
    {
        string name = "fromdir_flatten";
        var log = new List<string>();
        try
        {
            string work = Dir(root, name);
            string srcDir = Path.Combine(work, "photos");
            Directory.CreateDirectory(Path.Combine(srcDir, "sub"));
            File.WriteAllText(Path.Combine(srcDir, "a.txt"), "A");
            File.WriteAllText(Path.Combine(srcDir, "sub", "b.txt"), "B");

            string archivePath = Path.Combine(work, "out.zip");
            SevenZipCompressor.CreateArchiveFromDirectory(srcDir, archivePath, ArchiveFormat.Zip,
                includeBaseDirectory: false);

            var listed = SevenZipArchive.List(archivePath, ArchiveFormat.Zip);
            var paths = listed.Where(i => !i.IsDir).Select(i => i.Path).OrderBy(p => p, StringComparer.Ordinal).ToList();
            log.Add($"条目: {string.Join(", ", paths)}");

            bool ok = paths.SequenceEqual(new[] { "a.txt", "sub/b.txt" });
            return new SelfTestResult(name, ok, ok ? null : "includeBaseDirectory=false 时归档内路径不符合预期", log);
        }
        catch (Exception ex)
        {
            log.Add(ex.ToString());
            return new SelfTestResult(name, false, ex.Message, log);
        }
    }

    private static SelfTestResult RunCreateFromDirectoryFilter(string root)
    {
        string name = "fromdir_filter";
        var log = new List<string>();
        try
        {
            string work = Dir(root, name);
            string srcDir = Path.Combine(work, "proj");
            Directory.CreateDirectory(Path.Combine(srcDir, ".git"));
            File.WriteAllText(Path.Combine(srcDir, "readme.txt"), "readme");
            File.WriteAllText(Path.Combine(srcDir, ".git", "config"), "ignored");

            string archivePath = Path.Combine(work, "out.zip");
            SevenZipCompressor.CreateArchiveFromDirectory(srcDir, archivePath, ArchiveFormat.Zip,
                includeBaseDirectory: false,
                filter: p => !p.Replace(Path.DirectorySeparatorChar, '/').Contains("/.git/"));

            var listed = SevenZipArchive.List(archivePath, ArchiveFormat.Zip);
            var paths = listed.Where(i => !i.IsDir).Select(i => i.Path).ToList();
            log.Add($"条目: {string.Join(", ", paths)}");

            bool ok = paths.Count == 1 && paths[0] == "readme.txt";
            return new SelfTestResult(name, ok, ok ? null : "filter 回调没有正确排除 .git 下的文件", log);
        }
        catch (Exception ex)
        {
            log.Add(ex.ToString());
            return new SelfTestResult(name, false, ex.Message, log);
        }
    }

    private static SelfTestResult RunCreateFromDirectoryEmptyThrows(string root)
    {
        string name = "fromdir_empty_throws";
        var log = new List<string>();
        try
        {
            string work = Dir(root, name);
            string srcDir = Path.Combine(work, "empty");
            Directory.CreateDirectory(srcDir);

            bool threw = false;
            try
            {
                SevenZipCompressor.CreateArchiveFromDirectory(srcDir, Path.Combine(work, "out.zip"), ArchiveFormat.Zip);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }

            log.Add($"空目录压缩是否按预期抛出 InvalidOperationException: {threw}");
            return new SelfTestResult(name, threw, threw ? null : "空目录没有抛出预期的异常", log);
        }
        catch (Exception ex)
        {
            log.Add(ex.ToString());
            return new SelfTestResult(name, false, ex.Message, log);
        }
    }

    // ---------------- SevenZipArchiveEditor ----------------

    private static (string archivePath, string a, string b, string c) SeedArchive(
        string work, ArchiveFormat format, string? password = null)
    {
        string srcDir = Path.Combine(work, "src");
        Directory.CreateDirectory(srcDir);
        string a = Path.Combine(srcDir, "a.txt");
        string b = Path.Combine(srcDir, "b.txt");
        string c = Path.Combine(srcDir, "c.txt");
        File.WriteAllText(a, "AAA");
        File.WriteAllText(b, "BBB");
        File.WriteAllText(c, "CCC");

        string ext = format == ArchiveFormat.Zip ? ".zip" : ".7z";
        string archivePath = Path.Combine(work, "seed" + ext);
        var entries = new List<SevenZipCompressor.Entry>
        {
            new() { DiskPath = a, ArchivePath = "a.txt" },
            new() { DiskPath = b, ArchivePath = "b.txt" },
            new() { DiskPath = c, ArchivePath = "c.txt" },
        };
        SevenZipCompressor.CreateArchive(entries, archivePath, format, password);
        return (archivePath, a, b, c);
    }

    private static SelfTestResult RunEditorAddDeleteSaveRoundTrip(string root, ArchiveFormat format)
    {
        string name = $"editor_add_delete_save_{format}";
        var log = new List<string>();
        try
        {
            string work = Dir(root, name);
            var (archivePath, _, _, _) = SeedArchive(work, format);

            string newFile = Path.Combine(work, "d.txt");
            File.WriteAllText(newFile, "DDD");

            using (var editor = SevenZipArchiveEditor.Open(archivePath, format))
            {
                log.Add($"打开后条目: {string.Join(",", editor.List().Select(e => e.Path))}");
                bool deleted = editor.Delete("b.txt");
                editor.AddFile(newFile, "d.txt");
                log.Add($"Delete(\"b.txt\") 返回: {deleted}");
                log.Add($"HasPendingChanges (保存前): {editor.HasPendingChanges}");
                editor.SaveChanges();
                log.Add($"HasPendingChanges (保存后): {editor.HasPendingChanges}");
                log.Add($"保存后条目: {string.Join(",", editor.List().Select(e => e.Path))}");
            }

            // 用完全独立的静态只读 API 重新打开，验证真的落盘了。
            var listed = SevenZipArchive.List(archivePath, format);
            var paths = listed.Select(i => i.Path).OrderBy(p => p, StringComparer.Ordinal).ToList();
            log.Add($"重新打开后条目: {string.Join(",", paths)}");

            string extractDir = Path.Combine(work, "extracted");
            SevenZipArchive.ExtractAll(archivePath, format, extractDir);
            bool contentOk = File.ReadAllText(Path.Combine(extractDir, "a.txt")) == "AAA"
                && !File.Exists(Path.Combine(extractDir, "b.txt"))
                && File.ReadAllText(Path.Combine(extractDir, "c.txt")) == "CCC"
                && File.ReadAllText(Path.Combine(extractDir, "d.txt")) == "DDD";

            bool ok = paths.SequenceEqual(new[] { "a.txt", "c.txt", "d.txt" }) && contentOk;
            return new SelfTestResult(name, ok, ok ? null : "增量增删保存后的归档内容不符合预期", log);
        }
        catch (Exception ex)
        {
            log.Add(ex.ToString());
            return new SelfTestResult(name, false, ex.Message, log);
        }
    }

    private static SelfTestResult RunEditorWithPassword(string root)
    {
        string name = "editor_with_password";
        var log = new List<string>();
        try
        {
            string work = Dir(root, name);
            var (archivePath, _, _, _) = SeedArchive(work, ArchiveFormat.SevenZip, "S3cr3t!");

            string newFile = Path.Combine(work, "d.txt");
            File.WriteAllText(newFile, "DDD");

            using (var editor = SevenZipArchiveEditor.Open(archivePath, ArchiveFormat.SevenZip, "S3cr3t!"))
            {
                editor.AddFile(newFile, "d.txt");
                editor.Delete("a.txt");
                editor.SaveChanges();
            }

            string extractDir = Path.Combine(work, "extracted");
            SevenZipArchive.ExtractAll(archivePath, ArchiveFormat.SevenZip, extractDir, "S3cr3t!");
            bool ok = !File.Exists(Path.Combine(extractDir, "a.txt"))
                && File.ReadAllText(Path.Combine(extractDir, "b.txt")) == "BBB"
                && File.ReadAllText(Path.Combine(extractDir, "d.txt")) == "DDD";

            log.Add($"带密码保存后解压校验: {ok}");
            return new SelfTestResult(name, ok, ok ? null : "带密码的增量保存/解压验证失败", log);
        }
        catch (Exception ex)
        {
            log.Add(ex.ToString());
            return new SelfTestResult(name, false, ex.Message, log);
        }
    }

    private static SelfTestResult RunEditorReplaceSemantics(string root)
    {
        string name = "editor_replace_semantics";
        var log = new List<string>();
        try
        {
            string work = Dir(root, name);
            var (archivePath, _, _, _) = SeedArchive(work, ArchiveFormat.Zip);

            string replacement = Path.Combine(work, "a-new.txt");
            File.WriteAllText(replacement, "A-REPLACED");

            using (var editor = SevenZipArchiveEditor.Open(archivePath, ArchiveFormat.Zip))
            {
                editor.AddFile(replacement, "a.txt"); // 和已有条目同名 => 替换
                var listed = editor.List();
                log.Add($"排队后条目数: {listed.Count}（应仍是 3，而不是 4）");
                editor.SaveChanges();
            }

            var finalListed = SevenZipArchive.List(archivePath, ArchiveFormat.Zip);
            string extractDir = Path.Combine(work, "extracted");
            SevenZipArchive.ExtractAll(archivePath, ArchiveFormat.Zip, extractDir);
            bool ok = finalListed.Count(i => !i.IsDir) == 3
                && File.ReadAllText(Path.Combine(extractDir, "a.txt")) == "A-REPLACED";

            return new SelfTestResult(name, ok, ok ? null : "同名 AddFile 应该替换旧条目而不是追加新条目", log);
        }
        catch (Exception ex)
        {
            log.Add(ex.ToString());
            return new SelfTestResult(name, false, ex.Message, log);
        }
    }

    private static SelfTestResult RunEditorBulkDeletePredicate(string root)
    {
        string name = "editor_bulk_delete_predicate";
        var log = new List<string>();
        try
        {
            string work = Dir(root, name);
            var (archivePath, _, _, _) = SeedArchive(work, ArchiveFormat.Zip);

            using (var editor = SevenZipArchiveEditor.Open(archivePath, ArchiveFormat.Zip))
            {
                int removed = editor.Delete(e => e.Path is "a.txt" or "b.txt");
                log.Add($"批量删除命中数: {removed}");
                editor.SaveChanges();
            }

            var finalListed = SevenZipArchive.List(archivePath, ArchiveFormat.Zip);
            var paths = finalListed.Select(i => i.Path).ToList();
            bool ok = paths.SequenceEqual(new[] { "c.txt" });
            return new SelfTestResult(name, ok, ok ? null : "按谓词批量删除后剩余条目不符合预期", log);
        }
        catch (Exception ex)
        {
            log.Add(ex.ToString());
            return new SelfTestResult(name, false, ex.Message, log);
        }
    }

    private static SelfTestResult RunEditorExtractAllReflectsPendingState(string root)
    {
        string name = "editor_extractall_pending_state";
        var log = new List<string>();
        try
        {
            string work = Dir(root, name);
            var (archivePath, _, _, _) = SeedArchive(work, ArchiveFormat.SevenZip);

            string newFile = Path.Combine(work, "d.txt");
            File.WriteAllText(newFile, "DDD");

            using var editor = SevenZipArchiveEditor.Open(archivePath, ArchiveFormat.SevenZip);
            editor.AddFile(newFile, "d.txt");
            editor.Delete("b.txt");

            // 注意：这里故意不调用 SaveChanges，直接 ExtractAll，验证它反映的是"排队中的
            // 逻辑状态"而不是磁盘上原封不动的归档内容。
            string extractDir = Path.Combine(work, "extracted_before_save");
            editor.ExtractAll(extractDir);

            bool ok = File.ReadAllText(Path.Combine(extractDir, "a.txt")) == "AAA"
                && !File.Exists(Path.Combine(extractDir, "b.txt"))
                && File.ReadAllText(Path.Combine(extractDir, "c.txt")) == "CCC"
                && File.ReadAllText(Path.Combine(extractDir, "d.txt")) == "DDD";

            // 同时确认磁盘上的归档这时候还没被改动过（SaveChanges 还没调用）。
            var stillOriginal = SevenZipArchive.List(archivePath, ArchiveFormat.SevenZip);
            bool diskUnchanged = stillOriginal.Select(i => i.Path).OrderBy(p => p, StringComparer.Ordinal)
                .SequenceEqual(new[] { "a.txt", "b.txt", "c.txt" });

            log.Add($"ExtractAll 反映排队状态: {ok}; 保存前磁盘归档未变: {diskUnchanged}");
            bool pass = ok && diskUnchanged;
            return new SelfTestResult(name, pass, pass ? null : "ExtractAll 在未 SaveChanges 时的行为不符合预期", log);
        }
        catch (Exception ex)
        {
            log.Add(ex.ToString());
            return new SelfTestResult(name, false, ex.Message, log);
        }
    }

    private static SelfTestResult RunEditorDisposeWithoutSaveDiscardsChanges(string root)
    {
        string name = "editor_dispose_without_save_discards";
        var log = new List<string>();
        try
        {
            string work = Dir(root, name);
            var (archivePath, _, _, _) = SeedArchive(work, ArchiveFormat.Zip);

            string newFile = Path.Combine(work, "d.txt");
            File.WriteAllText(newFile, "DDD");

            using (var editor = SevenZipArchiveEditor.Open(archivePath, ArchiveFormat.Zip))
            {
                editor.AddFile(newFile, "d.txt");
                editor.Delete("a.txt");
                log.Add($"Dispose 前 HasPendingChanges: {editor.HasPendingChanges}");
                // 故意不调用 SaveChanges，让 using 块结束时直接 Dispose。
            }

            var listed = SevenZipArchive.List(archivePath, ArchiveFormat.Zip);
            var paths = listed.Select(i => i.Path).OrderBy(p => p, StringComparer.Ordinal).ToList();
            log.Add($"Dispose 后重新打开磁盘上的条目: {string.Join(",", paths)}（应该还是原始的 a/b/c，排队的改动被丢弃）");

            bool ok = paths.SequenceEqual(new[] { "a.txt", "b.txt", "c.txt" });
            return new SelfTestResult(name, ok, ok ? null : "Dispose() 不应该自动保存排队中的改动", log);
        }
        catch (Exception ex)
        {
            log.Add(ex.ToString());
            return new SelfTestResult(name, false, ex.Message, log);
        }
    }

    private static SelfTestResult RunEditorCreateFromScratch(string root)
    {
        string name = "editor_create_from_scratch";
        var log = new List<string>();
        try
        {
            string work = Dir(root, name);
            string archivePath = Path.Combine(work, "brand-new.7z");
            string srcDir = Path.Combine(work, "contents");
            Directory.CreateDirectory(srcDir);
            File.WriteAllText(Path.Combine(srcDir, "x.txt"), "X");
            File.WriteAllText(Path.Combine(srcDir, "y.txt"), "Y");

            bool existedBeforeSave;
            using (var editor = SevenZipArchiveEditor.Create(archivePath, ArchiveFormat.SevenZip))
            {
                int added = editor.AddDirectory(srcDir, includeBaseDirectory: false);
                log.Add($"AddDirectory 排队文件数: {added}");
                existedBeforeSave = File.Exists(archivePath);
                editor.SaveChanges();
            }

            bool existedAfterSave = File.Exists(archivePath);
            var listed = SevenZipArchive.List(archivePath, ArchiveFormat.SevenZip);
            var paths = listed.Select(i => i.Path).OrderBy(p => p, StringComparer.Ordinal).ToList();
            log.Add($"Create() 首次 SaveChanges 前文件存在: {existedBeforeSave}; 之后存在: {existedAfterSave}; 条目: {string.Join(",", paths)}");

            bool ok = !existedBeforeSave && existedAfterSave && paths.SequenceEqual(new[] { "x.txt", "y.txt" });
            return new SelfTestResult(name, ok, ok ? null : "SevenZipArchiveEditor.Create() 从零新建归档的流程不符合预期", log);
        }
        catch (Exception ex)
        {
            log.Add(ex.ToString());
            return new SelfTestResult(name, false, ex.Message, log);
        }
    }

    private static SelfTestResult RunEditorOpenMissingFileThrows(string root)
    {
        string name = "editor_open_missing_file_throws";
        var log = new List<string>();
        try
        {
            string work = Dir(root, name);
            string missing = Path.Combine(work, "does-not-exist.zip");

            bool threw = false;
            try
            {
                SevenZipArchiveEditor.Open(missing, ArchiveFormat.Zip);
            }
            catch (FileNotFoundException)
            {
                threw = true;
            }

            log.Add($"Open() 对不存在的文件是否按预期抛出 FileNotFoundException: {threw}");
            return new SelfTestResult(name, threw, threw ? null : "Open() 对不存在的归档没有抛出预期的异常", log);
        }
        catch (Exception ex)
        {
            log.Add(ex.ToString());
            return new SelfTestResult(name, false, ex.Message, log);
        }
    }

    private static SelfTestResult RunEditorUnsupportedFormatThrows(string root)
    {
        string name = "editor_unsupported_format_throws";
        var log = new List<string>();
        try
        {
            bool threwTar = false, threwTarGz = false;
            try { SevenZipArchiveEditor.Create(Path.Combine(root, "x.tar"), ArchiveFormat.Tar); }
            catch (NotSupportedException) { threwTar = true; }

            try { SevenZipArchiveEditor.Create(Path.Combine(root, "x.tar.gz"), ArchiveFormat.TarGZip); }
            catch (NotSupportedException) { threwTarGz = true; }

            log.Add($"Tar 拒绝: {threwTar}; TarGZip 拒绝: {threwTarGz}");
            bool ok = threwTar && threwTarGz;
            return new SelfTestResult(name, ok, ok ? null : "SevenZipArchiveEditor 应该拒绝 tar/tar.gz 格式", log);
        }
        catch (Exception ex)
        {
            log.Add(ex.ToString());
            return new SelfTestResult(name, false, ex.Message, log);
        }
    }
}
