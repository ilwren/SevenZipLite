# API 参考（`SevenZipLite` 类库）

> [English version](./SevenZipLite.en.md)

命名空间：`SevenZipLite`（NuGet 包 `SevenZipLite.Native`）。本文档覆盖面向使用者的公共 API；
`SevenZipLite.Native`/`SevenZipLite.Callbacks`/`SevenZipLite.Streams` 下的类型都是 `internal`
互操作实现细节，不在本文档范围内（想了解它们，请看 `docs/development-notes.zh-CN.md` 和源码
里的详细注释）。另外还包含 `SevenZipLite.SelfTest` 项目里几个对使用方也有用的测试/基准工具。

## 目录

- [`ArchiveFormat`](#archiveformat)
- [`ArchiveFormatExtensions`](#archiveformatextensions)
- [`SevenZipCompressor`](#sevenzipcompressor)
- [`SevenZipArchive`](#sevenziparchive)
- [`ExtractItemInfo`](#extractiteminfo)
- [`ZipMethodOverride`](#zipmethodoverride)
- [异常与错误处理](#异常与错误处理)
- [线程与并发](#线程与并发)
- [原生库加载与平台要求](#原生库加载与平台要求)
- [`SevenZipLite.SelfTest`（可选：测试/基准工具）](#sevenziplideselftest可选测试基准工具)

---

## `ArchiveFormat`

```csharp
public enum ArchiveFormat
{
    SevenZip,
    Zip,
    Tar,
    TarGZip,
}
```

本库支持的四种归档格式。不支持 RAR/ISO/CAB 等其他 7-Zip 原生库理论上能读的格式——这是刻意
的范围限制，不是底层能力限制（底层链接的是官方未裁剪的 7-Zip 引擎）。

## `ArchiveFormatExtensions`

```csharp
public static ArchiveFormat DetectFromFileName(string path);
public static bool SupportsPassword(this ArchiveFormat format);
```

| 成员 | 说明 |
|---|---|
| `DetectFromFileName(path)` | 按扩展名猜测格式（`.7z` → `SevenZip`，`.zip` → `Zip`，`.tar` → `Tar`，`.tar.gz`/`.tgz` → `TarGZip`）。无法识别的扩展名抛 `NotSupportedException`。 |
| `format.SupportsPassword()` | 仅 `SevenZip`/`Zip` 返回 `true`；`Tar`/`TarGZip` 返回 `false`（这两种格式本身不支持加密）。 |

## `SevenZipCompressor`

创建归档的高层 API。

```csharp
public static class SevenZipCompressor
{
    public sealed class Entry
    {
        public required string DiskPath { get; init; }
        public required string ArchivePath { get; init; }
    }

    public static IReadOnlyList<string> CreateArchive(
        IReadOnlyList<Entry> entries,
        string outputArchivePath,
        ArchiveFormat format,
        string? password = null,
        long volumeSize = 0,
        ZipMethodOverride? zipMethodOverride = null,
        int? threadCount = null);
}
```

### `Entry`

| 成员 | 说明 |
|---|---|
| `DiskPath` | 磁盘上的真实源文件路径。 |
| `ArchivePath` | 归档内的相对路径，建议用 `/` 分隔（跨平台一致）。 |

### `CreateArchive`

| 参数 | 说明 |
|---|---|
| `entries` | 要打包的文件列表（目前只支持文件，不支持显式添加空目录条目；空目录若需要保留，请自行在归档内放一个占位文件）。 |
| `outputArchivePath` | 最终归档路径，**不带卷号后缀**；不分卷时这就是最终文件名。 |
| `format` | 归档格式；`TarGZip` 内部会先打一个临时 `.tar` 再套一层 gzip。 |
| `password` | 可选密码，仅 `SevenZip`/`Zip` 生效；对不支持密码的格式传非 null 值会被静默忽略。 |
| `volumeSize` | 分卷大小（字节），`0` 表示不分卷；大于 0 时会在同目录生成 `outputArchivePath.001`、`.002`……返回值就是这些实际生成的卷路径列表。 |
| `zipMethodOverride` | 仅在 `format == Zip` 时有意义，见 [`ZipMethodOverride`](#zipmethodoverride)。 |
| `threadCount` | 压缩线程数，默认 `null` 时取 `Environment.ProcessorCount`。传 `1` 强制单线程。见[线程与并发](#线程与并发)。 |

**返回值**：实际生成的归档文件路径列表——不分卷时是单元素列表 `[outputArchivePath]`；分卷时是
`[outputArchivePath.001, outputArchivePath.002, ...]`。

**示例**：

```csharp
using SevenZipLite;

var entries = new List<SevenZipCompressor.Entry>
{
    new() { DiskPath = @"C:\data\report.pdf", ArchivePath = "report.pdf" },
    new() { DiskPath = @"C:\data\photo.jpg",  ArchivePath = "images/photo.jpg" },
};

// 不分卷、不加密的 7z
var volumes = SevenZipCompressor.CreateArchive(entries, @"C:\out\backup.7z", ArchiveFormat.SevenZip);

// 加密 zip，50MB 分卷
var volumes2 = SevenZipCompressor.CreateArchive(
    entries, @"C:\out\backup.zip", ArchiveFormat.Zip,
    password: "s3cr3t", volumeSize: 50L * 1024 * 1024);

// 强制单线程压缩（例如在内存/CPU 受限的设备上）
var volumes3 = SevenZipCompressor.CreateArchive(
    entries, @"C:\out\backup.7z", ArchiveFormat.SevenZip, threadCount: 1);
```

## `SevenZipArchive`

读取/解压归档的高层 API。

```csharp
public static class SevenZipArchive
{
    public static List<ExtractItemInfo> List(string archivePathOrFirstVolume, ArchiveFormat format);
    public static void ExtractAll(string archivePathOrFirstVolume, ArchiveFormat format, string outputDir, string? password = null);
}
```

| 成员 | 说明 |
|---|---|
| `List(path, format)` | 列出归档内所有条目（含目录条目）。`path` 可以是完整路径、分卷归档的第一卷（如 `xxx.7z.001`），或分卷的基础名（`xxx.7z`，会自动探测 `.001`/`.002`……）。 |
| `ExtractAll(path, format, outputDir, password)` | 解压全部内容到 `outputDir`（自动创建目录）。`password` 对不支持密码的格式会被忽略。密码错误或数据损坏时抛 `InvalidOperationException`。 |

**示例**：

```csharp
using SevenZipLite;

var items = SevenZipArchive.List(@"C:\out\backup.7z", ArchiveFormat.SevenZip);
foreach (var item in items)
    Console.WriteLine($"{item.Path}\t{(item.IsDir ? "<DIR>" : item.Size.ToString())}");

SevenZipArchive.ExtractAll(@"C:\out\backup.zip", ArchiveFormat.Zip, @"C:\restore", password: "s3cr3t");

// 分卷归档：传基础名或第一卷都可以
SevenZipArchive.ExtractAll(@"C:\out\backup.7z", ArchiveFormat.SevenZip, @"C:\restore");       // 自动探测 .001/.002/...
SevenZipArchive.ExtractAll(@"C:\out\backup.7z.001", ArchiveFormat.SevenZip, @"C:\restore");   // 效果相同
```

## `ExtractItemInfo`

```csharp
public sealed class ExtractItemInfo
{
    public required uint Index { get; init; }
    public required string Path { get; init; }
    public required bool IsDir { get; init; }
    public long Size { get; init; }
}
```

`SevenZipArchive.List` 返回的条目信息。`Path` 统一用 `/` 分隔（内部已经把 Windows 归档里的
`\` 替换成了 `/`）。`Size` 对目录条目通常是 `0`。

## `ZipMethodOverride`

```csharp
public enum ZipMethodOverride
{
    Store = 0,
    Deflate = 8,
}
```

显式指定 zip 归档的压缩方法（对应 7-Zip 原生 `NFileHeader::NCompressionMethod` 的取值），通过
标准 `ISetProperties::SetProperties` 传入 `name="m"` 实现——这与 7-Zip 命令行 `-mm=` 参数是
同一套机制。只在 `SevenZipCompressor.CreateArchive(..., format: ArchiveFormat.Zip, ...)` 时有
意义；对其他格式传这个参数会被忽略。

- `Store`：纯拷贝，不压缩，速度最快，适合已经是压缩格式的文件（图片/视频/已压缩的归档）。
- `Deflate`：标准 zip 压缩算法，兼容性最好（几乎所有解压工具都认识）。

## 异常与错误处理

本库不定义自己的异常类型，统一抛 BCL 标准异常：

| 场景 | 异常类型 |
|---|---|
| 原生库调用返回非 `S_OK` 的 HRESULT（含密码错误、数据损坏、不支持的操作等） | `InvalidOperationException`，`Message` 里包含具体 HRESULT（如 `0x80004005`）方便进一步排查 |
| 无法从文件名判断格式（`DetectFromFileName`） | `NotSupportedException` |
| 找不到原生库（`7zlite.dll`/`lib7zlite.so`） | `System.DllNotFoundException`（.NET 内置），见[原生库加载与平台要求](#原生库加载与平台要求) |
| 普通 .NET I/O 错误（磁盘满、权限不足、路径不存在等） | 对应的标准 `System.IO` 异常（`IOException`/`UnauthorizedAccessException`/`DirectoryNotFoundException` 等），未经封装直接向上抛出 |

HRESULT 速查（常见的几个）：

| HRESULT | 含义 | 常见触发场景 |
|---|---|---|
| `0x80004005`（E_FAIL） | 通用失败 | 密码错误、数据损坏、归档格式不匹配 |
| `0x80070057`（E_INVALIDARG） | 参数无效 | 内部 bug（已修复过一次，见 `docs/development-notes.zh-CN.md`），一般不应该在正常使用中遇到 |

## 线程与并发

- `SevenZipCompressor.CreateArchive` 本身是**同步阻塞**的，会一直占用调用线程直到压缩完成；
  如果在 UI 线程调用，请自行包一层 `Task.Run(...)`（三个 Demo 项目都是这么做的）。
- 压缩内部默认启用多线程（`threadCount` 参数，默认 `Environment.ProcessorCount`），这是 7-Zip
  原生的多线程压缩能力，不是"并行调用多个 `CreateArchive`"的意思——**不要**从多个线程同时调用
  `CreateArchive`/`ExtractAll` 操作同一个输出路径；对不同路径的并发调用没有已知问题，但也没有
  专门测试/优化过。
- 解压路径（`ExtractAll`）目前**没有**多线程加速，是 7-Zip 引擎自身的单线程顺序解压。
- 多文件 zip/tar 场景下，多线程压缩的收益取决于文件数量是否 ≥ 线程数（7-Zip 按文件粒度分发
  给工作线程，单个文件不会被拆开处理）；7z/LZMA2 格式对单个大文件也有实测的多线程加速。

## 原生库加载与平台要求

本库通过 `[LibraryImport("7zlite")]` 加载原生库，.NET 默认探测规则会在应用输出目录查找：

| 平台 | 期望文件名 | 获取方式 |
|---|---|---|
| Windows (win-x64/win-x86/win-arm64) | `7zlite.dll` | 7-zip.org 官方发布的 `7z.dll` 改名而来，未做任何修改 |
| Linux (linux-x64) | `lib7zlite.so` | 用仓库自带的 `native/7zip-src` 未修改源码本机编译 |
| Android (arm64-v8a/armeabi-v7a/x86_64/x86) | `lib7zlite.so` | 同上，交叉编译到对应 ABI |

通过 NuGet 包 `SevenZipLite.Native` 引用本库时：
- 如果消费方项目声明了 `RuntimeIdentifier`（或做 self-contained/单文件发布），标准的
  `runtimes/{rid}/native/*` 机制会自动处理。
- 如果是普通、没有指定 RID 的 `dotnet run`/`dotnet build`（例如桌面应用调试场景），包内置的
  `build`/`buildTransitive` MSBuild `.targets` 会按当前编译机器的 OS/架构自动复制对应原生库到
  输出目录（仅覆盖 win-x64/win-x86/win-arm64/linux-x64 四个桌面 RID）。
- **Android/.NET MAUI 项目不在上述自动机制覆盖范围内**：需要参考
  `samples/SevenZipLite.MauiDemo/SevenZipLite.MauiDemo.csproj` 里 `<AndroidNativeLibrary>`
  的用法，手动把对应 ABI 目录下的 `lib7zlite.so` 接进你自己的 MAUI/Android 项目。

找不到原生库时会抛 `System.DllNotFoundException: Unable to load DLL '7zlite'...`，这是 .NET
内置异常，不是本库自定义的。

## `SevenZipLite.SelfTest`（可选：测试/基准工具）

这个项目本来是给仓库自己的 Demo/测试用的，但它的 API 是 `public` 的，如果你想在自己的项目里
做类似的"这份 7-Zip 封装在我的目标设备上到底靠不靠谱"验证，也可以直接引用它（`ProjectReference`
或者把源码复制过去，它没有专门发布成独立 NuGet 包）。

### `SelfTestRunner.RunAll(string rootDir)`

跑一遍 13 组格式 × 分卷 × 密码 × 方法覆盖的往返正确性测试（创建 → 列出 → 解压 → 逐字节比对），
返回 `IReadOnlyList<SelfTestResult>`（每条记录 `Name`/`Pass`/`Detail`/`Log`）。

### `PerfTestRunner.Run(...)`

```csharp
public static PerfTestResult Run(
    string rootDir, long sizeBytes,
    ArchiveFormat format = ArchiveFormat.SevenZip,
    string? password = null,
    ZipMethodOverride? zipMethodOverride = null,
    bool keepFiles = false,
    Action<string>? onProgress = null);
```

生成一个体积可配置（设计上限约 1GB）的合成大文件，计时压缩/解压，返回吞吐量（MB/s）、压缩比、
是否通过流式 SHA-256 校验。用于观察真实吞吐量，不是正确性测试的一部分。

### `MultiThreadStressRunner.Run(...)`

```csharp
public static bool Run(
    string rootDir, int iterations, int fileCount,
    ArchiveFormat format = ArchiveFormat.Zip,
    string? password = null,
    Action<string>? log = null);
```

反复创建/解压一个包含若干文件的归档并做 SHA-256 校验，用更多文件数/轮次给"多线程压缩路径是否
稳定"提供比 `SelfTestRunner` 更强的信心。详见 `docs/development-notes.zh-CN.md` 里的多线程 bug
排查记录。
