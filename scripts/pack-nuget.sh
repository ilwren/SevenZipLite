#!/usr/bin/env bash
# 构建并打包 SevenZipLite.Native NuGet 包。
# Build and pack the SevenZipLite.Native NuGet package.
#
# 用法 / Usage:
#   ./scripts/pack-nuget.sh [version] [output-dir]
#
#   version     可选，语义化版本号（如 1.2.3 或 1.2.3-preview.1），不传则用
#               Directory.Build.props 里的默认值。
#               Optional semantic version (e.g. 1.2.3 or 1.2.3-preview.1);
#               falls back to the default in Directory.Build.props if omitted.
#   output-dir  可选，产物输出目录，默认 ./artifacts
#               Optional output directory, defaults to ./artifacts
#
# 示例 / Examples:
#   ./scripts/pack-nuget.sh
#   ./scripts/pack-nuget.sh 1.0.0
#   ./scripts/pack-nuget.sh 1.0.0-rc.1 ./out
#
# 前置条件 / Prerequisites:
#   - 已安装 .NET SDK（与 src/SevenZipLite/SevenZipLite.csproj 里的 TargetFramework 匹配）
#     .NET SDK installed (matching the TargetFramework in SevenZipLite.csproj)
#   - 想要打进包里的原生库已经放好在仓库根 native-libs/<rid>/ 下（见该目录 README.md）；
#     缺失的 RID 会被自动跳过，不会导致失败，但产出的包在对应平台上无法工作。
#     Native binaries you want bundled are in place under the repo-root
#     native-libs/<rid>/ (see that folder's README.md); missing RIDs are silently
#     skipped rather than failing, but the resulting package won't work on that
#     platform.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PROJECT="$REPO_ROOT/src/SevenZipLite/SevenZipLite.csproj"

VERSION="${1:-}"
OUTPUT_DIR="${2:-$REPO_ROOT/artifacts}"

mkdir -p "$OUTPUT_DIR"

echo "== SevenZipLite.Native NuGet 打包 / packing =="
echo "项目 / project : $PROJECT"
echo "输出目录 / output: $OUTPUT_DIR"
if [ -n "$VERSION" ]; then
  echo "版本 / version  : $VERSION"
else
  echo "版本 / version  : (使用 Directory.Build.props 默认值 / using Directory.Build.props default)"
fi
echo

echo "-- 检查原生库覆盖情况 / native library coverage --"
for rid in win-x64 win-x86 win-arm64 linux-x64 android-arm64 android-arm android-x64 android-x86; do
  case "$rid" in
    win-*) f="$REPO_ROOT/native-libs/$rid/7zlite.dll" ;;
    *)     f="$REPO_ROOT/native-libs/$rid/lib7zlite.so" ;;
  esac
  if [ -f "$f" ]; then
    printf "  [x] %-14s %s\n" "$rid" "$f"
  else
    printf "  [ ] %-14s (缺失/missing: %s)\n" "$rid" "$f"
  fi
done
echo

VERSION_ARG=()
if [ -n "$VERSION" ]; then
  VERSION_ARG=(-p:Version="$VERSION")
fi

dotnet restore "$PROJECT"
dotnet build "$PROJECT" -c Release "${VERSION_ARG[@]}"
dotnet pack "$PROJECT" -c Release --no-build -o "$OUTPUT_DIR" "${VERSION_ARG[@]}"

echo
echo "== 完成 / done =="
find "$OUTPUT_DIR" -maxdepth 1 -name "*.nupkg" -o -name "*.snupkg" | sed 's/^/  /'
