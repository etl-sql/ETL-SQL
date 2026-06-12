using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ETL_SQL.Core.Common
{
    /// <summary>
    /// Machine-bound symmetric encryption.
    /// Windows: uses DPAPI <see cref="DataProtectionScope.LocalMachine"/> so data is
    /// unreadable if moved to another machine.
    /// Linux/macOS: derives a machine-unique AES-256 key from <c>/etc/machine-id</c>
    /// (or the hostname as a fallback), then encrypts with AES-256-CBC.
    /// </summary>
    internal static class MachineBoundCrypto
    {
        // AES IV is always 16 bytes; prepend it to every ciphertext on non-Windows.
        private const int AesIvLength = 16;

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

        // ── AES-256-CBC helpers ───────────────────────────────────────────────────

        private static byte[] AesEncrypt(byte[] data, byte[] key)
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.GenerateIV();

            using var ms = new MemoryStream();
            ms.Write(aes.IV, 0, aes.IV.Length);
            using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
            {
                cs.Write(data, 0, data.Length);
                cs.FlushFinalBlock();
            }
            return ms.ToArray();
        }

        private static byte[] AesDecrypt(byte[] data, byte[] key)
        {
            if (data.Length < AesIvLength)
                throw new CryptographicException("Ciphertext is too short to contain an IV.");

            var iv = new byte[AesIvLength];
            Array.Copy(data, 0, iv, 0, AesIvLength);

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;

            using var ms = new MemoryStream();
            using (var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Write))
            {
                cs.Write(data, AesIvLength, data.Length - AesIvLength);
                cs.FlushFinalBlock();
            }
            return ms.ToArray();
        }
    }
}
