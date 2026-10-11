using System.Runtime.InteropServices;

namespace SevenZipLite.Native;

/// <summary>
/// 一些 7-Zip COM 接口方法把数值参数声明成 <c>const UInt64*</c>（而不是按值传递的
/// <c>UInt64</c>），用指针是否为 NULL 来表达"这个值现在不可用/未知"（比如
/// <c>IProgress::SetCompleted(const UInt64 *completeValue)</c>、
/// <c>IArchiveOpenCallback::SetTotal/SetCompleted(const UInt64 *files, const UInt64 *bytes)</c>）。
/// 这些参数在托管互操作层上按约定声明为 <c>nint</c>（裸指针值），调用方必须先判断是否为 0
/// （NULL），再用这里的辅助方法把指针指向的 8 字节数据读出来，<b>绝不能把指针地址本身当成
/// 数值使用</b>——这是本项目踩过的一个真实 bug：早期把 <c>nint completeValue</c> 直接
/// <c>(ulong)</c> 强转当成"已完成字节数"汇报给进度回调，实际汇报出去的是该 UInt64 在
/// native 侧栈/堆上的内存地址，数值远大于总字节数，导致"进度超过 100%"的假象。
/// </summary>
internal static class NativePtr
{
    /// <summary>
    /// 读取一个可能为 NULL 的 <c>const UInt64*</c>；指针为 0 时返回 <paramref name="fallback"/>
    /// （native 用 NULL 表示"这个统计量现在不可用"，不是"值为 0"）。
    /// </summary>
    public static ulong ReadUInt64OrDefault(nint ptr, ulong fallback = 0)
        => ptr == 0 ? fallback : unchecked((ulong)Marshal.ReadInt64(ptr));
}
