#!/usr/bin/env bash
# ============================================================
#  Chert 发布打包脚本 —— GUI / CLI 分离包（2026-10-03 起）
# ============================================================
#  产出三个独立包：
#    1) Chert-Launcher-{ver}-win-x64-gui.zip   自包含界面（主用）
#    2) Chert-Light-{ver}-win-x64-gui.zip       轻量界面（需本机已装 .NET 运行时）
#    3) chert-cli-{ver}-win-x64.zip             命令行工具（自包含）
#
#  过去 GUI + CLI 塞在一个包里，等于各带一份 .NET 运行时
#  （2.6.0 实测合体 110.86 MB）；拆开后只要界面的用户省 44%。
#
#  用法：
#    VER=2.6.0 ./tools/publish-split.sh            # 打包 + 压缩
#    VER=2.6.0 ./tools/publish-split.sh --no-zip  # 只 publish 不压缩
# ============================================================
set -euo pipefail

VER="${VER:-}"
if [ -z "$VER" ]; then
  echo "错误：请用 VER=<版本号> 指定版本，例如 VER=2.6.0 $0" >&2
  exit 1
fi

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT_DIR="${OUT_DIR:-$REPO_ROOT/../_tmp/publish-$VER}"
DO_ZIP=1
[ "${1:-}" = "--no-zip" ] && DO_ZIP=0

PROJ_APP="$REPO_ROOT/src/Chert.App/Chert.App.csproj"
PROJ_CLI="$REPO_ROOT/tools/Chert.Cli/Chert.Cli.csproj"

# 沙箱/受限环境里 NuGet 走代理会失败
unset HTTP_PROXY HTTPS_PROXY ALL_PROXY || true

echo "=== Chert $VER 发布 ==="
echo "输出目录: $OUT_DIR"
rm -rf "$OUT_DIR"
mkdir -p "$OUT_DIR"

# ---------- ① GUI 自包含 ----------
echo ""
echo "--- [1/3] GUI 自包含（single-file, SelfContained）---"
dotnet publish "$PROJ_APP" -c Release -r win-x64 \
  -p:PublishSingleFile=true -p:SelfContained=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o "$OUT_DIR/gui-full"

# ---------- ② GUI 轻量 ----------
echo ""
echo "--- [2/3] GUI 轻量（framework-dependent）---"
dotnet publish "$PROJ_APP" -c Release -r win-x64 \
  -p:PublishSingleFile=true -p:SelfContained=false \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o "$OUT_DIR/gui-light"

# ---------- ③ CLI ----------
echo ""
echo "--- [3/3] CLI 自包含 ---"
dotnet publish "$PROJ_CLI" -c Release -r win-x64 \
  -p:PublishSingleFile=true -p:SelfContained=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o "$OUT_DIR/cli"

# ---------- GUI 自包含包剔除 CLI 产物 ----------
# Chert.App 的 publish 目录会带出 chert.*（CLI 引用 Chert.App 时复制），
# 打 GUI 包前先清掉，避免用户以为一个包里有两套入口。
for junk in chert.exe chert.dll chert.pdb chert.runtimeconfig.json chert.deps.json; do
  rm -f "$OUT_DIR/gui-full/$junk"
done

if [ "$DO_ZIP" -eq 0 ]; then
  echo ""
  echo "=== 完成（未压缩）==="
  find "$OUT_DIR" -maxdepth 2 -name "*.exe" -exec ls -lh {} \;
  exit 0
fi

# ---------- 压缩 ----------
# 用 7z（若可用）否则退回 PowerShell Compress-Archive
ZIPPER=""
if command -v 7z >/dev/null 2>&1; then
  ZIPPER="7z"
elif [ -x "/d/Program Files/7-Zip/7z.exe" ]; then
  ZIPPER="/d/Program Files/7-Zip/7z.exe"
fi

mk_zip() {  # $1=源目录  $2=包内根目录名  $3=输出zip
  if [ -n "$ZIPPER" ]; then
    ( cd "$1" && "$ZIPPER" a -tzip -mx=9 "$3" "$2" >/dev/null )
  else
    powershell -NoProfile -Command \
      "Compress-Archive -Path (Join-Path '$1' '*') -DestinationPath '$3' -Force"
  fi
}

echo ""
echo "=== 压缩 ==="
mk_zip "$OUT_DIR/gui-full"  "Chert Launcher" "$OUT_DIR/Chert-Launcher-$VER-win-x64-gui.zip"
mk_zip "$OUT_DIR/gui-light" "Chert Launcher" "$OUT_DIR/Chert-Light-$VER-win-x64-gui.zip"
mk_zip "$OUT_DIR/cli"       "chert-cli"      "$OUT_DIR/chert-cli-$VER-win-x64.zip"

echo ""
echo "=== 产出 ==="
ls -lh "$OUT_DIR"/*.zip 2>/dev/null || ls -lh "$OUT_DIR"

cat <<EOF

--- 下一步（发布时）---
1. 上传这三个 zip 到 CNB Release：v$VER
2. 更新 https://remix-laser-raising-studio.github.io/chert-upgrade/latest.json 的 wpf 段：
     "guiAvailable": true,
     "guiDownloadUrl": ".../Chert-Launcher-$VER-win-x64-gui.zip",
     "guiLightDownloadUrl": ".../Chert-Light-$VER-win-x64-gui.zip",
     "cliAvailable": true,
     "cliDownloadUrl": ".../chert-cli-$VER-win-x64.zip"
   旧字段 downloadUrl / lightDownloadUrl 保留一段过渡期（代码仍兼容）。
3. 用户在更新弹窗选「只更新界面」或「全部更新」。
EOF
