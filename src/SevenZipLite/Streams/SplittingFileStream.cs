namespace SevenZipLite.Streams;

/// <summary>
/// 按用户报告中约定的方案："分卷"功能完全在 C# 层实现，不依赖 7-Zip 原生的
/// IArchiveUpdateCallback2/IArchiveOpenVolumeCallback 多卷机制（那套机制更复杂，
/// 且要求 native 侧知道卷大小策略）。做法：把 7z/zip/tar 引擎写出的“一整条”
/// 字节流，在 C# 层按固定大小切成 archive.ext.001 / .002 / .003 ... 多个物理文件。
///
/// 之所以要支持“任意位置回写”（Seek 到已写过的区域再 Write），是因为 7z 格式在
/// 归档创建完成后，需要回到文件开头回写 32 字节的签名头(签名头里记录了紧随其后的
/// header 的偏移和大小，这些值只有全部数据写完后才知道)。Zip 格式在支持 Seek 的
/// 输出流上，也会在每个文件写完后回填本地文件头的 CRC/大小字段。
/// 因此本类实现了完整的 Stream.Seek + 跨卷边界读写，而不是只能顺序追加写。
/// </summary>
internal sealed class SplittingFileStream : Stream
{
    private readonly string _archivePath;
    private readonly long _volumeSize; // <= 0 表示不分卷
    private readonly List<string> _createdVolumes = new();

    private FileStream? _current;
    private int _currentVolumeIndex = -1;
    private long _position;
    private long _length;

    public SplittingFileStream(string archivePath, long volumeSize)
    {
        _archivePath = archivePath;
        _volumeSize = volumeSize;
    }

    /// <summary>本次写入过程中实际创建出的卷文件路径列表（按顺序）。</summary>
    public IReadOnlyList<string> CreatedVolumes => _createdVolumes;

    private string VolumePath(int index)
    {
        if (_volumeSize <= 0) return _archivePath;
        return $"{_archivePath}.{index + 1:D3}";
    }

    private void EnsureVolume(int index)
    {
        if (_currentVolumeIndex == index && _current is not null) return;

        _current?.Flush();
        _current?.Dispose();

        string path = VolumePath(index);
        _current = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        _currentVolumeIndex = index;
        if (!_createdVolumes.Contains(path)) _createdVolumes.Add(path);
    }

    public override bool CanRead => false;
    public override bool CanSeek => true;
    public override bool CanWrite => true;
    public override long Length => _length;

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
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        if (newPos < 0) throw new IOException("negative seek");
        _position = newPos;
        return _position;
    }

    public override void SetLength(long value) => _length = Math.Max(_length, value);

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (_volumeSize <= 0)
        {
            EnsureVolume(0);
            _current!.Position = _position;
            _current.Write(buffer, offset, count);
            _position += count;
            _length = Math.Max(_length, _position);
            return;
        }

        int remaining = count;
        int bufOffset = offset;
        while (remaining > 0)
        {
            int volIndex = (int)(_position / _volumeSize);
            long offsetInVolume = _position % _volumeSize;
            long spaceInVolume = _volumeSize - offsetInVolume;
            int toWrite = (int)Math.Min(remaining, spaceInVolume);

            EnsureVolume(volIndex);
            _current!.Position = offsetInVolume;
            _current.Write(buffer, bufOffset, toWrite);

            _position += toWrite;
            bufOffset += toWrite;
            remaining -= toWrite;
            _length = Math.Max(_length, _position);
        }
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("write-only");

    public override void Flush() => _current?.Flush();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _current?.Flush();
            _current?.Dispose();
        }
        base.Dispose(disposing);
    }
}
