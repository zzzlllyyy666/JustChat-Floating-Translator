# JustChat

English | [中文](README.zh-CN.md)

JustChat is an open-source floating writing panel for macOS: write Chinese in a separate window, preview and edit the translation, then write the text back to the original cursor position. It is not an input method and does not read or modify the existing contents of the target input field.

## Features

- Summon or hide the panel globally with `⌘⇧ Space`.
- Translate Chinese drafts into English, Japanese, Korean, Spanish, French, or German.
- Generate a translation with `⌘T` and write it back to the original cursor position with `⌘S`; the translation remains editable before write-back.
- Set default industry and tone preferences, such as "cross-border e-commerce" or "natural and professional, without excessive formality."
- Supports multiline text, repeated paragraphs, prefix preservation, and translation integrity checks.
- Stays in the menu bar without taking up Dock space; optional launch at login.
- Automatically follows the macOS light and dark appearance.
- Temporarily uses the clipboard during write-back and restores the original clipboard if the user has not changed it.

## Requirements

- macOS 14 or later.
- A Swift 6 toolchain, available from Xcode or the corresponding Command Line Tools.
- Your own DeepSeek API Key, plus network access to DeepSeek and an account with available balance.

## Install on your Mac

Download or clone this repository, then run the following command from the repository root:

```bash
/bin/zsh Scripts/install-local.sh
```

The script compiles a Release build for the current Mac architecture, installs it to `~/Applications/JustChat.app`, and opens it. The build output contains only the Swift executable and app configuration. It does not copy files from outside the source directory and does not include an API Key.

On first launch, a three-step guide introduces the DeepSeek connection, Accessibility authorization, and a short hands-on walkthrough. You can choose "Later" on each of the first two steps; this skips only the current step and continues to the next one rather than leaving the entire guide. After you click "Get Started" on the final step, the guide no longer appears automatically. You can reopen it at any time by choosing "Usage Guide…" from the JustChat menu-bar icon.

1. Enter your DeepSeek API Key and click "Connect." This sends the single message "你好" to verify your account and network. The real request may incur a very small API charge.
2. Follow the guide to allow JustChat under "System Settings → Privacy & Security → Accessibility." The page automatically checks the permission status.
3. Press `⌘⇧ Space` to summon the panel, enter Chinese and press `⌘T` to generate a translation, then select the target input field and press `⌘S` to write it back.
4. To launch the app automatically at login, enable "Open JustChat at Login" in the menu-bar menu.

When upgrading from an older version, the installer stops the previously registered `com.zhangliuyi.justchat.translation-service` background service and moves the old app and service registration files to `.build/install-backup.*`. Existing API configuration remains in place and is not copied into the backup.

### Build without installing

```bash
/bin/zsh Scripts/package-app.sh release
open .build/JustChat.app
```

By default, the script builds for the current architecture. You can also set `PACKAGE_ARCH=arm64`, `PACKAGE_ARCH=x86_64`, or `PACKAGE_ARCH=universal`. Successful cross-compilation does not mean real-device acceptance has been completed for that architecture.

## Code signing

To preserve Accessibility permission across upgrades, create a stable local signing identity once on your Mac before the first installation:

```bash
/bin/zsh Scripts/setup-local-signing.sh
/bin/zsh Scripts/install-local.sh
```

The script uses the OpenSSL and Security tools bundled with macOS to create `JustChat Local Code Signing`. The certificate and private key are stored only in the current user's login keychain, and temporary files are deleted immediately. Subsequent builds automatically reuse this identity without requiring a paid Apple Developer Program membership. Without this certificate, the build falls back to ad-hoc signing; **the ad-hoc identity changes with the binary, so authorization must be granted again after an update**.

Set `CODESIGN_IDENTITY=-` to force ad-hoc signing. Developers with a stable certificate can also specify it explicitly:

```bash
CODESIGN_IDENTITY='Apple Development: your full certificate name' /bin/zsh Scripts/package-app.sh release
```

The installer does not modify the macOS permissions database. After changing the signing identity, you must allow the current JustChat app again under "System Settings → Privacy & Security → Accessibility."

This project publishes source code and does not provide precompiled packages that bypass Gatekeeper. Local self-signing only preserves permissions on your own Mac and is not equivalent to signing for public distribution. To distribute a precompiled app to others, sign it with your own Developer ID and complete Apple notarization.

## Privacy and configuration

- Chinese drafts, industry, tone, and target language are sent to DeepSeek for translation. Do not enter passwords, private keys, or other information that should not be sent to a third party.
- The API Key is stored in `~/Library/Application Support/JustChat/.env.local`, with directory permissions `700` and file permissions `600`. This is a plaintext configuration file readable by the current user, not encrypted Keychain storage.
- The app collects no telemetry, starts no local translation service, and shares no service with iPhone, Windows, or other clients.
- Uninstalling the app does not automatically delete its configuration. To remove it completely, delete the configuration directory above as well.

## Development and verification

Run all unit tests:

```bash
swift test --disable-sandbox --scratch-path .build-review
```

Check source-release boundaries, build the app, and verify reinstallation:

```bash
/bin/zsh Scripts/check-source.sh
/bin/zsh Tests/InstallationTests.sh
```

Generate light and dark screenshots of the three-step guide, standard editor, and API settings page:

```bash
BIN_DIR=$(swift build --disable-sandbox --show-bin-path)
"$BIN_DIR/JustChatWriter" --ui-check "$PWD/.build/ui-check"
```

Tests and UI checks use fictitious keys, isolated configuration, and mocked network responses; they do not call the real DeepSeek service. Real connection verification occurs only when the user explicitly clicks "Connect" or starts a translation.

## License

This project uses the [MIT License](LICENSE). DeepSeek is a product name of its respective rights holder; this project is not an official DeepSeek client.
