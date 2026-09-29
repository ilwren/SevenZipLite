using System.Runtime.InteropServices.Marshalling;
using SevenZipLite.Native;

namespace SevenZipLite.Callbacks;

/// <summary>最简单的打开回调：不汇报进度，直接放行。</summary>
[GeneratedComClass]
internal sealed partial class ArchiveOpenCallback : IArchiveOpenCallback
{
    public int SetTotal(nint files, nint bytes) => HResult.S_OK;
    public int SetCompleted(nint files, nint bytes) => HResult.S_OK;
}
