#!/bin/zsh
set -euo pipefail

PROJECT_DIR="${0:A:h:h}"
cd "$PROJECT_DIR"
/bin/zsh Scripts/check-source.sh
PACKAGE_ARCH="$(/usr/bin/uname -m)" CODESIGN_IDENTITY=- /bin/zsh Scripts/package-app.sh release

mkdir -p "$PROJECT_DIR/.build"
TEST_ROOT=$(/usr/bin/mktemp -d "$PROJECT_DIR/.build/installation-test.XXXXXX")
trap 'rm -rf "$TEST_ROOT"' EXIT
TEST_HOME="$TEST_ROOT/home"
TEST_WORK="$TEST_ROOT/work"
APP="$TEST_HOME/Applications/JustChat.app"
mkdir -p "$TEST_HOME" "$TEST_WORK"

install_test_app() {
    HOME="$TEST_HOME" JUSTCHAT_SKIP_BUILD=1 JUSTCHAT_SKIP_OPEN=1 \
        JUSTCHAT_WORK_DIR="$TEST_WORK" /bin/zsh Scripts/install-local.sh
}

install_test_app
[[ -x "$APP/Contents/MacOS/JustChatWriter" ]]
/usr/bin/codesign --verify --deep --strict "$APP"
[[ "$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$APP/Contents/Info.plist")" == app.justchat.writer ]]
[[ "$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$APP/Contents/Info.plist")" == 0.4.4 ]]
[[ ! -e "$APP/Contents/Helpers/node" ]]
[[ ! -e "$APP/Contents/Resources/TranslationService" ]]
if /usr/libexec/PlistBuddy -c 'Print :TranslationAPIBaseURL' "$APP/Contents/Info.plist" >/dev/null 2>&1; then
    echo "安装包仍包含旧本地服务地址" >&2
    exit 1
fi

# Reinstallation must leave the user's key and preferences untouched.
SUPPORT_DIR="$TEST_HOME/Library/Application Support/JustChat"
mkdir -p "$SUPPORT_DIR"
printf 'DEEPSEEK_API_KEY=unit-test-key\n' > "$SUPPORT_DIR/.env.local"
chmod 600 "$SUPPORT_DIR/.env.local"
install_test_app
[[ "$(<"$SUPPORT_DIR/.env.local")" == 'DEEPSEEK_API_KEY=unit-test-key' ]]
/usr/bin/codesign --verify --deep --strict "$APP"

echo "安装检查通过：本机应用可重复安装，不包含 Node，且不会改写 API 配置。"
