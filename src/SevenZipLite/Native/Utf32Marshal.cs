using System.Runtime.InteropServices;
using System.Text;

namespace SevenZipLite.Native;

/// <summary>
/// 关键坑点：7-Zip 源码里 <c>wchar_t</c> / <c>OLECHAR</c> / <c>BSTR</c> 全部依赖平台原生 wchar_t，
/// 而 <c>_WIN32</c> 宏是否被定义会导致完全不同的两套字符串 ABI，本类型必须在运行时区分对待
/// （见下面每个方法里的 <see cref="OperatingSystem.IsWindows"/> 分支）：
///
/// - **非 Windows（Linux / Android，glibc、bionic 均如此）**：wchar_t 是 **4 字节**
///   （事实上的 UTF-32），这不是本项目自选的技术决策，而是 7-Zip 源码
///   <c>CPP/Common/MyWindows.h</c> 里 "typedef wchar_t WCHAR; typedef WCHAR OLECHAR;"
///   与平台 ABI 共同决定的既成事实。这条分支下 7-Zip 的 BSTR 也不是真正的 OLE BSTR，
///   而是它自造的简化版本（见 <c>CPP/Common/MyWindows.cpp</c> 的
///   SysAllocStringLen/SysFreeString）：
///     [4字节：字符串的字节长度 byteLen][UTF-32数据...][4字节 0 结尾]
///                                                       ^
///                                          返回给调用者的指针指向这里
///   释放时使用 free(ptr - 4)。由于 Linux/Android 上一个进程只有一个 libc 堆，.NET 的
///   Marshal.AllocHGlobal/FreeHGlobal 在这两个平台上正是对 malloc/free 的直接包装，
///   所以托管层分配、原生层释放（或反之）是安全的。
///
/// - **Windows（_WIN32 有定义，不管是 MSVC 全量版 7z.dll 还是 MinGW-w64 裁剪版）**：
///   `MyWindows.h` 完全不会定义自己的 wchar_t/BSTR，而是直接用 Windows SDK 真正的
///   wchar_t（2 字节，UTF-16，与 .NET string 原生一致）和真正的 OLE BSTR（用
///   SysAllocStringByteLen/SysFreeString 分配/释放，由 OLEAUT32.dll 管理专属的 OLE
///   任务分配器，**不能**跟 Linux/Android 分支那样直接用 Marshal.AllocHGlobal/
///   FreeHGlobal 或裸 malloc/free 去分配/释放——分配器不匹配一样会导致堆损坏）。
///   这条分支统一委托给 .NET BCL 自带、专门对接真 BSTR/真 UTF-16 wchar_t* 的
///   Marshal.StringToBSTR / FreeBSTR / PtrToStringBSTR / StringToHGlobalUni /
///   PtrToStringUni，不自己造轮子，从而保证分配器、编码、长度前缀格式三者都与
///   Windows 版 7z.dll 的预期完全一致。
///
/// ⚠️ 真机踩坑记录（2026-09）：这个类之前只有非 Windows 分支的实现，类型注释和
/// SevenZipLite.csproj 里都写着"本项目明确不支持 Windows"；但后续需求变化后，
/// 项目已经在 win-x64/win-x86 上跑通了原生库编译和 P/Invoke 互操作骨架，这个
/// "不支持 Windows"的假设早已过时却没有同步更新代码——实际后果是所有经过 VT_BSTR
/// 的字符串（归档内文件路径、密码等）在 Windows 上都会被按错误的编码/错误的分配器
/// 读写，轻则乱码，重则被 native 端识别到长度/编码异常后连锁触发新的内存越界。
/// 现在按上面所述统一在运行时区分两条分支来修复。
/// </summary>
internal static unsafe class Utf32Marshal
{
    // ============ 非 Windows（Linux / Android）分支：UTF-32 wchar_t + 自造 BSTR ============

