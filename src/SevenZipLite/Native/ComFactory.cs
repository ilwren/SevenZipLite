using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace SevenZipLite.Native;

/// <summary>
/// 封装 .NET 8+ 内置的 StrategyBasedComWrappers，用于：
///   - RCW 方向：把 native CreateObject() 返回的裸指针包装成强类型的
///     [GeneratedComInterface] 接口（例如 IInArchive / IOutArchive）。
///   - CCW 方向：把我们自己实现的回调 / 流对象（标了 [GeneratedComClass]）
///     包装成可以传给 native 的裸接口指针。
/// </summary>
internal static class ComFactory
{
    private static readonly StrategyBasedComWrappers Wrappers = new();

    public static T GetRcw<T>(nint unknownPtr) where T : class
    {
        object obj = Wrappers.GetOrCreateObjectForComInstance(
            unknownPtr, CreateObjectFlags.None);
        return (T)obj;
    }

    /// <summary>
    /// 把托管对象包装成指向 <typeparamref name="T"/> 这个具体接口 vtable 的裸指针。
    /// <para>
    /// 关键点：当一个类同时实现多个 COM 接口（例如 UpdateCallback 同时实现
    /// IArchiveUpdateCallback 和 ICryptoGetTextPassword2）时，
    /// <c>GetOrCreateComInterfaceForObject</c> 返回的"默认"指针具体对应哪个接口的 vtable
    /// 是未文档化、不可依赖的实现细节——它可能不是调用方期望的那个接口。
    /// 如果把这个裸指针直接当作错误的接口类型传给 native 代码，native 会按照
    /// "期望接口"的 vtable 布局去调用函数指针，而实际内存里放的是"另一个接口"的
    /// （槽位数量、签名都不同的）vtable，导致读到越界/错位的函数指针——表现为
    /// 调用"成功"返回 S_OK 但参数是垃圾值，且托管方法根本没被执行。
    /// 因此这里必须再做一次显式 QueryInterface，用 T 的真实 IID 换取"保证正确"的接口指针，
    /// 而不能依赖 GetOrCreateComInterfaceForObject 返回值恰好是我们想要的接口。
    /// </para>
    /// </summary>
    public static nint GetCcw<T>(object managedObject) where T : class
    {
        nint unknown = Wrappers.GetOrCreateComInterfaceForObject(
            managedObject, CreateComInterfaceFlags.None);
        try
        {
            Guid iid = typeof(T).GUID;
            int hr = Marshal.QueryInterface(unknown, in iid, out nint result);
            if (hr != 0 || result == 0)
                throw new InvalidOperationException(
                    $"QueryInterface({typeof(T).Name}) 失败, HRESULT=0x{hr:X8}");
            return result;
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }
}
