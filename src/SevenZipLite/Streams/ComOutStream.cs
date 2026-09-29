using System.Runtime.InteropServices.Marshalling;
using SevenZipLite.Native;

namespace SevenZipLite.Streams;

/// <summary>
/// 把一个可写 .NET Stream 暴露成原生 IOutStream，供 7z 引擎写入
/// （用于"解压输出文件"、"生成归档文件"两种场景）。
/// </summary>
[GeneratedComClass]
internal sealed partial class ComOutStream : IOutStream, IDisposable
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;

    public ComOutStream(Stream stream, bool leaveOpen = false)
    {
        _stream = stream;
        _leaveOpen = leaveOpen;
    }

    public unsafe int Write(nint data, uint size, out uint processedSize)
    {
        processedSize = 0;
        try
        {
            if (size == 0) return 0;
            var span = new ReadOnlySpan<byte>((void*)data, (int)size);
            _stream.Write(span);
            processedSize = size;
            return 0;
        }
        catch
        {
            return unchecked((int)0x80004005);
        }
    }

    public unsafe int Seek(long offset, uint seekOrigin, nint newPosition)
    {
        try
        {
            if (seekOrigin > 2) return unchecked((int)0x80070057);
            long pos = _stream.Seek(offset, (SeekOrigin)seekOrigin);
            // 见 ComInStream.Seek 上的注释：newPosition 对应 UInt64*，native 可能传 NULL。
            if (newPosition != 0) *(ulong*)newPosition = (ulong)pos;
            return 0;
        }
        catch
        {
            return unchecked((int)0x80004005);
        }
    }

    public int SetSize(ulong newSize)
    {
        try
        {
            _stream.SetLength((long)newSize);
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
