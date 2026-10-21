using System.Runtime.InteropServices;
using SevenZipLite.Callbacks;
using SevenZipLite.Native;
using SevenZipLite.Streams;

namespace SevenZipLite;

/// <summary>
/// 面向对象、可以用 <c>using</c> 管理的"对一个归档做增删改查"的会话。和
/// <see cref="SevenZipCompressor"/>/<see cref="SevenZipArchive"/> 那套"一次性创建/一次性
/// 列出解压"的静态方法不同，这个类支持对<b>已存在的 7z/zip 归档</b>做真正的<b>增量</b>增删
/// 条目——不需要解压整个归档重新打包，而是复用 7-Zip 原生的"保留未改动条目、只重新编码
/// 新增条目"更新机制（<c>IOutArchive::UpdateItems</c> 配合 <c>indexInArchive</c>）。
/// <para>
/// 典型用法：
/// <code>
/// using var editor = SevenZipArchiveEditor.Open("data.zip", ArchiveFormat.Zip, password: "pwd");
/// editor.Delete("old/file.txt");
/// editor.AddFile(@"C:\new.txt", "new.txt");
/// editor.AddDirectory(@"C:\extra-stuff");
/// editor.SaveChanges();   // 此时才真正落盘，之前的 Add/Delete 只是排队
/// </code>
/// 新建一个还不存在的归档、之后逐步往里加东西，用 <see cref="Create"/> 代替 <see cref="Open"/>。
/// </para>
/// <para>
/// <b>已知限制（刻意控制范围，避免第一版就铺太大）：</b><br/>
/// 1) 只支持 <see cref="ArchiveFormat.SevenZip"/> / <see cref="ArchiveFormat.Zip"/> 两种格式——
///    tar 的内部实现、tar.gz（单一压缩流，没有"条目索引"概念）都不支持原地增删，用这两种格式
///    仍然只能靠 <see cref="SevenZipCompressor.CreateArchive"/> 整体重新打包。<br/>
/// 2) 不支持分卷归档（<c>volumeSize &gt; 0</c> 创建出来的 <c>.001/.002/…</c> 那种）——
///    <see cref="Open"/> 只认单一完整文件。<br/>
/// 3) <see cref="Dispose"/> <b>不会</b>自动保存未提交的改动——<see cref="AddFile"/>/
///    <see cref="AddDirectory(string, bool, Func{string, bool}?)"/>/<see cref="Delete(string)"/>
///    只是在内存里排队，必须显式调用 <see cref="SaveChanges"/> 才会真正写回磁盘；
///    <c>using</c> 块结束时如果忘了调用 <see cref="SaveChanges"/>，排队的改动会被直接丢弃
///    （可以用 <see cref="HasPendingChanges"/> 自查）。这是有意的设计（类比数据库事务
///    "不 Commit 就等于没发生"），不是 bug。<br/>
/// 4) 归档内路径按原样做**区分大小写**的精确字符串匹配（<see cref="StringComparison.Ordinal"/>），
///    不做大小写折叠、不支持通配符——需要批量匹配时用 <see cref="Delete(Func{EntryInfo, bool})"/>
///    这个接受过滤回调的重载。
/// </para>
/// <para>
/// English summary: an object-oriented, <c>using</c>-friendly session for incrementally
/// editing an <b>existing</b> 7z/zip archive (add/delete entries without re-packing the whole
/// thing from scratch), built on 7-Zip's native "keep unchanged entries, only re-encode new
/// ones" update mechanism. Known limitations: only 7z/Zip (not Tar/TarGZip); no multi-volume
/// support; <see cref="Dispose"/> does NOT auto-save pending changes (call
/// <see cref="SaveChanges"/> explicitly — it's transactional, like a DB transaction that rolls
/// back if you never commit); path matching is exact/case-sensitive.
/// </para>
/// </summary>
public sealed class SevenZipArchiveEditor : IDisposable
{
    /// <summary>
    /// <see cref="List"/> 返回的单个条目视图——是"如果现在调用 <see cref="SaveChanges"/>，
    /// 归档最终会是什么样子"的实时快照，包含还没保存的 <see cref="AddFile"/> 结果。
    /// </summary>
    public sealed class EntryInfo
    {
        public required string Path { get; init; }
        public required bool IsDir { get; init; }
        public long Size { get; init; }

