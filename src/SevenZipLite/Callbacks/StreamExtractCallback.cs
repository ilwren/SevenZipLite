using System.Runtime.InteropServices.Marshalling;
using SevenZipLite.Native;
using SevenZipLite.Streams;

namespace SevenZipLite.Callbacks;

/// <summary>
/// 单条目解压回调：把 <see cref="IInArchive.Extract"/> 用显式 indices 数组限定为\"只解压这一个
/// 条目\"时收到的唯一一次 <see cref="GetStream"/> 回调，直接写进调用方提供的目标 <see cref="Stream"/>
/// （而不是像 <see cref="ExtractCallback"/> 那样在磁盘上创建新文件）——用于
/// <see cref="SevenZipLite.SevenZipArchive.ExtractEntryToStream"/>。
///
/// <para>
/// 复用 <see cref="ExtractCallback"/> 的"单个共享字段 + GetStream/SetOperationResult 成对及时
/// Dispose"写法和前提（native 对该回调的调用严格单线程顺序，见 <see cref="ExtractCallback"/>
/// 类型注释）——这里只解压一个条目，严格顺序的前提天然成立，不需要额外验证。
/// </para>
/// <para>
/// 关键区别：目标 <see cref="Stream"/> 是调用方传进来的、调用方自己负责生命周期管理的对象，
/// 不是我们在内部新建的 <see cref="FileStream"/>——所以这里构造 <see cref="ComOutStream"/> 时
/// 必须传 <c>leaveOpen: true</c>，绝不能让这个回调意外把调用方的流关掉。
/// </para>
/// </summary>
[GeneratedComClass]
internal sealed partial class StreamExtractCallback :
    IArchiveExtractCallback, ICryptoGetTextPassword, ICryptoGetTextPassword2, IDisposable
{
    private readonly uint _targetIndex;
    private readonly Stream _destination;
    private readonly string? _password;
    private readonly IProgress<SevenZipProgress>? _progress;
    private readonly CancellationToken _cancellationToken;
    private ComOutStream? _currentAdapter;
    private ulong _total;

    /// <summary>解压过程中 native 最终对目标条目报告的操作结果（<c>NArchive::NExtract::NOperationResult</c>
    /// 枚举值，0 = kOK），调用方可以用它判断密码是否正确/数据是否损坏。</summary>
    public int? OperationResult { get; private set; }

    public StreamExtractCallback(uint targetIndex, Stream destination, string? password,
        IProgress<SevenZipProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        _targetIndex = targetIndex;
        _destination = destination;
        _password = password;
        _progress = progress;
        _cancellationToken = cancellationToken;
    }

    private int AbortIfCancelled() => _cancellationToken.IsCancellationRequested ? HResult.E_ABORT : HResult.S_OK;

    public int SetTotal(ulong total)
    {
        _total = total;
        _progress?.Report(new SevenZipProgress(0, total));
        return AbortIfCancelled();
    }

    public int SetCompleted(nint completeValue)
    {
        ulong completed = NativePtr.ReadUInt64OrDefault(completeValue);
        _progress?.Report(new SevenZipProgress(completed, _total));
        return AbortIfCancelled();
    }

    public int GetStream(uint index, out nint outStream, int askExtractMode)
    {
        outStream = 0;
        ReleaseCurrent();
        if (_cancellationToken.IsCancellationRequested) return HResult.E_ABORT;

        if (askExtractMode != AskMode.kExtract) return HResult.S_OK;
        if (index != _targetIndex) return HResult.S_OK; // 理论上不会发生：indices 数组里只有这一个

        // leaveOpen: true —— _destination 是调用方的流，生命周期由调用方管理，这里绝不能关闭它。
        _currentAdapter = new ComOutStream(_destination, leaveOpen: true);
        outStream = ComFactory.GetCcw<IOutStream>(_currentAdapter);
        return HResult.S_OK;
    }

    public int PrepareOperation(int askExtractMode) => HResult.S_OK;

    public int SetOperationResult(int opRes)
    {
        OperationResult = opRes;
        ReleaseCurrent();
        return HResult.S_OK;
    }

    private void ReleaseCurrent()
    {
        _currentAdapter?.Dispose(); // 只释放 ComOutStream 适配器本身，不影响底层 _destination（leaveOpen: true）
        _currentAdapter = null;
    }

    public int CryptoGetTextPassword(out nint password)
    {
        password = Utf32Marshal.AllocBStr(_password ?? string.Empty);
        return HResult.S_OK;
    }

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

    public void Dispose() => ReleaseCurrent();
}
