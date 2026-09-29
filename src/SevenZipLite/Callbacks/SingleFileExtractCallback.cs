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
    private ComOutStream? _currentAdapter;

    public SingleFileExtractCallback(string outputPath)
    {
        _outputPath = outputPath;
    }

    public int SetTotal(ulong total) => HResult.S_OK;
    public int SetCompleted(nint completeValue) => HResult.S_OK;

    public int GetStream(uint index, out nint outStream, int askExtractMode)
    {
        outStream = 0;
        _currentAdapter?.Dispose();
        _currentAdapter = null;

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
