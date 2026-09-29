using System.Runtime.InteropServices;

namespace SevenZipLite.Native;

/// <summary>
/// 对应 7-Zip 源码里的 <c>tagPROPVARIANT</c>。
///
/// ⚠️ 真机崩溃修复记录（2026-09）：这个结构体的 <c>Size</c> 之前被写死成 16，注释里
/// 曾经错误地断言"无论 32/64 位、无论 ARM/x86，只要不是 _WIN32 编译，这个布局都是稳定的
/// 16 字节"——这句话本身没错，但遗漏了一个致命前提：**这个 16 字节的精简版 tagPROPVARIANT
/// 只在 `CPP/Common/MyWindows.h` 的 `#else // _WIN32` 分支里生效（即 Linux/Android）**。
/// 在真正的 Windows 上（不管是 MSVC 官方全量版 7z.dll，还是 MinGW-w64 编译的裁剪版
/// 7z.dll，只要是 Windows 目标，`_WIN32` 宏都会被定义），`MyWindows.h` 完全不会定义任何
/// 自己的 PROPVARIANT，而是直接使用 Windows SDK 自带、真正的 <c>PROPVARIANT</c>
/// （来自 oaidl.h/propidl.h）——这个真实结构体在 **32 位下是 16 字节，但 64 位下是
/// 24 字节**（8 字节头部 + 联合体里含指针成员在 x64 下需要 8 字节对齐，导致整体多出
/// 8 字节尾部 padding）。
///
/// 后果：在 win-x64 上，托管这边只分配了 16 字节的缓冲区传给 native 的
/// <c>IInArchive::GetProperty</c>，但 native 端（真正的 Windows PROPVARIANT）按 24
/// 字节的结构体在清空/写入（常见的 <c>memset(pvar, 0, sizeof(PROPVARIANT))</c> 或
/// 逐字段清空逻辑），多写出去的 8 个字节踩到了托管堆里下一个对象的内存，被 Windows
/// 堆管理器检测到并以 <c>STATUS_HEAP_CORRUPTION (0xC0000374)</c> 直接让进程崩溃——
/// 这正是 SevenZipLite.WpfDemo 在 win-x64 真机上一启动/一调用就崩溃、且 VS 调试器都
/// 来不及附加的根本原因。
///
/// 修复方式：把 <c>Size</c> 从 16 改成 **24**（按"across 全部目标平台的最大可能值"
/// 统一取上限），不改动任何 FieldOffset。这样做在所有平台下都是安全的：
/// - Linux/Android（native 端实际只有 16 字节）：native 端最多只会读写前 16 字节，
///   我们多分配出来的 8 字节尾部 padding 始终不会被 native 触碰，纯粹闲置，无副作用。
/// - Windows x86（native 端实际也是 16 字节）：同上，安全。
/// - Windows x64（native 端实际是 24 字节）：分配大小与 native 端完全一致，不会再有
///   越界写入。
/// 之所以能这样"一刀切用最大值"而不需要按平台/位数分别定义不同大小的结构体，是因为
/// vt（偏移 0）和实际数据联合体（偏移 8）在 x86/x64 下的**起始偏移完全相同**——x64 相比
/// x86 多出来的 8 字节纯粹是结构体末尾的对齐 padding，不影响任何已使用字段的偏移量。
/// 本项目目前对 PropVariant 的全部用法都是"单个 out 参数"，不涉及 PropVariant 数组的
/// 连续内存步长（stride）问题，所以"扩大到共同上限"这个简单修法不遗漏边界情况。
///
/// ⚠️ 但这个"扩大到 24 字节"的修法只对**一个方向**绝对安全，必须区分对待：
/// - **方向 A：我们调用 native**（<c>IInArchive.GetProperty</c> /
///   <c>IInArchive.GetArchiveProperty</c>，仍然用 <c>out PropVariant value</c>）——
///   这个方向下，缓冲区是**我们自己分配**的（source generator 在调用点声明一个按我们
///   结构体大小分配的本地变量，取地址传给 native），我们分配多大都行，native 只会往里
///   写它自己认为需要的字节数（≤24），肯定不会超出我们给的空间，永远安全。
/// - **方向 B：native 调用我们**（<c>IArchiveUpdateCallback.GetProperty</c>，即压缩时
///   native 反过来问我们"这个条目的属性是什么"）——这个方向下，缓冲区是 **native 自己
///   在它的栈上分配**的，大小按 native 自己编译时的真实 PROPVARIANT 决定：win-x64 是
///   24 字节没问题，但 **win-x86 只有 16 字节**！如果这个方向也偷懒用
///   <c>out PropVariant value</c>（source generator 对可 blittable 的 out 结构体参数，
///   生成的反向桩代码是直接把 native 给的指针当成 <c>PropVariant*</c> 用，我们方法体里
///   对 <c>value</c> 的赋值会整个结构体（24 字节）写进去），就会在 win-x86 上把 native
///   栈上那个只有 16 字节的局部变量后面 8 个字节踩掉，导致*新的*（这次是 x86 特有的）
///   栈/堆损坏——所以 <c>IArchiveUpdateCallback.GetProperty</c> 改成了手动指针参数
///   （<c>nint value</c>），配合下面的 <see cref="WriteToNative"/> **只写前 16 字节**，
///   不管 native 实际分配了 16 还是 24 字节都不会越界（7-Zip 用到的所有 VARTYPE，数据
///   都落在偏移 0-15 以内，16 字节完全够用，偏移 16-23 纯粹是 x64 的尾部 padding，
///   从来不会被读取，不写它不影响任何功能）。
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PropVariant
{
    [FieldOffset(0)] public ushort vt; // VARTYPE
    [FieldOffset(2)] public ushort wReserved1;
    [FieldOffset(4)] public ushort wReserved2;
    [FieldOffset(6)] public ushort wReserved3;

    [FieldOffset(8)] public sbyte cVal;
    [FieldOffset(8)] public byte bVal;
    [FieldOffset(8)] public short iVal;
    [FieldOffset(8)] public ushort uiVal;
    [FieldOffset(8)] public int lVal;
    [FieldOffset(8)] public uint ulVal;
    [FieldOffset(8)] public long hVal;   // LARGE_INTEGER.QuadPart (VT_I8)
    [FieldOffset(8)] public ulong uhVal; // ULARGE_INTEGER.QuadPart (VT_UI8)
    [FieldOffset(8)] public short boolVal; // VARIANT_BOOL: -1=true, 0=false
    [FieldOffset(8)] public uint filetimeLow;
    [FieldOffset(12)] public uint filetimeHigh;
    [FieldOffset(8)] public nint bstrVal; // BSTR (Windows 上是真 UTF-16 BSTR，非 Windows 是 7-Zip 自造 UTF-32 版本，见 Utf32Marshal)

    public const ushort VT_EMPTY = 0;
    public const ushort VT_I2 = 2;
    public const ushort VT_I4 = 3;
    public const ushort VT_BSTR = 8;
    public const ushort VT_BOOL = 11;
    public const ushort VT_UI1 = 17;
    public const ushort VT_UI2 = 18;
    public const ushort VT_UI4 = 19;
    public const ushort VT_I8 = 20;
    public const ushort VT_UI8 = 21;
    public const ushort VT_FILETIME = 64;

    /// <summary>释放 PROPVARIANT 内部持有的资源（目前只有 VT_BSTR 需要释放）。
    /// 对应 native 端的 VariantClear。</summary>
    public void Clear()
    {
        if (vt == VT_BSTR && bstrVal != 0)
        {
            Utf32Marshal.FreeBStr(bstrVal);
        }
        vt = VT_EMPTY;
        bstrVal = 0;
    }

    /// <summary>把 PROPVARIANT 转换成托管对象，便于上层代码使用。</summary>
    public readonly object? ToManaged()
    {
        switch (vt)
        {
            case VT_EMPTY: return null;
            case VT_BOOL: return boolVal != 0;
            case VT_UI1: return bVal;
            case VT_I2: return iVal;
            case VT_UI2: return uiVal;
            case VT_I4: return lVal;
            case VT_UI4: return ulVal;
            case VT_I8: return hVal;
            case VT_UI8: return uhVal;
            case VT_BSTR: return bstrVal == 0 ? null : Utf32Marshal.PtrToStringBStr(bstrVal);
            case VT_FILETIME:
                {
                    long ft = ((long)filetimeHigh << 32) | filetimeLow;
                    // Windows FILETIME: 100ns 间隔数，起点 1601-01-01
                    try { return DateTime.FromFileTimeUtc(ft); }
                    catch { return null; }
                }
            default: return null;
        }
    }

    public static PropVariant FromUInt32(uint v) => new() { vt = VT_UI4, ulVal = v };
    public static PropVariant FromUInt64(ulong v) => new() { vt = VT_UI8, uhVal = v };
    public static PropVariant FromBool(bool v) => new() { vt = VT_BOOL, boolVal = (short)(v ? -1 : 0) };

    /// <summary>创建一个持有新分配 BSTR 的 PROPVARIANT（调用方负责最终 Clear()）。</summary>
    public static PropVariant FromString(string s) => new() { vt = VT_BSTR, bstrVal = Utf32Marshal.AllocBStr(s) };

    /// <summary>
    /// 把一个 PropVariant 的值安全地写入 native 提供的原始指针（用于"native 调用我们"的
    /// 反向回调场景，例如 <c>IArchiveUpdateCallback.GetProperty</c>）。只写固定 16 字节
    /// （vt + 3 个保留字共 8 字节头部 + 8 字节数据联合体），绝不写这个类型自身
    /// <c>sizeof</c> 出来的 24 字节——因为 native 在 win-x86 上给我们的缓冲区可能只有
    /// 16 字节，写多了会越界踩坏 native 自己的栈。详见类型注释里"方向 A / 方向 B"的说明。
    /// </summary>
    public static unsafe void WriteToNative(nint dest, in PropVariant value)
    {
        if (dest == 0) return;
        fixed (PropVariant* src = &value)
        {
            Buffer.MemoryCopy(src, (void*)dest, 16, 16);
        }
    }
}
