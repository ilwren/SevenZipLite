namespace SevenZipLite.Streams;

/// <summary>
/// SplittingFileStream 的逆操作：把若干个卷文件（archive.ext.001 / .002 / ...）
/// 拼接成一个逻辑上连续、可随机 Seek 的只读流，供 7z 引擎当作单一输入流打开/解压。
/// </summary>
internal sealed class JoinedVolumeStream : Stream
{
    private readonly List<string> _volumes;
    private readonly long[] _volumeStartOffsets;
    private readonly long _totalLength;

    private FileStream? _current;
    private int _currentIndex = -1;
    private long _position;

    public JoinedVolumeStream(IReadOnlyList<string> volumePaths)
    {
        if (volumePaths.Count == 0) throw new ArgumentException("no volumes");
        _volumes = volumePaths.ToList();
        _volumeStartOffsets = new long[_volumes.Count];
        long acc = 0;
        for (int i = 0; i < _volumes.Count; i++)
        {
            _volumeStartOffsets[i] = acc;
            acc += new FileInfo(_volumes[i]).Length;
        }
        _totalLength = acc;
    }

    /// <summary>按“基础路径.001 / .002 / ...”约定自动发现同目录下的所有卷文件。
    /// 如果连 ".001" 都不存在，则把 basePathOrFirstVolume 当作单个非分卷文件处理。</summary>
    public static List<string> DiscoverVolumes(string basePathOrFirstVolume)
    {
        string basePath = basePathOrFirstVolume;
        // 允许调用者直接传入 "xxx.7z.001" 或 "xxx.7z"（基础名）两种写法。
        if (basePath.Length > 4 && basePath[^4] == '.' && basePath[^3..].All(char.IsDigit))
        {
            basePath = basePath[..^4];
        }

        var result = new List<string>();
        if (File.Exists($"{basePath}.001"))
        {
            int i = 1;
            while (File.Exists($"{basePath}.{i:D3}"))
            {
                result.Add($"{basePath}.{i:D3}");
                i++;
            }
        }
        else if (File.Exists(basePath))
        {
            result.Add(basePath);
        }
        else
        {
            throw new FileNotFoundException("archive (or its first volume) not found", basePathOrFirstVolume);
        }
        return result;
    }

    private void EnsureVolume(int index)
    {
        if (_currentIndex == index && _current is not null) return;
        _current?.Dispose();
        _current = new FileStream(_volumes[index], FileMode.Open, FileAccess.Read, FileShare.Read);
        _currentIndex = index;
    }

    private int LocateVolume(long pos)
    {
        for (int i = _volumeStartOffsets.Length - 1; i >= 0; i--)
        {
            if (pos >= _volumeStartOffsets[i]) return i;
        }
        return 0;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _totalLength;

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        long newPos = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _totalLength + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        if (newPos < 0) throw new IOException("negative seek");
        _position = newPos;
        return _position;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= _totalLength) return 0;

        int volIndex = LocateVolume(_position);
        EnsureVolume(volIndex);

        long offsetInVolume = _position - _volumeStartOffsets[volIndex];
        _current!.Position = offsetInVolume;

        long spaceInVolume = _current.Length - offsetInVolume;
        int toRead = (int)Math.Min(count, Math.Min(spaceInVolume, _totalLength - _position));
        if (toRead <= 0) return 0;

        int n = _current.Read(buffer, offset, toRead);
        _position += n;
        return n;
    }

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("read-only");

    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Flush() { }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _current?.Dispose();
        base.Dispose(disposing);
    }
}
