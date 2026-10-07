# 安卓签名密钥（WarmAsBefore 专用）

本目录存放 v1.8 起所有安卓版本的统一签名密钥（本机）。**以后只用这一个，不要再生成新的。
明文密钥文件和密码明文不进 GitHub**（.gitignore 已排除整个 Signing/ 目录，README.md 除外）。

## 流程
1. **云端 CI 打包 + 签名**：GitHub Actions 用仓库 Secrets（`WARMASBEFORE_P12_B64` / `WARMASBEFORE_KEY_PASS` / `WARMASBEFORE_KEY_ALIAS`，见仓库 Settings → Secrets and variables → Actions）动态注入密钥，直接出**签名版** APK（`WarmAsBefore-vX-android-arm64-signed.apk`）。Secret 未配置时回退出未签名 APK，可装机测试。
2. **本地备份（不进任何仓库/云端）**：签名密钥文件（`Signing/warmasbefore-release.p12` + `keystore-info.txt`）及任意 7z 备份包**只存本机**（或 SD 卡等自有存储），不进 GitHub、不进 `dist/`、不进任何会同步到远端的位置。

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
- 别名：`warmasbefore`；库密码 = 私钥密码，见本机 keystore-info.txt
- SHA-256 指纹：58:5E:8E:09:7C:45:0A:25:4A:65:BB:ED:D9:3A:31:FA:99:CF:9F:4E:88:CA:39:76:FB:37:55:F5:36:96:54:06

> 明文 `.p12`、密码明文、任何加密备份包（含 7z）都不进 GitHub（`.gitignore` 已排除 `dist/` 与 `Signing/`，除本 README 外）。
> 密钥只放在本机 / 自有存储 + GitHub Secrets（加密变量），仓库保持公开。若需轮换密钥，重新生成全部文件并同步更新 Secrets 即可。