    /// <summary>把 C# string 编码为 UTF-32 code point 数组（不含结尾 0）。</summary>
    private static uint[] ToUtf32CodePoints(string s)
    {
        // string 本身是 UTF-16，需要正确处理代理对，转换成 UTF-32 code point。
        var list = new List<uint>(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                list.Add((uint)char.ConvertToUtf32(s[i], s[i + 1]));
                i++;
            }
            else
            {
                list.Add(s[i]);
            }
        }
        return list.ToArray();
    }

    private static string FromUtf32CodePoints(uint* p, int count)
    {
        var sb = new StringBuilder(count);
        for (int i = 0; i < count; i++)
        {
            sb.Append(char.ConvertFromUtf32(unchecked((int)(p[i] & 0x7FFFFFFF))));
        }
        return sb.ToString();
    }

    private static nint AllocW_NonWindows(string s)
    {
        uint[] cps = ToUtf32CodePoints(s);
        nint p = Marshal.AllocHGlobal((cps.Length + 1) * 4);
        uint* dst = (uint*)p;
        for (int i = 0; i < cps.Length; i++) dst[i] = cps[i];
        dst[cps.Length] = 0;
        return p;
    }

    private static string PtrToStringW_NonWindows(nint p)
    {
        uint* src = (uint*)p;
        int len = 0;
        while (src[len] != 0) len++;
        return FromUtf32CodePoints(src, len);
    }

    private static nint AllocBStr_NonWindows(string s)
    {
        uint[] cps = ToUtf32CodePoints(s);
        int byteLen = cps.Length * 4;
        nint block = Marshal.AllocHGlobal(4 + byteLen + 4);
        *(uint*)block = (uint)byteLen;
        nint data = block + 4;
        uint* dst = (uint*)data;
        for (int i = 0; i < cps.Length; i++) dst[i] = cps[i];
        dst[cps.Length] = 0;
        return data;
    }

    private static string PtrToStringBStr_NonWindows(nint bstr)
    {
        uint byteLen = *(uint*)(bstr - 4);
        int count = (int)(byteLen / 4);
        return FromUtf32CodePoints((uint*)bstr, count);
    }

    // ============ 对外统一入口：运行时按平台分派 ============

    /// <summary>分配一段以 0 结尾的宽字符串（普通 wchar_t*，不带 BSTR 长度前缀）。
    /// 调用方用 FreeW 释放。Windows 上是真 UTF-16，非 Windows 上是 7-Zip 自造的 UTF-32。</summary>
    public static nint AllocW(string? s)
    {
        if (s is null) return 0;
        return OperatingSystem.IsWindows()
            ? Marshal.StringToHGlobalUni(s) // 真正的 Windows wchar_t*（UTF-16），底层就是 AllocHGlobal
            : AllocW_NonWindows(s);
    }

    public static void FreeW(nint p)
    {
        // StringToHGlobalUni 内部也是用 AllocHGlobal 分配的，两个平台分支统一用 FreeHGlobal 释放即可。
        if (p != 0) Marshal.FreeHGlobal(p);
    }

    /// <summary>从 0 结尾的宽字符串指针读取为 C# string。</summary>
    public static string? PtrToStringW(nint p)
    {
        if (p == 0) return null;
        return OperatingSystem.IsWindows()
            ? Marshal.PtrToStringUni(p)
            : PtrToStringW_NonWindows(p);
    }

    /// <summary>分配一个 BSTR。Windows 上是真正的 OLE BSTR（SysAllocStringByteLen，
    /// UTF-16 数据），非 Windows 上是 7-Zip 自造的简化版本
    /// （[4字节byteLen][UTF-32数据][4字节0]，返回值指向数据区）。
    /// 调用方负责最终 Clear()/FreeBStr()。</summary>
    public static nint AllocBStr(string? s)
    {
        if (s is null) return 0;
        return OperatingSystem.IsWindows()
            ? Marshal.StringToBSTR(s) // 真 BSTR，由 OLEAUT32 的 SysAllocStringByteLen 分配
            : AllocBStr_NonWindows(s);
    }

    /// <summary>释放由 AllocBStr（或 native 端 SysAllocStringLen/SysAllocStringByteLen）
    /// 分配的 BSTR。两个平台的分配器不同，释放方式必须对应匹配，不能混用。</summary>
    public static void FreeBStr(nint bstr)
    {
        if (bstr == 0) return;
        if (OperatingSystem.IsWindows())
        {
            Marshal.FreeBSTR(bstr); // 对应 SysFreeString，走 OLE 任务分配器，不能用 FreeHGlobal 代替
        }
        else
        {
            Marshal.FreeHGlobal(bstr - 4);
        }
    }

    /// <summary>读取一个 BSTR 为 C# string（自动识别真 BSTR / 7-Zip 自造 BSTR）。</summary>
    public static string? PtrToStringBStr(nint bstr)
    {
        if (bstr == 0) return null;
        return OperatingSystem.IsWindows()
            ? Marshal.PtrToStringBSTR(bstr)
            : PtrToStringBStr_NonWindows(bstr);
    }
}
