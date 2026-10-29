#!/bin/zsh
set -euo pipefail

SCRIPT_DIR="${0:A:h}"
PROJECT_DIR="${SCRIPT_DIR:h}"

for relative in \
    Sources/JustChatWriter/LocalTranslationService.swift \
    Scripts/prepare-node.sh \
    Resources/NodeRuntime.entitlements; do
    if [[ -e "$PROJECT_DIR/$relative" ]]; then
        echo "发布检查失败：仍存在旧服务文件 $relative" >&2
        exit 1
    fi
done

FORBIDDEN_FILE=$(/usr/bin/find "$PROJECT_DIR" \
    \( -path '*/.build' -o -path '*/.build-*' -o -path '*/.git' \) -prune \
    -o -type f \( -name '.env' -o -name '.env.*' -o -name '.DS_Store' \
    -o -name '*.p12' -o -name '*.p8' -o -name '*.mobileprovision' \) -print -quit)
if [[ -n "$FORBIDDEN_FILE" ]]; then
    echo "发布检查失败：敏感或本机文件不应进入源码包：$FORBIDDEN_FILE" >&2
    exit 1
fi

SOURCE_ENTRIES=(
    "$PROJECT_DIR/Package.swift"
    "$PROJECT_DIR/README.md"
    "$PROJECT_DIR/LICENSE"
    "$PROJECT_DIR/Scripts"
    "$PROJECT_DIR/Sources"
    "$PROJECT_DIR/Resources"
    "$PROJECT_DIR/Tests"
)
SENSITIVE_MATCH=$(/usr/bin/find "${SOURCE_ENTRIES[@]}" -type f -print0 | \
    /usr/bin/xargs -0 /usr/bin/awk '
        /-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----/ ||
        /sk-[A-Za-z0-9_-]{16,}/ ||
        /\/Users\/[^\/]+\// {
            printf "%s:%d:%s\n", FILENAME, FNR, $0
        }
    ')
if [[ -n "$SENSITIVE_MATCH" ]]; then
    echo "发布检查失败：发现疑似密钥或个人绝对路径：" >&2
    echo "$SENSITIVE_MATCH" >&2
    exit 1
fi

SYMLINK=$(/usr/bin/find "${SOURCE_ENTRIES[@]}" -type l -print -quit)
if [[ -n "$SYMLINK" ]]; then
    echo "发布检查失败：源码包不应包含符号链接：$SYMLINK" >&2
    exit 1
fi

echo "源码发布检查通过：未发现密钥、个人绝对路径、敏感文件或旧 Node 服务文件。"
