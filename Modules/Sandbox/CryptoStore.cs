using System.Security.Cryptography;
using System.Text;

namespace WarmAsBefore.Modules.Sandbox;

/// <summary>
/// 加密落盘存储：AES-GCM 加密 + HMAC-SHA256 防篡改。
/// 密钥平台相关：Windows 用 DPAPI 保护主密钥；非 Windows 用 PBKDF2 从应用标识派生。
/// 文件布局：{魔数 "WAB1"(4B) | nonce(12B) | tag(16B) | ciphertext} + 旁路 .hmac(32B)。
/// 任何字节被篡改 → HMAC 不匹配 → 返回 null 并记录日志。
/// </summary>
public sealed class CryptoStore
{
    private const string Magic = "WAB1";
    private readonly byte[] _masterKey;
    private readonly byte[] _hmacKey;
    private readonly string _root;

    public CryptoStore(string root)
    {
        _root = root;
        Directory.CreateDirectory(Path.Combine(root, ".sandbox"));
        _masterKey = LoadOrCreateMasterKey();
        _hmacKey = DeriveHmacKey(_masterKey);
    }

    private static byte[] LoadOrCreateMasterKey()
    {
#if WINDOWS
        try
        {
            var keyDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WarmAsBefore", ".sandbox");
            Directory.CreateDirectory(keyDir);
            var keyPath = Path.Combine(keyDir, "master.key");
            if (File.Exists(keyPath))
            {
                var plain = WindowsDPAPI.Unprotect(File.ReadAllBytes(keyPath));
                if (plain is not null && plain.Length == 32) return plain;
            }
            var fresh = new byte[32];
            RandomNumberGenerator.Fill(fresh);
            File.WriteAllBytes(keyPath, WindowsDPAPI.Protect(fresh));
            return fresh;
        }
        catch (Exception ex)
        {
            App.WriteLog("CryptoStore.DPAPI -> " + ex.Message);
            return PBKDF2Derive();
        }
#else
        return PBKDF2Derive();
#endif
    }

    private static byte[] Pbkdf2(byte[] password, byte[] salt, int iterations, int outputLen)
    {
        // .NET 9 签名：Pbkdf2(password, salt, iterationCount, hashAlgorithmName, outputLength) → byte[]
        return Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, outputLen);
    }

    private static byte[] PBKDF2Derive()
    {
        var salt = Encoding.UTF8.GetBytes("WarmAsBefore::Sandbox::v1");
        return Pbkdf2(salt, salt, 100_000, 32);
    }

    private static byte[] DeriveHmacKey(byte[] master)
    {
        return Pbkdf2(master, Encoding.UTF8.GetBytes("hmac-key-salt"), 10_000, 32);
    }

    public string EncryptAndStore(string fileName, string plaintext)
    {
        var dir = Path.Combine(_root, ".sandbox");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);

        var data = Encoding.UTF8.GetBytes(plaintext);
        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);
        var ct = new byte[data.Length];
        var tag = new byte[16];

        using var gcm = new AesGcm(_masterKey, 16);
        gcm.Encrypt(nonce, data, ct, tag, tag);

        var payload = new byte[4 + nonce.Length + tag.Length + ct.Length];
        Encoding.ASCII.GetBytes(Magic).CopyTo(payload, 0);
        nonce.CopyTo(payload, 4);
        tag.CopyTo(payload, 16);
        ct.CopyTo(payload, 32);

        File.WriteAllBytes(path, payload);
        File.WriteAllBytes(path + ".hmac", ComputeHmac(payload));
        return path;
    }

    /// <summary>读取并解密；返回 null 表示文件不存在或防篡改校验失败。</summary>
    public string? TryRead(string fileName)
    {
        var path = Path.Combine(_root, ".sandbox", fileName);
        var hmacPath = path + ".hmac";
        if (!File.Exists(path) || !File.Exists(hmacPath)) return null;

        var stored = File.ReadAllBytes(path);
        var storedHmac = File.ReadAllBytes(hmacPath);
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
        using var gcm = new AesGcm(_masterKey, 16);
        gcm.Decrypt(nonce, ct, pt, tag, tag);
        return Encoding.UTF8.GetString(pt);
    }

    private byte[] ComputeHmac(byte[] payload)
    {
        using var h = new HMACSHA256(_hmacKey);
        return h.ComputeHash(payload);
    }

#if WINDOWS
    /// <summary>Windows DPAPI（crypt32）封装：主密钥按当前用户隔离。</summary>
    private static class WindowsDPAPI
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct DATA_BLOB
        {
            public uint cbData;
            public IntPtr lpData;
        }

        public static byte[] Protect(byte[] data)
        {
            var inputBlob = CreateBlob(data);
            try
            {
                var ok = CryptProtectData(ref inputBlob, null, IntPtr.Zero, IntPtr.Zero,
                    IntPtr.Zero, 0x01 /* CRYPTPROTECT_UI_FORBIDDEN */, out var outputBlob);
                if (!ok) throw new CryptographicException("CryptProtectData failed");
                return ExtractBlob(outputBlob, inputBlob);
            }
            finally { ReleaseBlob(inputBlob); }
        }

        public static byte[]? Unprotect(byte[] data)
        {
            var inputBlob = CreateBlob(data);
            try
            {
                var ok = CryptUnprotectData(ref inputBlob, out var description, IntPtr.Zero, IntPtr.Zero,
                    IntPtr.Zero, 0, out var outputBlob);
                if (!ok) return null;
                return ExtractBlob(outputBlob, inputBlob);
            }
            finally { ReleaseBlob(inputBlob); }
        }

        private static DATA_BLOB CreateBlob(byte[] data)
        {
            var ptr = System.Runtime.InteropServices.Marshal.AllocHGlobal(data.Length);
            System.Runtime.InteropServices.Marshal.Copy(data, 0, ptr, data.Length);
            return new DATA_BLOB { cbData = (uint)data.Length, lpData = ptr };
        }

        private static byte[] ExtractBlob(IntPtr outPtr, DATA_BLOB inputBlob)
        {
            var outBlob = System.Runtime.InteropServices.Marshal.PtrToStructure<DATA_BLOB>(outPtr);
            try
            {
                var result = new byte[outBlob.cbData];
                System.Runtime.InteropServices.Marshal.Copy(outBlob.lpData, result, 0, (int)outBlob.cbData);
                return result;
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FreeHGlobal(outBlob.lpData);
                System.Runtime.InteropServices.Marshal.FreeHGlobal(outPtr);
            }
        }

        private static void ReleaseBlob(DATA_BLOB blob)
        {
            if (blob.lpData != IntPtr.Zero) System.Runtime.InteropServices.Marshal.FreeHGlobal(blob.lpData);
        }

        [System.Runtime.InteropServices.DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CryptProtectData(
            ref DATA_BLOB pDataIn,
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string? szDataDescr,
            IntPtr pOptionalEntropy,
            IntPtr pvReserved,
            IntPtr pPromptStruct,
            int dwFlags,
            out IntPtr pDataOut);

        [System.Runtime.InteropServices.DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CryptUnprotectData(
            ref DATA_BLOB pDataIn,
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] out string szDataDescr,
            IntPtr pOptionalEntropy,
            IntPtr pvReserved,
            IntPtr pPromptStruct,
            int dwFlags,
            out IntPtr pDataOut);
    }
#endif
}
