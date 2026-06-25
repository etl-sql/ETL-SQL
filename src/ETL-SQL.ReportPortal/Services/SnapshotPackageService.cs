using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ETL_SQL.Core.Storage;
using ETL_SQL.Reporting;

namespace ETL_SQL.ReportPortal.Services;

public sealed class SnapshotPackageService(
    PortalConfig config,
    IArtifactStorage artifacts,
    ILogger<SnapshotPackageService> logger)
{
    public const string Extension = ".etlsnap";
    private const string LayoutEntryName = "layout.json";
    private const string MetadataEntryName = "manifest.json";
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private static readonly byte[] Magic = "ETLSNAP1"u8.ToArray();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null
    };

    public static string BuildSnapshotKey(int reportId, string jobId) =>
        $"report_{reportId}_{jobId}{Extension}";

    public static bool IsPackageKey(string key) =>
        key.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    public static bool IsLegacyJsonKey(string key) =>
        key.EndsWith(".snapshot.json", StringComparison.OrdinalIgnoreCase)
        || key.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    public static string ToPackageKey(string key)
    {
        var normalized = key.Replace('\\', '/');
        if (IsPackageKey(normalized)) return normalized;
        return normalized.EndsWith(".snapshot.json", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^".snapshot.json".Length] + Extension
            : Path.ChangeExtension(normalized, Extension)!.Replace('\\', '/');
    }

    public async Task SaveAsync(ReportManifest manifest, string key, CancellationToken ct = default)
    {
        if (!IsPackageKey(key))
            throw new InvalidOperationException($"Snapshot packages must use the {Extension} extension.");

        var layoutJson = JsonSerializer.Serialize(manifest, JsonOptions);
        var compressedPackage = CreateCompressedPackage(layoutJson);
        var encryptedPackage = Encrypt(compressedPackage);
        await artifacts.WriteAllBytesAsync(ArtifactArea.Snapshots, key, encryptedPackage, ct: ct);
    }

    public async Task<ReportManifest?> LoadAsync(string key, CancellationToken ct = default)
    {
        var json = await LoadLayoutJsonAsync(key, ct);
        return JsonSerializer.Deserialize<ReportManifest>(json, JsonOptions);
    }

    public async Task<string> LoadLayoutJsonAsync(string key, CancellationToken ct = default)
    {
        if (IsLegacyJsonKey(key))
            return await artifacts.ReadAllTextAsync(ArtifactArea.Snapshots, key, ct);

        if (!IsPackageKey(key))
            throw new InvalidDataException($"Unsupported snapshot artifact extension: {key}");

        var encryptedPackage = await artifacts.ReadAllBytesAsync(ArtifactArea.Snapshots, key, ct);
        var compressedPackage = Decrypt(encryptedPackage);
        return ReadLayoutJson(compressedPackage);
    }

    public async Task<string?> MigrateLegacyJsonAsync(string legacyKey, CancellationToken ct = default)
    {
        if (!IsLegacyJsonKey(legacyKey) || !await artifacts.ExistsAsync(ArtifactArea.Snapshots, legacyKey, ct))
            return null;

        var targetKey = ToPackageKey(legacyKey);
        var manifest = await LoadAsync(legacyKey, ct);
        if (manifest is null)
            return null;

        await SaveAsync(manifest, targetKey, ct);
        await artifacts.DeleteAsync(ArtifactArea.Snapshots, legacyKey, ct);
        logger.LogInformation("Migrated legacy plaintext snapshot {LegacySnapshot} to encrypted package {SnapshotPackage}",
            legacyKey, targetKey);
        return targetKey;
    }

    private byte[] Encrypt(byte[] plaintext)
    {
        var keyVersion = string.IsNullOrWhiteSpace(config.Dataset.AtRestKeyVersion)
            ? "v1"
            : config.Dataset.AtRestKeyVersion;
        var keyVersionBytes = Encoding.UTF8.GetBytes(keyVersion);
        if (keyVersionBytes.Length > ushort.MaxValue)
            throw new InvalidOperationException("Dataset at-rest key version is too long.");

        var key = DeriveAesKey(config.Dataset.AtRestKey, "Portal:Dataset:AtRestKey");
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagLength];
        using var aes = new AesGcm(key, TagLength);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, keyVersionBytes);

        using var output = new MemoryStream(Magic.Length + 2 + keyVersionBytes.Length + NonceLength + TagLength + ciphertext.Length);
        output.Write(Magic);
        Span<byte> length = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)keyVersionBytes.Length);
        output.Write(length);
        output.Write(keyVersionBytes);
        output.Write(nonce);
        output.Write(tag);
        output.Write(ciphertext);
        return output.ToArray();
    }

    private byte[] Decrypt(byte[] package)
    {
        if (package.Length < Magic.Length + 2 + NonceLength + TagLength
            || !package.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new InvalidDataException("Snapshot package is not a recognized ETL-SQL snapshot.");

        var offset = Magic.Length;
        var keyVersionLength = BinaryPrimitives.ReadUInt16BigEndian(package.AsSpan(offset, 2));
        offset += 2;
        if (package.Length < offset + keyVersionLength + NonceLength + TagLength)
            throw new InvalidDataException("Snapshot package is truncated.");

        var keyVersion = Encoding.UTF8.GetString(package, offset, keyVersionLength);
        offset += keyVersionLength;
        var nonce = package.AsSpan(offset, NonceLength);
        offset += NonceLength;
        var tag = package.AsSpan(offset, TagLength);
        offset += TagLength;
        var ciphertext = package.AsSpan(offset);
        var plaintext = new byte[ciphertext.Length];

        var key = ResolveReadKey(keyVersion);
        using var aes = new AesGcm(key, TagLength);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, Encoding.UTF8.GetBytes(keyVersion));
        return plaintext;
    }

    private byte[] ResolveReadKey(string keyVersion)
    {
        if (string.Equals(keyVersion, config.Dataset.AtRestKeyVersion, StringComparison.OrdinalIgnoreCase))
            return DeriveAesKey(config.Dataset.AtRestKey, "Portal:Dataset:AtRestKey");

        if (config.Dataset.PreviousAtRestKeys.TryGetValue(keyVersion, out var previousKey))
            return DeriveAesKey(previousKey, $"Portal:Dataset:PreviousAtRestKeys:{keyVersion}");

        if (string.Equals(keyVersion, config.Dataset.LegacyAtRestKeyVersion, StringComparison.OrdinalIgnoreCase))
            return DeriveAesKey(config.Dataset.AtRestKey, "Portal:Dataset:AtRestKey");

        throw new InvalidDataException($"No configured dataset at-rest key can decrypt snapshot key version '{keyVersion}'.");
    }

    private static byte[] DeriveAesKey(string? base64Key, string configName)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
            throw new InvalidOperationException($"{configName} is required to encrypt report snapshots.");

        try
        {
            var keyMaterial = Convert.FromBase64String(base64Key);
            if (keyMaterial.Length < 32)
                throw new InvalidOperationException($"{configName} must decode to at least 32 bytes.");
            return SHA256.HashData(keyMaterial);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException($"{configName} must be base64 encoded.", ex);
        }
    }

    private static byte[] CreateCompressedPackage(string layoutJson)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, MetadataEntryName, JsonSerializer.Serialize(new
            {
                format = "etl-sql.snapshot",
                version = 1,
                layout = LayoutEntryName,
                createdAt = DateTimeOffset.UtcNow
            }));
            WriteEntry(zip, LayoutEntryName, layoutJson);
        }
        return output.ToArray();
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.SmallestSize);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private static string ReadLayoutJson(byte[] compressedPackage)
    {
        using var input = new MemoryStream(compressedPackage, writable: false);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        var entry = zip.GetEntry(LayoutEntryName)
            ?? throw new InvalidDataException($"Snapshot package is missing {LayoutEntryName}.");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
