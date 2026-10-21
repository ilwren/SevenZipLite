namespace SevenZipLite.SelfTest;

/// <summary>
/// 专项验证 <see cref="SevenZipCompressor.CreateArchive"/> 对 <see cref="SevenZipCompressor.Entry"/>
/// 列表的显式参数校验：空（null/空字符串）的 <c>DiskPath</c>/<c>ArchivePath</c> 应该在压缩真正
/// 开始之前就被拦下来，抛出带具体下标和上下文信息的 <see cref="ArgumentException"/>，而不是让
/// .NET BCL 的 <c>FileInfo</c>/<c>File.GetLastWriteTimeUtc</c> 抛出一个不带任何上下文的
/// <see cref="ArgumentNullException"/>（"Value cannot be null. (Parameter 'path')"）。
/// <para>
/// 背景：一个真实用户在 Android 项目里遇到过这类报错——调查后发现是上游代码（不是本库）
/// 把一个本该是 null 的路径变量未经判空直接传了进来，排查时才发现默认的报错信息完全看不出
/// 是哪一条 Entry 出的问题。详见 CHANGELOG 对应条目。
/// </para>
/// </summary>
public static class EntryValidationTestRunner
{
    public static IReadOnlyList<SelfTestResult> RunAll(string root)
    {
        return new List<SelfTestResult>
        {
            RunNullDiskPathThrowsArgumentException(root),
            RunEmptyDiskPathThrowsArgumentException(root),
            RunNullArchivePathThrowsArgumentException(root),
            RunNullEntriesListThrowsArgumentNullException(root),
            RunValidEntriesStillWork(root),
        };
    }

    private static string Dir(string root, string name)
    {
        string d = Path.Combine(root, "entryval_" + name);
        Directory.CreateDirectory(d);
        return d;
    }

    private static SelfTestResult RunNullDiskPathThrowsArgumentException(string root)
    {
        string name = "null_diskpath_throws_clear_argument_exception";
        var log = new List<string>();
        try
        {
            string work = Dir(root, name);
            var entries = new List<SevenZipCompressor.Entry>
            {
                new() { DiskPath = null!, ArchivePath = "a.txt" }
            };

            bool threwArgumentException = false;
            bool messageMentionsIndex = false;
            try
            {
                SevenZipCompressor.CreateArchive(entries, Path.Combine(work, "out.7z"), ArchiveFormat.SevenZip);
            }
            catch (ArgumentException ex) when (ex is not ArgumentNullException)
            {
                threwArgumentException = true;
                messageMentionsIndex = ex.Message.Contains("entries[0]", StringComparison.Ordinal);
                log.Add("捕获到的异常信息: " + ex.Message);
            }

            log.Add($"是否抛出 ArgumentException（而不是裸的 ArgumentNullException）: {threwArgumentException}");
            log.Add($"异常信息是否包含具体下标 entries[0]: {messageMentionsIndex}");

            bool ok = threwArgumentException && messageMentionsIndex;
            return new SelfTestResult(name, ok, ok ? null : "null DiskPath 没有按预期抛出带上下文的 ArgumentException", log);
        }
        catch (Exception ex)
        {
            log.Add(ex.ToString());
            return new SelfTestResult(name, false, ex.Message, log);
        }
    }

    private static SelfTestResult RunEmptyDiskPathThrowsArgumentException(string root)
    {
        string name = "empty_diskpath_throws_clear_argument_exception";
        var log = new List<string>();
        try
        {
            string work = Dir(root, name);
            var entries = new List<SevenZipCompressor.Entry>
            {
                new() { DiskPath = "", ArchivePath = "a.txt" }
            };

            bool threw = false;
            try
            {
                SevenZipCompressor.CreateArchive(entries, Path.Combine(work, "out.7z"), ArchiveFormat.SevenZip);
            }
            catch (ArgumentException ex)
            {
                threw = true;
                log.Add("捕获到的异常信息: " + ex.Message);
            }

            log.Add($"空字符串 DiskPath 是否抛出 ArgumentException: {threw}");
            return new SelfTestResult(name, threw, threw ? null : "空字符串 DiskPath 没有按预期抛出异常", log);
        }
        catch (Exception ex)
        {
            log.Add(ex.ToString());
            return new SelfTestResult(name, false, ex.Message, log);
        }
    }

