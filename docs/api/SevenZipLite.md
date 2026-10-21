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
- [`SevenZipArchiveEditor`（增量增删条目）](#sevenziparchiveeditor增量增删条目)
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
        public string? DiskPath { get; init; }
        public required string ArchivePath { get; init; }
        public Func<Stream>? OpenStream { get; init; }
        public long? Length { get; init; }
        public DateTime? LastWriteTimeUtc { get; init; }
    }

    public static IReadOnlyList<string> CreateArchive(
        IReadOnlyList<Entry> entries,
        string outputArchivePath,
        ArchiveFormat format,
        string? password = null,
        long volumeSize = 0,
        ZipMethodOverride? zipMethodOverride = null,
        int? threadCount = null,
        IProgress<SevenZipProgress>? progress = null,
        CancellationToken cancellationToken = default,
        bool encryptHeaders = true);
}
```

### `Entry`

| 成员 | 说明 |
|---|---|
| `DiskPath` | 磁盘上的真实源文件路径。必须是一个能被 `File.OpenRead` 直接打开的真实文件系统路径——不能是 URI 字符串或类似 Android `content://` 这种虚拟路径（如果路径来自文件选择器/SAF，需要先把内容拷贝到真实的临时文件）。与 `OpenStream` **互斥，两者必须且只能设置一个**；两者都不设置、或两者都设置，都会在 `CreateArchive` 里立刻抛出带具体下标的 `ArgumentException`，而不是等到内部调用 `File.*` 时才报一个不带上下文的 `ArgumentNullException`。 |
| `ArchivePath` | 归档内的相对路径，建议用 `/` 分隔（跨平台一致）。**不能是 `null`/空字符串**，否则抛出带具体下标的 `ArgumentException`。 |
| `OpenStream` | 条目内容来自内存/网络等非磁盘来源时，用这个委托代替 `DiskPath`——压缩过程中每次需要读取这个条目的内容都会调用一次，必须返回一个从头开始、**支持 `Seek`** 的只读流（native 7-Zip 压缩时会对输入流调用 `Seek`；不支持 `Seek` 的来源，比如原始网络流，需要先自己缓冲到 `MemoryStream` 或临时文件）。设置这个字段时必须同时设置 `Length`，否则抛 `ArgumentException`。 |
| `Length` | 仅在设置了 `OpenStream` 时必需：流的精确字节长度（必须 ≥ 0）。7-Zip 的 native 层会在真正读取内容**之前**就需要把 `kpidSize` 写入归档头，无法事后从流本身推断，长度不准确会产生头部声明大小和实际数据对不上的损坏归档，且不一定在压缩阶段就报错。使用 `DiskPath` 时不需要设置，会自动从文件系统读取。 |
| `LastWriteTimeUtc` | 仅在设置了 `OpenStream` 时可选：条目在归档里记录的修改时间（UTC）。不设置则该条目不写入修改时间属性。使用 `DiskPath` 时不需要设置，会自动从文件系统读取。 |

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
| `progress` | 可选的进度回调，汇报字节级压缩进度（见 `SevenZipProgress`：`BytesCompleted`/`BytesTotal`/`Percentage`）。 |
| `cancellationToken` | 可选的取消令牌，取消粒度是"条目之间"，正在压缩中的单个条目无法从内部中途打断；取消后抛出 `OperationCanceledException`。 |
| `encryptHeaders` | 是否同时加密归档头（文件名/文件大小/目录结构等元数据），仅在 `format == ArchiveFormat.SevenZip` 且提供了非空密码时才有意义（对应官方 7z 命令行的 `-mhe=on`；zip 格式本身不支持这个能力，不管传什么都无效）。**默认 `true`**——这和官方 7z.exe 命令行的默认值（`false`，只加密内容不加密文件头）刻意相反：只给密码不设置这个属性时，哪怕整个归档都加了密码，文件名/大小/目录结构这些元数据仍然不需要密码就能看到（`SevenZipArchive.List` 不用密码也能跑通），这是很多用户容易踩的直觉盲区。需要旧行为（比如希望别人不知道密码也能浏览归档内容）显式传 `false`。 |

**返回值**：实际生成的归档文件路径列表——不分卷时是单元素列表 `[outputArchivePath]`；分卷时是
`[outputArchivePath.001, outputArchivePath.002, ...]`。