        /// <summary>true 表示这是还没 <see cref="SaveChanges"/> 的新增条目（只存在于内存里，
        /// 磁盘上的归档文件还没有它）；false 表示这是已经保存在磁盘归档里的既有条目。</summary>
        public required bool IsPendingChange { get; init; }
    }

    private readonly string _archivePath;
    private readonly ArchiveFormat _format;
    private readonly string? _password;
    private readonly Guid _clsid;
    private readonly bool _encryptHeaders;

    // "已经保存在磁盘上"的基准状态；Open()/SaveChanges() 成功后（重新）建立。
    private IInArchive? _inArchive;
    private IOutArchive? _outArchive;
    private nint _outArchivePtr; // 和 _outArchive 配套的原始 native 指针，SaveChanges 时用来调 ISetProperties
    private FileStream? _inputStream;
    private List<ExtractItemInfo> _baseItems = new();
    private bool _hasBaseArchive;

    // 排队中、还没 SaveChanges 的改动。
    private readonly Dictionary<string, string> _pendingAdds = new(StringComparer.Ordinal); // archivePath -> diskPath
    private readonly HashSet<uint> _deletedBaseIndices = new();

    private bool _disposed;

    private SevenZipArchiveEditor(string archivePath, ArchiveFormat format, string? password, bool encryptHeaders)
    {
        _clsid = ClsidFor(format); // 先校验格式，不支持的格式在这里就抛异常
        _archivePath = Path.GetFullPath(archivePath);
        _format = format;
        _password = format.SupportsPassword() ? password : null;
        _encryptHeaders = encryptHeaders;
    }

    /// <summary>打开一个已存在的 7z/zip 归档用于增量编辑。</summary>
    /// <param name="archivePath">要打开的归档路径。</param>
    /// <param name="format">归档格式，仅支持 7z/zip。</param>
    /// <param name="password">可选密码。</param>
    /// <param name="encryptHeaders">
    /// 每次 <see cref="SaveChanges"/> 写回磁盘时，是否同时加密归档头（文件名/大小/目录结构等
    /// 元数据），语义和默认值同 <see cref="SevenZipCompressor.CreateArchive"/> 的同名参数
    /// （仅在 <c>format == ArchiveFormat.SevenZip</c> 且提供了密码时有意义，默认 <c>true</c>，
    /// 和官方 7z.exe 命令行的默认值相反，因为"设了密码的压缩包"的直觉预期通常是文件名也保密）。
    /// </param>
    /// <exception cref="NotSupportedException"><paramref name="format"/> 不是 7z/zip。</exception>
    /// <exception cref="FileNotFoundException"><paramref name="archivePath"/> 不存在。</exception>
    public static SevenZipArchiveEditor Open(
        string archivePath, ArchiveFormat format, string? password = null, bool encryptHeaders = true)
    {
        var editor = new SevenZipArchiveEditor(archivePath, format, password, encryptHeaders);
        if (!File.Exists(editor._archivePath))
            throw new FileNotFoundException($"归档不存在: {archivePath}", editor._archivePath);

        editor._hasBaseArchive = true;
        editor.OpenBase();
        return editor;
    }

    /// <summary>
    /// 新建一个空白编辑会话，<paramref name="archivePath"/> 不需要已经存在——调用
    /// <see cref="AddFile"/>/<see cref="AddDirectory(string, bool, Func{string, bool}?)"/> 排队
    /// 要写入的内容，第一次 <see cref="SaveChanges"/> 时才会真正创建这个文件（如果
    /// <paramref name="archivePath"/> 处已经有文件，会被直接覆盖，语义同
    /// <see cref="SevenZipCompressor.CreateArchive"/>）。<paramref name="encryptHeaders"/>
    /// 的语义见 <see cref="Open"/> 的参数说明。
    /// </summary>
    /// <exception cref="NotSupportedException"><paramref name="format"/> 不是 7z/zip。</exception>
    public static SevenZipArchiveEditor Create(
        string archivePath, ArchiveFormat format, string? password = null, bool encryptHeaders = true)
    {
        var editor = new SevenZipArchiveEditor(archivePath, format, password, encryptHeaders);
        editor._hasBaseArchive = false;
        return editor;
    }

