# 温暖如初 构建指南

---

## Windows 平台

### 1. 下载安装 .NET SDK

**方法一：官方安装程序（推荐）**

1. 打开 https://dotnet.microsoft.com/zh-cn/download/dotnet/10.0
2. 下载 **（建议）.NET SDK 10.0.x**（选 Windows x64，建议 10.0+）
3. 双击安装，一路下一步
4. 安装完成后打开 **命令提示符 (cmd)** 或 **PowerShell**，验证：

```cmd
dotnet --version
```
> 应输出类似 `10.0.x`

**方法二：winget（如果装了 winget）**
```cmd
winget install Microsoft.DotNet.SDK.10
```

### 2. 安装 MAUI 工作负载

```cmd
dotnet workload install maui
```
> 耗时约 2-5 分钟，会自动下载 Android SDK、Windows SDK 等

### 3. 配置环境变量（一般不需要）

SDK 安装程序会自动添加 PATH。如果 `dotnet` 命令找不到：
- 按 `Win + R` → `sysdm.cpl` → 高级 → 环境变量
- 检查 `Path` 变量是否包含：
  - `C:\Program Files\dotnet\`
- 如果没有，手动添加后重启终端

可选验证：
```cmd
dotnet --list-sdks
dotnet workload list    # 应看到 maui
```

### 4. 构建项目

```cmd
cd WarmAsBefore

:: Android APK
dotnet build -f net10.0-android

:: Windows EXE（.NET 10 已移除 win10-x64 RID，统一 win-x64 + RuntimeIdentifierOverride；
:: Override 是 WindowsAppSDK #3337 的官方 workaround，必须带着）
dotnet publish WarmAsBefore.csproj -f net10.0-windows10.0.19041.0 -c Release -r win-x64 --self-contained false -p:RuntimeIdentifierOverride=win-x64 -p:UseMonoRuntime=false
```

输出位置：
- Android APK：`bin\Debug\net10.0-android\com.companyname.warmasbefore-Signed.apk`
- Windows EXE：`bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\WarmAsBefore.exe`

### 5. 发布脚本（已提供）

直接用项目根目录下的脚本：
```cmd
:: Windows EXE
publish_windows.bat

:: Android APK（需要先安装 Android 工作负载）
dotnet build -f net10.0-android
```

### 6. 卸载

```cmd
:: 卸载 .NET SDK
控制面板 → 程序和功能 → Microsoft （建议）.NET SDK 10.0.x → 卸载

:: 或使用 winget
winget uninstall Microsoft.DotNet.SDK.10

:: 清理 MAUI 工作负载（会先卸载 SDK）
dotnet workload uninstall maui
```

---

## macOS 平台

### 1. 下载安装 .NET SDK

1. 打开 https://dotnet.microsoft.com/zh-cn/download/dotnet/10.0
2. 下载 **（建议）.NET SDK 10.0.x**（选 macOS x64 或 Apple Silicon 版）
3. 双击 `.pkg` 安装
4. 终端验证：

```bash
dotnet --version
```

### 2. 安装 MAUI 工作负载

```bash
dotnet workload install maui
```

### 3. 安装 Xcode（构建 iOS 需要）

- 从 Mac App Store 安装 Xcode 16+
- 首次打开 Xcode，同意协议，等待组件安装
- 安装 iOS 模拟器

```bash
sudo xcode-select --switch /Applications/Xcode.app/Contents/Developer
xcodebuild -downloadAllPlatforms
```

### 4. 构建项目

```bash
cd WarmAsBefore

:: Android APK
dotnet build -f net10.0-android

:: iOS IPA
dotnet build -f net10.0-ios

:: macOS Catalyst（可选）
dotnet build -f net10.0-maccatalyst
```

### 5. 卸载

```bash
:: 卸载 .NET SDK
sudo rm -rf /usr/local/share/dotnet
sudo rm -rf /etc/paths.d/dotnet