**异常**：`entries` 为 `null` 抛 `ArgumentNullException`；`entries` 非空但某一项的 `ArchivePath`
为 `null`/空字符串，或 `DiskPath`/`OpenStream` 两者都没设置/都设置了，或设置了 `OpenStream` 但
`Length` 缺失/为负数，都抛 `ArgumentException`，异常信息包含具体是第几个条目
（如 `entries[2].DiskPath 为空...`），方便直接定位是哪一条数据出的问题，而不用自己一条条排查。

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

**条目内容来自 `Stream`（不落盘）的示例**：

```csharp
byte[] payload = GetDataFromSomewhere(); // 比如网络下载结果、数据库 BLOB 等

var streamEntries = new List<SevenZipCompressor.Entry>
{
    // OpenStream 每次被调用都要返回一个新的、从头开始的流——MemoryStream 构造出一个新实例
    // 天然满足这一点；如果底层资源只能被消费一次，需要自己做好可重复读取的包装。
    new()
    {
        ArchivePath = "payload.bin",
        OpenStream = () => new MemoryStream(payload, writable: false),
        Length = payload.Length,
    },
};
SevenZipCompressor.CreateArchive(streamEntries, @"C:\out\backup.7z", ArchiveFormat.SevenZip);
```

### `CreateArchiveFromDirectory`

压缩整个磁盘目录的便捷方法，内部就是递归遍历目录构造出 `Entry` 列表后调用 `CreateArchive`，
省去手工遍历/拼接归档内相对路径的麻烦。含 `CreateArchiveFromDirectoryAsync` 异步版本。

```csharp
public static IReadOnlyList<string> CreateArchiveFromDirectory(
    string sourceDirectory,
    string outputArchivePath,
    ArchiveFormat format,
    bool includeBaseDirectory = true,
    Func<string, bool>? filter = null,
    string? password = null,
    long volumeSize = 0,
    ZipMethodOverride? zipMethodOverride = null,
    int? threadCount = null,
    IProgress<SevenZipProgress>? progress = null,
    CancellationToken cancellationToken = default,
    bool encryptHeaders = true);
```

