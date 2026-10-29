#!/bin/zsh
set -euo pipefail

SCRIPT_DIR="${0:A:h}"
PROJECT_DIR="${SCRIPT_DIR:h}"
SOURCE_APP="$PROJECT_DIR/.build/JustChat.app"
DESTINATION_APP="$HOME/Applications/JustChat.app"
WORK_DIR="${JUSTCHAT_WORK_DIR:-$PROJECT_DIR/.build}"
LEGACY_LABEL="com.zhangliuyi.justchat.translation-service"
LEGACY_AGENT="$HOME/Library/LaunchAgents/$LEGACY_LABEL.plist"
LEGACY_TARGET="gui/$(/usr/bin/id -u)/$LEGACY_LABEL"

if [[ "${JUSTCHAT_SKIP_BUILD:-0}" != 1 ]]; then
    /bin/zsh "$SCRIPT_DIR/package-app.sh" "${JUSTCHAT_BUILD_CONFIGURATION:-release}"
fi
[[ -d "$SOURCE_APP" ]] || { echo "缺少已构建的应用：$SOURCE_APP" >&2; exit 1; }
/usr/bin/codesign --verify --deep --strict "$SOURCE_APP"

mkdir -p "$WORK_DIR" "${DESTINATION_APP:h}"
BACKUP_DIR=$(/usr/bin/mktemp -d "$WORK_DIR/install-backup.XXXXXX")
STAGING_DIR=$(/usr/bin/mktemp -d "$WORK_DIR/install-stage.XXXXXX")
/usr/bin/ditto "$SOURCE_APP" "$STAGING_DIR/JustChat.app"

# Remove only the registration created by earlier JustChat releases. The user's
# API configuration stays in place and is never copied into the app or backups.
if [[ -f "$LEGACY_AGENT" ]]; then
    if /bin/launchctl print "$LEGACY_TARGET" >/dev/null 2>&1; then
        /bin/launchctl bootout "$LEGACY_TARGET"
    fi
    mv "$LEGACY_AGENT" "$BACKUP_DIR/legacy-translation-service.plist"
fi

# Quit only the installed copy, never a development or test process.
for writer_pid in $(/usr/bin/pgrep -x JustChatWriter || true); do
    executable=$(/bin/ps -p "$writer_pid" -o comm=)
    if [[ "$executable" == "$DESTINATION_APP/Contents/MacOS/JustChatWriter" ]]; then
        /bin/kill -TERM "$writer_pid"
    fi
done

if [[ -d "$DESTINATION_APP" ]]; then
    mv "$DESTINATION_APP" "$BACKUP_DIR/JustChat.app"
fi
mv "$STAGING_DIR/JustChat.app" "$DESTINATION_APP"
/usr/bin/codesign --verify --deep --strict "$DESTINATION_APP"

if [[ "${JUSTCHAT_SKIP_OPEN:-0}" != 1 ]]; then
    /usr/bin/open "$DESTINATION_APP"
fi

echo "已安装：$DESTINATION_APP"
echo "旧应用及旧服务注册备份：$BACKUP_DIR"
echo "如系统提示，请在“隐私与安全性 → 辅助功能”中允许 JustChat。"
