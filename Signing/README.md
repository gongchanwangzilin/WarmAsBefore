# 安卓签名密钥（WarmAsBefore 专用）

本目录存放 v1.8 起所有安卓版本的统一签名密钥。

## 文件
- `warmasbefore-release.p12` — PKCS12 密钥库（Android 签名用）
- `keystore-info.txt` — 别名 / 有效期限等信息（不含密码）

## 密码
- 密钥库 + 私钥密码：`@2012N10y26r`（与 7z 压缩包密码相同，请妥善保管）

## 7z 加密包
`warmasbefore-signing.7z` 位于本目录上一级的 `dist/` 里，密码同为 `@2012N10y26r`：

```
7z x -p@2012N10y26r warmasbefore-signing.7z
```

## 签名 CI 用法
GitHub Actions 里解压 7z 后，`dotnet publish` 加：
```
-p:AndroidKeyStore=true
-p:KeyStore=<绝对路径>/warmasbefore-release.p12
-p:KeyStorePassword='@2012N10y26r'
-p:Package signing 相关参数...
```
（具体参数见 .github/workflows/build-android.yml）

> 密码明文写在仓库 README 里是为了让 CI 能复现签名；真正的秘密是密码本身，
> 泄露此仓库即泄露密码。若需轮换，重新生成本目录全部文件即可。
