using System.Runtime.InteropServices;
using SevenZipLite.Callbacks;
using SevenZipLite.Native;
using SevenZipLite.Streams;

namespace SevenZipLite;

/// <summary>
/// 显式指定 Zip 写入时使用的压缩方法（对应 7-Zip 原生 zip handler 里
/// <c>NFileHeader::NCompressionMethod</c> 的数值），通过标准 <c>ISetProperties::SetProperties</c>
/// 传入 name="m" 实现——这与真实 7z 宿主程序（7zFM/命令行 "-mm=" 参数）选择压缩方法用的
/// 是完全同一套机制，是一个通用能力，不是 bug workaround。
/// </summary>
public enum ZipMethodOverride
{
    /// <summary>NFileHeader::NCompressionMethod::kStore = 0，纯拷贝不压缩，不经过编解码器注册表。</summary>
    Store = 0,
    /// <summary>NFileHeader::NCompressionMethod::kDeflate = 8，走 CreateCoder_Id 注册表查找。</summary>
    Deflate = 8,
}

/// <summary>创建 7z、zip、tar、tar.gz 归档的高层 API。</summary>
public static class SevenZipCompressor
{
    public sealed class Entry
    {
        public required string DiskPath { get; init; }
        public required string ArchivePath { get; init; } // 建议用 '/' 分隔
    }

    private static Guid ClsidFor(ArchiveFormat format) => format switch
    {
        ArchiveFormat.SevenZip => Guids.CLSID_7z,
        ArchiveFormat.Zip => Guids.CLSID_Zip,
        ArchiveFormat.Tar => Guids.CLSID_Tar,
        ArchiveFormat.TarGZip => Guids.CLSID_GZip,
        _ => throw new NotSupportedException(format.ToString())
    };

    /// <summary>
    /// 创建归档。outputArchivePath 是最终归档路径（不带卷号后缀）；
    /// 若 volumeSize > 0，会在同目录生成 outputArchivePath.001 / .002 / … 分卷文件，
    /// 否则生成单一完整文件（此时文件名就是 outputArchivePath 本身）。
    /// 密码仅对 7z / zip 生效（对应报告里"仅 7z/zip 支持密码"的结论）；
    /// 对 tar / tar.gz 传入 password 会被忽略。
    /// </summary>
    /// <param name="entries">要打包的条目列表。</param>
    /// <param name="outputArchivePath">最终归档路径（不带卷号后缀）。</param>
    /// <param name="format">归档格式。</param>
    /// <param name="password">可选密码（仅 7z/zip 生效）。</param>
    /// <param name="volumeSize">分卷大小（字节），0 表示不分卷。</param>
    /// <param name="zipMethodOverride">仅在 format=Zip 时有意义，显式指定压缩方法。</param>
    /// <param name="threadCount">压缩线程数，默认 null 时取 <see cref="Environment.ProcessorCount"/>。
    /// 传 1 可以强制单线程（等价于历史上的 <c>mt=1</c> 修复方案，现在已经不再需要，仅作为
    /// 兼容/调试选项保留）。多线程压缩已经过验证安全，见 <see cref="Callbacks.UpdateCallback"/>
    /// 类型注释里对 <c>_pendingStreams</c> 设计的说明。</param>
    public static IReadOnlyList<string> CreateArchive(
        IReadOnlyList<Entry> entries,
        string outputArchivePath,
        ArchiveFormat format,
        string? password = null,
        long volumeSize = 0,
        ZipMethodOverride? zipMethodOverride = null,
        int? threadCount = null)
    {
        if (!format.SupportsPassword()) password = null;

        if (format == ArchiveFormat.TarGZip)
        {
            return CreateTarGz(entries, outputArchivePath, volumeSize, threadCount);
        }

        var items = entries.Select(e => new UpdateSourceItem
        {
            ArchivePath = e.ArchivePath,
            IsDir = false,
            DiskPath = e.DiskPath,
            Size = new FileInfo(e.DiskPath).Length,
            MTime = File.GetLastWriteTimeUtc(e.DiskPath)
        }).ToList();

        return CreateCore(ClsidFor(format), items, outputArchivePath, password, volumeSize,
            format == ArchiveFormat.Zip ? zipMethodOverride : null, threadCount);
    }

