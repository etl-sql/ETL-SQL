using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Common
{
    /// <summary>
    /// Utility class for encryption and decryption of strings and files.
    /// Uses AES-256 with PBKDF2 key derivation.
    /// </summary>
    public static class CryptoUtils
    {
        private const int KeySize = 256;
        private const int Iterations = 10000;
        private const int SaltSize = 16;
        private const int IvSize = 16;

        /// <summary>
        /// Encrypts a string using the specified password.
        /// </summary>
        /// <param name="plainText">The text to encrypt.</param>
        /// <param name="password">The password used for key derivation.</param>
        /// <returns>A base64-encoded string prefixed with "ENC:".</returns>
        public static string Encrypt(string plainText, string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize / 8);

            using var aes = Aes.Create();
            aes.Key = key;
            aes.GenerateIV();
            byte[] iv = aes.IV;

            using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream();
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
        /// <param name="cipherText">The encrypted text (must start with "ENC:").</param>
        /// <param name="password">The password used for key derivation.</param>
        /// <returns>The decrypted plaintext string.</returns>
        public static string Decrypt(string cipherText, string password)
        {
            if (string.IsNullOrEmpty(cipherText)) return cipherText;
            if (!cipherText.StartsWith("ENC:")) return cipherText;

            byte[] fullBytes = Convert.FromBase64String(cipherText.Substring(4));
            
            if (fullBytes.Length < SaltSize + IvSize)
                throw new ExecutionException("Invalid encrypted connection string format.");

            byte[] salt = new byte[SaltSize];
            byte[] iv = new byte[IvSize];
            byte[] encrypted = new byte[fullBytes.Length - SaltSize - IvSize];

            Buffer.BlockCopy(fullBytes, 0, salt, 0, SaltSize);
            Buffer.BlockCopy(fullBytes, SaltSize, iv, 0, IvSize);
            Buffer.BlockCopy(fullBytes, SaltSize + IvSize, encrypted, 0, encrypted.Length);

            byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize / 8);

            using var aes = Aes.Create();
            using var decryptor = aes.CreateDecryptor(key, iv);
            using var ms = new MemoryStream(encrypted);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs, Encoding.UTF8);
            
            return sr.ReadToEnd();
        }
        /// <summary>
        /// Encrypts a file on disk.
        /// </summary>
        /// <param name="inputFile">The path to the source file.</param>
        /// <param name="outputFile">The path to the destination encrypted file.</param>
        /// <param name="password">The password used for key derivation.</param>
        public static void EncryptFile(string inputFile, string outputFile, string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize / 8);

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
        /// Decrypts a file that was encrypted using <see cref="EncryptFile"/>.
        /// </summary>
        /// <param name="inputFile">The path to the encrypted source file.</param>
        /// <param name="outputFile">The path to the destination decrypted file.</param>
        /// <param name="password">The password used for key derivation.</param>
        public static void DecryptFile(string inputFile, string outputFile, string password)
        {
            using (var fsIn = new FileStream(inputFile, FileMode.Open))
            {
                byte[] salt = new byte[SaltSize];
                byte[] iv = new byte[IvSize];
                fsIn.ReadExactly(salt, 0, SaltSize);
                fsIn.ReadExactly(iv, 0, IvSize);

                byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize / 8);

                using var aes = Aes.Create();
                using var decryptor = aes.CreateDecryptor(key, iv);
                using (var cs = new CryptoStream(fsIn, decryptor, CryptoStreamMode.Read))
                using (var fsOut = new FileStream(outputFile, FileMode.Create))
                {
                    cs.CopyTo(fsOut);
                }
            }
        }
    }
}
