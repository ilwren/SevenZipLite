namespace SevenZipLite;

/// <summary>
/// 压缩/解压过程中的一次进度快照，对应 7-Zip 原生 <c>IProgress::SetTotal</c>/
/// <c>SetCompleted</c> 回调汇报的字节数（不是文件个数）。
///
/// <para>
/// 对于 tar.gz：由于其内部实现是"先在临时文件生成 tar，再对这个 tar 整体做 gzip"两个
/// 独立阶段（见 <see cref="SevenZipCompressor"/>/<see cref="SevenZipArchive"/> 里对应方法
/// 的注释），<see cref="Percentage"/> 会先从 0 跑到 100 一次（第一阶段：tar 打包 /
/// 解出内层 tar），再从 0 跑到 100 一次（第二阶段：gzip 压缩 / 展开 tar 里的各个条目）——
/// 这是如实反映底层两阶段结构，不是 bug。
/// </para>
/// </summary>
public readonly struct SevenZipProgress
{
    public SevenZipProgress(ulong bytesCompleted, ulong bytesTotal)
    {
        BytesCompleted = bytesCompleted;
        BytesTotal = bytesTotal;
    }

    /// <summary>已处理的字节数。</summary>
    public ulong BytesCompleted { get; }

    /// <summary>
    /// 本次操作预计处理的总字节数。native 端在真正调用 <c>SetTotal</c> 之前这里会先是 0
    /// （代表"总量还未知"，不代表"已经处理完 0 字节的 100%"）。
    /// </summary>
    public ulong BytesTotal { get; }

    /// <summary>0~100 的完成百分比；<see cref="BytesTotal"/> 为 0（总量未知）时返回 0，
    /// 避免除零，调用方不应把这个 0 误解成"卡住了"。</summary>
    public double Percentage => BytesTotal == 0 ? 0 : Math.Min(100.0, BytesCompleted * 100.0 / BytesTotal);

    public override string ToString() => $"{BytesCompleted}/{BytesTotal} ({Percentage:F1}%)";
}
