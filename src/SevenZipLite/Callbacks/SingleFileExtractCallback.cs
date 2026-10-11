using System.Runtime.InteropServices.Marshalling;
using SevenZipLite.Native;
using SevenZipLite.Streams;

namespace SevenZipLite.Callbacks;

/// <summary>
/// 专用于 gzip 容器（永远只有 1 个内层流）的解压回调：不关心条目的名字/属性，
/// 直接把唯一的数据流写到指定的目标文件路径。用于 tar.gz 的第一阶段解包。
/// </summary>
[GeneratedComClass]
internal sealed partial class SingleFileExtractCallback : IArchiveExtractCallback, IDisposable
{
    private readonly string _outputPath;
    private readonly IProgress<SevenZipProgress>? _progress;
    private readonly CancellationToken _cancellationToken;
    private ComOutStream? _currentAdapter;
    private ulong _total;

    public SingleFileExtractCallback(string outputPath, IProgress<SevenZipProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _outputPath = outputPath;
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
        // completeValue 是 const UInt64*，必须解引用，不能直接把指针地址当数值用，详见
        // NativePtr 类型注释。
        ulong completed = NativePtr.ReadUInt64OrDefault(completeValue);
        _progress?.Report(new SevenZipProgress(completed, _total));
        return AbortIfCancelled();
    }

    public int GetStream(uint index, out nint outStream, int askExtractMode)
    {
        outStream = 0;
        _currentAdapter?.Dispose();
        _currentAdapter = null;
        if (_cancellationToken.IsCancellationRequested) return HResult.E_ABORT;

        if (askExtractMode != AskMode.kExtract) return HResult.S_OK;

        var fs = new FileStream(_outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        _currentAdapter = new ComOutStream(fs);
        outStream = ComFactory.GetCcw<IOutStream>(_currentAdapter);
        return HResult.S_OK;
    }

    public int PrepareOperation(int askExtractMode) => HResult.S_OK;

    public int SetOperationResult(int opRes)
    {
        _currentAdapter?.Dispose();
        _currentAdapter = null;
        return HResult.S_OK;
    }

    public void Dispose()
    {
        _currentAdapter?.Dispose();
        _currentAdapter = null;
    }
}
