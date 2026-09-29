using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace SevenZipLite.Native;

/*
 * 下面这些 partial interface 使用 .NET 8+ 的 Source-Generated COM
 * ([GeneratedComInterface]) 重新声明了 7-Zip 原生 COM ABI 的一部分子集。
 *
 * 关键正确性前提（务必保持）：
 *   1) 每个接口的方法声明顺序必须与 7-Zip 源码头文件中的宏展开顺序完全一致，
 *      因为这决定了虚表(vtable)槽位顺序 —— 源码依据见文件头注释里指向的具体 .h 行号。
 *   2) 如果一个 C++ 接口继承自另一个接口（如 IInStream : ISequentialInStream，
 *      IArchiveExtractCallback : IProgress），C# 也用 interface 继承表达，
 *      source generator 会按"先基类槽位、后派生类槽位"的顺序生成 vtable，与 C++ 单继承虚表布局一致。
 *   3) 允许省略某个接口"尾部"我们用不到的方法（因为它们在 vtable 末尾，不影响我们
 *      要调用的前面槽位的正确性），但绝不能跳过中间的方法，也不能省略基接口的方法。
 *      每处省略都在下面用注释标出对应关系。
 *   4) 所有方法都标注 [PreserveSig]，方法签名里显式返回 HRESULT(int)，
 *      不使用 source-gen 默认的"HRESULT 自动转异常"模式 —— 因为 7-Zip 很多"失败"码
 *      （如 S_FALSE）是业务语义而非错误，必须由调用方显式处理。
 */

// ============ IStream.h : groupId = 3 ============

[GeneratedComInterface]
[Guid("23170F69-40C1-278A-0000-000300010000")]
internal partial interface ISequentialInStream
{
    [PreserveSig]
    int Read(nint data, uint size, out uint processedSize);
}

[GeneratedComInterface]
[Guid("23170F69-40C1-278A-0000-000300020000")]
internal partial interface ISequentialOutStream
{
    [PreserveSig]
    int Write(nint data, uint size, out uint processedSize);
}

[GeneratedComInterface]
[Guid("23170F69-40C1-278A-0000-000300030000")]
internal partial interface IInStream : ISequentialInStream
{
    [PreserveSig]
    int Seek(long offset, uint seekOrigin, nint newPosition); // UInt64* ，可为 NULL（调用方不关心结果位置）
}

[GeneratedComInterface]
[Guid("23170F69-40C1-278A-0000-000300040000")]
internal partial interface IOutStream : ISequentialOutStream
{
    [PreserveSig]
    int Seek(long offset, uint seekOrigin, nint newPosition); // UInt64* ，可为 NULL（调用方不关心结果位置）

    [PreserveSig]
    int SetSize(ulong newSize);
}

// ============ IProgress.h : groupId = 0, subId = 5 ============

[GeneratedComInterface]
[Guid("23170F69-40C1-278A-0000-000000050000")]
internal partial interface IProgress
{
    [PreserveSig]
    int SetTotal(ulong total);

    [PreserveSig]
    int SetCompleted(nint completeValue); // const UInt64* ，可传 0 表示 NULL
}

// ============ Archive/IArchive.h : groupId = 6 ============

[GeneratedComInterface]
[Guid("23170F69-40C1-278A-0000-000600100000")]
internal partial interface IArchiveOpenCallback
{
    [PreserveSig]
    int SetTotal(nint files, nint bytes); // const UInt64* files, const UInt64* bytes

    [PreserveSig]
    int SetCompleted(nint files, nint bytes);
}

[GeneratedComInterface]
[Guid("23170F69-40C1-278A-0000-000600200000")]
internal partial interface IArchiveExtractCallback : IProgress
{
    [PreserveSig]
    int GetStream(uint index, out nint outStream, int askExtractMode); // out ISequentialOutStream*

    [PreserveSig]
    int PrepareOperation(int askExtractMode);

    [PreserveSig]
    int SetOperationResult(int opRes);
}

[GeneratedComInterface]
[Guid("23170F69-40C1-278A-0000-000600600000")]
internal partial interface IInArchive
{
    [PreserveSig]
    int Open(nint stream, nint maxCheckStartPosition, nint openCallback); // IInStream*, const UInt64*, IArchiveOpenCallback*

    [PreserveSig]
    int Close();

    [PreserveSig]
    int GetNumberOfItems(out uint numItems);

    [PreserveSig]
    int GetProperty(uint index, uint propID, out PropVariant value);

    [PreserveSig]
    int Extract(nint indices, uint numItems, int testMode, nint extractCallback); // const UInt32*, IArchiveExtractCallback*

    [PreserveSig]
    int GetArchiveProperty(uint propID, out PropVariant value);

    // GetNumberOfProperties / GetPropertyInfo / GetNumberOfArchiveProperties / GetArchivePropertyInfo
    // 位于 vtable 尾部，本项目不需要枚举属性名，予以省略（见文件头注释第 3 条）。
}

[GeneratedComInterface]
[Guid("23170F69-40C1-278A-0000-000600800000")]
internal partial interface IArchiveUpdateCallback : IProgress
{
    [PreserveSig]
    int GetUpdateItemInfo(uint index, out int newData, out int newProps, out uint indexInArchive);

    // 注意：这里故意不用 `out PropVariant value`（不像 IInArchive.GetProperty 那样）。
    // 这个方法是 native 反过来调用我们（压缩时问"这个条目属性是什么"），缓冲区由 native
    // 在它自己的栈上分配，win-x86 下只有 16 字节；用 `out PropVariant` 会让 source
    // generator 生成"整个 24 字节结构体直接写入 native 指针"的桩代码，在 win-x86 上越界
    // 8 字节。改成裸指针 + PropVariant.WriteToNative（只写前 16 字节）从根上避免这个问题。
    // 详见 PropVariant.cs 类型注释里的"方向 A / 方向 B"说明。
    [PreserveSig]
    int GetProperty(uint index, uint propID, nint value); // PROPVARIANT*

    [PreserveSig]
    int GetStream(uint index, out nint inStream); // out ISequentialInStream*

    [PreserveSig]
    int SetOperationResult(int operationResult);
}

[GeneratedComInterface]
[Guid("23170F69-40C1-278A-0000-000600A00000")]
internal partial interface IOutArchive
{
    [PreserveSig]
    int UpdateItems(nint outStream, uint numItems, nint updateCallback); // ISequentialOutStream*, IArchiveUpdateCallback*

    // GetFileTimeType 位于尾部，省略。
}

[GeneratedComInterface]
[Guid("23170F69-40C1-278A-0000-000600030000")]
internal partial interface ISetProperties
{
    [PreserveSig]
    int SetProperties(nint names, nint values, uint numProps); // const wchar_t* const*, const PROPVARIANT*
}

// ============ IPassword.h : groupId = 5 ============

[GeneratedComInterface]
[Guid("23170F69-40C1-278A-0000-000500100000")]
internal partial interface ICryptoGetTextPassword
{
    [PreserveSig]
    int CryptoGetTextPassword(out nint password); // out BSTR
}

[GeneratedComInterface]
[Guid("23170F69-40C1-278A-0000-000500110000")]
internal partial interface ICryptoGetTextPassword2
{
    [PreserveSig]
    int CryptoGetTextPassword2(out int passwordIsDefined, out nint password); // out BSTR
}
