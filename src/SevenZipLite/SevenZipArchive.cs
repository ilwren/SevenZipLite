using SevenZipLite.Callbacks;
using SevenZipLite.Native;
using SevenZipLite.Streams;

namespace SevenZipLite;

/// <summary>读取/解压 7z、zip、tar、tar.gz 归档的高层 API。</summary>
public static class SevenZipArchive
{
    private static Guid ClsidFor(ArchiveFormat format) => format switch
    {
        ArchiveFormat.SevenZip => Guids.CLSID_7z,
        ArchiveFormat.Zip => Guids.CLSID_Zip,
        ArchiveFormat.Tar => Guids.CLSID_Tar,
        ArchiveFormat.TarGZip => Guids.CLSID_GZip, // 外层用 gzip 打开
        _ => throw new NotSupportedException(format.ToString())
    };

    private static IInArchive CreateHandler(Guid clsid)
    {
        int hr = NativeMethods.CreateObject(clsid, Guids.IID_IInArchive, out nint ptr);
        if (hr != HResult.S_OK || ptr == 0)
            throw new InvalidOperationException($"CreateObject(IInArchive) 失败, HRESULT=0x{hr:X8}");
        return ComFactory.GetRcw<IInArchive>(ptr);
    }

    private static List<ExtractItemInfo> ListOpened(IInArchive archive)
    {
        int hr = archive.GetNumberOfItems(out uint count);
        Check(hr, nameof(archive.GetNumberOfItems));

        var list = new List<ExtractItemInfo>();
        for (uint i = 0; i < count; i++)
        {
            string path = GetStringProp(archive, i, PropId.kpidPath) ?? $"item_{i}";
            bool isDir = GetBoolProp(archive, i, PropId.kpidIsDir);
            long size = GetInt64Prop(archive, i, PropId.kpidSize);
            list.Add(new ExtractItemInfo { Index = i, Path = path.Replace('\\', '/'), IsDir = isDir, Size = size });
        }
        return list;
    }

    private static string? GetStringProp(IInArchive archive, uint index, uint propId)
    {
        int hr = archive.GetProperty(index, propId, out PropVariant v);
        Check(hr, "GetProperty");
        var s = v.ToManaged() as string;
        v.Clear();
        return s;
    }

    private static bool GetBoolProp(IInArchive archive, uint index, uint propId)
    {
        int hr = archive.GetProperty(index, propId, out PropVariant v);
        Check(hr, "GetProperty");
        bool b = v.ToManaged() is bool bb && bb;
        v.Clear();
        return b;
    }

    private static long GetInt64Prop(IInArchive archive, uint index, uint propId)
    {
        int hr = archive.GetProperty(index, propId, out PropVariant v);
        Check(hr, "GetProperty");
        object? o = v.ToManaged();
        v.Clear();
        return o switch
        {
            ulong ul => (long)ul,
            uint ui => ui,
            long l => l,
            int i => i,
            _ => 0
        };
    }

    private static void Check(int hr, string what, CancellationToken cancellationToken = default)
    {
        if (hr == HResult.S_OK) return;
        if (hr == HResult.E_ABORT && cancellationToken.IsCancellationRequested)
        {
            // 调用方主动取消时，native 端的回调（ArchiveOpenCallback/ExtractCallback）会
            // 返回 E_ABORT，Open/Extract 把这个 HRESULT 一路传播上来——这里把它翻译成
            // 标准的 .NET 取消异常，而不是当成一个真正的失败报出去。
            // When the caller cancels, the native callbacks (ArchiveOpenCallback/
            // ExtractCallback) return E_ABORT, which Open/Extract propagate up — this
            // translates it into a standard .NET cancellation exception instead of
            // reporting it as a genuine failure.
            throw new OperationCanceledException(cancellationToken);
        }
        if (hr == HResult.S_FALSE)
        {
            // S_FALSE（=1）在 7-Zip 原生代码里是一个正常的“成功”返回码，含义是
            // “没能把这段数据识别/解析成这种格式的有效归档”（见 7zIn.cpp 注释
            // "S_FALSE means that file is not archive"），常见触发场景：
            //   1) 密码错误或未提供密码，导致加密的归档头/内容无法正确解析；
            //   2) 文件已损坏，或根本不是该 format 对应的归档格式；
            // 这里给出比裸 HRESULT 更有指向性的提示，而不是让调用方去查 COM 错误码表。
            throw new InvalidOperationException(
                $"{what} 返回 S_FALSE：密码错误、未提供密码，或文件不是有效的归档（已损坏/格式不匹配）。");
        }
        throw new InvalidOperationException($"{what} 失败, HRESULT=0x{hr:X8}");
    }

