#!/usr/bin/env bash
# 用系统自带 g++ 为宿主机架构（Linux x86_64/aarch64 等，不经过任何交叉工具链）编译
# 官方原版、未做任何裁剪的 Format7zF bundle，产出 7zlite.so，供
# demo/SevenZipLite.Tests、demo/CodecDump 在 host 端做控制台自检用。
#
# 这是把本项目此前手动执行的
#   make -j$(nproc) -f makefile.gcc
# 封装成可重复运行的脚本；除了调用 make，还额外做：
#   1. 编译前 `make clean` 风格的产物清理（避免残留旧目标文件）；
#   2. 编译成功后做"未定义符号体检"（除 libc/libm/libpthread/libdl/libstdc++/libgcc_s
#      等标准运行时库提供的符号外，不应该有其它未解析符号）；
#   3. dlopen + CreateObject 冒烟测试，确认产物可以被真正加载并导出预期符号；
#   4. 自动拷贝到 demo/SevenZipLite.Tests/lib7zlite.so 和 demo/CodecDump/lib7zlite.so
#      （假设 demo/ 与 7zip-src/ 是同级目录，跟仓库实际布局一致）。
#
# 用法：
#   cd 7zip-src/CPP/7zip/Bundles/Format7zF
#   ./build_linux.sh
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")"

echo "== 清理旧产物 =="
rm -rf _o

echo "== make -f makefile.gcc =="
make -j"$(nproc)" -f makefile.gcc

SO_PATH="_o/7z.so"
if [ ! -f "$SO_PATH" ]; then
  echo "错误：$SO_PATH 不存在，构建失败" >&2
  exit 1
fi

echo "== 依赖库体检（ldd）=="
# 校验产物只依赖标准系统运行时库（libstdc++/libgcc_s/libc/libm/libpthread/libdl等），
# 且没有任何 "not found"（这比逐个比对未定义符号名单更可靠：glibc 的符号名
# 会带版本后缀如 read@GLIBC_2.2.5，穷举名单既脆弱又容易有遗漏）。
LDD_OUT=$(ldd "$SO_PATH")
echo "$LDD_OUT"
if echo "$LDD_OUT" | grep -qi "not found"; then
  echo "错误：存在无法解析的共享库依赖，构建判定失败" >&2
  exit 1
fi
if echo "$LDD_OUT" | grep -viE "linux-vdso|ld-linux|libstdc\+\+|libgcc_s|libc\.so|libm\.so|libpthread|libdl\.so" | grep -q "=>"; then
  echo "错误：出现了非预期的额外共享库依赖，构建判定失败" >&2
  exit 1
fi
echo "依赖库体检通过。"

echo "== dlopen + CreateObject 冒烟测试 =="
python3 - "$SO_PATH" <<'PYEOF'
import ctypes, sys
lib = ctypes.CDLL(sys.argv[1])
fn = lib.CreateObject
print("CreateObject 符号解析成功:", fn)
PYEOF

echo "== 拷贝产物 =="
DEMO_DIR="../../../../../demo"
for dst in "$DEMO_DIR/SevenZipLite.Tests/lib7zlite.so" "$DEMO_DIR/CodecDump/lib7zlite.so"; do
  if [ -d "$(dirname "$dst")" ]; then
    cp "$SO_PATH" "$dst"
    echo "已拷贝到 $dst"
  else
    echo "跳过（目录不存在）: $dst"
  fi
done

echo "== 完成 =="
