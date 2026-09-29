using System.Collections.Concurrent;
using System.Runtime.InteropServices.Marshalling;
using SevenZipLite.Native;
using SevenZipLite.Streams;

namespace SevenZipLite.Callbacks;

public sealed class UpdateSourceItem
{
    public required string ArchivePath { get; init; } // 归档内的相对路径（正斜杠）
    public required bool IsDir { get; init; }
    public string? DiskPath { get; init; } // 非目录时：磁盘上的真实文件路径
    public long Size { get; init; }
    public DateTime? MTime { get; init; }
}

/// <summary>
/// 归档创建回调：实现 IArchiveUpdateCallback（含基接口 IProgress）向 native 侧提供每个
/// 待压缩条目的属性和数据流；同时实现 ICryptoGetTextPassword2 用于加密归档时提供密码。
///
/// <para>
/// 输入流生命周期管理（重要，踩过两次坑，都记录在这里）：<br/>
/// 1) <b>不能</b>用一个"当前流"共享字段在 <c>GetStream</c>/<c>SetOperationResult</c> 里
///    成对 Dispose（<see cref="ExtractCallback"/> 那种写法）——那是假设 native
///    对这个回调的调用是严格单线程顺序的（GetStream(i) 完整读完才会有 GetStream(i+1)）。
///    这个假设在多线程压缩（<c>mt&gt;1</c>）下不成立：跟踪原生源码
///    （<c>ZipUpdate.cpp::Update2</c>，多线程分发路径）发现，
///    <c>SetOperationResult</c> 在多线程模式下是紧跟着 <c>GetStream</c> 之后、
///    在真正开始压缩**之前**、从同一个主调度线程里调用的（用来做进度记账），
///    根本不是"这个文件已经压缩完，可以关闭输入流了"的信号——真正的读取发生在
///    "稍后由某个工作线程異步执行" 的 <c>CThreadInfo::WaitAndCode()</c> 里。
///    如果在 <c>SetOperationResult</c> 里就把共享字段指向的流 Dispose 掉，会在
///    工作线程真正开始读之前（甚至读到一半时）就把 <c>FileStream</c> 关掉，
///    导致 <c>ObjectDisposedException</c>，被 <see cref="ComInStream.Read"/> 的
///    catch 吞掉转成 E_FAIL——这才是"批量属性/多线程压缩下必现 E_FAIL"这个历史 bug
///    的真正根因，跟 .NET 源生成 COM 互操作本身是否支持从 native 创建的线程回调
///    没有关系（这是本项目早期排查时的一个误诊，见 demo/README.md 对应章节的更正说明）。<br/>
/// 2) 但也<b>不能</b>干脆什么都不 Dispose、指望 .NET 的 <c>ComWrappers</c> 在
///    native 对某个 CCW 接口指针 <c>Release()</c> 到引用计数 0 时自动调用
///    <see cref="IDisposable.Dispose"/>——实测验证过它并不会这样做（这跟经典 COM
///    互操作、或者 RCW 方向的 <c>Marshal.ReleaseComObject</c> 不是一回事）。
///    对本类这种只读的输入流问题不大（<c>FileShare.Read</c> 不独占），但对应用在
///    <see cref="ExtractCallback"/> 上的同款"不主动 Dispose"写法是灾难性的：解压用的
///    输出流是 <c>FileShare.None</c> 独占写入，不及时关闭会导致后续任何尝试打开
///    同一个文件的代码（包括我们自己的校验逻辑）报
///    "the process cannot access the file because it is being used by another process"。<br/>
/// 3) 综合以上两条，本类采用的正确做法是：每次 <c>GetStream</c> 创建的
///    <see cref="ComInStream"/> 都不主动在回调内部的任何一步 Dispose，而是登记进
///    <see cref="_pendingStreams"/> 这个线程安全集合里，统一等到整个
///    <c>IOutArchive.UpdateItems</c> 调用彻底返回、<see cref="Dispose"/>
///    （<c>using var updateCb = ...</c>）触发时再一次性全部关闭——因为这些都是只读输入流，
///    在整个压缩过程期间都保持打开不会有正确性问题，只是会让"同时打开的文件描述符数量"
///    略高于理论最优（等于本次归档的文件总数），这个代价与"少量文件"的封装库定位相比可以接受。
/// </para>
/// </summary>
[GeneratedComClass]
internal sealed partial class UpdateCallback : IArchiveUpdateCallback, ICryptoGetTextPassword2, IDisposable
{
    private readonly IReadOnlyList<UpdateSourceItem> _items;
    private readonly string? _password;
    private readonly ConcurrentBag<ComInStream> _pendingStreams = new();

