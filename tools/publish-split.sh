#!/usr/bin/env bash
# ============================================================
#  Chert 发布打包脚本（2026-10-03 起）
# ============================================================
#  产出四个包：
#    1) Chert-Launcher-{ver}-win-x64.zip          GUI+CLI 合体·自包含（兼容旧用户）
#    2) Chert-Light-{ver}-win-x64.zip              GUI+CLI 合体·轻量
#    3) Chert-Launcher-{ver}-win-x64-gui.zip      仅 GUI·自包含（主用）
#    4) chert-cli-{ver}-win-x64.zip                仅 CLI·自包含
#
#  为什么同时保留合体包与分离包：
#    合体包（2.6.0 实测 110.86 MB）里 GUI 与 CLI 各带一份 .NET 运行时；
#    只要界面的用户装分离包可省 44%。合体包保留给「一个包搞定」的场景与旧用户。
#
#  ★ 签名流程（2026-10-03 用户约定）：
#    本脚本跑到构建完成为止 → 提示人工做数字签名 → 再跑 --sign-done 续做（验签 + 压缩）。
#    不要试图脚本内自动签名：签名证书在用户手上。
#
#  用法：
#    VER=2.6.0 ./tools/publish-split.sh              # 全流程（内部会停在签名点）
#    VER=2.6.0 ./tools/publish-split.sh --build-only # 只构建到 exe，停在签名点
#    VER=2.6.0 ./tools/publish-split.sh --sign-done  # 签名完成后继续：验签 + 压缩
#    VER=2.6.0 ./tools/publish-split.sh --no-zip     # 只 publish 不压缩
# ============================================================
set -euo pipefail

VER="${VER:-}"
if [ -z "$VER" ]; then
  echo "错误：请用 VER=<版本号> 指定版本，例如 VER=2.6.0 $0" >&2
  exit 1
fi

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT_DIR="${OUT_DIR:-$REPO_ROOT/../_tmp/publish-$VER}"
MODE="${1:-}"

# 沙箱 / 受限环境里 NuGet 走代理会失败
unset HTTP_PROXY HTTPS_PROXY ALL_PROXY || true

PROJ_APP="$REPO_ROOT/src/Chert.App/Chert.App.csproj"
PROJ_CLI="$REPO_ROOT/tools/Chert.Cli/Chert.Cli.csproj"

build_all() {
  echo "=== Chert $VER 构建 ==="
  echo "输出目录: $OUT_DIR"
  rm -rf "$OUT_DIR"
  mkdir -p "$OUT_DIR"

  echo ""
  echo "--- [1/4] GUI 自包含（single-file, SelfContained）---"
  dotnet publish "$PROJ_APP" -c Release -r win-x64 \
    -p:PublishSingleFile=true -p:SelfContained=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -o "$OUT_DIR/gui-full"

  echo ""
  echo "--- [2/4] GUI 轻量（framework-dependent）---"
  dotnet publish "$PROJ_APP" -c Release -r win-x64 \
    -p:PublishSingleFile=true -p:SelfContained=false \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -o "$OUT_DIR/gui-light"

  echo ""
  echo "--- [3/4] CLI 自包含 ---"
  dotnet publish "$PROJ_CLI" -c Release -r win-x64 \
    -p:PublishSingleFile=true -p:SelfContained=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -o "$OUT_DIR/cli"

  echo ""
  echo "--- [4/4] GUI+CLI 合体（复用 GUI 产物 + 补 CLI）---"
  cp -r "$OUT_DIR/gui-full" "$OUT_DIR/both-full"
  cp "$OUT_DIR/cli"/chert.* "$OUT_DIR/both-full/" 2>/dev/null || true

  # 分离的 GUI 包要剔掉 CLI 产物：
  # Chert.App 的 publish 目录会带出 chert.*（CLI 引用 Chert.App 时被复制），
  # 不清掉会让用户以为一个包里有两套入口。
  for junk in chert.exe chert.dll chert.pdb chert.runtimeconfig.json chert.deps.json; do
    rm -f "$OUT_DIR/gui-full/$junk"
    rm -f "$OUT_DIR/gui-light/$junk"
  done

  # 合体包里 CLI 改名，避免与界面的 exe 混淆
  ( cd "$OUT_DIR/both-full" && [ -f chert.exe ] && mv -f chert.exe chert-cli.exe ) || true
}

