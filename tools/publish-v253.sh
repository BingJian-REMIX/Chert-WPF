#!/bin/bash
# MCLCS v2.5.3 发布脚本
# 发布 GUI 启动器 + CLI 工具，生成 portable 与 single-file 两种 ZIP 包
set -e

export DOTNET_ROOT=/opt/dotnet10
DOTNET=/opt/dotnet10/dotnet
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/dist/v2.5.3"
RID="win-x64"

echo "=== MCLCS v2.5.3 Publish ==="
echo "Root: $ROOT"

mkdir -p "$OUT"

# --- 1. GUI 启动器 (portable) ---
echo ""
echo "[1/4] Publishing GUI (portable, framework-dependent)..."
$DOTNET publish "$ROOT/src/Chert.App/Chert.App.csproj" \
    -c Release \
    -f net8.0-windows \
    -r $RID \
    -p:EnableWindowsTargeting=true \
    -p:PublishReadyToRun=false \
    --output "$OUT/gui-portable"

# --- 2. CLI 工具 (portable) ---
echo ""
echo "[2/4] Publishing CLI (portable, framework-dependent)..."
$DOTNET publish "$ROOT/tools/Chert.Cli/Chert.Cli.csproj" \
    -c Release \
    -f net8.0-windows \
    -r $RID \
    -p:EnableWindowsTargeting=true \
    -p:PublishReadyToRun=false \
    --output "$OUT/cli-portable"

# --- 3. GUI 启动器 (single-file) ---
echo ""
echo "[3/4] Publishing GUI (single-file, self-contained)..."
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

# --- 4. CLI 工具 (single-file) ---
echo ""
echo "[4/4] Publishing CLI (single-file, self-contained)..."
$DOTNET publish "$ROOT/tools/Chert.Cli/Chert.Cli.csproj" \
    -c Release \
    -f net8.0-windows \
    -r $RID \
    -p:EnableWindowsTargeting=true \
    -p:PublishSingleFile=true \
    -p:SelfContained=true \
    -p:PublishReadyToRun=false \
    --output "$OUT/cli-singlefile"

# --- 打包 ZIP ---
echo ""
echo "=== Packaging ZIP ==="
cd "$OUT"

# 合并 portable
mkdir -p "MCLCS-v2.5.3-portable"
cp -r gui-portable/* "MCLCS-v2.5.3-portable/"
cp -r cli-portable/* "MCLCS-v2.5.3-portable/"
zip -r "MCLCS-v2.5.3-portable.zip" "MCLCS-v2.5.3-portable/" > /dev/null

# 合并 single-file
mkdir -p "MCLCS-v2.5.3-singlefile"
cp -r gui-singlefile/* "MCLCS-v2.5.3-singlefile/"
cp -r cli-singlefile/* "MCLCS-v2.5.3-singlefile/"
zip -r "MCLCS-v2.5.3-singlefile.zip" "MCLCS-v2.5.3-singlefile/" > /dev/null

# single-file 自包含包约 128MB，超过多数平台附件上限（100MB），
# 按 90MB 做字节级精确切分（split，无 marker，合并即还原原 zip）。
echo "=== Splitting single-file into volumes (90MB each) ==="
SPLIT_SIZE=90m
rm -f "MCLCS-v2.5.3-singlefile.zip."*
split -b "$SPLIT_SIZE" -d -a 2 "MCLCS-v2.5.3-singlefile.zip" "MCLCS-v2.5.3-singlefile.zip."
rm -f "MCLCS-v2.5.3-singlefile.zip"   # 删除中间大包，仅保留分卷

echo "=== Done ==="
ls -la "$OUT"/*.zip*
echo ""
echo "Portable ZIP:     $(du -h "$OUT/MCLCS-v2.5.3-portable.zip" | cut -f1)"
echo "Single-file 分卷: $(du -h "$OUT"/MCLCS-v2.5.3-singlefile.zip.* | cut -f1 | tr '\n' ' ')"
echo ""
echo "合并命令 (Linux/macOS): cat MCLCS-v2.5.3-singlefile.zip.* > MCLCS-v2.5.3-singlefile.zip"
echo "合并命令 (Windows):     copy /b MCLCS-v2.5.3-singlefile.zip.00+MCLCS-v2.5.3-singlefile.zip.01 MCLCS-v2.5.3-singlefile.zip"