    public UpdateCallback(IReadOnlyList<UpdateSourceItem> items, string? password)
    {
        _items = items;
        _password = password;
    }

    public int SetTotal(ulong total) => HResult.S_OK;
    public int SetCompleted(nint completeValue) => HResult.S_OK;

    public int GetUpdateItemInfo(uint index, out int newData, out int newProps, out uint indexInArchive)
    {
        newData = 1;
        newProps = 1;
        indexInArchive = uint.MaxValue; // -1: 全新归档，不存在“已有条目”
        return HResult.S_OK;
    }

    public int GetProperty(uint index, uint propID, nint value)
    {
        // value 是 native 在它自己栈上分配的 PROPVARIANT* 缓冲区（win-x86 下只有 16
        // 字节），这里先在托管这边本地构造好完整的 PropVariant，最后统一用
        // PropVariant.WriteToNative 只写前 16 字节落地，绝不整块 blit 24 字节结构体。
        // 详见 PropVariant.cs 类型注释与 ComInterfaces.cs 里对应接口声明的注释。
        var pv = default(PropVariant);
        var item = _items[(int)index];
        switch (propID)
        {
            case PropId.kpidPath:
                pv = PropVariant.FromString(item.ArchivePath);
                break;
            case PropId.kpidIsDir:
                pv = PropVariant.FromBool(item.IsDir);
                break;
            case PropId.kpidSize:
                pv = PropVariant.FromUInt64((ulong)item.Size);
                break;
            case PropId.kpidMTime:
                if (item.MTime is { } dt)
                {
                    long ft = dt.ToFileTimeUtc();
                    pv = new PropVariant
                    {
                        vt = PropVariant.VT_FILETIME,
                        filetimeLow = unchecked((uint)ft),
                        filetimeHigh = unchecked((uint)(ft >> 32))
                    };
                }
                break;
            default:
                pv = default; // VT_EMPTY
                break;
        }
        PropVariant.WriteToNative(value, pv);
        return HResult.S_OK;
    }

    public int GetStream(uint index, out nint inStream)
    {
        inStream = 0;

        var item = _items[(int)index];
        if (item.IsDir || item.DiskPath is null) return HResult.S_OK;

        var fs = new FileStream(item.DiskPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var adapter = new ComInStream(fs);
        _pendingStreams.Add(adapter); // 统一延迟到 Dispose() 里关闭，理由见类型注释
        inStream = ComFactory.GetCcw<IInStream>(adapter);
        return HResult.S_OK;
    }

    public int SetOperationResult(int operationResult) => HResult.S_OK;

    public int CryptoGetTextPassword2(out int passwordIsDefined, out nint password)
    {
        if (_password is null)
        {
            passwordIsDefined = 0;
            password = 0;
        }
        else
        {
            passwordIsDefined = 1;
            password = Utf32Marshal.AllocBStr(_password);
        }
        return HResult.S_OK;
    }

    public void Dispose()
    {
        // 统一在这里一次性关闭本次 UpdateItems 期间创建过的所有输入流，见类型注释里
        // 关于"为什么不能在 GetStream/SetOperationResult 里成对及时 Dispose"的说明。
        while (_pendingStreams.TryTake(out var adapter))
            adapter.Dispose();
    }
}