    /// <summary>是否有尚未 <see cref="SaveChanges"/> 的排队改动（新增和/或删除）。</summary>
    public bool HasPendingChanges => _pendingAdds.Count > 0 || _deletedBaseIndices.Count > 0;

    /// <summary>
    /// 列出"如果现在 SaveChanges，归档最终会是什么样子"——已保存的条目（排除标记删除的）
    /// 加上排队中还没保存的新增条目，不是单纯读磁盘上归档现在的样子。
    /// </summary>
    public IReadOnlyList<EntryInfo> List()
    {
        ThrowIfDisposed();
        var result = new List<EntryInfo>(_baseItems.Count + _pendingAdds.Count);
        foreach (var item in _baseItems)
        {
            if (_deletedBaseIndices.Contains(item.Index)) continue;
            result.Add(new EntryInfo { Path = item.Path, IsDir = item.IsDir, Size = item.Size, IsPendingChange = false });
        }
        foreach (var (archivePath, diskPath) in _pendingAdds)
        {
            long size = 0;
            try { size = new FileInfo(diskPath).Length; } catch { /* 文件可能在排队期间被删了，Size 就留 0 */ }
            result.Add(new EntryInfo { Path = archivePath, IsDir = false, Size = size, IsPendingChange = true });
        }
        return result;
    }

    /// <summary>
    /// 排队添加（或替换）一个文件。如果 <paramref name="archivePath"/> 和某个现有条目
    /// （不管是已保存的还是排队中的）重名，等效于先 <see cref="Delete(string)"/> 再添加——
    /// 也就是"更新/替换"语义。只是排队，不会立刻读取/占用 <paramref name="diskPath"/>，真正
    /// 读取发生在 <see cref="SaveChanges"/> 时。
    /// </summary>
    /// <exception cref="FileNotFoundException"><paramref name="diskPath"/> 不存在。</exception>
    public void AddFile(string diskPath, string archivePath)
    {
        ThrowIfDisposed();
        if (!File.Exists(diskPath))
            throw new FileNotFoundException($"文件不存在: {diskPath}", diskPath);

        string normalized = NormalizeArchivePath(archivePath);
        var existing = _baseItems.Find(i => !_deletedBaseIndices.Contains(i.Index)
            && string.Equals(i.Path, normalized, StringComparison.Ordinal));
        if (existing is not null) _deletedBaseIndices.Add(existing.Index);

        _pendingAdds[normalized] = diskPath;
    }

    /// <summary>
    /// 把一整个磁盘目录排队添加进来，遍历/相对路径规则和
    /// <see cref="SevenZipCompressor.CreateArchiveFromDirectory"/> 完全一致（复用同一套
    /// <see cref="DirectoryEntryBuilder"/>）。返回排队添加的文件数。
    /// </summary>
    public int AddDirectory(string sourceDirectory, bool includeBaseDirectory = true, Func<string, bool>? filter = null)
    {
        ThrowIfDisposed();
        var entries = DirectoryEntryBuilder.BuildEntries(sourceDirectory, includeBaseDirectory, filter);
        // DirectoryEntryBuilder 只会产出磁盘路径条目（从不设置 OpenStream），DiskPath 必然非空。
        foreach (var entry in entries) AddFile(entry.DiskPath!, entry.ArchivePath);
        return entries.Count;
    }

    /// <summary>
    /// 排队删除一个条目（精确匹配 <paramref name="archivePath"/>，区分大小写）。对排队中还没
    /// 保存的新增条目直接从队列里撤销；对已保存在磁盘归档里的条目标记为"下次 SaveChanges 时
    /// 不再写入"。返回是否真的找到了要删除的东西。
    /// </summary>
    public bool Delete(string archivePath)
    {
        ThrowIfDisposed();
        string normalized = NormalizeArchivePath(archivePath);

        if (_pendingAdds.Remove(normalized)) return true;

        var existing = _baseItems.Find(i => !_deletedBaseIndices.Contains(i.Index)
            && string.Equals(i.Path, normalized, StringComparison.Ordinal));
        if (existing is null) return false;

        _deletedBaseIndices.Add(existing.Index);
        return true;
    }