    private static IReadOnlyList<string> CreateCore(
        Guid clsid, List<UpdateSourceItem> items, string outputArchivePath, string? password, long volumeSize,
        ZipMethodOverride? zipMethodOverride = null, int? threadCount = null)
    {
        int hr = NativeMethods.CreateObject(clsid, Guids.IID_IOutArchive, out nint ptr);
        if (hr != HResult.S_OK || ptr == 0)
            throw new InvalidOperationException($"CreateObject(IOutArchive) 失败, HRESULT=0x{hr:X8}");
        var outArchive = ComFactory.GetRcw<IOutArchive>(ptr);

        // 注意：ISetProperties::SetProperties 每次调用都会先整体重置属性
        // （见 ZipHandler.h::InitMethodProps() 里的 "_props.Init(); m_MainMethod = -1;"，
        // 7z/tar 的对应 SetProperties 实现同理），所以这里必须把"线程数"和"显式压缩方法覆盖"
        // 合并成同一次调用，分两次调用会导致后一次把前一次设置的属性冲掉。
        //
        // 线程数默认取 Environment.ProcessorCount，真正启用多线程压缩——这是从"历史上
        // 曾经因为一个真实 bug 被迫永久 mt=1"恢复过来的（bug 本身及修复方式见下面
        // SetUInt32Properties 方法上那段"多线程压缩 E_FAIL 根因排查记录"，已重新排查并
        // 更正过结论）。传 threadCount=1 仍然可以强制单线程（等价于旧行为），留作
        // 兼容/调试用途。
        uint effectiveThreads = (uint)Math.Max(1, threadCount ?? Environment.ProcessorCount);
        var propNames = new List<string> { "mt" };
        var propValues = new List<uint> { effectiveThreads };
        if (zipMethodOverride is { } methodOverride)
        {
            propNames.Add("m");
            propValues.Add((uint)methodOverride);
        }
        SetUInt32Properties(ptr, propNames, propValues);

        using var split = new SplittingFileStream(outputArchivePath, volumeSize);
        var outAdapter = new ComOutStream(split, leaveOpen: true);
        nint streamCcw = ComFactory.GetCcw<IOutStream>(outAdapter);

        using var updateCb = new UpdateCallback(items, password);
        nint cbCcw = ComFactory.GetCcw<IArchiveUpdateCallback>(updateCb);

        int hr2 = outArchive.UpdateItems(streamCcw, (uint)items.Count, cbCcw);
        split.Flush();
        if (hr2 != HResult.S_OK)
            throw new InvalidOperationException($"IOutArchive.UpdateItems 失败, HRESULT=0x{hr2:X8}");

        return split.CreatedVolumes.Count > 0
            ? split.CreatedVolumes
            : new List<string> { outputArchivePath };
    }

