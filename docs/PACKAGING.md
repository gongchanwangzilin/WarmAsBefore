# 打包（P10）

两支脚本与 CI 完全对齐：本地打出来的和 GitHub Actions 出的 artifact 是同一份东西。

## Windows（本地）

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish-windows.ps1
```

- 产物：`publish/win-x64/WarmAsBefore.exe`（`WindowsPackageType=None`，无 MSIX、无需证书）。
- 命令与 `.github/workflows/build-win-x64.yml` 一致：
  `dotnet publish -c Release -f net10.0-windows10.0.19041.0 -r win-x64 --self-contained false -p:UseMonoRuntime=false -p:RuntimeIdentifierOverride=win-x64`
- 脚本末尾自带「互不夹带」核对：Windows 产物里出现 `proot` / `android` / `aarch64` 就报错退出。

## Android（APK / AAB）

```bash
# 需要 .NET 10 SDK + Android SDK（ANDROID_HOME 或 ANDROID_SDK_ROOT）
bash scripts/publish-android.sh          # APK
bash scripts/publish-android.sh aab      # AAB
```

- 产物：`publish/android/*.apk`（或 `*.aab`）。
- 本机没有 Android SDK：脚本会直接说明并退出，不出半截包。要出包走两种之一：
  1. 装 SDK（Android SDK Build-Tools + Platform，`dotnet workload install maui-android`）；
  2. 推 master 让 `.github/workflows/build-android.yml` 出 APK artifact。
- 默认 APK；上架渠道用 `aab`。

## 发布节奏

- 推 `master` → CI 出 X64 artifact（不建 Release）。
- 公开 Release 只在 `v*` tag 或手动 dispatch 时出，且需版本号与 `RELEASE_NOTES`。

## 互不夹带核对（手工版）

```powershell
# Windows 产物不该带 Android 原生件
Get-ChildItem publish/win-x64 -Recurse -File | Where-Object { $_.Name -match "proot|aarch64" }
```

```bash
# Android 产物不该带 Windows 专有依赖（System.Drawing / Speech）
unzip -l publish/android/*.apk | grep -Ei "system\.drawing|speech" || echo "干净"
```

条件编译已经按平台分开（`proot` 等 native / aarch64 只在 android 条件里，`System.Drawing` / `Speech` 只在 windows 条件里），这两条核对是用来确认它没有退化。
