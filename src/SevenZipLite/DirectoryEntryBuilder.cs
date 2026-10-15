namespace SevenZipLite;

/// <summary>
/// "把一个磁盘目录铺平成 (DiskPath, ArchivePath) 列表" 的共享逻辑，被
/// <see cref="SevenZipCompressor.CreateArchiveFromDirectory"/> 和
/// <see cref="SevenZipArchiveEditor.AddDirectory"/> 两处复用，避免递归遍历/相对路径拼接/
/// 过滤规则这些细节写两份还可能行为不一致。
/// <para>
/// 已知限制：只枚举文件，不会给空目录单独生成一个"目录条目"——和 <see cref="SevenZipCompressor.Entry"/>
/// 这套"按文件列表描述归档内容"的既有模型保持一致（它本来就没有"纯目录条目"的概念）。
/// 如果目录树里有空子目录，解压后不会重新生成那个空目录。
/// </para>
/// </summary>
internal static class DirectoryEntryBuilder
{
    /// <param name="sourceDirectory">要压缩的源目录。</param>
    /// <param name="includeBaseDirectory">true：归档内路径带上 <paramref name="sourceDirectory"/>
    /// 本身的目录名作为根（例如压缩 /data/photos 得到 photos/a.jpg）；false：直接把目录内容铺平到
    /// 归档根（得到 a.jpg，不含 photos/ 这一层）。</param>
    /// <param name="filter">可选过滤回调，入参是文件的完整磁盘路径，返回 false 表示排除这个文件
    /// （比如排除 .git/node_modules 目录下的内容）。不传则包含目录下的全部文件。</param>
    public static List<SevenZipCompressor.Entry> BuildEntries(
        string sourceDirectory, bool includeBaseDirectory, Func<string, bool>? filter)
    {
        string fullSourceDir = Path.GetFullPath(sourceDirectory);
        if (!Directory.Exists(fullSourceDir))
            throw new DirectoryNotFoundException($"源目录不存在: {sourceDirectory}");

        string baseName = new DirectoryInfo(fullSourceDir).Name;
        var entries = new List<SevenZipCompressor.Entry>();

        foreach (string file in Directory.EnumerateFiles(fullSourceDir, "*", SearchOption.AllDirectories))
        {
            if (filter is not null && !filter(file)) continue;

            string relative = Path.GetRelativePath(fullSourceDir, file)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
            string archivePath = includeBaseDirectory ? $"{baseName}/{relative}" : relative;

            entries.Add(new SevenZipCompressor.Entry { DiskPath = file, ArchivePath = archivePath });
        }

        return entries;
    }
}