    /// <summary>
    /// 批量调用 IOutArchive 上的 ISetProperties::SetProperties，一次性传入若干个
    /// name/value(VT_UI4) 属性对。
    ///
    /// 必须批量传、不能分多次调用 SetProperties：native 侧每次 SetProperties 实现
    /// 开头都会先整体重置属性状态（例如 Zip 的 ZipHandler.h::InitMethodProps() 里
    /// "_props.Init(); m_MainMethod = -1;"，7z/tar 的实现同理），后一次调用会把前一次
    /// 设置的值全部冲掉，包括下面 CreateCore 里始终需要设置的 "mt"（线程数）。
    ///
    /// "多线程压缩下 IOutArchive.UpdateItems 必现 E_FAIL(0x80004005)" 根因排查记录
    /// （重要，勿删；这段历史上出过一次误诊，下面是更正后的最终结论）：
    ///
    /// <b>症状</b>：Zip 归档只要选中一个真正需要编解码器的压缩方法（Deflate 等，Store
    /// 除外）且线程数&gt;1，UpdateItems 就必现 E_FAIL。
    ///
    /// <b>第一次排查给出的结论（已证明是误诊，记录下来避免以后又得出同样错误的结论）</b>：
    /// 当时通过在 100% 官方未裁剪的 Format7zF 源码里插入 native 诊断日志，观察到失败发生
    /// 在"任何一次 GetStream/Read 回调被调用之前"，据此推断是".NET 源生成 COM 互操作
    /// （System.Runtime.InteropServices.Marshalling）不支持从 native 自己创建、未依附
    /// CLR 的工作线程安全地回调进 CCW"，并用 ISetProperties::SetProperties("mt", 1)
    /// 强制单线程压缩来"修复"——这个变通方案确实能让症状消失（所以当时被误认为是修复），
    /// 但诊断日志插入的位置不对，实际上从未真正定位到失败发生的确切代码行，结论是拍脑袋
    /// 推断出来的。
    ///
    /// <b>重新排查后的真实根因</b>：在本类曾经的 <c>UpdateCallback.GetStream</c> 实现里，
    /// 用一个共享字段 <c>_currentAdapter</c> 缓存"当前"输入流，并在每次 <c>GetStream</c>
    /// 开头、以及 <c>SetOperationResult</c> 里都 Dispose 它——这个写法隐含假设了 native
    /// 对该回调的调用是**严格单线程顺序**的（GetStream(i) 的流被完整读完，才会轮到
    /// GetStream(i+1)）。跟踪 7-Zip 官方源码 <c>ZipUpdate.cpp::Update2</c>（多线程分发
    /// 路径）发现这个假设是错的：<c>SetOperationResult</c> 在多线程模式下紧跟在
    /// <c>GetStream</c> 之后、在真正开始压缩**之前**、从同一个主调度线程里就被调用了
    /// （只是用来做进度记账），根本不代表"这个文件已经压缩完，可以关闭输入流了"——真正的
    /// 读取发生在**稍后**由某个工作线程异步执行的 <c>CThreadInfo::WaitAndCode()</c> 里。
    /// 于是：主线程刚为第 2 个文件调用完 GetStream/SetOperationResult，就会把共享字段
    /// 指向的第 1 个文件的 <c>FileStream</c> Dispose 掉，而这时工作线程可能才刚开始、
    /// 或者正在读第 1 个文件——读到一半的 <c>FileStream</c> 突然被关闭，抛出
    /// <c>ObjectDisposedException</c>，被 <see cref="Streams.ComInStream.Read"/> 里的
    /// catch-all 吞掉转成 E_FAIL，逐层向上传播成 <c>UpdateItems</c> 的最终失败——这是一个
    /// 普普通通的"共享可变状态被跨调用覆盖"竞态 bug，跟 .NET 互操作是否支持从 native
    /// 线程回调完全没有关系。强制 <c>mt=1</c> 能让症状消失，只是因为单线程路径
    /// （<c>Update2St</c>）下 GetStream/SetOperationResult 确实是严格顺序配对的，
    /// 恰好绕开了这个假设被打破的场景，并不是因为多线程回调本身不安全。
    ///
    /// 这个真实根因是在实现"大文件吞吐量测试"、观察到"单线程压缩速度低得不合理"后，
    /// 用临时诊断日志重新定位出来的：给 <c>ComInStream.Read</c> 的 catch 块和
    /// <c>UpdateCallback.GetStream</c> 加日志，强行把 <c>mt</c> 改回 &gt;1 复现，日志
    /// 里清清楚楚记录着 <c>ObjectDisposedException: Cannot access a closed Stream</c>，
    /// 而不是任何跟线程attach/COM套间相关的异常。
    ///
    /// <b>正式修复</b>：见 <see cref="Callbacks.UpdateCallback"/> 类型注释——不再用共享
    /// 字段 + GetStream/SetOperationResult 成对 Dispose 的写法，改为把每次 GetStream
    /// 创建的流登记进一个线程安全集合，统一延迟到整个 UpdateItems 调用结束后、
    /// <c>UpdateCallback.Dispose()</c> 里一次性关闭。修复后 <c>mt</c> 恢复默认为
    /// <see cref="Environment.ProcessorCount"/>（见 <see cref="CreateCore"/>），压缩
    /// 吞吐量实测有明显提升（沙箱环境 2 核下，128MB 合成数据用 7z/LZMA2 格式压缩，
    /// 单线程约 7 MB/s，多线程约 12~31 MB/s，跑分本身在共享/嘈杂的沙箱环境里波动较大，
    /// 但方向和量级是一致的），且已经过更高并发（8 线程、20 文件、15 轮循环，覆盖
    /// 7z/zip/tar 三种格式）反复压力测试验证，未见任何一次数据损坏或崩溃——测试代码见
    /// <c>SevenZipLite.SelfTest.MultiThreadStressRunner</c>。
    ///
    /// 解压（<see cref="Callbacks.ExtractCallback"/>）<b>没有</b>改成同样的"延迟到最后统一
    /// 释放"写法，仍然保留共享字段 + 及时 Dispose：这是因为解压场景下及时关闭输出文件是
    /// 必需的（不然会导致"文件正被另一进程占用"这类错误，本身也确实在改造过程中踩到过），
    /// 而"及时 Dispose"这个动作本身在解压里之所以安全，是因为我们从未观察到、也没有理论
    /// 依据认为 7-Zip 的 Extract 路径会对同一个回调实例发起交叉/并发调用——如果未来要验证
    /// 或支持"解压多线程压缩出的归档"这类场景，需要先用类似的方式重新确认 native 侧的真实
    /// 调用时序，而不是想当然套用这里的结论。
    /// </summary>
    private static unsafe void SetUInt32Properties(nint outArchivePtr, IReadOnlyList<string> names, IReadOnlyList<uint> values)
    {
        if (names.Count != values.Count || names.Count == 0)
            throw new ArgumentException("names/values 数量必须一致且非空");

        var setProps = ComFactory.GetRcw<ISetProperties>(outArchivePtr);

        // native 端 PROPVARIANT 的真实大小是**平台相关**的，不是固定 16 字节——这是本方法
        // 早期版本的一个真实 bug（已修复，记录见下，供以后维护者参考，不要再犯）：
        //
        //   - 在 Windows 64 位（win-x64/win-arm64，用的是 OS 自带 <oaidl.h> 里的
        //     tagPROPVARIANT）：sizeof(PROPVARIANT) == 24 字节。这是因为真实 PROPVARIANT
        //     的匿名 union 里还包含 CAUB/CAC/CAPROPVARIANT 这类"计数+指针"形式的成员
        //     （例如 { ULONG cElems; PROPVARIANT *pElems; }），在 64 位下为了让内部指针
        //     按 8 字节对齐，整个 union 被撑到 16 字节，加上 8 字节头（vt + 3×WORD）＝24。
        //   - 在 Windows 32 位（win-x86）：同样的 union 因为指针只有 4 字节，整体是 8 字节，
        //     加上 8 字节头＝16。
        //   - 在 Linux/Android（用的是 7-Zip 自己在 Common/MyWindows.h 里重新定义的简化版
        //     tagPROPVARIANT，union 成员只有 CHAR/SHORT/LONG/LARGE_INTEGER/FILETIME/BSTR
        //     这些最大 8 字节的类型，没有上面那些"计数+指针"结构体）：不管 32/64 位，
        //     恒定是 8(头)+8(union)=16 字节。
        //
        // 只传 1 个属性时，因为数组只有一个元素、偏移量 0 对两种步幅都成立，这个 bug
        // 完全不会暴露；一旦同时传 2 个或以上属性（例如 "mt"+"m" 一起设置——这正是本类
        // 每次创建 zip 归档时的标准做法），用错误的 16 字节步幅在 win-x64/win-arm64 上会导致：
        // 第 2 个元素被 native 从错误的偏移（我们写在偏移 16，native 用 sizeof=24 读偏移 24）
        // 读出未初始化的垃圾数据，且我们分配的缓冲区本身也不够大（只分配了 16*n 字节，
        // native 却会按 24*n 字节去访问，读到缓冲区外）——这正是真机反馈里
        // "SetProperties(mt,m) 在 win-x64 上报 E_INVALIDARG(0x80070057)，但只传 mt 时正常"
        // 这个现象的完整成因。
        int propVariantSize = OperatingSystem.IsWindows() && nint.Size == 8 ? 24 : 16;

        int n = names.Count;
        var namePtrs = new nint[n];
        nint namesArray = Marshal.AllocHGlobal(nint.Size * n);
        nint valuesArray = Marshal.AllocHGlobal(propVariantSize * n);
        try
        {
            for (int i = 0; i < n; i++)
            {
                namePtrs[i] = Utf32Marshal.AllocW(names[i]);
                Marshal.WriteIntPtr(namesArray, i * nint.Size, namePtrs[i]);
                var pv = PropVariant.FromUInt32(values[i]);
                // 每个 PropVariant 槽位清零后再写：不管步幅是 16 还是 24，PropVariant.WriteToNative
                // 只写前 16 字节，槽位剩余部分（win-x64/arm64 下是最后 8 字节）必须清零而不是
                // 保留 AllocHGlobal 给的未初始化垃圾——虽然 VT_UI4 的读取本不会用到这部分，
                // 但清零是对"未定义行为"更保守、也更容易排查问题的做法。
                new Span<byte>((void*)(valuesArray + i * propVariantSize), propVariantSize).Clear();
                PropVariant.WriteToNative(valuesArray + i * propVariantSize, pv);
            }

            int hr = setProps.SetProperties(namesArray, valuesArray, (uint)n);
            if (hr != HResult.S_OK)
                throw new InvalidOperationException($"ISetProperties.SetProperties({string.Join(",", names)}) 失败, HRESULT=0x{hr:X8}");
        }
        finally
        {
            foreach (var p in namePtrs) Utf32Marshal.FreeW(p);
            Marshal.FreeHGlobal(namesArray);
            Marshal.FreeHGlobal(valuesArray);
        }
    }

