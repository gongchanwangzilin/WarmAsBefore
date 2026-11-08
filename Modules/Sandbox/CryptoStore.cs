using System.Security.Cryptography;
using System.Text;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.Sandbox;

/// <summary>
/// 加密落盘存储：AES-GCM 加密 + HMAC-SHA256 防篡改。
/// 密钥由 <see cref="LocalSecretProtector"/> 统一负责：Windows 用 DPAPI 按当前用户保护，
/// 非 Windows 用「本设备随机生成」的 32 字节密钥（应用私有目录 + Unix 0600），
/// **不再从硬编码常量派生**——那种写法所有设备都是同一把钥匙，等于没加密。
///
/// 文件布局：{魔数 "WAB1"(4B) | nonce(12B) | tag(16B) | ciphertext} + 旁路 .hmac(32B)。
/// 任何字节被篡改 → HMAC 不匹配 → 返回 null 并记录日志。
/// 读取失败（损坏/截断/认证失败）一律返回 null，绝不把异常抛给调用方。
/// </summary>
public sealed class CryptoStore
{
    private static readonly byte[] MagicBytes = "WAB1"u8.ToArray();

    /// <summary>
    /// GCM 附加认证数据。加解密两侧必须使用**同一个固定值**：
    /// 此前加密侧传的是全零 tag、解密侧传的是文件里的真实 tag，两侧 AAD 不一致，
    /// 导致任何写入过的数据都解不开（恒定 AuthenticationTagMismatch）。
    /// </summary>
    private static readonly byte[] Aad = "WAB1::sandbox::v1"u8.ToArray();

    private readonly string _root;
    private byte[]? _masterKey;
    private byte[]? _hmacKey;

    // 惰性初始化：主密钥（含 Windows DPAPI 触碰）在首次真正加解密时才加载，
    // 不在构造时做 —— 避免 DI 注入 SandboxPolicy 时（如进设置页）同步触碰 DPAPI 卡死界面。
    private byte[] MasterKey => _masterKey ??= LoadOrCreateMasterKey();
    private byte[] HmacKey => _hmacKey ??= DeriveHmacKey(MasterKey);

    public CryptoStore(string root)
    {
        _root = root;
        try { Directory.CreateDirectory(Path.Combine(root, ".sandbox")); }
        catch (Exception ex) { App.WriteLog("CryptoStore.CreateDir -> " + ex.Message); }
    }