:: 清理 NuGet 缓存（可选）
rm -rf ~/.nuget
```

---

## Linux (x86-64) 平台

### 1. 安装 .NET SDK

```bash
wget https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh
chmod +x /tmp/dotnet-install.sh
/tmp/dotnet-install.sh --version 10.0.100
```

添加环境变量：
```bash
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$DOTNET_ROOT:$PATH
```
建议添加到 `~/.bashrc`。

### 2. 安装 MAUI Android 工作负载

```bash
dotnet workload install maui-android
```

### 3. 构建 Android APK

```bash
cd WarmAsBefore
dotnet build -f net10.0-android
```

> Linux 上只能构建 Android 目标，Windows / iOS 需要对应平台。

---

## 常见问题

**Q: `NETSDK1100: To build a project targeting Windows on this operating system, set the EnableWindowsTargeting property to true.`**  
A: 在 Linux/macOS 上构建时加上：
```bash
dotnet build -f net10.0-android -p:EnableWindowsTargeting=false
```

**Q: 找不到 Android SDK**  
A: MAUI workload 会自动安装 Android SDK，默认位置：
- Windows：`C:\Program Files\dotnet\packs\Microsoft.Android.Sdk.Windows\`
- macOS/Linux：`~/.dotnet/packs/Microsoft.Android.Sdk.*`
如遇到问题，手动设置：
```cmd
set ANDROID_HOME=%LOCALAPPDATA%\Android\Sdk
```

**Q: Windows 构建报错 `API 级别 35 未安装`**  
A: 自动安装：
```cmd
dotnet build -t:InstallAndroidDependencies -f net10.0-android "-p:AcceptAndroidSDKLicenses=true"
```

**Q: 点击 EXE 没反应（无窗口弹出）**  
A: 常见原因与排查：
1. 先看是否有崩溃日志：`Win + R` → `eventvwr` → Windows 日志 → 应用程序，找 `.NET Runtime` 报错
2. 确保发布用的是官方要求的命令（.NET 10 写法）：`-r win-x64 --self-contained false -p:RuntimeIdentifierOverride=win-x64 -p:UseMonoRuntime=false`。**`RuntimeIdentifierOverride` 必须带着**——它就是绕开 WindowsAppSDK #3337 资源部署 bug 的 workaround；漏掉它（只写 `-r win-x64`）会导致 XAML 主题资源缺失，启动时报 `Cannot locate resource from 'ms-appx:///Microsoft.UI.Xaml/Themes/themeresources.xaml'`。注意 .NET 10 已移除旧写法 `win10-x64`（会报 NETSDK1083）
3. 不要用 `PublishSingleFile=true`（与 WinAppSDK 冲突，会静默失败）
4. 首次运行时 Windows Defender/SmartScreen 可能拦截：点「更多信息」→「仍要运行」
5. 如果崩溃日志提到字体：项目 `Resources/Fonts` 下必须有实际 `.ttf` 文件，且 `MauiProgram.cs` 的 `fonts.AddFont` 注册的文件名必须与之一致
6. 启动日志在 `Documents\warm_startup.log`（注意 360 等工具迁移过文档目录的机器，实际路径可能是 `D:\360MoveData\...`）

**Q: 构建太慢？**  
A: 首次构建需下载 NuGet 包和 Android SDK，后续会缓存。可用：
```cmd
dotnet build -c Release    # Release 模式，跳过调试符号
```

---

## 项目文件结构

```
WarmAsBefore/                ← git 仓库根（即应用项目目录）
├── BUILD_GUIDE.md           ← 本指南
├── publish_windows.bat      ← Windows EXE 一键发布
├── publish_windows.sh       ← bash 版
├── publish_android.sh       ← Android 发布脚本
├── README.md / DEVELOPMENT.md
├── docs/                    ← 架构与组件文档
└── WarmAsBefore.csproj      ← 主项目文件 (三平台)
```
