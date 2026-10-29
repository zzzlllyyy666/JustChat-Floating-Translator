# JustChat

JustChat 是一个开源的 macOS 浮动写作面板：在独立窗口里写中文，预览并修改译文，再把文本写回原来的光标位置。它不是输入法，也不会读取或修改目标输入框中的已有内容。

翻译由 Swift 直接请求 DeepSeek 完成。**不需要 Node.js、npm、Homebrew、常驻本地服务或其他客户端，也不需要付费 Apple Developer Program。**

## 功能

- `⌘⇧ Space` 全局呼出或收起面板。
- 中文草稿翻译为英语、日语、韩语、西班牙语、法语或德语。
- `⌘T` 生成译文，`⌘S` 写回原光标位置；写回前仍可编辑译文。
- 自定义默认行业和语气，例如“跨境电商”“自然、专业，不要太客套”。
- 支持多行文本、重复段落、前缀保留和译文完整性检查。
- 菜单栏常驻，不占用 Dock；可选择登录时打开。
- 自动跟随 macOS 浅色和深色外观。
- 写入时临时使用剪贴板，并在内容未被用户改动时恢复原剪贴板。

## 环境要求

- macOS 14 或更新版本。
- Swift 6 工具链，可从 Xcode 或对应的命令行开发工具获得。
- 用户自己的 DeepSeek API Key，以及可访问 DeepSeek 的网络和账户余额。

### 已验证环境与兼容边界

JustChat 0.4.4 已在 **Apple Silicon（arm64）＋ macOS 26.6.2＋ Swift 6.3.3** 环境通过实际使用、权限和界面验证。macOS 14 是最低部署目标，不代表所有 macOS 14 或更新版本都已完成真机测试。

| 项目 | 已验证情况与边界 |
| --- | --- |
| 芯片架构 | Apple Silicon（arm64）已验证。直接复制当前 App 不适用于 Intel Mac；构建脚本支持 `x86_64` 和 `universal`，但 Intel 真机尚未验证。 |
| macOS 版本 | macOS 26.6.2 已验证；其他 macOS 14 或更新版本尚未逐一实测。 |
| Mac 年份 | 没有针对特定年份或显卡的限制，主要取决于芯片、系统版本和 Swift 工具链。 |
| 浅色与深色外观 | 均已检查；主按钮会跟随系统强调色，不同用户的按钮颜色可能不同。 |
| 辅助显示设置 | 高对比度、降低透明度和放大文字尚未专项测试。面板固定为 340 × 360，极大字体下可能出现拥挤。 |

在未实测环境中，安装、系统授权和文本写回行为可能存在差异。反馈兼容性问题时，请提供 Mac 芯片、macOS 版本、JustChat 版本和具体复现步骤。

## Windows 版（开发预览）

`windows/` 包含独立的 .NET 10＋WPF 工程，不依赖 Mac 版或常驻 Node 服务。当前提供置顶面板、六种目标语言、DeepSeek 基础翻译、30 秒超时、最多两次完整性尝试、错误提示、`Ctrl+Shift+Space` 全局呼出／收起、托盘常驻和文本写回。自定义行业与语气，以及 Mac 版的前缀锁定、分段和重复草稿优化尚未接入。

填写自己的 DeepSeek API Key 后，点击“保存 Key”可使用 Windows DPAPI 按当前用户加密，并保存到 `%APPDATA%\JustChat\deepseek.key`。草稿只会在主动点击“翻译”时发送至 DeepSeek；翻译期间如果修改草稿或目标语言，旧请求的结果不会回填。

译文生成后可直接编辑，再点击“写回”或按 `Ctrl+S`。JustChat 会激活最近使用的外部窗口并发送 `Ctrl+V`，不会读取目标输入框的已有内容。写回前会保存剪贴板，粘贴后仅在剪贴板未被用户改动时恢复原内容；写回成功后开始新草稿。目标窗口关闭、切换或粘贴失败时，草稿和译文会保留。

文本写回依赖目标应用支持剪贴板粘贴。目标应用以更高权限运行、处于安全桌面，或拦截模拟按键时，Windows 可能阻止写回；此时可在面板中复制译文后手动粘贴。

关闭主窗口只会隐藏到托盘，不会退出应用。双击托盘图标或选择“打开 JustChat”可重新显示面板，选择“退出 JustChat”才会彻底退出。如果 `Ctrl+Shift+Space` 已被其他程序占用，面板会提示注册失败，仍可使用托盘入口。

在安装了 .NET 10 SDK 的 Windows 电脑上，可从仓库根目录运行核心测试并构建 x64 单文件应用：

