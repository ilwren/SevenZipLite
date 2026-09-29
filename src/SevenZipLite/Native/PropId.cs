namespace SevenZipLite.Native;

/// <summary>对应 CPP/7zip/PropID.h（本项目仅列出用得到的属性 ID）。</summary>
internal static class PropId
{
    public const uint kpidPath = 3;
    public const uint kpidName = 4;
    public const uint kpidIsDir = 6;
    public const uint kpidSize = 7;
    public const uint kpidAttrib = 9;
    public const uint kpidMTime = 12;
    public const uint kpidCRC = 19;
}

/// <summary>对应 CPP/7zip/Archive/IArchive.h 中 NExtract::NAskMode。</summary>
internal static class AskMode
{
    public const int kExtract = 0;
    public const int kTest = 1;
    public const int kSkip = 2;
}

/// <summary>对应 NExtract::NOperationResult。</summary>
internal static class OperationResult
{
    public const int kOK = 0;
    public const int kUnsupportedMethod = 1;
    public const int kDataError = 2;
    public const int kCRCError = 3;
    public const int kUnavailable = 4;
    public const int kUnexpectedEnd = 5;
    public const int kDataAfterEnd = 6;
    public const int kIsNotArc = 7;
    public const int kHeadersError = 8;
    public const int kWrongPassword = 9;
}

internal static class HResult
{
    public const int S_OK = 0;
    public const int S_FALSE = 1;
    public const int E_NOTIMPL = unchecked((int)0x80004001);
    public const int E_NOINTERFACE = unchecked((int)0x80004002);
    public const int E_ABORT = unchecked((int)0x80004004);
    public const int E_FAIL = unchecked((int)0x80004005);
}
