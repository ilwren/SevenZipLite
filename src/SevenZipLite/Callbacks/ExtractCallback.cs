using System.Runtime.InteropServices.Marshalling;
using SevenZipLite.Native;
using SevenZipLite.Streams;

namespace SevenZipLite.Callbacks;

/// <summary>
/// 解压回调：实现 IArchiveExtractCallback（含基接口 IProgress）用于接收解压过程，
/// 同时实现 ICryptoGetTextPassword / ICryptoGetTextPassword2 用于在加密归档需要密码时
/// 向 native 侧提供密码（7z 用前者，zip 用后者，具体由 native 端按格式自行 QueryInterface 决定）。
///
/// 注意：本类用"单个共享字段 + GetStream/SetOperationResult 成对及时 Dispose"的写法，
/// 这要求 native 端对这个回调的调用是**严格单线程顺序**的（GetStream(i) -> 完整写完 ->
/// SetOperationResult -> GetStream(i+1) -> ...），不能有任何交叉/并发。解压路径目前
/// 确认满足这个前提（7-Zip 的 Extract 在我们实际用到的场景下是单线程顺序处理每个条目
/// 的，逐条 GetStream/SetOperationResult 严格配对）。**不要**照抄
/// <see cref="UpdateCallback"/> 那种"不主动 Dispose，靠 COM 引用计数"的写法搬到这里——
/// 已经实测验证过 .NET 的 ComWrappers 在 native Release() 引用计数到 0 时**不会**自动调用
/// IDisposable.Dispose()，如果这里不在 SetOperationResult 里及时关闭上一个输出文件，
/// 写入用的 FileStream（FileShare.None 独占写）会一直不关闭，导致下一次尝试打开/读取
/// 同一个文件时报"the process cannot access the file because it is being used by another
/// process"——这是真实踩过的坑，不是假设。
/// </summary>
[GeneratedComClass]
internal sealed partial class ExtractCallback : IArchiveExtractCallback, ICryptoGetTextPassword, ICryptoGetTextPassword2, IDisposable
{
    private readonly IReadOnlyDictionary<uint, ExtractItemInfo> _items;
    private readonly string _outputDir;
    private readonly string? _password;
    private nint _currentOutStreamCcw;
    private ComOutStream? _currentAdapter;

    public List<(uint Index, int OpResult)> Results { get; } = new();

    public ExtractCallback(IReadOnlyDictionary<uint, ExtractItemInfo> items, string outputDir, string? password)
    {
        _items = items;
        _outputDir = outputDir;
        _password = password;
    }

    public int SetTotal(ulong total) => HResult.S_OK;
    public int SetCompleted(nint completeValue) => HResult.S_OK;

    public int GetStream(uint index, out nint outStream, int askExtractMode)
    {
        outStream = 0;
        ReleaseCurrent();

        if (askExtractMode != AskMode.kExtract) return HResult.S_OK;
        if (!_items.TryGetValue(index, out var info) || info.IsDir) return HResult.S_OK;

        string fullPath = System.IO.Path.Combine(_outputDir, info.Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
        string? dir = System.IO.Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        _currentAdapter = new ComOutStream(fs);
        _currentOutStreamCcw = ComFactory.GetCcw<IOutStream>(_currentAdapter);
        outStream = _currentOutStreamCcw;
        return HResult.S_OK;
    }

    public int PrepareOperation(int askExtractMode) => HResult.S_OK;

    public int SetOperationResult(int opRes)
    {
        Results.Add((0, opRes)); // 具体 index 由 GetStream 调用顺序推断，这里只记录结果本身用于验证
        ReleaseCurrent();
        return HResult.S_OK;
    }

    private void ReleaseCurrent()
    {
        _currentAdapter?.Dispose();
        _currentAdapter = null;
        _currentOutStreamCcw = 0;
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
