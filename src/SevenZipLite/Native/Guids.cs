namespace SevenZipLite.Native;

/// <summary>
/// 7-Zip 的所有 CLSID / IID 都遵循固定模板，规则来自 7-Zip 源码
/// CPP/7zip/IDecl.h 中的 Z7_DECL_IFACE_7ZIP_SUB 宏 以及
/// CPP/7zip/UI/Client7z/Client7z.cpp 中构造格式 CLSID 的方式：
///   CLSID = {0x23170F69, 0x40C1, 0x278A, 0x10,0x00,0x00,0x01,0x10, formatId, 0x00,0x00}
///   IID   = {0x23170F69, 0x40C1, 0x278A, 0x00,0x00,0x00,groupId,0x00, subId, 0x00,0x00}
/// 本文件中的每个 GUID 均按上述字节模板逐字节手工核算得出（并非凭记忆抄录）。
/// </summary>
internal static class Guids
{
    // ---- 归档格式 CLSID（用于 CreateObject 创建 IInArchive / IOutArchive 实例）----
    // formatId 取值见各 *Register.cpp: 7z=0x07, Zip=0x01, Tar=0xEE, GZip=0xEF
    public static readonly Guid CLSID_7z   = new("23170F69-40C1-278A-1000-000110070000");
    public static readonly Guid CLSID_Zip  = new("23170F69-40C1-278A-1000-000110010000");
    public static readonly Guid CLSID_Tar  = new("23170F69-40C1-278A-1000-000110EE0000");
    public static readonly Guid CLSID_GZip = new("23170F69-40C1-278A-1000-000110EF0000");

    // ---- 接口 IID：groupId=3 （IStream.h）----
    public static readonly Guid IID_ISequentialInStream  = new("23170F69-40C1-278A-0000-000300010000"); // subId=0x01
    public static readonly Guid IID_ISequentialOutStream = new("23170F69-40C1-278A-0000-000300020000"); // subId=0x02
    public static readonly Guid IID_IInStream            = new("23170F69-40C1-278A-0000-000300030000"); // subId=0x03
    public static readonly Guid IID_IOutStream           = new("23170F69-40C1-278A-0000-000300040000"); // subId=0x04

    // ---- 接口 IID：groupId=0 （IProgress.h）----
    public static readonly Guid IID_IProgress = new("23170F69-40C1-278A-0000-000000050000"); // subId=0x05

    // ---- 接口 IID：groupId=6 （Archive/IArchive.h）----
    public static readonly Guid IID_IArchiveOpenCallback    = new("23170F69-40C1-278A-0000-000600100000"); // subId=0x10
    public static readonly Guid IID_IArchiveExtractCallback = new("23170F69-40C1-278A-0000-000600200000"); // subId=0x20, base=IProgress
    public static readonly Guid IID_ISetProperties          = new("23170F69-40C1-278A-0000-000600030000"); // subId=0x03
    public static readonly Guid IID_IInArchive              = new("23170F69-40C1-278A-0000-000600600000"); // subId=0x60
    public static readonly Guid IID_IArchiveUpdateCallback  = new("23170F69-40C1-278A-0000-000600800000"); // subId=0x80, base=IProgress
    public static readonly Guid IID_IOutArchive             = new("23170F69-40C1-278A-0000-000600A00000"); // subId=0xA0

    // ---- 接口 IID：groupId=5 （IPassword.h）----
    public static readonly Guid IID_ICryptoGetTextPassword  = new("23170F69-40C1-278A-0000-000500100000"); // subId=0x10
    public static readonly Guid IID_ICryptoGetTextPassword2 = new("23170F69-40C1-278A-0000-000500110000"); // subId=0x11
}
