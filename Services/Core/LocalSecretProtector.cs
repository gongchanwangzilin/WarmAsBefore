using System.Security.Cryptography;
using System.Text;

namespace WarmAsBefore.Services;

/// <summary>
/// 本机密钥/敏感文件的统一保护器。
///
/// 设计要点（对应审计 S3 / S5）：
///  · Windows：DPAPI（按当前用户），与 CryptoStore 既有做法一致；
///  · 非 Windows：AES-256-GCM，主密钥是**本设备随机生成**的 32 字节，
///    落在应用私有目录、Unix 权限 0600。绝不再用「常量派生」当密钥——
///    那种写法所有设备算出来都是同一把钥匙，等于没加密。
///  · 任何一步失败都返回 null / 抛异常由调用方决定降级，不静默退回明文密钥。
/// </summary>
public static class LocalSecretProtector
{
    private const string KeyFileName = "device.key";
    private static readonly object KeyGate = new();
    private static byte[]? _deviceKey;

    /// <summary>设备级主密钥（仅非 Windows 使用）。随机生成一次后持久化，绝不从常量派生。</summary>
    public static byte[] GetOrCreateDeviceKey()
    {
        lock (KeyGate)
        {
            if (_deviceKey is { Length: 32 }) return _deviceKey;

            var dir = Path.Combine(FileSystem.AppDataDirectory, "WarmAsBefore", ".sandbox");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, KeyFileName);

            if (File.Exists(path))
            {
                try
                {
                    var existing = File.ReadAllBytes(path);
                    if (existing.Length == 32)
                    {
                        _deviceKey = existing;
                        return _deviceKey;
                    }
                    App.WriteLog("LocalSecretProtector: device.key 长度异常，重新生成");
                }
                catch (Exception ex)
                {
                    App.WriteLog("LocalSecretProtector.ReadKey -> " + ex.Message);
                }
            }

            var fresh = new byte[32];
            RandomNumberGenerator.Fill(fresh);
            File.WriteAllBytes(path, fresh);
            TryRestrictToOwner(path);
            _deviceKey = fresh;
            return _deviceKey;
        }
    }

    /// <summary>加密一段字节。Windows 走 DPAPI；其他平台走 AES-GCM（设备密钥）。</summary>
    public static byte[] Protect(byte[] plain)
    {
#if WINDOWS
        return WindowsDpapi.Protect(plain);
#else
        return AesProtect(plain);
#endif
    }

    /// <summary>解密；数据被篡改、格式不对或密钥不可用都返回 null（由调用方按「没有这份数据」降级）。</summary>
    public static byte[]? Unprotect(byte[] protectedBytes)
    {
        try
        {
#if WINDOWS
            return WindowsDpapi.Unprotect(protectedBytes);
#else
            return AesUnprotect(protectedBytes);
#endif
        }
        catch (Exception ex)
        {
            App.WriteLog("LocalSecretProtector.Unprotect -> " + ex.Message);
            return null;
        }
    }

    private static byte[] AesProtect(byte[] plain)
    {
        var key = GetOrCreateDeviceKey();
        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);
        var ct = new byte[plain.Length];
        var tag = new byte[16];
        using (var gcm = new AesGcm(key, 16))
            gcm.Encrypt(nonce, plain, ct, tag, null);

        var payload = new byte[12 + 16 + ct.Length];
        nonce.CopyTo(payload, 0);
        tag.CopyTo(payload, 12);
        ct.CopyTo(payload, 28);
        return payload;
    }

    private static byte[]? AesUnprotect(byte[] protectedBytes)
    {
        if (protectedBytes.Length < 28) return null;
        var key = GetOrCreateDeviceKey();
        var nonce = protectedBytes.AsSpan(0, 12).ToArray();
        var tag = protectedBytes.AsSpan(12, 16).ToArray();
        var ct = protectedBytes.AsSpan(28);
        var pt = new byte[ct.Length];
        using (var gcm = new AesGcm(key, 16))
            gcm.Decrypt(nonce, ct, pt, tag, null);
        return pt;
    }

    /// <summary>Unix 下把密钥文件权限收到仅属主可读写；不支持的平台直接跳过。</summary>
    private static void TryRestrictToOwner(string path)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex)
        {
            App.WriteLog("LocalSecretProtector.Chmod -> " + ex.Message);
        }
    }

#if WINDOWS
    private static class WindowsDpapi
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct DATA_BLOB
        {
            public uint cbData;
            public IntPtr lpData;
        }

        public static byte[] Protect(byte[] data)
        {
            var input = CreateBlob(data);
            try
            {
                var ok = CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero,
                    IntPtr.Zero, 0x01 /* CRYPTPROTECT_UI_FORBIDDEN */, out var output);
                if (!ok) throw new CryptographicException("CryptProtectData failed");
                return ExtractBlob(output);
            }
            finally { ReleaseBlob(input); }
        }

        public static byte[]? Unprotect(byte[] data)
        {
            var input = CreateBlob(data);
            try
            {
                var ok = CryptUnprotectData(ref input, out _, IntPtr.Zero, IntPtr.Zero,
                    IntPtr.Zero, 0, out var output);
                if (!ok) return null;
                return ExtractBlob(output);
            }
            finally { ReleaseBlob(input); }
        }

        private static DATA_BLOB CreateBlob(byte[] data)
        {
            var ptr = System.Runtime.InteropServices.Marshal.AllocHGlobal(data.Length);
            System.Runtime.InteropServices.Marshal.Copy(data, 0, ptr, data.Length);
            return new DATA_BLOB { cbData = (uint)data.Length, lpData = ptr };
        }

        private static byte[] ExtractBlob(IntPtr outPtr)
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
                // crypt32 的返回缓冲是 LocalAlloc，必须 LocalFree（不能用 FreeHGlobal）
                if (outBlob.lpData != IntPtr.Zero) LocalFree(outBlob.lpData);
                if (outPtr != IntPtr.Zero) LocalFree(outPtr);
            }
        }

        private static void ReleaseBlob(DATA_BLOB blob)
        {
            if (blob.lpData != IntPtr.Zero) System.Runtime.InteropServices.Marshal.FreeHGlobal(blob.lpData);
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LocalFree(IntPtr hMem);

        [System.Runtime.InteropServices.DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CryptProtectData(
            ref DATA_BLOB pDataIn,
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string? szDataDescr,
            IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out IntPtr pDataOut);

        [System.Runtime.InteropServices.DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CryptUnprotectData(
            ref DATA_BLOB pDataIn,
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] out string szDataDescr,
            IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out IntPtr pDataOut);
    }
#endif
}
