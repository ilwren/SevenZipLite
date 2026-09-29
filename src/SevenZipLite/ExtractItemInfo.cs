namespace SevenZipLite;

/// <summary>归档内一个条目的基本信息（用于解压时决定目标路径/是否目录）。</summary>
public sealed class ExtractItemInfo
{
    public required uint Index { get; init; }
    public required string Path { get; init; }
    public required bool IsDir { get; init; }
    public long Size { get; init; }
}
