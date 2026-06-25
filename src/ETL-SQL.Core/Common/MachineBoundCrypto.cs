using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ETL_SQL.Core.Common;
/// <summary>
/// Machine-bound symmetric encryption.
/// Windows: uses DPAPI <see cref="DataProtectionScope.LocalMachine"/> so data is
/// unreadable if moved to another machine.
/// Linux/macOS: derives a machine-unique AES-256 key from <c>/etc/machine-id</c>
/// (or the hostname as a fallback), then encrypts with authenticated AES-256-GCM.
/// </summary>
internal static class MachineBoundCrypto
{
    private static readonly byte[] MagicV2 = Encoding.ASCII.GetBytes("ETLSQLM2");
    private const int AesIvLength = 16;
    private const int AesGcmNonceLength = 12;
    private const int AesGcmTagLength = 16;

    // ── Public API ────────────────────────────────────────────────────────────

    public static void EncryptFile(string inputPath, string outputPath)
    {
        var plaintext = File.ReadAllBytes(inputPath);
        var ciphertext = Protect(plaintext);
        File.WriteAllBytes(outputPath, ciphertext);
    }

    public static void DecryptFile(string inputPath, string outputPath)
    {
        var ciphertext = File.ReadAllBytes(inputPath);
        var plaintext = Unprotect(ciphertext);
        File.WriteAllBytes(outputPath, plaintext);
    }

    // ── Core protect/unprotect ────────────────────────────────────────────────

    internal static byte[] Protect(byte[] data)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ProtectedData.Protect(data, null, DataProtectionScope.LocalMachine);

        return AesEncrypt(data, GetMachineKey());
    }

    internal static byte[] Unprotect(byte[] data)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ProtectedData.Unprotect(data, null, DataProtectionScope.LocalMachine);

        return AesDecrypt(data, GetMachineKey());
    }

    // ── Non-Windows key derivation ────────────────────────────────────────────

    private static byte[] GetMachineKey()
    {
        var machineSecret = ReadMachineSecret();
        // HKDF-SHA256: IKM = machine secret, no salt, label = context string
        return HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes(machineSecret),
            32,
            info: "etl-sql-machine-bound"u8.ToArray());
    }

    private static string ReadMachineSecret()
    {
        // Linux: systemd machine-id is stable across reboots, unique per installation
        if (File.Exists("/etc/machine-id"))
            return File.ReadAllText("/etc/machine-id").Trim();

        // macOS / containers without machine-id: fall back to hostname
        return Environment.MachineName;
    }

    // ── AES-256-GCM helpers ───────────────────────────────────────────────────

    private static byte[] AesEncrypt(byte[] data, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(AesGcmNonceLength);
        var ciphertext = new byte[data.Length];
        var tag = new byte[AesGcmTagLength];
        using (var aes = new AesGcm(key, AesGcmTagLength))
        {
            aes.Encrypt(nonce, data, ciphertext, tag);
        }

        using var ms = new MemoryStream(MagicV2.Length + AesGcmNonceLength + AesGcmTagLength + ciphertext.Length);
        ms.Write(MagicV2, 0, MagicV2.Length);
        ms.Write(nonce, 0, nonce.Length);
        ms.Write(tag, 0, tag.Length);
        ms.Write(ciphertext, 0, ciphertext.Length);
        return ms.ToArray();
    }

    private static byte[] AesDecrypt(byte[] data, byte[] key)
    {
        if (HasMagicV2(data))
        {
            if (data.Length < MagicV2.Length + AesGcmNonceLength + AesGcmTagLength)
                throw new CryptographicException("Ciphertext is too short to contain authenticated payload metadata.");

            var offset = MagicV2.Length;
            var nonce = data.AsSpan(offset, AesGcmNonceLength).ToArray();
            offset += AesGcmNonceLength;
            var tag = data.AsSpan(offset, AesGcmTagLength).ToArray();
            offset += AesGcmTagLength;
            var ciphertext = data.AsSpan(offset).ToArray();
            var plaintext = new byte[ciphertext.Length];
            using (var aes = new AesGcm(key, AesGcmTagLength))
            {
                aes.Decrypt(nonce, ciphertext, tag, plaintext);
            }

            return plaintext;
        }

        // Legacy non-Windows payloads used AES-CBC with the IV prepended. Keep read compatibility.
        if (data.Length < AesIvLength)
            throw new CryptographicException("Ciphertext is too short to contain an IV.");

        var iv = new byte[AesIvLength];
        Array.Copy(data, 0, iv, 0, AesIvLength);

        using var legacyAes = Aes.Create();
        legacyAes.Key = key;
        legacyAes.IV = iv;

        using var ms = new MemoryStream();
        using (var cs = new CryptoStream(ms, legacyAes.CreateDecryptor(), CryptoStreamMode.Write))
        {
            cs.Write(data, AesIvLength, data.Length - AesIvLength);
            cs.FlushFinalBlock();
        }
        return ms.ToArray();
    }

    private static bool HasMagicV2(byte[] data) =>
        data.Length >= MagicV2.Length && data.AsSpan(0, MagicV2.Length).SequenceEqual(MagicV2);
}