| 参数 | 说明 |
|---|---|
| `sourceDirectory` | 要压缩的源目录，不存在时抛 `DirectoryNotFoundException`。 |
| `includeBaseDirectory` | `true`（默认）：归档内路径带上 `sourceDirectory` 自身的目录名作为根（例如压缩 `C:\data\photos` 得到 `photos/a.jpg`，类似资源管理器右键"压缩到 xxx.zip"）；`false`：直接把目录内容铺平到归档根（得到 `a.jpg`，不含 `photos/` 这一层）。 |
| `filter` | 可选过滤回调，入参是文件的完整磁盘路径，返回 `false` 表示排除这个文件（比如排除 `.git`/`node_modules` 目录下的内容）；不传则包含目录下的全部文件。 |
| 其余参数 | 含义和默认值和 [`CreateArchive`](#createarchive) 完全一致（密码/分卷/压缩方法/线程数/进度/取消/是否加密归档头）。 |

**已知限制**：只递归枚举文件，空子目录不会在归档里单独保留一个"目录条目"（解压后不会重新生成
那个空目录）。目录下没有任何文件可压缩时（目录为空，或全部被 `filter` 排除）抛
`InvalidOperationException`。

**示例**：

```csharp
// 压缩整个目录，归档内路径带 "photos/" 前缀
SevenZipCompressor.CreateArchiveFromDirectory(@"C:\data\photos", @"C:\out\photos.zip", ArchiveFormat.Zip);

// 铺平到根，并排除 .git 目录
SevenZipCompressor.CreateArchiveFromDirectory(
    @"C:\repo", @"C:\out\repo.zip", ArchiveFormat.Zip,
    includeBaseDirectory: false,
    filter: path => !path.Replace('\\', '/').Contains("/.git/"));
```

## `SevenZipArchive`

读取/解压归档的高层 API。

```csharp
public static class SevenZipArchive
{
    public static List<ExtractItemInfo> List(string archivePathOrFirstVolume, ArchiveFormat format,
        string? password = null, CancellationToken cancellationToken = default);
    public static void ExtractAll(string archivePathOrFirstVolume, ArchiveFormat format, string outputDir,
        string? password = null, IProgress<SevenZipProgress>? progress = null,
        CancellationToken cancellationToken = default);
    public static void ExtractEntryToStream(string archivePathOrFirstVolume, ArchiveFormat format,
        string entryPath, Stream outputStream, string? password = null,
        IProgress<SevenZipProgress>? progress = null, CancellationToken cancellationToken = default);
    public static byte[] ExtractEntryBytes(string archivePathOrFirstVolume, ArchiveFormat format,
        string entryPath, string? password = null, IProgress<SevenZipProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
```

| 成员 | 说明 |
|---|---|
| `List(path, format, password, cancellationToken)` | 列出归档内所有条目（含目录条目）。`path` 可以是完整路径、分卷归档的第一卷（如 `xxx.7z.001`），或分卷的基础名（`xxx.7z`，会自动探测 `.001`/`.002`……）。`password` 仅在归档启用了"加密文件名"（即 `encryptHeaders: true` 创建出来的 7z 归档，见 [`CreateArchive`](#createarchive)）时才需要——没加密文件头的归档不传密码也能列出文件名/大小列表。 |
| `ExtractAll(path, format, outputDir, password, progress, cancellationToken)` | 解压全部内容到 `outputDir`（自动创建目录）。`password` 对不支持密码的格式会被忽略。密码错误或数据损坏时抛 `InvalidOperationException`。`progress`/`cancellationToken` 含义同 [`CreateArchive`](#createarchive)。 |
| `ExtractEntryToStream(path, format, entryPath, outputStream, password, progress, cancellationToken)` | 把归档内**单个**条目的内容直接写进调用方提供的 `outputStream`，不经过磁盘临时文件。`entryPath` 必须和 `List()` 返回的 `ExtractItemInfo.Path` 完全一致（`/` 分隔、区分大小写；传 `\` 分隔会自动归一化）。`outputStream` 必须可写且**支持 `Seek`**（native 解压时会对输出流调用 `Seek`），生命周期由调用方自己管理，本方法不会关闭它。找不到条目抛 `FileNotFoundException`；`entryPath` 指向目录抛 `InvalidOperationException`。`tar.gz` 内部会先解出临时 tar 再从中定位条目，对调用方透明。 |
| `ExtractEntryBytes(path, format, entryPath, password, progress, cancellationToken)` | `ExtractEntryToStream` 的便捷封装，直接返回条目内容的 `byte[]`（内部用 `MemoryStream` 中转）。适合条目不大的场景；条目很大时建议用 `ExtractEntryToStream` 搭配自己的流，避免整个内容常驻内存。 |

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

// 只取归档里的一个条目，直接写到内存/网络流，不落盘
using var ms = new MemoryStream();
SevenZipArchive.ExtractEntryToStream(@"C:\out\backup.7z", ArchiveFormat.SevenZip, "images/photo.jpg", ms);

// 或者更省事，直接拿 byte[]
byte[] photoBytes = SevenZipArchive.ExtractEntryBytes(@"C:\out\backup.7z", ArchiveFormat.SevenZip, "images/photo.jpg");
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

## `SevenZipArchiveEditor`（增量增删条目）

面向对象、可以用 `using` 管理的"对一个**已存在的 7z/zip 归档**做增删条目"会话。和上面
`SevenZipCompressor`/`SevenZipArchive` 那套"一次性整体创建/整体解压"的静态方法不同，
`SevenZipArchiveEditor` 支持**真正的增量更新**——基于 7-Zip 原生 `IOutArchive::UpdateItems`
的"保留未改动条目、只重新编码新增条目"机制，不需要解压整个归档重新打包。

```csharp
public sealed class SevenZipArchiveEditor : IDisposable
{
    public sealed class EntryInfo
    {
        public required string Path { get; init; }
        public required bool IsDir { get; init; }
        public long Size { get; init; }
        public required bool IsPendingChange { get; init; }
    }

    public static SevenZipArchiveEditor Open(string archivePath, ArchiveFormat format, string? password = null, bool encryptHeaders = true);
    public static SevenZipArchiveEditor Create(string archivePath, ArchiveFormat format, string? password = null, bool encryptHeaders = true);

    public bool HasPendingChanges { get; }

    public IReadOnlyList<EntryInfo> List();
    public void AddFile(string diskPath, string archivePath);
    public int AddDirectory(string sourceDirectory, bool includeBaseDirectory = true, Func<string, bool>? filter = null);
    public bool Delete(string archivePath);
    public int Delete(Func<EntryInfo, bool> predicate);
    public void ExtractAll(string outputDir, IProgress<SevenZipProgress>? progress = null, CancellationToken cancellationToken = default);
    public void SaveChanges(IProgress<SevenZipProgress>? progress = null, CancellationToken cancellationToken = default);
    public void Dispose();
}
```

| 成员 | 说明 |
|---|---|
| `Open(path, format, password, encryptHeaders)` | 打开一个**已存在**的 7z/zip 归档；文件不存在抛 `FileNotFoundException`。`encryptHeaders`（默认 `true`）控制每次 `SaveChanges()` 写回时是否同时加密归档头，含义和默认值同 [`CreateArchive`](#createarchive) 的同名参数。 |
| `Create(path, format, password, encryptHeaders)` | 新建一个空白编辑会话，`path` 不需要已经存在；第一次 `SaveChanges()` 时才真正创建这个文件（如果 `path` 处已有文件会被直接覆盖，语义同 `CreateArchive`）。`encryptHeaders` 含义同上。 |
| `List()` | 返回"如果现在 `SaveChanges`，归档最终会是什么样子"——已保存条目（排除标记删除的）加上排队中还没保存的新增条目，**不是**单纯读磁盘上归档现在的样子（`EntryInfo.IsPendingChange` 区分两者）。 |
| `AddFile(diskPath, archivePath)` | 排队添加/替换一个文件。`diskPath` 不存在立刻抛 `FileNotFoundException`（fail fast）；`archivePath` 和现有条目（已保存或排队中）同名时是**替换**语义。只是排队，真正读取发生在 `SaveChanges` 时。 |
| `AddDirectory(...)` | 排队添加整个目录，遍历规则与 [`CreateArchiveFromDirectory`](#createarchivefromdirectory) 完全一致。返回排队添加的文件数。 |
| `Delete(archivePath)` | 精确匹配（区分大小写）删除一个条目，返回是否真的找到了要删除的东西。 |
| `Delete(predicate)` | 对 `List()` 当前快照里匹配 `predicate` 的每一项调用 `Delete(string)`，返回实际删除的条目数。 |
| `ExtractAll(outputDir, progress, cancellationToken)` | 把当前逻辑状态（已保存减去删除，加上排队新增）解压出来。`progress` 只覆盖"从磁盘归档解压已保存条目"这一步，排队中的新增条目是直接 `File.Copy` 过去的，不计入这个进度。 |
| `SaveChanges(progress, cancellationToken)` | 把排队的改动真正写回磁盘：写入临时文件，成功后才原子替换原文件（中途失败不会破坏原归档，临时文件会被清理）；成功后自动以新内容为基准重新打开。没有排队改动时直接返回（no-op）。 |
| `Dispose()` | 释放原生句柄。**不会自动 `SaveChanges`**——排队中的改动如果没有显式保存，这里会被直接丢弃（可以用 `HasPendingChanges` 自查）。这是有意的事务式设计，不是 bug。 |

**范围限制**：

- **仅支持 `ArchiveFormat.SevenZip`/`ArchiveFormat.Zip`**——`Tar`/`TarGZip` 内部实现没有
  "条目索引"概念，无法原地增删，对这两种格式调用 `Open`/`Create` 会抛出
  `NotSupportedException`，仍然只能用 `SevenZipCompressor.CreateArchive` 整体重新打包。
- **不支持分卷归档**——`Open` 只认单一完整文件，不处理 `.7z.001`/`.002`… 这类分卷。
- 不支持单条目提取（只有 `ExtractAll`，和静态的 `SevenZipArchive` 一致）。
- 路径匹配按原样做**区分大小写**的精确字符串比较，不做大小写折叠、不支持通配符。

**示例**：

```csharp
using var editor = SevenZipArchiveEditor.Open(@"C:\out\backup.zip", ArchiveFormat.Zip, password: "s3cr3t");

editor.Delete("old/report.pdf");
editor.AddFile(@"C:\data\new-report.pdf", "new-report.pdf");
editor.AddDirectory(@"C:\data\extra-photos", includeBaseDirectory: false);

Console.WriteLine(editor.HasPendingChanges); // true
editor.SaveChanges(); // 此时才真正落盘；不调用的话 using 结束时这些改动会被丢弃
```

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
| `ExtractEntryToStream`/`ExtractEntryBytes` 指定的 `entryPath` 在归档内不存在 | `FileNotFoundException` |
| `ExtractEntryToStream`/`ExtractEntryBytes` 指定的 `entryPath` 是目录条目 | `InvalidOperationException` |
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

### ⚠️ Android 平台额外要求：必须切到 CoreCLR 运行时（`UseMonoRuntime=false`）

本库的互操作层基于 .NET 8+ 的 Source-Generated COM（`[GeneratedComInterface]`/
`[GeneratedComClass]`，底层依赖 `System.Runtime.InteropServices.ComWrappers`）。
**`.NET for Android` 默认使用的 Mono 运行时完全不支持 `ComWrappers`**（MS Learn 官方 API 文档对
`ComWrappers` 类标注了 `[UnsupportedOSPlatform("android")]`），不管是 MAUI、Avalonia for
Android，还是任何其他基于 `.NET for Android` SDK 的宿主项目，只要用 Mono 运行时调用本库的任意
`CreateArchive`/`List`/`ExtractAll`/`SevenZipArchiveEditor` 方法，都会在
`ComWrappers.GetOrCreateObjectForComInstance` 处抛出：

```
System.PlatformNotSupportedException: Operation is not supported on this platform.
   at System.Runtime.InteropServices.ComWrappers.GetOrCreateObjectForComInstance(...)
   at SevenZipLite.Native.ComFactory.GetRcw(...)
```

**这不是本库的 bug，也不是某个具体 API 的问题——不管换成哪个创建/读取方法，Android 默认运行时下
必现。** 修复方式是在你的 Android 宿主项目（`.csproj`）里显式切换到 .NET 10 引入的
**实验性** CoreCLR 运行时：

```xml
<PropertyGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android'">
    <!-- CoreCLR 要求 API 24（Android 7.0 Nougat）及以上，默认模板给的 21 需要调高 -->
    <SupportedOSPlatformVersion>24.0</SupportedOSPlatformVersion>
    <UseMonoRuntime>false</UseMonoRuntime>
</PropertyGroup>
```

前提/限制：
- **必须是 `net10.0-android` 或更高**——`UseMonoRuntime=false` 这个开关是 .NET 10 才引入的，
  `net8.0-android`/`net9.0-android` 上这个属性不存在，不会生效。
- **只支持 64 位 ABI**：`arm64-v8a`/`x86_64`（emulator 常见的两种），**不支持**
  `armeabi-v7a`/`x86` 这两个 32 位 ABI——CoreCLR 根本没有对应的运行时包，真机/模拟器是 32 位
  架构的话这条路走不通。
- 截至 .NET 10，这仍是官方明确标注的**实验性、不建议用于生产环境**的功能（包体积更大、部分调试/
  诊断能力不完整）；根据官方路线图，.NET 11 起 CoreCLR 会转正为 Android/iOS/Mac Catalyst 的
  默认运行时，Mono 变成需要显式选择的兼容选项，届时这个限制会自然消失。
- 改完属性后务必做一次**彻底干净重建**（删 `bin`/`obj`，模拟器上 `adb uninstall` 卸载旧 APK，
  再重新 build+deploy）——仅仅改属性不清缓存，有可能继续装着改动前编译出的旧 Mono 版 APK，
  表现成"改了还是不行"。可以解包新生成的 APK 确认是否真的切到了 CoreCLR：
  `unzip -l <apk 路径> | grep -E "libcoreclr|libmonosgen"`，看到 `libcoreclr.so` 而不是
  `libmonosgen-2.0.so` 才说明真的生效了。

更详细的真机问题排查记录（含官方证据链接）见
[`docs/investigation-report.zh-CN.md`](./investigation-report.zh-CN.md) 第 8.7 节。

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
