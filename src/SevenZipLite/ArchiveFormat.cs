namespace SevenZipLite;

/// <summary>本项目按报告约定只覆盖这四种格式（不含 RAR/ISO/CAB 等）。</summary>
public enum ArchiveFormat
{
    SevenZip,
    Zip,
    Tar,
    TarGZip,
}

public static class ArchiveFormatExtensions
{
    public static ArchiveFormat DetectFromFileName(string path)
    {
        string p = path.ToLowerInvariant();
        if (p.EndsWith(".tar.gz") || p.EndsWith(".tgz")) return ArchiveFormat.TarGZip;
        if (p.EndsWith(".tar")) return ArchiveFormat.Tar;
        if (p.EndsWith(".zip")) return ArchiveFormat.Zip;
        if (p.EndsWith(".7z")) return ArchiveFormat.SevenZip;
        throw new NotSupportedException($"无法从文件名判断格式: {path}");
    }

    public static bool SupportsPassword(this ArchiveFormat format) =>
        format is ArchiveFormat.SevenZip or ArchiveFormat.Zip;
}