list_exes() {
  echo ""
  echo "=== 产出 exe（待签名）==="
  find "$OUT_DIR" -maxdepth 2 -name "*.exe" -exec ls -lh {} \; 2>/dev/null || true
}

pause_for_sign() {
  list_exes
  cat <<EOF

============================================================
  ★ 现在暂停：请对上面列出的 exe 做数字签名
============================================================
  签名完成后重新执行：

      VER=$VER $0 --sign-done

  该步骤会先验签（PowerShell Get-AuthenticodeSignature），
  确认签名状态后才继续压缩。
============================================================
EOF
}

verify_signatures() {
  echo ""
  echo "=== 验签 ==="
  local bad=0 total=0
  while IFS= read -r exe; do
    [ -z "$exe" ] && continue
    total=$((total + 1))
    local st
    st="$(powershell -NoProfile -Command \
      "(Get-AuthenticodeSignature -LiteralPath '$exe').Status" 2>/dev/null | tr -d '\r' | tail -1)"
    case "$st" in
      Valid) echo "  [OK]   $st  $(basename "$exe")" ;;
      *)     echo "  [WARN] ${st:-Unknown}  $(basename "$exe")"; bad=$((bad + 1)) ;;
    esac
  done < <(find "$OUT_DIR" -maxdepth 2 -name "*.exe" 2>/dev/null)

  if [ "$total" -gt 0 ] && [ "$bad" -eq "$total" ]; then
    echo "  全部 $total 个 exe 都未签名 —— 若本次不签名，忽略此警告即可。"
  elif [ "$bad" -gt 0 ]; then
    echo "  ⚠ $bad/$total 个 exe 签名无效，请确认签名是否成功。"
  fi
}

zip_all() {
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
  # 合体包沿用对外旧名，保持与历史 latest.json / 用户预期兼容
  mk_zip "$OUT_DIR/gui-light" "Chert Launcher" "$OUT_DIR/Chert-Light-$VER-win-x64.zip"
  mk_zip "$OUT_DIR/both-full"  "Chert Launcher" "$OUT_DIR/Chert-Launcher-$VER-win-x64.zip"
  # 分离包
  mk_zip "$OUT_DIR/gui-full"  "Chert Launcher" "$OUT_DIR/Chert-Launcher-$VER-win-x64-gui.zip"
  mk_zip "$OUT_DIR/cli"       "chert-cli"      "$OUT_DIR/chert-cli-$VER-win-x64.zip"

  echo ""
  echo "=== 产出 ==="
  ls -lh "$OUT_DIR"/*.zip 2>/dev/null || ls -lh "$OUT_DIR"

  cat <<EOF

--- 下一步（发布时）---
1. 上传这四个 zip 到 Release：v$VER
     Chert-Launcher-$VER-win-x64.zip          （GUI+CLI 自包含）
     Chert-Light-$VER-win-x64.zip              （GUI+CLI 轻量）
     Chert-Launcher-$VER-win-x64-gui.zip      （仅 GUI 自包含）
     chert-cli-$VER-win-x64.zip                （仅 CLI 自包含）
2. 更新 https://remix-laser-raising-studio.github.io/chert-upgrade/latest.json 的 wpf 段：
     "guiAvailable": true,
     "guiDownloadUrl": ".../Chert-Launcher-$VER-win-x64-gui.zip",
     "guiLightDownloadUrl": ".../Chert-Light-$VER-win-x64.zip",
     "cliAvailable": true,
     "cliDownloadUrl": ".../chert-cli-$VER-win-x64.zip"
   旧字段 downloadUrl 指向合体自包含包（保留一段过渡期，代码仍优先读新字段）。
3. 用户在更新弹窗选「只更新界面」或「全部更新」。
EOF
}

case "$MODE" in
  --sign-done)
    verify_signatures
    zip_all
    ;;
  --build-only)
    build_all
    pause_for_sign
    ;;
  --no-zip)
    build_all
    list_exes
    ;;
  *)
    echo ""
    echo "流程：构建 → 【暂停：你数字签名】→ 验签 + 压缩"
    echo ""
    build_all
    pause_for_sign
    ;;
esac
