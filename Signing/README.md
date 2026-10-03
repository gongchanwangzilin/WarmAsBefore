# 安卓签名密钥（WarmAsBefore 专用）

本目录存放 v1.8 起所有安卓版本的统一签名密钥（本机）。**以后只用这一个，不要再生成新的。
明文密钥文件和密码明文不进 GitHub**（.gitignore 已排除整个 Signing/ 目录，README.md 除外）。

## 流程
1. **云端（GitHub）**：只保留 `dist/warmasbefore-signing.7z` 加密备份包（7z 密码头加密）。
2. **云端 CI 打包**：GitHub Actions 出**未签名** APK（`WarmAsBefore-vX-android-arm64.apk`），可直接装机测试。
3. **本地签名**：把 CI 出的未签名 APK 下下来，用本目录的 `warmasbefore-release.p12` 签名后再发布
   （密码见本机 `keystore-info.txt`，不入仓库）：

```
apksigner sign \
  --ks Signing/warmasbefore-release.p12 \
  --ks-pass pass:'<见本机 keystore-info.txt>' \
  --ks-key-alias warmasbefore \
  --key-pass pass:'<见本机 keystore-info.txt>' \
  --out WarmAsBefore-vX-android-arm64-signed.apk \
  WarmAsBefore-vX-android-arm64.apk
```

## 密钥信息（本机维护）
- 文件：`Signing/warmasbefore-release.p12`（PKCS12，4.4KB，RSA 4096，有效期至 2094 年）
- 别名：`warmasbefore`；库密码 = 私钥密码 = 7z 备份密码，见本机 keystore-info.txt
- SHA-256 指纹：58:5E:8E:09:7C:45:0A:25:4A:65:BB:ED:D9:3A:31:FA:99:CF:9F:4E:88:CA:39:76:FB:37:55:F5:36:96:54:06

## 云端 7z 加密备份包
`dist/warmasbefore-signing.7z` 是唯一的云端密钥备份（7z 密码头加密，密码见本机
keystore-info.txt）：
```
7z x -p'<见本机 keystore-info.txt>' dist/warmasbefore-signing.7z
```

> 明文 `.p12` 和任何密码明文不进 GitHub。云端 7z 包是备份，万一本地丢了可恢复。
> 仓库公开 = 密码公开，任何拿到 7z 包的人都能解出密钥；若需轮换，重新生成全部文件即可。