    private static byte[] LoadOrCreateMasterKey()
    {
        try
        {
            var keyDir = Path.Combine(FileSystem.AppDataDirectory, "WarmAsBefore", ".sandbox");
            Directory.CreateDirectory(keyDir);
            var keyPath = Path.Combine(keyDir, "master.key");

            if (TryLoadKey(keyPath, out var existing)) return existing;

            // 兼容早期 Windows 版本：主密钥曾写在 Roaming\AppData 下，同样由 DPAPI 保护。
            if (OperatingSystem.IsWindows())
            {
                var legacy = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "WarmAsBefore", ".sandbox", "master.key");
                if (!string.Equals(legacy, keyPath, StringComparison.OrdinalIgnoreCase)
                    && TryLoadKey(legacy, out existing))
                {
                    File.WriteAllBytes(keyPath, LocalSecretProtector.Protect(existing));
                    return existing;
                }
            }

            var fresh = new byte[32];
            RandomNumberGenerator.Fill(fresh);
            File.WriteAllBytes(keyPath, LocalSecretProtector.Protect(fresh));
            return fresh;
        }
        catch (Exception ex)
        {
            // 不再退回「常量派生密钥」：那等于给所有设备发同一把钥匙。
            // 这里直接抛出，让上层按「加密存储不可用」降级，比静默弱加密安全。
            App.WriteLog("CryptoStore.LoadMasterKey -> " + ex);
            throw;
        }
    }

    private static bool TryLoadKey(string path, out byte[] key)
    {
        key = Array.Empty<byte>();
        try
        {
            if (!File.Exists(path)) return false;
            var plain = LocalSecretProtector.Unprotect(File.ReadAllBytes(path));
            if (plain is not { Length: 32 }) return false;
            key = plain;
            return true;
        }
        catch (Exception ex)
        {
            App.WriteLog("CryptoStore.TryLoadKey -> " + ex.Message);
            return false;
        }
    }

    private static byte[] Pbkdf2(byte[] password, byte[] salt, int iterations, int outputLen)
        => Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, outputLen);

    private static byte[] DeriveHmacKey(byte[] master)
        => Pbkdf2(master, Encoding.UTF8.GetBytes("hmac-key-salt"), 10_000, 32);

    /// <summary>文件名只允许是名字本身，不能带目录/盘符/上级跳转——写盘边界由这里把死。</summary>
    private static bool IsSafeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        if (fileName.Contains(':') || Path.IsPathRooted(fileName)) return false;
        return !fileName.Contains('/') && !fileName.Contains('\\') && fileName is not ("." or "..");
    }

    public string EncryptAndStore(string fileName, string plaintext)
    {
        if (!IsSafeFileName(fileName))
            throw new ArgumentException("加密存储的文件名不合法：" + fileName, nameof(fileName));

        var dir = Path.Combine(_root, ".sandbox");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);

        var data = Encoding.UTF8.GetBytes(plaintext);
        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);
        var ct = new byte[data.Length];
        var tag = new byte[16];

        using (var gcm = new AesGcm(MasterKey, 16))
            gcm.Encrypt(nonce, data, ct, tag, Aad);

        var payload = new byte[4 + nonce.Length + tag.Length + ct.Length];
        MagicBytes.CopyTo(payload, 0);
        nonce.CopyTo(payload, 4);
        tag.CopyTo(payload, 16);
        ct.CopyTo(payload, 32);

        // 先写临时文件再替换，避免中途崩溃留下半个密文文件（读取端会当作没有这份数据）。
        var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            File.WriteAllBytes(tmp, payload);
            File.WriteAllBytes(tmp + ".hmac", ComputeHmac(payload));
            File.Move(tmp, path, overwrite: true);
            File.Move(tmp + ".hmac", path + ".hmac", overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); }
            catch (Exception ex) { App.WriteLog("CryptoStore.CleanTmp -> " + ex.Message); }
            try { if (File.Exists(tmp + ".hmac")) File.Delete(tmp + ".hmac"); }
            catch (Exception ex) { App.WriteLog("CryptoStore.CleanTmpHmac -> " + ex.Message); }
        }
        return path;
    }

    /// <summary>读取并解密；文件不存在、被篡改、被截断或认证失败都返回 null，并记录原因。</summary>
    public string? TryRead(string fileName)
    {
        try
        {
            if (!IsSafeFileName(fileName))
            {
                App.WriteLog("CryptoStore: 非法文件名 " + fileName);
                return null;
            }
            var path = Path.Combine(_root, ".sandbox", fileName);
            var hmacPath = path + ".hmac";
            if (!File.Exists(path) || !File.Exists(hmacPath)) return null;

            var stored = File.ReadAllBytes(path);
            var storedHmac = File.ReadAllBytes(hmacPath);

            // 先验结构再动密码学：截断/外部写入的文件不该走到 BlockCopy 才炸。
            if (stored.Length < 32 || !stored.AsSpan(0, 4).SequenceEqual(MagicBytes))
            {
                App.WriteLog("CryptoStore: 文件结构不完整 " + fileName);
                return null;
            }
            if (storedHmac.Length != 32)
            {
                App.WriteLog("CryptoStore: HMAC 长度异常 " + fileName);
                return null;
            }

            var expectedHmac = ComputeHmac(stored);
            if (!CryptographicOperations.FixedTimeEquals(storedHmac, expectedHmac))
            {
                App.WriteLog("CryptoStore: HMAC mismatch for " + fileName + " (tampering detected)");
                return null;
            }

            var nonce = new byte[12];
            var tag = new byte[16];
            Buffer.BlockCopy(stored, 4, nonce, 0, 12);
            Buffer.BlockCopy(stored, 16, tag, 0, 16);
            var ct = stored.AsSpan(32);

            var pt = new byte[ct.Length];
            using (var gcm = new AesGcm(MasterKey, 16))
                gcm.Decrypt(nonce, ct, pt, tag, Aad);
            return Encoding.UTF8.GetString(pt);
        }
        catch (CryptographicException ex)
        {
            App.WriteLog("CryptoStore.TryRead(" + fileName + ") 认证失败 -> " + ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            App.WriteLog("CryptoStore.TryRead(" + fileName + ") -> " + ex);
            return null;
        }
    }

    private byte[] ComputeHmac(byte[] payload)
    {
        using var h = new HMACSHA256(HmacKey);
        return h.ComputeHash(payload);
    }
}