```powershell
dotnet test windows/JustChat.Core.Tests/JustChat.Core.Tests.csproj --configuration Release
dotnet publish windows/JustChat.Windows/JustChat.Windows.csproj --configuration Release --runtime win-x64 --self-contained true --output artifacts/win-x64 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

将 `win-x64` 改为 `win-arm64` 可构建 ARM64 版本。`.github/workflows/windows.yml` 会在 Windows 上运行核心测试，并分别构建两种架构的自包含产物。Windows 编译产物和真机兼容性以实际 CI 与验收结果为准。

## 安装到自己的 Mac

下载或克隆本仓库后，在仓库根目录执行：

```bash
/bin/zsh Scripts/install-local.sh
```

脚本会在本机编译当前 Mac 架构的 Release 版本，安装到 `~/Applications/JustChat.app` 并打开。构建产物只包含 Swift 可执行文件和应用配置，不会复制源码目录之外的文件，也不会携带 API Key。

首次打开会显示三步使用指南，依次介绍 DeepSeek 连接、辅助功能授权和第一次操作练习。前两步可选择“稍后再说”，但只会跳过当前步骤并继续下一步，不会退出整份指南。最后点击“开始使用”后，指南不再自动显示；以后随时可从菜单栏 JustChat 图标中的“使用指南…”重新打开。

1. 填写自己的 DeepSeek API Key，再点击“连接”。连接按钮会发送一句“你好”验证账户和网络，这次真实请求可能产生少量 API 费用。
2. 按引导在“系统设置 → 隐私与安全性 → 辅助功能”中允许 JustChat；页面会自动检测权限状态。
3. 按 `⌘⇧ Space` 呼出面板，输入中文后用 `⌘T` 生成译文，选择目标输入框后用 `⌘S` 写入。
4. 如需登录后自动打开，在菜单栏勾选“登录时打开 JustChat”。

从旧版升级时，安装器会退出旧版注册的 `com.zhangliuyi.justchat.translation-service` 后台服务，并把旧应用和服务注册文件移动到 `.build/install-backup.*`。已有 API 配置保持原位，不会被复制进备份。

### 只构建，不安装

```bash
/bin/zsh Scripts/package-app.sh release
open .build/JustChat.app
```

默认构建当前架构。也可设置 `PACKAGE_ARCH=arm64`、`PACKAGE_ARCH=x86_64` 或 `PACKAGE_ARCH=universal`。交叉编译成功不等于对应架构的真机验收已经完成。

## 签名说明

为了在升级后保留辅助功能权限，首次安装前可在自己的 Mac 上创建一次固定本地签名：

```bash
/bin/zsh Scripts/setup-local-signing.sh
/bin/zsh Scripts/install-local.sh
```

脚本使用系统自带的 OpenSSL 和 Security 工具创建 `JustChat Local Code Signing`，证书及私钥只保存在当前用户的登录钥匙串，临时文件会立即删除。之后的构建会自动复用该身份，不需要付费 Apple Developer Program。没有该证书时，构建回退到 ad-hoc 签名；**ad-hoc 身份随二进制变化，更新后需要重新授权**。

设置 `CODESIGN_IDENTITY=-` 可强制使用 ad-hoc。有固定证书的开发者也可显式指定：

```bash
CODESIGN_IDENTITY='Apple Development: 你的完整证书名称' /bin/zsh Scripts/package-app.sh release
```

安装器不会修改 macOS 的权限数据库。更换签名身份后，需要在“系统设置 → 隐私与安全性 → 辅助功能”中重新允许当前 JustChat。

本项目公开的是源码，不提供绕过 Gatekeeper 的预编译安装包。本地自签名只用于保持本机权限，不等同于公开发行签名。若要向他人分发预编译应用，应自行使用 Developer ID 签名并完成 Apple 公证。

## 隐私与配置

- 中文草稿、行业、语气和目标语言会发送至 DeepSeek 用于翻译，请勿输入密码、私钥等不应发送给第三方的信息。
- API Key 保存在 `~/Library/Application Support/JustChat/.env.local`，目录权限为 `700`，文件权限为 `600`。这是当前用户可读的明文配置文件，不是 Keychain 加密存储。
- 应用不收集遥测数据，不启动本地翻译服务，也不向 iPhone、Windows 或其他客户端共享服务。
- 卸载应用不会自动删除配置。需要彻底删除时，可一并移除上述配置目录。

## 开发与验证

运行全部单元测试：

```bash
swift test --disable-sandbox --scratch-path .build-review
```

检查源码发布边界、构建应用并验证重复安装：

```bash
/bin/zsh Scripts/check-source.sh
/bin/zsh Tests/InstallationTests.sh
```

生成三步使用指南、普通编辑器和 API 设置页的浅色、深色截图：

```bash
BIN_DIR=$(swift build --disable-sandbox --show-bin-path)
"$BIN_DIR/JustChatWriter" --ui-check "$PWD/.build/ui-check"
```

测试和 UI 检查使用虚构 Key、隔离配置及模拟网络响应，不调用真实 DeepSeek。真实连接验证只会在用户主动点击“连接”或发起翻译时发生。

## 许可证

本项目使用 [MIT License](LICENSE)。DeepSeek 是其各自权利人的产品名称；本项目不是 DeepSeek 官方客户端。