    // tar.gz = gzip( tar(files) )：先在临时文件生成不分卷、不加密的 tar，
    // 再把这个 tar 作为 gzip 容器唯一的内层流，走一次只有 1 个条目的 UpdateItems，
    // 分卷（如果需要）在这一步的外层 gzip 输出上进行。
    private static IReadOnlyList<string> CreateTarGz(IReadOnlyList<Entry> entries, string outputArchivePath, long volumeSize, int? threadCount = null)
    {
        string tempTar = Path.Combine(Path.GetTempPath(), $"sevenziplite_{Guid.NewGuid():N}.tar");
        try
        {
            var tarItems = entries.Select(e => new UpdateSourceItem
            {
                ArchivePath = e.ArchivePath,
                IsDir = false,
                DiskPath = e.DiskPath,
                Size = new FileInfo(e.DiskPath).Length,
                MTime = File.GetLastWriteTimeUtc(e.DiskPath)
            }).ToList();
            CreateCore(Guids.CLSID_Tar, tarItems, tempTar, null, 0, threadCount: threadCount);

            string innerName = Path.GetFileNameWithoutExtension(outputArchivePath) + ".tar";
            var gzItem = new List<UpdateSourceItem>
            {
                new UpdateSourceItem
                {
                    ArchivePath = innerName,
                    IsDir = false,
                    DiskPath = tempTar,
                    Size = new FileInfo(tempTar).Length,
                    MTime = File.GetLastWriteTimeUtc(tempTar)
                }
            };
            return CreateCore(Guids.CLSID_GZip, gzItem, outputArchivePath, null, volumeSize, threadCount: threadCount);
        }
        finally
        {
            try { File.Delete(tempTar); } catch { /* 忽略 */ }
        }
    }
}
