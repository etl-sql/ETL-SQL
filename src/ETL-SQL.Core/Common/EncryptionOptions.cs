using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Core.Common;
/// <summary>
/// Represents the encryption configuration for a data source and provides
/// helper methods to process files based on those settings.
/// </summary>
public class EncryptionOptions
{
    /// <summary>
    /// Process-level secret holding the portal at-rest key (base64), set by the portal/orchestrator
    /// host from <c>Portal:Dataset:AtRestKey</c>. <c>ENCRYPT = PORTAL</c> resolves the key from here at
    /// run time so it is never embedded in (and persisted with) scheduled-job SQL.
    /// </summary>
    public const string PortalAtRestKeyEnvVar = "ETLSQL_DATASET_ATREST_KEY";

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
        Password = "";

        if (options == null) return;

        if (options.TryGetValue("ENCRYPT", out var enc))
        {
            var mode = enc.ToUpperInvariant();
            IsMachineBound = mode == "MACHINE";
            Enabled = mode is "ON" or "TRUE" or "MACHINE" or "PASSWORD" or "KEYFILE" or "PORTAL";

            if (mode == "PORTAL")
            {
                // Resolve the portal at-rest key from the process secret; behaves as ENCRYPT=PASSWORD
                // with that key. The key is never present in the connection options / persisted SQL.
                var portalKey = Environment.GetEnvironmentVariable(PortalAtRestKeyEnvVar);
                if (string.IsNullOrWhiteSpace(portalKey))
                    throw new ExecutionException(
                        $"ENCRYPT = PORTAL requires the portal at-rest key (env {PortalAtRestKeyEnvVar}) to be configured.");
                Password = portalKey;
            }
        }

        if (options.TryGetValue("ALGORITHM", out var algo))
        {
            Algorithm = algo.ToUpperInvariant() switch
            {
                "SHA2_256" or "SHA256" => HashAlgorithmName.SHA256,
                "SHA2_512" or "SHA512" => HashAlgorithmName.SHA512,
                // MD5/SHA1 are intentionally rejected for encryption key derivation (cryptographically
                // broken). They remain available only for non-security checksum HASH() functions.
                "MD5" or "SHA1" => throw new ExecutionException(
                    $"Encryption algorithm '{algo}' is not allowed (weak). Use SHA256 or SHA512."),
                _ => throw new ExecutionException($"Unsupported encryption algorithm: {algo}")
            };
        }

        if (options.TryGetValue("KEYFILE", out var kf)) KeyFile = kf;
        if (options.TryGetValue("PASSPHRASE", out var pp)) Passphrase = pp;
        if (options.TryGetValue("PASSWORD", out var p)) Password = p;

        // Fail closed: a password-based encryption mode must supply an actual key. Previously a
        // missing PASSWORD silently fell back to a hardcoded, source-public default, which gave
        // only the appearance of confidentiality. MACHINE/PORTAL/KEYFILE provide their own key.
        if (Enabled && !IsMachineBound && string.IsNullOrEmpty(KeyFile) && string.IsNullOrEmpty(Password))
            throw new ExecutionException(
                "Encryption is enabled but no key was supplied. Provide PASSWORD or KEYFILE, " +
                "or use ENCRYPT = MACHINE or ENCRYPT = PORTAL.");
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
    /// Decrypts a stream if encryption is enabled.
    /// </summary>
    public Stream DecryptStream(Stream inputStream)
    {
        if (!Enabled)
        {
            return inputStream;
        }

        if (IsMachineBound)
        {
            return MachineBoundCrypto.DecryptStream(inputStream);
        }
        else if (!string.IsNullOrEmpty(KeyFile))
        {
            return CryptoUtils.DecryptStreamWithSsh(inputStream, KeyFile, Passphrase);
        }
        else
        {
            return CryptoUtils.DecryptStream(inputStream, Password, Algorithm);
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