    private static void OpenStream(IInArchive archive, Stream input, string? password = null,
        CancellationToken cancellationToken = default)
    {
        var adapter = new ComInStream(input, leaveOpen: true);
        nint streamCcw = ComFactory.GetCcw<IInStream>(adapter);
        // 传入密码：7z「加密文件名」(header encryption / -mhe=on) 归档在 Open 阶段就需要
        // 密码才能解出头部结构，不传会在 Open 时失败（见 ArchiveOpenCallback 的文档注释）。
        var openCb = new ArchiveOpenCallback(password, cancellationToken);
        nint cbCcw = ComFactory.GetCcw<IArchiveOpenCallback>(openCb);
        int hr = archive.Open(streamCcw, 0, cbCcw);
        Check(hr, "IInArchive.Open", cancellationToken);
    }

    /// <summary>列出普通格式（7z/zip/tar）归档内容；不支持 tar.gz（tar.gz 只有单一内层流，
    /// 请用 <see cref="ListTarGz"/>）。archivePathOrFirstVolume 可以是完整路径，也可以是
    /// 分卷归档的第一卷（如 xxx.7z.001），也可以是分卷的基础名（xxx.7z，会自动探测 .001/.002…）。
    /// password 仅在归档启用了「加密文件名」（7z header encryption）时才需要——普通的仅加密
    /// 内容、不加密文件名的归档可以不传 password 也能正常列出文件名/大小列表。</summary>
    public static List<ExtractItemInfo> List(string archivePathOrFirstVolume, ArchiveFormat format,
        string? password = null, CancellationToken cancellationToken = default)
    {
        if (format == ArchiveFormat.TarGZip) return ListTarGz(archivePathOrFirstVolume, cancellationToken);

        var volumes = JoinedVolumeStream.DiscoverVolumes(archivePathOrFirstVolume);
        using var input = new JoinedVolumeStream(volumes);
        var archive = CreateHandler(ClsidFor(format));
        OpenStream(archive, input, password, cancellationToken);
        try { return ListOpened(archive); }
        finally { archive.Close(); }
    }

    /// <summary>
    /// <see cref="List"/> 的异步版本。底层 native 调用本身是阻塞式的，这里只是用
    /// <c>Task.Run</c> 把它丢到线程池，让调用方
    /// 的 await 不卡当前线程，不是真正意义上的异步 I/O。
    /// </summary>
    public static Task<List<ExtractItemInfo>> ListAsync(string archivePathOrFirstVolume, ArchiveFormat format,
        string? password = null, CancellationToken cancellationToken = default)
        => Task.Run(() => List(archivePathOrFirstVolume, format, password, cancellationToken), cancellationToken);

    /// <summary>解压普通格式（7z/zip/tar）到指定目录。</summary>
    /// <param name="archivePathOrFirstVolume">归档路径；可以是完整路径，也可以是分卷归档的
    /// 第一卷（如 xxx.7z.001），也可以是分卷的基础名（xxx.7z，会自动探测 .001/.002…）。</param>
    /// <param name="format">归档格式。</param>
    /// <param name="outputDir">解压目标目录。</param>
    /// <param name="password">可选密码（仅 7z/zip 生效）。</param>
    /// <param name="progress">可选的进度回调，汇报字节级进度（<see cref="SevenZipProgress"/>）；
    /// tar.gz 会分两阶段各汇报一次 0~100%，见该类型注释。</param>
    /// <param name="cancellationToken">可选的取消令牌；取消粒度是"条目之间"，正在写入中的
    /// 单个条目无法从内部中途打断，详见 <see cref="Callbacks.ExtractCallback"/> 的说明。
    /// 取消后会抛出 <see cref="OperationCanceledException"/>。</param>
    public static void ExtractAll(string archivePathOrFirstVolume, ArchiveFormat format, string outputDir,
        string? password = null, IProgress<SevenZipProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (format == ArchiveFormat.TarGZip)
        {
            ExtractTarGz(archivePathOrFirstVolume, outputDir, progress, cancellationToken);
            return;
        }

        var volumes = JoinedVolumeStream.DiscoverVolumes(archivePathOrFirstVolume);
        using var input = new JoinedVolumeStream(volumes);
        var archive = CreateHandler(ClsidFor(format));
        OpenStream(archive, input, password, cancellationToken);
        try
        {
            var items = ListOpened(archive);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(outputDir);
            // 先建目录条目，避免文件写入时目录还不存在（虽然 GetStream 内部也会自动 CreateDirectory）
            foreach (var it in items.Where(i => i.IsDir))
            {
                Directory.CreateDirectory(Path.Combine(outputDir, it.Path.Replace('/', Path.DirectorySeparatorChar)));
            }

            var dict = items.ToDictionary(i => i.Index);
            using var extractCb = new ExtractCallback(dict, outputDir, password, progress, cancellationToken);
            nint cbCcw = ComFactory.GetCcw<IArchiveExtractCallback>(extractCb);
            int hr = archive.Extract(0, 0xFFFFFFFFu, 0, cbCcw);
            Check(hr, "IInArchive.Extract", cancellationToken);
        }
        finally
        {
            archive.Close();
        }
    }

