using System.Runtime.InteropServices;

namespace SevenZipLite.Native;

/// <summary>
/// 对应裁剪后的原生库 7zlite.so / lib7zlite.so 导出的 C 接口
/// (CPP/7zip/Archive/ArchiveExports.cpp 中的 CreateObject 导出函数)。
/// 库名固定写成 "7zlite"：.NET 在 Unix 系（含 Android）上解析 DllImport 名称时，
/// 会自动尝试 "lib7zlite.so"，因此本机 Linux 与 Android 上无需区分库名。
/// </summary>
internal static partial class NativeMethods
{
    private const string LibName = "7zlite";

    [LibraryImport(LibName, EntryPoint = "CreateObject")]
    public static partial int CreateObject(in Guid clsid, in Guid iid, out nint outObject);
}
