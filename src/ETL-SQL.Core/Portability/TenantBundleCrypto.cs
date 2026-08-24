using System.Security.Cryptography;
using PgpCore;

namespace ETL_SQL.Core.Portability;

/// <summary>
/// OpenPGP signing and payload encryption for the tenant portability bundle, per
/// <c>docs/architecture/TenantPortability.md</c> §13.1.
/// </summary>
/// <remarks>
/// OpenPGP rather than JOSE because <c>PgpCore</c> is already a cleared dependency here and
/// <c>CREATE PGP KEY PAIR</c> already gives customers a key story; adding a JOSE stack would have
/// meant a second crypto surface for the same job. The operator signs the manifest — an authenticity
/// claim — while payloads are encrypted to the tenant's recipient key, which is the confidentiality
/// claim. They are deliberately separate.
/// </remarks>
public static class TenantBundleCrypto
{
    public static async Task<byte[]> EncryptAsync(
        byte[] plaintext, string recipientPublicKeyFile, CancellationToken ct = default)
    {
        RequireFile(recipientPublicKeyFile, "recipient public key");
        var keys = new EncryptionKeys(new FileInfo(recipientPublicKeyFile));
        using var pgp = new PGP(keys);
        using var input = new MemoryStream(plaintext, writable: false);
        using var output = new MemoryStream();
        await pgp.EncryptAsync(input, output).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return output.ToArray();
    }

    public static async Task<byte[]> DecryptAsync(
        byte[] ciphertext, string privateKeyFile, string? passphrase, CancellationToken ct = default)
    {
        RequireFile(privateKeyFile, "private key");
        var keys = new EncryptionKeys(new FileInfo(privateKeyFile), passphrase ?? string.Empty);
        using var pgp = new PGP(keys);
        using var input = new MemoryStream(ciphertext, writable: false);
        using var output = new MemoryStream();
        await pgp.DecryptAsync(input, output).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return output.ToArray();
    }

    public static async Task DecryptAsync(
        Stream ciphertext, Stream plaintext, string privateKeyFile, string? passphrase,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        ArgumentNullException.ThrowIfNull(plaintext);
        RequireFile(privateKeyFile, "private key");
        var keys = new EncryptionKeys(new FileInfo(privateKeyFile), passphrase ?? string.Empty);
        using var pgp = new PGP(keys);
        await pgp.DecryptAsync(ciphertext, plaintext).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
    }

    public static async Task SignDetachedAsync(
        string manifestPath, string signaturePath, string signingPrivateKeyFile,
        string? passphrase, CancellationToken ct = default)
    {
        RequireFile(signingPrivateKeyFile, "operator signing key");
        var keys = new EncryptionKeys(new FileInfo(signingPrivateKeyFile), passphrase ?? string.Empty);
        using var pgp = new PGP(keys);
        using var input = File.OpenRead(manifestPath);
        using var output = File.Create(signaturePath);
        await pgp.SignDetachedAsync(input, output, armor: true, headers: null).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Verifies the detached operator signature over the manifest. Returns false rather than throwing
    /// for an invalid signature, because "this bundle is not authentic" is an expected validation
    /// outcome, not an exceptional one.
    /// </summary>
    public static async Task<bool> VerifyDetachedAsync(
        string manifestPath, string signaturePath, string operatorPublicKeyFile,
        CancellationToken ct = default)
    {
        RequireFile(operatorPublicKeyFile, "operator public key");
        if (!File.Exists(signaturePath)) return false;

        try
        {
            var keys = new EncryptionKeys(new FileInfo(operatorPublicKeyFile));
            using var pgp = new PGP(keys);
            using var input = File.OpenRead(manifestPath);
            using var signature = File.OpenRead(signaturePath);
            var verified = await pgp.VerifyDetachedAsync(input, signature).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return verified;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Deliberately broad. The underlying failures are BouncyCastle's (PgpException and
            // friends) and Core does not reference BouncyCastle directly, but the contract here is
            // simply "did this verify?" — a malformed, truncated, or foreign signature is a failed
            // verification, not a crash, and every one of them must answer no rather than propagate.
            return false;
        }
    }

    /// <summary>
    /// SHA-256 over the armored recipient key file. Deliberately <em>not</em> the OpenPGP key
    /// fingerprint — it is a stable identifier for "which key was this encrypted to" that needs no
    /// key parsing, and it is named a digest so nobody mistakes it for the fingerprint a customer
    /// would compare against their keyring.
    /// </summary>
    public static string Fingerprint(string recipientPublicKeyFile)
    {
        RequireFile(recipientPublicKeyFile, "recipient public key");
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(recipientPublicKeyFile)))
            .ToLowerInvariant();
    }

    private static void RequireFile(string path, string what)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException($"The {what} was not found.", path);
    }
}
