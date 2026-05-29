using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ETL_SQL.Core.Common.Exceptions;
using PgpCore;

namespace ETL_SQL.Common
{
    /// <summary>
    /// Utility class for encryption and decryption of strings and files.
    /// Uses AES-256 with PBKDF2 key derivation or RSA (SSH) key pairs.
    /// </summary>
    public static class CryptoUtils
    {
        private const int KeySize = 256;
        private const int Iterations = 600000;
        private const int SaltSize = 16;
        private const int IvSize = 16;
        private const byte CURRENT_VERSION = 1;

        /// <summary>
        /// Encrypts a string using the specified password and optional algorithm.
        /// </summary>
        public static string Encrypt(string plainText, string password, HashAlgorithmName? algo = null)
        {
            if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("Password cannot be null or empty.", nameof(password));
            var hashAlgo = algo ?? HashAlgorithmName.SHA256;
            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, hashAlgo, KeySize / 8);

            using var aes = Aes.Create();
            aes.Key = key;
            aes.GenerateIV();
            byte[] iv = aes.IV;

            using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream();
            ms.WriteByte(CURRENT_VERSION);
            ms.Write(salt, 0, SaltSize);
            ms.Write(iv, 0, IvSize);

            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            using (var sw = new StreamWriter(cs, Encoding.UTF8))
            {
                sw.Write(plainText);
            }

            return "ENC:" + Convert.ToBase64String(ms.ToArray());
        }

