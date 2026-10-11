using System.Runtime.InteropServices.Marshalling;
using SevenZipLite.Native;

namespace SevenZipLite.Callbacks;

/// <summary>
/// 打开归档用的回调：不汇报进度，直接放行；同时实现 <see cref="ICryptoGetTextPassword"/>，
/// 用于 7z「加密文件名」（header encryption，-mhe=on）归档——这类归档在
/// <c>IInArchive.Open</c> 阶段就需要密码才能解出头部结构（文件名/大小等），而不是等到
/// <c>IInArchive.Extract</c> 才要密码。
///
/// 如果不实现这个接口，native 端在 Open 时发现需要密码但拿不到，会把当前归档当成
/// "无法识别的格式"处理并返回 S_FALSE（7-Zip 源码 7zIn.cpp 注释："S_FALSE means that
/// file is not archive"）——这是一个正常的、非异常性质的 COM 成功码，但上层
/// <c>Check()</c> 用 <c>hr != S_OK</c> 判断失败，于是会被包装成一条很迷惑的
/// "IInArchive.Open 失败, HRESULT=0x00000001" 异常抛出来，看起来像是密码本身有问题，
/// 实际上和密码内容（是否含特殊字符）完全无关——哪怕密码是纯 ASCII，只要没实现这个接口，
/// 加密文件名的 7z 归档一律在 Open 这一步就报同样的错。已实测复现并验证此修复有效。
/// </summary>
[GeneratedComClass]
internal sealed partial class ArchiveOpenCallback : IArchiveOpenCallback, ICryptoGetTextPassword
{
    private readonly string? _password;
    private readonly CancellationToken _cancellationToken;

    public ArchiveOpenCallback(string? password = null, CancellationToken cancellationToken = default)
    {
        _password = password;
        _cancellationToken = cancellationToken;
    }

    // Open 阶段通常很快（只读头部），这里支持取消主要是为了配合分卷归档——
    // DiscoverVolumes 探测到的卷可能很多、卷本身也可能很大，Open 阶段仍然需要顺序读完
    // 它们的头部信息。取消粒度和 Update/ExtractCallback 一致，见那两个类型上的说明。
    // Open is usually fast (header-only read); supporting cancellation here mainly
    // matters for split-volume archives — JoinedVolumeStream may span many/large
    // volumes and Open still needs to sequentially read through their headers.
    // Cancellation granularity matches Update/ExtractCallback, see their docs.
    private int AbortIfCancelled() => _cancellationToken.IsCancellationRequested ? HResult.E_ABORT : HResult.S_OK;

    public int SetTotal(nint files, nint bytes) => AbortIfCancelled();
    public int SetCompleted(nint files, nint bytes) => AbortIfCancelled();

    public int CryptoGetTextPassword(out nint password)
    {
        password = Utf32Marshal.AllocBStr(_password ?? string.Empty);
        return HResult.S_OK;
    }
}