    /// <summary>
    /// 按条件批量删除——对 <see cref="List"/> 当前快照里匹配 <paramref name="predicate"/> 的每一项
    /// 调用 <see cref="Delete(string)"/>。比如删除某个子目录下的全部内容：
    /// <c>editor.Delete(e =&gt; e.Path.StartsWith("old-folder/"));</c>。返回实际删除的条目数。
    /// </summary>
    public int Delete(Func<EntryInfo, bool> predicate)
    {
        ThrowIfDisposed();
        int count = 0;
        foreach (var entry in List())
        {
            if (predicate(entry) && Delete(entry.Path)) count++;
        }
        return count;
    }

    /// <summary>
    /// 把当前逻辑状态（已保存条目减去标记删除的，加上排队中的新增）解压到
    /// <paramref name="outputDir"/>。
    /// <para>
    /// 进度回调 <paramref name="progress"/> 只覆盖"从磁盘归档解压已保存条目"这一步——排队中
    /// 还没保存的新增条目是直接从原始磁盘路径 <c>File.Copy</c> 过去的，不经过 7-Zip、不计入
    /// 这个进度回调（通常很快，而且源头就是本地磁盘文件，没有压缩/解压开销）。
    /// </para>
    /// </summary>
    public void ExtractAll(string outputDir, IProgress<SevenZipProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        Directory.CreateDirectory(outputDir);

        if (_hasBaseArchive && _inArchive is not null)
        {
            var keptItems = _baseItems.FindAll(i => !_deletedBaseIndices.Contains(i.Index));
            if (keptItems.Count > 0)
            {
                foreach (var dirItem in keptItems)
                {
                    if (!dirItem.IsDir) continue;
                    Directory.CreateDirectory(Path.Combine(outputDir, dirItem.Path.Replace('/', Path.DirectorySeparatorChar)));
                }

                var dict = keptItems.ToDictionary(i => i.Index);
                using var extractCb = new ExtractCallback(dict, outputDir, _password, progress, cancellationToken);
                nint cbCcw = ComFactory.GetCcw<IArchiveExtractCallback>(extractCb);

                int hr;
                if (keptItems.Count == _baseItems.Count)
                {
                    // 没有任何条目被标记删除：直接走"提取全部"的快路径（indices=NULL, numItems=0xFFFFFFFF
                    // 是 7-Zip 的约定写法），不用构造索引数组。
                    hr = _inArchive.Extract(0, 0xFFFFFFFFu, 0, cbCcw);
                }
                else
                {
                    uint[] indices = keptItems.ConvertAll(i => i.Index).ToArray();
                    unsafe
                    {
                        fixed (uint* p = indices)
                        {
                            hr = _inArchive.Extract((nint)p, (uint)indices.Length, 0, cbCcw);
                        }
                    }
                }
                Check(hr, "IInArchive.Extract", cancellationToken);
            }
        }

        foreach (var (archivePath, diskPath) in _pendingAdds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string dest = Path.Combine(outputDir, archivePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(diskPath, dest, overwrite: true);
        }
    }

    /// <summary>
    /// 把排队中的全部 <see cref="AddFile"/>/<see cref="Delete(string)"/> 改动真正写回磁盘。
    /// 没有排队改动时直接返回（no-op）。
    /// <para>
    /// 实现方式：保留的旧条目通过 <c>IOutArchive::UpdateItems</c> 的 <c>indexInArchive</c> 机制
    /// 直接从原归档搬运原始压缩字节（不解压重新编码），新增条目正常编码——整体写到一个临时
    /// 文件，确认成功后才释放旧句柄、删除原文件、把临时文件移动过去（原文件在整个过程中途
    /// 失败都不会被破坏，失败时临时文件会被清理掉，原归档保持原样）。成功后会重新以"刚保存的
    /// 新内容"为基准重新打开，后续 <see cref="List"/>/<see cref="ExtractAll"/>/再次
    /// <see cref="AddFile"/>/<see cref="Delete(string)"/> 都基于这个新基准。
    /// </para>
    /// </summary>
    public void SaveChanges(IProgress<SevenZipProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!HasPendingChanges) return;

        var finalItems = new List<UpdateSourceItem>(_baseItems.Count + _pendingAdds.Count);
        foreach (var item in _baseItems)
        {
            if (_deletedBaseIndices.Contains(item.Index)) continue;
            finalItems.Add(new UpdateSourceItem { ArchivePath = item.Path, IsDir = item.IsDir, ExistingArchiveIndex = item.Index });
        }
        foreach (var (archivePath, diskPath) in _pendingAdds)
        {
            finalItems.Add(new UpdateSourceItem
            {
                ArchivePath = archivePath,
                IsDir = false,
                DiskPath = diskPath,
                Size = new FileInfo(diskPath).Length,
                MTime = File.GetLastWriteTimeUtc(diskPath)
            });
        }

        string tempOutput = $"{_archivePath}.sevenziplite-tmp-{Guid.NewGuid():N}";
        try
        {
            using (var tempStream = new FileStream(tempOutput, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var outAdapter = new ComOutStream(tempStream, leaveOpen: true);
                nint outCcw = ComFactory.GetCcw<IOutStream>(outAdapter);
                using var updateCb = new UpdateCallback(finalItems, _password, progress, cancellationToken);
                nint cbCcw = ComFactory.GetCcw<IArchiveUpdateCallback>(updateCb);

                (IOutArchive writer, nint writerPtr) = _hasBaseArchive
                    ? (_outArchive!, _outArchivePtr)
                    : CreateFreshOutArchive(_clsid);

                // 和 SevenZipCompressor.CreateCore 同一套属性设置（"mt" 线程数 + 按条件
                // 的 "he" 归档头加密），详见 SevenZipCompressor.SetProperties 的类型注释和
                // CreateCore 里 "he" 那段注释——这里复用同一个方法，保证两条创建/写入路径的
                // 加密语义完全一致，不会出现"一个默认加密文件头、另一个默认不加密"的不一致。
                uint effectiveThreads = (uint)Math.Max(1, Environment.ProcessorCount);
                var propNames = new List<string> { "mt" };
                var propValues = new List<PropVariant> { PropVariant.FromUInt32(effectiveThreads) };
                if (_format == ArchiveFormat.SevenZip && _password is not null && _encryptHeaders)
                {
                    propNames.Add("he");
                    propValues.Add(PropVariant.FromBool(true));
                }
                SevenZipCompressor.SetProperties(writerPtr, propNames, propValues);

                int hr = writer.UpdateItems(outCcw, (uint)finalItems.Count, cbCcw);
                Check(hr, "IOutArchive.UpdateItems", cancellationToken);
            }
        }
        catch
        {
            TryDeleteFile(tempOutput);
            throw;
        }

        // 写临时文件成功了，现在才安全地释放旧句柄（必须先释放才能删除/替换原文件，尤其是
        // Windows 下对仍被打开的文件做 Delete/Move 会直接失败）。
        CloseBaseHandles();
        TryDeleteFile(_archivePath);
        File.Move(tempOutput, _archivePath);

        _hasBaseArchive = true;
        OpenBase();
        _pendingAdds.Clear();
        _deletedBaseIndices.Clear();
    }

    /// <summary>
    /// 释放原生句柄和打开的文件流。<b>不会</b>自动 <see cref="SaveChanges"/>——排队中的改动
    /// 如果没有显式保存，这里会被直接丢弃，详见类型注释。可以安全地重复调用。
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CloseBaseHandles();
    }

