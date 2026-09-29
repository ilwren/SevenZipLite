using System.Runtime.InteropServices.Marshalling;
using SevenZipLite.Native;

namespace SevenZipLite.Streams;

/// <summary>
/// 把一个只读、可 Seek 的 .NET Stream 暴露成原生 IInStream 给 7z 引擎读取
/// （用于"打开归档"、"读取待压缩的输入文件"两种场景）。
/// </summary>
[GeneratedComClass]
internal sealed partial class ComInStream : IInStream, IDisposable
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;

    public ComInStream(Stream stream, bool leaveOpen = false)
    {
        _stream = stream;
        _leaveOpen = leaveOpen;
    }

    public unsafe int Read(nint data, uint size, out uint processedSize)
    {
        processedSize = 0;
        try
        {
            if (size == 0) return 0; // S_OK
            var span = new Span<byte>((void*)data, (int)size);
            int n = _stream.Read(span);
            processedSize = (uint)n;
            return 0; // S_OK
        }
        catch
        {
            return unchecked((int)0x80004005); // E_FAIL
        }
    }

    public unsafe int Seek(long offset, uint seekOrigin, nint newPosition)
    {
        try
        {
            if (seekOrigin > 2) return unchecked((int)0x80070057); // E_INVALIDARG
            long pos = _stream.Seek(offset, (SeekOrigin)seekOrigin);
            // newPosition 对应 native 端的 UInt64*，调用方可能传 NULL 表示不关心结果位置
            // （例如 7zOut.cpp 里 Stream->Seek(pos, STREAM_SEEK_SET, NULL)）。
            // 用 out ulong 声明这个参数在 source-gen COM 里无法容忍空指针
            // （native 传 NULL 时，生成的 marshalling stub 会直接返回 E_POINTER 而不会调用到这里），
            // 所以这里改用裸指针，自己判空后再写。
            if (newPosition != 0) *(ulong*)newPosition = (ulong)pos;
            return 0;
        }
        catch
        {
            return unchecked((int)0x80004005);
        }
    }

    public void Dispose()
    {
        if (!_leaveOpen) _stream.Dispose();
    }
}