    private static SelfTestResult RunNullArchivePathThrowsArgumentException(string root)
    {
        string name = "null_archivepath_throws_clear_argument_exception";
        var log = new List<string>();
        try
        {
            string work = Dir(root, name);
            string realFile = Path.Combine(work, "a.txt");
            File.WriteAllText(realFile, "data");

            var entries = new List<SevenZipCompressor.Entry>
            {
                new() { DiskPath = realFile, ArchivePath = null! }
            };

            bool threwArgumentException = false;
            bool messageMentionsIndex = false;
            try
            {
                SevenZipCompressor.CreateArchive(entries, Path.Combine(work, "out.7z"), ArchiveFormat.SevenZip);
            }
            catch (ArgumentException ex) when (ex is not ArgumentNullException)
            {
                threwArgumentException = true;
                messageMentionsIndex = ex.Message.Contains("entries[0]", StringComparison.Ordinal);
                log.Add("捕获到的异常信息: " + ex.Message);
            }

            log.Add($"是否抛出 ArgumentException（而不是裸的 ArgumentNullException）: {threwArgumentException}");
            log.Add($"异常信息是否包含具体下标 entries[0]: {messageMentionsIndex}");

            bool ok = threwArgumentException && messageMentionsIndex;
            return new SelfTestResult(name, ok, ok ? null : "null ArchivePath 没有按预期抛出带上下文的 ArgumentException", log);
        }
        catch (Exception ex)
        {
            log.Add(ex.ToString());
            return new SelfTestResult(name, false, ex.Message, log);
        }
    }

    private static SelfTestResult RunNullEntriesListThrowsArgumentNullException(string root)
    {
        string name = "null_entries_list_throws_argument_null_exception";
        var log = new List<string>();
        try
        {
            string work = Dir(root, name);
            bool threw = false;
            try
            {
                SevenZipCompressor.CreateArchive(null!, Path.Combine(work, "out.7z"), ArchiveFormat.SevenZip);
            }
            catch (ArgumentNullException ex)
            {
                threw = true;
                log.Add("捕获到的异常信息: " + ex.Message);
            }

            log.Add($"entries 整个为 null 是否抛出 ArgumentNullException: {threw}");
            return new SelfTestResult(name, threw, threw ? null : "entries 为 null 没有按预期抛出异常", log);
        }
        catch (Exception ex)
        {
            log.Add(ex.ToString());
            return new SelfTestResult(name, false, ex.Message, log);
        }
    }

    private static SelfTestResult RunValidEntriesStillWork(string root)
    {
        string name = "valid_entries_unaffected_by_new_validation";
        var log = new List<string>();
        try
        {
            string work = Dir(root, name);
            string realFile = Path.Combine(work, "a.txt");
            File.WriteAllText(realFile, "normal case, should be unaffected by the new validation");

            var entries = new List<SevenZipCompressor.Entry>
            {
                new() { DiskPath = realFile, ArchivePath = "a.txt" }
            };

            var volumes = SevenZipCompressor.CreateArchive(entries, Path.Combine(work, "out.7z"), ArchiveFormat.SevenZip);
            bool ok = volumes.Count == 1 && File.Exists(volumes[0]);
            log.Add($"正常 entries 仍然能成功创建归档: {ok}");

            return new SelfTestResult(name, ok, ok ? null : "新加的校验意外影响了正常用例", log);
        }
        catch (Exception ex)
        {
            log.Add(ex.ToString());
            return new SelfTestResult(name, false, ex.Message, log);
        }
    }
}