    // ---------------- 内部实现 ----------------

    private static (IOutArchive, nint) CreateFreshOutArchive(Guid clsid)
    {
        int hr = NativeMethods.CreateObject(clsid, Guids.IID_IOutArchive, out nint ptr);
        if (hr != HResult.S_OK || ptr == 0)
            throw new InvalidOperationException($"CreateObject(IOutArchive) 失败, HRESULT=0x{hr:X8}");
        return (ComFactory.GetRcw<IOutArchive>(ptr), ptr);
    }

    private void OpenBase()
    {
        int hr = NativeMethods.CreateObject(_clsid, Guids.IID_IInArchive, out nint inPtr);
        if (hr != HResult.S_OK || inPtr == 0)
            throw new InvalidOperationException($"CreateObject(IInArchive) 失败, HRESULT=0x{hr:X8}");
        var inArchive = ComFactory.GetRcw<IInArchive>(inPtr);

        // 关键一步：对同一个 native 对象（刚才那个 inPtr 指向的 COM 对象身份）再做一次
        // QueryInterface 换 IOutArchive，而不是另外 CreateObject 一个新对象——这样
        // UpdateItems 的 indexInArchive 机制才能correlate 到这次 Open() 读到的真实内容，
        // 是 7-Zip 官方"原地更新归档"（7z.exe 的 "u" 命令）用的同一套机制。
        // The key step: QueryInterface for IOutArchive on the SAME native COM object
        // identity (the one inPtr refers to) rather than CreateObject()-ing a separate
        // new object — only this way does UpdateItems' indexInArchive mechanism
        // correlate to the content actually read by this Open() call. This mirrors the
        // exact mechanism the official 7z.exe "u" (update) command uses.
        int hrQi = Marshal.QueryInterface(inPtr, in Guids.IID_IOutArchive, out nint outPtr);
        if (hrQi != 0 || outPtr == 0)
        {
            throw new NotSupportedException(
                $"QueryInterface(IOutArchive) 失败, HRESULT=0x{hrQi:X8}——这个格式的原生 handler 不支持" +
                $"原地更新（SevenZipArchiveEditor 只支持 7z/zip 且要求原生库是完整读写版本）。");
        }
        var outArchive = ComFactory.GetRcw<IOutArchive>(outPtr);

        var fileStream = new FileStream(_archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var inAdapter = new ComInStream(fileStream, leaveOpen: true);
            nint streamCcw = ComFactory.GetCcw<IInStream>(inAdapter);
            var openCb = new ArchiveOpenCallback(_password);
            nint openCbCcw = ComFactory.GetCcw<IArchiveOpenCallback>(openCb);
            int hrOpen = inArchive.Open(streamCcw, 0, openCbCcw);
            Check(hrOpen, "IInArchive.Open");
        }
        catch
        {
            fileStream.Dispose();
            throw;
        }

        _inArchive = inArchive;
        _outArchive = outArchive;
        _outArchivePtr = outPtr;
        _inputStream = fileStream;
        _baseItems = SevenZipArchive.ListOpened(inArchive);
    }

