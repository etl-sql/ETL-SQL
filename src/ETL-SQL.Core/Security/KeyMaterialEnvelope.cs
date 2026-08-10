using System.Security.Cryptography;
using System.Text;

namespace ETL_SQL.Core.Security;

/// <summary>Authenticated, versioned payload envelope backed by <see cref="IKeyMaterialProvider"/>.</summary>
public static class KeyMaterialEnvelope
{
    public const string Prefix = "km1:";

    public static async Task<string> ProtectAsync(
        string plaintext,
        IKeyMaterialProvider provider,
        KeyMaterialRequest request,
        CancellationToken cancellationToken = default)
    {
        using var lease = await provider.ResolveAsync(request, cancellationToken);
        var normalized = request.Normalize();
        var nonce = RandomNumberGenerator.GetBytes(12);
        var input = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[input.Length];
        var tag = new byte[16];
        var aad = AssociatedData(normalized.Scope, normalized.Purpose, lease.Descriptor.Version);
        using var aes = new AesGcm(SHA256.HashData(lease.Bytes.Span), 16);
        aes.Encrypt(nonce, input, ciphertext, tag, aad);
        return $"{Prefix}{normalized.Purpose}:{lease.Descriptor.Version}:" +
            Convert.ToBase64String([.. nonce, .. tag, .. ciphertext]);
    }

    public static async Task<string> UnprotectAsync(
        string envelope,
        IKeyMaterialProvider provider,
        string serverDerivedScope,
        KeyPurpose requiredPurpose,
        CancellationToken cancellationToken = default)
    {
        if (!envelope.StartsWith(Prefix, StringComparison.Ordinal))
            throw new InvalidDataException("Payload is not a key-material envelope.");
        var parts = envelope.Split(':', 4);
        if (parts.Length != 4 || !Enum.TryParse<KeyPurpose>(parts[1], out var encodedPurpose)
            || encodedPurpose != requiredPurpose || string.IsNullOrWhiteSpace(parts[2]))
            throw new InvalidDataException("Payload key purpose or version is invalid.");
        byte[] payload;
        try { payload = Convert.FromBase64String(parts[3]); }
        catch (FormatException ex) { throw new InvalidDataException("Payload envelope is invalid.", ex); }
        if (payload.Length < 28) throw new InvalidDataException("Payload envelope is truncated.");

        using var lease = await provider.ResolveAsync(
            new KeyMaterialRequest(serverDerivedScope, requiredPurpose, parts[2]), cancellationToken);
        var plaintext = new byte[payload.Length - 28];
        using var aes = new AesGcm(SHA256.HashData(lease.Bytes.Span), 16);
        aes.Decrypt(payload[..12], payload[28..], payload[12..28], plaintext,
            AssociatedData(serverDerivedScope, requiredPurpose, parts[2]));
        return Encoding.UTF8.GetString(plaintext);
    }

    private static byte[] AssociatedData(string scope, KeyPurpose purpose, string version) =>
        Encoding.UTF8.GetBytes($"{scope}\n{purpose}\n{version}");
}
