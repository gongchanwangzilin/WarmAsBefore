# 安卓签名密钥（WarmAsBefore 专用）

本目录存放 v1.8 起所有安卓版本的统一签名密钥。**以后只用这一个，不要再生成新的。**

## 文件
- `warmasbefore-release.p12` — PKCS12 密钥库（Android 签名用，仅 4.4KB）
- `keystore-info.txt` — 别名 / 有效期限等信息（不含密码）

## 密钥信息
- 别名 (alias)：`warmasbefore`
- 密钥库密码 + 私钥密码：`@2012N10y26r`
- 算法：RSA 4096，有效期 25000 天（至 2094 年）

## 7z 加密备份包
`dist/warmasbefore-signing.7z`（密码同为 `@2012N10y26r`，创建时用了 `-mhe` 头加密）：

```
7z x -p'@2012N10y26r' dist/warmasbefore-signing.7z
```

## CI 签名用法
`.github/workflows/build-android.yml` 直接读本目录的明文 `.p12`（7z 包是给你手动备份用的，CI 不依赖它）：
```
dotnet publish ... -p:AndroidKeyStore=true \
  -p:AndroidKeyStorePassword='@2012N10y26r' \
  -p:AndroidSigningKeyStore=Signing/warmasbefore-release.p12 \
  -p:AndroidSigningKeyAlias=warmasbefore \
  -p:AndroidSigningKeyPass='@2012N10y26r'
```

> 密钥库小（4.4KB），直接随仓库分发；密码写在 CI 里。
> 仓库是公开的 = 密钥公开，任何拿到仓库的人都能给同包名的 APK 签名。
> 若日后需轮换：重新生成 Signing/ 全部文件 + dist/warmasbefore-signing.7z，并改 CI 密码。
