using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Core.Common
{
    /// <summary>
    /// Represents the encryption configuration for a data source and provides
    /// helper methods to process files based on those settings.
    /// </summary>
    public class EncryptionOptions
    {
        public bool Enabled { get; }
        /// <summary>True when ENCRYPT = MACHINE; data is bound to this machine via DPAPI (Windows) or a machine-unique key (Linux/macOS).</summary>
        public bool IsMachineBound { get; }
        public HashAlgorithmName Algorithm { get; }
        public string? KeyFile { get; }
        public string? Passphrase { get; }
        public string Password { get; }

        public EncryptionOptions(Dictionary<string, string>? options)
        {
            Enabled = false;
            Algorithm = HashAlgorithmName.SHA256;
            Password = "DefaultETLPass123!";

            if (options == null) return;

            if (options.TryGetValue("ENCRYPT", out var enc))
            {
                var mode = enc.ToUpperInvariant();
                IsMachineBound = mode == "MACHINE";
                Enabled = mode is "ON" or "TRUE" or "MACHINE" or "PASSWORD" or "KEYFILE";
            }

            if (options.TryGetValue("ALGORITHM", out var algo))
            {
                Algorithm = algo.ToUpperInvariant() switch
                {
                    "MD5" => HashAlgorithmName.MD5,
                    "SHA1" => HashAlgorithmName.SHA1,
                    "SHA2_256" or "SHA256" => HashAlgorithmName.SHA256,
                    "SHA2_512" or "SHA512" => HashAlgorithmName.SHA512,
                    _ => throw new ExecutionException($"Unsupported encryption algorithm: {algo}")
                };
            }

            if (options.TryGetValue("KEYFILE", out var kf)) KeyFile = kf;
            if (options.TryGetValue("PASSPHRASE", out var pp)) Passphrase = pp;
            if (options.TryGetValue("PASSWORD", out var p)) Password = p;
        }

        /// <summary>
        /// Decrypts a file if encryption is enabled.
        /// </summary>
        /// <param name="inputFile">The encrypted source file.</param>
        /// <param name="outputFile">The target decrypted file.</param>
        public void DecryptFile(string inputFile, string outputFile)
        {
            if (!Enabled)
            {
                System.IO.File.Copy(inputFile, outputFile, true);
                return;
            }

            if (IsMachineBound)
            {
                MachineBoundCrypto.DecryptFile(inputFile, outputFile);
            }
            else if (!string.IsNullOrEmpty(KeyFile))
            {
                CryptoUtils.DecryptFileWithSsh(inputFile, outputFile, KeyFile, true, Passphrase);
            }
            else
            {
                CryptoUtils.DecryptFile(inputFile, outputFile, Password, true, Algorithm);
            }
        }

        /// <summary>
        /// Encrypts a file if encryption is enabled.
        /// </summary>
        /// <param name="inputFile">The plain source file.</param>
        /// <param name="outputFile">The target encrypted file.</param>
        public void EncryptFile(string inputFile, string outputFile)
        {
            if (!Enabled)
            {
                System.IO.File.Copy(inputFile, outputFile, true);
                return;
            }

            if (IsMachineBound)
            {
                MachineBoundCrypto.EncryptFile(inputFile, outputFile);
            }
            else if (!string.IsNullOrEmpty(KeyFile))
            {
                CryptoUtils.EncryptFileWithSsh(inputFile, outputFile, KeyFile, true);
            }
            else
            {
                CryptoUtils.EncryptFile(inputFile, outputFile, Password, true, Algorithm);
            }
        }
    }
}
