#!/bin/bash
# MCLCS v2.5.4 发布脚本（single-file 版）
# 仅发布自包含 single-file 包（GUI + CLI 合并为一个 ZIP），不分卷。
# 理由：CNB Release 单文件资产上限 64GiB，128MB 自包含包远未触及，无需切分。
set -e

export DOTNET_ROOT=/opt/dotnet10
DOTNET=/opt/dotnet10/dotnet
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/dist/v2.5.4"
RID="win-x64"
VERSION="2.5.4"

echo "=== MCLCS v$VERSION Publish (single-file only) ==="
echo "Root: $ROOT"

mkdir -p "$OUT"

# --- 1. GUI 启动器 (single-file, self-contained) ---
echo ""
echo "[1/3] Publishing GUI (single-file, self-contained)..."
$DOTNET publish "$ROOT/src/Chert.App/Chert.App.csproj" \
    -c Release \
    -f net8.0-windows \
    -r $RID \
    -p:EnableWindowsTargeting=true \
    -p:PublishSingleFile=true \
    -p:SelfContained=true \
    -p:PublishReadyToRun=false \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    --output "$OUT/gui-singlefile"

# --- 2. CLI 工具 (single-file, self-contained) ---
echo ""
echo "[2/3] Publishing CLI (single-file, self-contained)..."
$DOTNET publish "$ROOT/tools/Chert.Cli/Chert.Cli.csproj" \
    -c Release \
    -f net8.0-windows \
    -r $RID \
    -p:EnableWindowsTargeting=true \
    -p:PublishSingleFile=true \
    -p:SelfContained=true \
    -p:PublishReadyToRun=false \
    --output "$OUT/cli-singlefile"

# --- 3. 合并 + 打包（单个 ZIP，不分卷）---
echo ""
echo "[3/3] Packaging single-file ZIP..."
cd "$OUT"
rm -rf "MCLCS-v$VERSION-singlefile"
mkdir -p "MCLCS-v$VERSION-singlefile"
cp -r gui-singlefile/* "MCLCS-v$VERSION-singlefile/"
cp -r cli-singlefile/* "MCLCS-v$VERSION-singlefile/"
zip -r "MCLCS-v$VERSION-singlefile.zip" "MCLCS-v$VERSION-singlefile/" > /dev/null

echo "=== Done ==="
ls -la "$OUT"/MCLCS-v$VERSION-singlefile.zip
echo "Single-file ZIP: $(du -h "$OUT/MCLCS-v$VERSION-singlefile.zip" | cut -f1)"
echo ""
echo "将 MCLCS-v$VERSION-singlefile.zip 作为 CNB Release v$VERSION 的单个资产上传即可"
echo "（单文件上限 64GiB，无需分卷；合并命令不再需要）"
