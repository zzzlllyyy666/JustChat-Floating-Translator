#!/bin/zsh
set -euo pipefail
umask 077

SCRIPT_DIR="${0:A:h}"
PROJECT_DIR="${SCRIPT_DIR:h}"
IDENTITY="JustChat Local Code Signing"
KEYCHAIN="$HOME/Library/Keychains/login.keychain-db"
OPENSSL=/usr/bin/openssl

if /usr/bin/security find-certificate -c "$IDENTITY" "$KEYCHAIN" >/dev/null 2>&1; then
    echo "本地签名已存在：$IDENTITY"
    exit 0
fi

mkdir -p "$PROJECT_DIR/.build"
TEMP_DIR=$(/usr/bin/mktemp -d "$PROJECT_DIR/.build/local-signing.XXXXXX")
trap 'rm -rf "$TEMP_DIR"' EXIT
CERT_PASSWORD=$($OPENSSL rand -hex 32)
export CERT_PASSWORD

$OPENSSL req -x509 -newkey rsa:2048 -sha256 -days 3650 -nodes \
    -keyout "$TEMP_DIR/private-key.pem" \
    -out "$TEMP_DIR/certificate.pem" \
    -subj "/CN=$IDENTITY" \
    -addext "keyUsage=critical,digitalSignature" \
    -addext "extendedKeyUsage=codeSigning"

PKCS12_FLAGS=()
if [[ "$($OPENSSL version)" == OpenSSL\ 3* ]]; then
    PKCS12_FLAGS=(-legacy)
fi
$OPENSSL pkcs12 -export "${PKCS12_FLAGS[@]}" \
    -in "$TEMP_DIR/certificate.pem" \
    -inkey "$TEMP_DIR/private-key.pem" \
    -out "$TEMP_DIR/identity.p12" \
    -passout "env:CERT_PASSWORD"

/usr/bin/security import "$TEMP_DIR/identity.p12" \
    -k "$KEYCHAIN" \
    -P "$CERT_PASSWORD" \
    -T /usr/bin/codesign
/usr/bin/security find-certificate -c "$IDENTITY" "$KEYCHAIN" >/dev/null

echo "已创建本地签名：$IDENTITY"
echo "证书和私钥仅保存在当前用户的登录钥匙串；临时文件已删除。"