        /// <summary>
        /// Decrypts a string that was encrypted using <see cref="Encrypt"/>.
        /// </summary>
        public static string Decrypt(string cipherText, string password, HashAlgorithmName? algo = null)
        {
            if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("Password cannot be null or empty.", nameof(password));
            if (string.IsNullOrEmpty(cipherText)) return cipherText;
            if (!cipherText.StartsWith("ENC:")) return cipherText;

            try
            {
                var hashAlgo = algo ?? HashAlgorithmName.SHA256;
                byte[] fullBytes = Convert.FromBase64String(cipherText.Substring(4));

                int offset = 0;
                int iterations = Iterations;
                int keySize = KeySize;

                // Check for version header
                if (fullBytes.Length > 0 && fullBytes[0] == 1)
                {
                    offset = 1;
                    // In the future, we can change iterations based on fullBytes[0]
                }

                if (fullBytes.Length < offset + SaltSize + IvSize)
                    throw new ExecutionException("Invalid encrypted connection string format.");

                byte[] salt = new byte[SaltSize];
                byte[] iv = new byte[IvSize];
                byte[] encrypted = new byte[fullBytes.Length - offset - SaltSize - IvSize];

                Buffer.BlockCopy(fullBytes, offset, salt, 0, SaltSize);
                Buffer.BlockCopy(fullBytes, offset + SaltSize, iv, 0, IvSize);
                Buffer.BlockCopy(fullBytes, offset + SaltSize + IvSize, encrypted, 0, encrypted.Length);

                byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, hashAlgo, keySize / 8);

                using var aes = Aes.Create();
                using var decryptor = aes.CreateDecryptor(key, iv);
                using var ms = new MemoryStream(encrypted);
                using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
                using var sr = new StreamReader(cs, Encoding.UTF8);

                return sr.ReadToEnd();
            }
            catch (CryptographicException)
            {
                throw new ExecutionException("Failed to decrypt: Invalid password or corrupted data.");
            }
            catch (Exception ex) when (!(ex is ExecutionException))
            {
                throw new ExecutionException($"Decryption error: {ex.Message}");
            }
        }
        /// <summary>
        /// Encrypts a file on disk using a password.
        /// </summary>
        public static void EncryptFile(string inputFile, string outputFile, string password, bool overwrite, HashAlgorithmName? algo = null)
        {
            if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("Password cannot be null or empty.", nameof(password));
            if (File.Exists(outputFile) && !overwrite)
                throw new ExecutionException($"Destination file already exists and OVERWRITE is OFF: {outputFile}");

            var hashAlgo = algo ?? HashAlgorithmName.SHA256;
            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, hashAlgo, KeySize / 8);

            using var aes = Aes.Create();
            aes.Key = key;
            aes.GenerateIV();
            byte[] iv = aes.IV;

            using (var fsOut = new FileStream(outputFile, FileMode.Create))
            {
                fsOut.Write(salt, 0, SaltSize);
                fsOut.Write(iv, 0, IvSize);

                using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
                using (var cs = new CryptoStream(fsOut, encryptor, CryptoStreamMode.Write))
                using (var fsIn = new FileStream(inputFile, FileMode.Open))
                {
                    fsIn.CopyTo(cs);
                }
            }
        }

        /// <summary>
        /// Decrypts a file on disk using a password.
        /// </summary>
        public static void DecryptFile(string inputFile, string outputFile, string password, bool overwrite, HashAlgorithmName? algo = null)
        {
            if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("Password cannot be null or empty.", nameof(password));
            if (File.Exists(outputFile) && !overwrite)
                throw new ExecutionException($"Destination file already exists and OVERWRITE is OFF: {outputFile}");

            var hashAlgo = algo ?? HashAlgorithmName.SHA256;
            using (var fsIn = new FileStream(inputFile, FileMode.Open))
            {
                byte[] salt = new byte[SaltSize];
                byte[] iv = new byte[IvSize];
                fsIn.ReadExactly(salt, 0, SaltSize);
                fsIn.ReadExactly(iv, 0, IvSize);

                byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, hashAlgo, KeySize / 8);

                using var aes = Aes.Create();
                using var decryptor = aes.CreateDecryptor(key, iv);
                using (var cs = new CryptoStream(fsIn, decryptor, CryptoStreamMode.Read))
                using (var fsOut = new FileStream(outputFile, FileMode.Create))
                {
                    cs.CopyTo(fsOut);
                }
            }
        }

        /// <summary>
        /// Encrypts a file using an SSH (RSA) public key.
        /// </summary>
        public static void EncryptFileWithSsh(string inputFile, string outputFile, string keyFile, bool overwrite)
        {
            if (File.Exists(outputFile) && !overwrite)
                throw new ExecutionException($"Destination file already exists and OVERWRITE is OFF: {outputFile}");

            string pem = File.ReadAllText(keyFile);
            using var rsa = RSA.Create();
            rsa.ImportFromPem(pem);

            byte[] aesKey = RandomNumberGenerator.GetBytes(KeySize / 8);
            byte[] encryptedKey = rsa.Encrypt(aesKey, RSAEncryptionPadding.OaepSHA256);

            using var aes = Aes.Create();
            aes.Key = aesKey;
            aes.GenerateIV();
            byte[] iv = aes.IV;

            using (var fsOut = new FileStream(outputFile, FileMode.Create))
            {
                // Format: [EncKeyLength(4)] [EncryptedKey] [IV(16)] [Data...]
                fsOut.Write(BitConverter.GetBytes(encryptedKey.Length), 0, 4);
                fsOut.Write(encryptedKey, 0, encryptedKey.Length);
                fsOut.Write(iv, 0, IvSize);

                using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
                using (var cs = new CryptoStream(fsOut, encryptor, CryptoStreamMode.Write))
                using (var fsIn = new FileStream(inputFile, FileMode.Open))
                {
                    fsIn.CopyTo(cs);
                }
            }
        }

        /// <summary>
        /// Decrypts a file using an SSH (RSA) private key.
        /// </summary>
        public static void DecryptFileWithSsh(string inputFile, string outputFile, string keyFile, bool overwrite, string? passphrase = null)
        {
            if (File.Exists(outputFile) && !overwrite)
                throw new ExecutionException($"Destination file already exists and OVERWRITE is OFF: {outputFile}");

            string pem = File.ReadAllText(keyFile);
            using var rsa = RSA.Create();
            if (string.IsNullOrEmpty(passphrase)) rsa.ImportFromPem(pem);
            else rsa.ImportFromEncryptedPem(pem, passphrase);

            using (var fsIn = new FileStream(inputFile, FileMode.Open))
            {
                byte[] lenBytes = new byte[4];
                fsIn.ReadExactly(lenBytes, 0, 4);
                int keyLen = BitConverter.ToInt32(lenBytes, 0);

                byte[] encryptedKey = new byte[keyLen];
                fsIn.ReadExactly(encryptedKey, 0, keyLen);

                byte[] iv = new byte[IvSize];
                fsIn.ReadExactly(iv, 0, IvSize);

                byte[] aesKey = rsa.Decrypt(encryptedKey, RSAEncryptionPadding.OaepSHA256);

                using var aes = Aes.Create();
                using var decryptor = aes.CreateDecryptor(aesKey, iv);
                using (var cs = new CryptoStream(fsIn, decryptor, CryptoStreamMode.Read))
                using (var fsOut = new FileStream(outputFile, FileMode.Create))
                {
                    cs.CopyTo(fsOut);
                }
            }
        }

        /// <summary>
        /// Highly secure, zero-password encryption bound to the current OS user and machine (DPAPI).
        /// Used primarily for session state and metadata hardening.
        /// </summary>
        public static string Protect(string plainText, string? optionalEntropy = null)
        {
            if (string.IsNullOrEmpty(plainText)) return plainText;
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return "DPAPI:" + Convert.ToBase64String(ProtectWindows(plainBytes, optionalEntropy));
            }

            return "MACHINE:" + Convert.ToBase64String(ProtectGeneric(plainBytes, optionalEntropy));
        }

        /// <summary>
        /// Decrypts data that was protected using <see cref="Protect"/>.
        /// Throws if attempted on a different machine or by a different OS user.
        /// </summary>
        public static string Unprotect(string cipherText, string? optionalEntropy = null)
        {
            if (string.IsNullOrEmpty(cipherText)) return cipherText;

            if (cipherText.StartsWith("DPAPI:"))
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    throw new ExecutionException("DPAPI encrypted data can only be decrypted on Windows.");

                byte[] cipherBytes = Convert.FromBase64String(cipherText.Substring(6));
                return Encoding.UTF8.GetString(UnprotectWindows(cipherBytes, optionalEntropy));
            }

            if (cipherText.StartsWith("MACHINE:"))
            {
                byte[] cipherBytes = Convert.FromBase64String(cipherText.Substring(8));
                return Encoding.UTF8.GetString(UnprotectGeneric(cipherBytes, optionalEntropy));
            }

            return cipherText;
        }

        [SupportedOSPlatform("windows")]
        private static byte[] ProtectWindows(byte[] plainBytes, string? entropy)
        {
            byte[]? entropyBytes = entropy != null ? Encoding.UTF8.GetBytes(entropy) : null;
            return ProtectedData.Protect(plainBytes, entropyBytes, DataProtectionScope.CurrentUser);
        }

        [SupportedOSPlatform("windows")]
        private static byte[] UnprotectWindows(byte[] cipherBytes, string? entropy)
        {
            byte[]? entropyBytes = entropy != null ? Encoding.UTF8.GetBytes(entropy) : null;
            return ProtectedData.Unprotect(cipherBytes, entropyBytes, DataProtectionScope.CurrentUser);
        }

        private static byte[] ProtectGeneric(byte[] plainBytes, string? entropy)
        {
            byte[] key = GetMachineKey(entropy);
            using var aes = Aes.Create();
            aes.Key = key;
            aes.GenerateIV();
            
            using var encryptor = aes.CreateEncryptor();
            using var ms = new MemoryStream();
            ms.Write(aes.IV, 0, aes.IV.Length); // Prepend IV
            
            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            {
                cs.Write(plainBytes, 0, plainBytes.Length);
            }
            return ms.ToArray();
        }

        private static byte[] UnprotectGeneric(byte[] cipherBytes, string? entropy)
        {
            byte[] key = GetMachineKey(entropy);
            using var aes = Aes.Create();
            aes.Key = key;
            
            byte[] iv = new byte[aes.BlockSize / 8];
            Buffer.BlockCopy(cipherBytes, 0, iv, 0, iv.Length);
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            using var msOut = new MemoryStream();
            using (var msIn = new MemoryStream(cipherBytes, iv.Length, cipherBytes.Length - iv.Length))
            using (var cs = new CryptoStream(msIn, decryptor, CryptoStreamMode.Read))
            {
                cs.CopyTo(msOut);
            }
            return msOut.ToArray();
        }

        private static byte[] GetMachineKey(string? entropy)
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string etlSqlDir = Path.Combine(appData, "etl-sql");
            if (!Directory.Exists(etlSqlDir)) Directory.CreateDirectory(etlSqlDir);
            
            string keyPath = Path.Combine(etlSqlDir, "machine.key");
            byte[] baseKey;

            if (File.Exists(keyPath))
            {
                baseKey = File.ReadAllBytes(keyPath);
            }
            else
            {
                baseKey = RandomNumberGenerator.GetBytes(32);
                File.WriteAllBytes(keyPath, baseKey);
            }

            if (entropy == null) return baseKey;

            // Mix entropy into the key to make it user-bound if entropy is provided
            return Rfc2898DeriveBytes.Pbkdf2(baseKey, Encoding.UTF8.GetBytes(entropy), 1000, HashAlgorithmName.SHA256, 32);
        }
        /// <summary>
        /// Encrypts a file using a PGP public key.
        /// </summary>
        public static async Task EncryptFileWithPgp(string inputFile, string outputFile, string keyFile, bool overwrite)
        {
            if (File.Exists(outputFile) && !overwrite)
                throw new ExecutionException($"Destination file already exists and OVERWRITE is OFF: {outputFile}");

            var keys = new EncryptionKeys(new FileInfo(keyFile));
            using var pgp = new PGP(keys);
            await pgp.EncryptFileAsync(new FileInfo(inputFile), new FileInfo(outputFile));
        }

        /// <summary>
        /// Decrypts a file using a PGP private key.
        /// </summary>
        public static async Task DecryptFileWithPgp(string inputFile, string outputFile, string keyFile, string? passphrase, bool overwrite)
        {
            if (File.Exists(outputFile) && !overwrite)
                throw new ExecutionException($"Destination file already exists and OVERWRITE is OFF: {outputFile}");

            EncryptionKeys keys = new EncryptionKeys(new FileInfo(keyFile), passphrase ?? string.Empty);
            using var pgp = new PGP(keys);
            await pgp.DecryptFileAsync(new FileInfo(inputFile), new FileInfo(outputFile));
        }

        /// <summary>
        /// Validates a file's lock status (waiting for lock to clear) and expected hash integrity.
        /// </summary>
        public static void ValidateFileAccess(string filePath, Dictionary<string, string>? options, ETL_SQL.Core.IExecutionContext context)
        {
            if (options == null) return;
            if (!File.Exists(filePath)) return;

            // 1. Wait for lock
            if (options.TryGetValue("WAIT_FOR_LOCK", out var wfl) && 
                (wfl.Equals("ON", StringComparison.OrdinalIgnoreCase) || wfl.Equals("TRUE", StringComparison.OrdinalIgnoreCase)))
            {
                int timeoutSec = 30;
                if (options.TryGetValue("LOCK_TIMEOUT_SEC", out var lts) && int.TryParse(lts, out var ltsv))
                {
                    timeoutSec = ltsv;
                }

                var start = DateTime.UtcNow;
                var timeout = TimeSpan.FromSeconds(timeoutSec);
                bool success = false;
                while (DateTime.UtcNow - start < timeout)
                {
                    if (File.Exists(filePath))
                    {
                        try
                        {
                            using (var fs = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                            {
                                success = true;
                                break;
                            }
                        }
                        catch (IOException)
                        {
                            // locked
                        }
                    }
                    System.Threading.Thread.Sleep(200);
                }

                if (!success && !File.Exists(filePath))
                {
                    throw new ExecutionException($"Timeout waiting for file to arrive and unlock: {filePath}");
                }
            }

            // 2. Hash validation
            if (options.TryGetValue("EXPECTED_HASH", out var eh) && !string.IsNullOrEmpty(eh))
            {
                string expectedHash = eh.Trim('\'', '\"').ToLowerInvariant();
                string algo = "SHA256";
                if (options.TryGetValue("ALGORITHM", out var a))
                {
                    algo = a.ToUpperInvariant();
                }

                using var stream = File.OpenRead(filePath);
                byte[] hashBytes;
                if (algo == "MD5")
                {
                    using var hasher = MD5.Create();
                    hashBytes = hasher.ComputeHash(stream);
                }
                else if (algo == "SHA1" || algo == "SHA-1")
                {
                    using var hasher = SHA1.Create();
                    hashBytes = hasher.ComputeHash(stream);
                }
                else if (algo == "SHA256" || algo == "SHA-256" || algo == "SHA2_256")
                {
                    using var hasher = SHA256.Create();
                    hashBytes = hasher.ComputeHash(stream);
                }
                else if (algo == "SHA512" || algo == "SHA-512" || algo == "SHA2_512")
                {
                    using var hasher = SHA512.Create();
                    hashBytes = hasher.ComputeHash(stream);
                }
                else
                {
                    throw new ExecutionException($"Unsupported hash algorithm: {algo}. Supported: MD5, SHA1, SHA256, SHA512.");
                }

                string actualHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
                if (actualHash != expectedHash)
                {
                    throw new ExecutionException($"File integrity check failed: Expected hash '{expectedHash}' but got '{actualHash}' for file '{filePath}'.");
                }
            }
        }
    }
}
