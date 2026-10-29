#!/bin/zsh
set -euo pipefail

SCRIPT_DIR="${0:A:h}"
PROJECT_DIR="${SCRIPT_DIR:h}"
CONFIGURATION="${1:-release}"
PACKAGE_ARCH="${PACKAGE_ARCH:-$(/usr/bin/uname -m)}"
APP_DIR="$PROJECT_DIR/.build/JustChat.app"
CONTENTS_DIR="$APP_DIR/Contents"

cd "$PROJECT_DIR"
export SWIFTPM_MODULECACHE_OVERRIDE="$PROJECT_DIR/.build/ModuleCache"
export CLANG_MODULE_CACHE_PATH="$PROJECT_DIR/.build/ClangModuleCache"
BUILD_FLAGS=(--disable-sandbox -c "$CONFIGURATION")
if [[ "$PACKAGE_ARCH" == universal ]]; then
    BUILD_FLAGS+=(--arch arm64 --arch x86_64)
elif [[ "$PACKAGE_ARCH" == arm64 || "$PACKAGE_ARCH" == x86_64 ]]; then
    BUILD_FLAGS+=(--arch "$PACKAGE_ARCH")
else
    echo "不支持的 PACKAGE_ARCH：$PACKAGE_ARCH（可选 arm64、x86_64、universal）" >&2
    exit 1
fi

swift build "${BUILD_FLAGS[@]}"
BIN_DIR=$(swift build "${BUILD_FLAGS[@]}" --show-bin-path)

rm -rf "$APP_DIR"
mkdir -p "$CONTENTS_DIR/MacOS"
cp "$BIN_DIR/JustChatWriter" "$CONTENTS_DIR/MacOS/JustChatWriter"
cp "$PROJECT_DIR/Resources/Info.plist" "$CONTENTS_DIR/Info.plist"
/usr/bin/plutil -lint "$CONTENTS_DIR/Info.plist"

# Reuse the per-user local certificate when available so TCC permissions survive
# rebuilds. Explicit CODESIGN_IDENTITY always wins, including an ad-hoc override.
LOCAL_SIGNING_IDENTITY="JustChat Local Code Signing"
if [[ -n "${CODESIGN_IDENTITY:-}" ]]; then
    SIGNING_IDENTITY="$CODESIGN_IDENTITY"
elif /usr/bin/security find-certificate -c "$LOCAL_SIGNING_IDENTITY" \
    "$HOME/Library/Keychains/login.keychain-db" >/dev/null 2>&1; then
    SIGNING_IDENTITY="$LOCAL_SIGNING_IDENTITY"
else
    SIGNING_IDENTITY=-
fi
echo "签名身份：$SIGNING_IDENTITY"
if [[ "$SIGNING_IDENTITY" == "-" ]]; then
    /usr/bin/codesign --force --sign - --identifier app.justchat.writer "$APP_DIR"
else
    SIGNING_FLAGS=(--force --options runtime --sign "$SIGNING_IDENTITY" --identifier app.justchat.writer)
    if [[ "$SIGNING_IDENTITY" == "Developer ID Application:"* ]]; then
        SIGNING_FLAGS+=(--timestamp)
    fi
    /usr/bin/codesign "${SIGNING_FLAGS[@]}" "$APP_DIR"
fi
/usr/bin/codesign --verify --deep --strict "$APP_DIR"

echo "$APP_DIR"