    private void CloseBaseHandles()
    {
        if (_inArchive is not null)
        {
            try { _inArchive.Close(); }
            catch { /* 马上就要整个丢弃这个对象了，Close() 失败也无所谓 */ }
        }
        _inArchive = null;
        _outArchive = null;
        _outArchivePtr = 0;
        _inputStream?.Dispose();
        _inputStream = null;
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* 尽力而为：调用方会看到真正的异常是上一层抛出的那个 */ }
    }

    private static string NormalizeArchivePath(string archivePath) => archivePath.Replace('\\', '/').TrimStart('/');

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SevenZipArchiveEditor));
    }

    private static Guid ClsidFor(ArchiveFormat format) => format switch
    {
        ArchiveFormat.SevenZip => Guids.CLSID_7z,
        ArchiveFormat.Zip => Guids.CLSID_Zip,
        _ => throw new NotSupportedException(
            $"SevenZipArchiveEditor 目前只支持 7z / Zip 格式的增量读写（{format} 不支持原地增删条目，" +
            "仍然只能用 SevenZipCompressor.CreateArchive 整体重新打包）。")
    };

    private static void Check(int hr, string what, CancellationToken cancellationToken = default)
    {
        if (hr == HResult.S_OK) return;
        if (hr == HResult.E_ABORT && cancellationToken.IsCancellationRequested)
            throw new OperationCanceledException(cancellationToken);
        if (hr == HResult.S_FALSE)
        {
            throw new InvalidOperationException(
                $"{what} 返回 S_FALSE：密码错误、未提供密码，或文件不是有效的归档（已损坏/格式不匹配）。");
        }
        throw new InvalidOperationException($"{what} 失败, HRESULT=0x{hr:X8}");
    }
}