    /// <summary>
    /// <see cref="ExtractAll"/> 的异步版本。底层 native 调用本身是阻塞式的，这里只是用
    /// <c>Task.Run</c> 把它丢到线程池，让调用方的 await
    /// 不卡当前线程，不是真正意义上的异步 I/O；真正的取消/进度语义由 native 回调驱动，
    /// 和同步版本完全一致，见 <see cref="ExtractAll"/> 参数说明。
    /// </summary>
    public static Task ExtractAllAsync(string archivePathOrFirstVolume, ArchiveFormat format, string outputDir,
        string? password = null, IProgress<SevenZipProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => Task.Run(
            () => ExtractAll(archivePathOrFirstVolume, format, outputDir, password, progress, cancellationToken),
            cancellationToken);

    // ---------------- tar.gz 组合处理 ----------------
    // gzip 归档只包含一条内层流，不是文件系统树；tar.gz = gzip(tar(files))。
    // 因此 tar.gz 的读取需要两步：先用 GZip handler 把内层流解出到临时 .tar 文件，
    // 再用 Tar handler 正常打开/解压这个临时文件。

    private static string ExtractInnerTarToTemp(string gzPathOrVolumeBase,
        IProgress<SevenZipProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var volumes = JoinedVolumeStream.DiscoverVolumes(gzPathOrVolumeBase);
        using var input = new JoinedVolumeStream(volumes);
        var archive = CreateHandler(Guids.CLSID_GZip);
        OpenStream(archive, input, cancellationToken: cancellationToken);
        string tempTar = Path.Combine(Path.GetTempPath(), $"sevenziplite_{Guid.NewGuid():N}.tar");
        try
        {
            var items = ListOpened(archive); // 应该只有 1 项
            cancellationToken.ThrowIfCancellationRequested();
            var dict = items.ToDictionary(i => i.Index);
            using var extractCb = new ExtractCallback(dict, Path.GetTempPath(), null);
            // 把唯一条目重定向到我们想要的临时文件名：借助 items[0].Path 无法控制文件名，
            // 所以这里直接手工调用一次性提取到指定路径。
            nint cbCcw = ComFactory.GetCcw<IArchiveExtractCallback>(extractCb);

            // 简化处理：用一个专门的一次性回调，把唯一的流写到 tempTar——这是 tar.gz 两阶段
            // 流程的第一阶段（gunzip 出内层 tar），实际的字节级解压工作量大多发生在这里，
            // 所以进度/取消也接到这一步上；第二阶段（下面 ListTarGz/ExtractTarGz 里对
            // tempTar 调用的 List/ExtractAll）会在这之后各自独立再汇报一轮 0~100%，
            // 两阶段的进度不是加权合并的，见 SevenZipProgress 类型注释。
            using var singleCb = new SingleFileExtractCallback(tempTar, progress, cancellationToken);
            nint singleCcw = ComFactory.GetCcw<IArchiveExtractCallback>(singleCb);
            int hr = archive.Extract(0, 0xFFFFFFFFu, 0, singleCcw);
            Check(hr, "IInArchive.Extract(gzip)", cancellationToken);
            return tempTar;
        }
        finally
        {
            archive.Close();
        }
    }

    private static List<ExtractItemInfo> ListTarGz(string gzPathOrVolumeBase,
        CancellationToken cancellationToken = default)
    {
        string tempTar = ExtractInnerTarToTemp(gzPathOrVolumeBase, cancellationToken: cancellationToken);
        try { return List(tempTar, ArchiveFormat.Tar, cancellationToken: cancellationToken); }
        finally { TryDelete(tempTar); }
    }

    private static void ExtractTarGz(string gzPathOrVolumeBase, string outputDir,
        IProgress<SevenZipProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        string tempTar = ExtractInnerTarToTemp(gzPathOrVolumeBase, progress, cancellationToken);
        try { ExtractAll(tempTar, ArchiveFormat.Tar, outputDir, progress: progress, cancellationToken: cancellationToken); }
        finally { TryDelete(tempTar); }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* 忽略 */ }
    }
}
