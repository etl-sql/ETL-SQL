using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Apache.Arrow;
using Apache.Arrow.Ipc;
using Apache.Arrow.Types;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Security;
using ETL_SQL.Core.Storage;
using ETL_SQL.Reporting;
using Microsoft.Extensions.Logging;

namespace ETL_SQL.Portal.Services;

public sealed class SnapshotPackageService(
    PortalConfig config,
    IArtifactStorage artifacts,
    Microsoft.Extensions.Logging.ILogger<SnapshotPackageService> logger,
    IKeyMaterialProvider? keyProvider = null)
{
    public const string Extension = ".etlsnap";
    internal const int ArrowRowThreshold = 10_000;
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

    private static int _machineFallbackWarned;

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

    public async Task SaveAsync(
        ReportManifest manifest,
        string key,
        CancellationToken ct = default,
        string? keyScope = null)
    {
        _ = ResolveKeyScope(keyScope);
        RequireSharedKeyProvider();
        if (!IsPackageKey(key))
            throw new InvalidOperationException($"Snapshot packages must use the {Extension} extension.");

        var compressedPackage = await CreateCompressedPackageAsync(manifest, ct);
        var encryptedPackage = await EncryptAsync(compressedPackage, ct, keyScope);
        await artifacts.WriteAllBytesAsync(ArtifactArea.Snapshots, key, encryptedPackage, ct: ct);
    }

    public async Task<ReportManifest?> LoadAsync(
        string key,
        CancellationToken ct = default,
        string? keyScope = null)
    {
        var json = await LoadLayoutJsonAsync(key, ct, keyScope);
        return JsonSerializer.Deserialize<ReportManifest>(json, JsonOptions);
    }

    public async Task<string> LoadLayoutJsonAsync(
        string key,
        CancellationToken ct = default,
        string? keyScope = null)
    {
        _ = ResolveKeyScope(keyScope);
        RequireSharedKeyProvider();
        if (IsLegacyJsonKey(key))
        {
            RejectSharedLegacySnapshot();
            return await artifacts.ReadAllTextAsync(ArtifactArea.Snapshots, key, ct);
        }

        if (!IsPackageKey(key))
            throw new InvalidDataException($"Unsupported snapshot artifact extension: {key}");

        var encryptedPackage = await artifacts.ReadAllBytesAsync(ArtifactArea.Snapshots, key, ct);
        var compressedPackage = await DecryptAsync(encryptedPackage, ct, keyScope);
        var manifest = await ReadManifestFromPackageAsync(compressedPackage, ct);
        return JsonSerializer.Serialize(manifest, JsonOptions);
    }

    public async Task<string> LoadLightweightLayoutJsonAsync(
        string key,
        Func<int, string> rowsUrlFactory,
        Func<int, string?>? arrowUrlFactory = null,
        CancellationToken ct = default,
        string? keyScope = null)
    {
        _ = ResolveKeyScope(keyScope);
        RequireSharedKeyProvider();
        if (IsLegacyJsonKey(key))
        {
            RejectSharedLegacySnapshot();
            return await artifacts.ReadAllTextAsync(ArtifactArea.Snapshots, key, ct);
        }

        if (!IsPackageKey(key))
            throw new InvalidDataException($"Unsupported snapshot artifact extension: {key}");

        var encryptedPackage = await artifacts.ReadAllBytesAsync(ArtifactArea.Snapshots, key, ct);
        var compressedPackage = await DecryptAsync(encryptedPackage, ct, keyScope);
        var manifest = await ReadLightweightManifestFromPackageAsync(compressedPackage, rowsUrlFactory, arrowUrlFactory, ct);
        return JsonSerializer.Serialize(manifest, JsonOptions);
    }

    public async Task<SnapshotVisualRows?> LoadRowsAsync(
        string key,
        int visualIndex,
        CancellationToken ct = default,
        string? keyScope = null)
    {
        _ = ResolveKeyScope(keyScope);
        RequireSharedKeyProvider();
        if (visualIndex < 0)
            return null;

        if (IsLegacyJsonKey(key))
        {
            RejectSharedLegacySnapshot();
            var json = await artifacts.ReadAllTextAsync(ArtifactArea.Snapshots, key, ct);
            var legacyManifest = JsonSerializer.Deserialize<ReportManifest>(json, JsonOptions);
            if (legacyManifest is null || visualIndex >= legacyManifest.Visuals.Count)
                return null;

            var legacyVisual = legacyManifest.Visuals[visualIndex];
            return new SnapshotVisualRows(legacyVisual.Columns.ToList(), legacyVisual.Rows, legacyVisual.Rows.Count);
        }

        if (!IsPackageKey(key))
            throw new InvalidDataException($"Unsupported snapshot artifact extension: {key}");

        var encryptedPackage = await artifacts.ReadAllBytesAsync(ArtifactArea.Snapshots, key, ct);
        var compressedPackage = await DecryptAsync(encryptedPackage, ct, keyScope);
        using var input = new MemoryStream(compressedPackage, writable: false);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        var metadata = await ReadPackageMetadataAsync(zip, ct);
        var table = metadata.Tables.FirstOrDefault(t => t.VisualIndex == visualIndex);
        if (table is not null)
        {
            var tableEntry = zip.GetEntry(table.Entry)
                ?? throw new InvalidDataException($"Snapshot package is missing Arrow table {table.Entry}.");
            await using var tableStream = tableEntry.Open();
            using var tableBuffer = new MemoryStream();
            await tableStream.CopyToAsync(tableBuffer, ct);
            tableBuffer.Position = 0;
            var rows = await ReadArrowRowsAsync(tableBuffer, table.Columns, ct);
            return new SnapshotVisualRows(table.Columns.ToList(), rows, table.RowCount);
        }

        var layoutManifest = await ReadLayoutManifestAsync(zip, ct);
        if (visualIndex >= layoutManifest.Visuals.Count)
            return null;

        var layoutVisual = layoutManifest.Visuals[visualIndex];
        return new SnapshotVisualRows(layoutVisual.Columns.ToList(), layoutVisual.Rows, layoutVisual.Rows.Count);
    }

    public async Task<byte[]?> LoadArrowTableAsync(
        string key,
        int visualIndex,
        CancellationToken ct = default,
        string? keyScope = null)
    {
        _ = ResolveKeyScope(keyScope);
        RequireSharedKeyProvider();
        if (visualIndex < 0 || !IsPackageKey(key))
            return null;

        var encryptedPackage = await artifacts.ReadAllBytesAsync(ArtifactArea.Snapshots, key, ct);
        var compressedPackage = await DecryptAsync(encryptedPackage, ct, keyScope);
        using var input = new MemoryStream(compressedPackage, writable: false);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        var metadata = await ReadPackageMetadataAsync(zip, ct);
        var table = metadata.Tables.FirstOrDefault(t => t.VisualIndex == visualIndex);
        if (table is null)
            return null;

        var tableEntry = zip.GetEntry(table.Entry)
            ?? throw new InvalidDataException($"Snapshot package is missing Arrow table {table.Entry}.");
        await using var stream = tableEntry.Open();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    internal async Task<IReadOnlyList<string>> ListPackageEntriesForTestsAsync(
        string key,
        CancellationToken ct = default,
        string? keyScope = null)
    {
        var encryptedPackage = await artifacts.ReadAllBytesAsync(ArtifactArea.Snapshots, key, ct);
        var compressedPackage = await DecryptAsync(encryptedPackage, ct, keyScope);
        using var input = new MemoryStream(compressedPackage, writable: false);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        return zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal).ToList();
    }

    internal async Task<string> ReadStoredLayoutJsonForTestsAsync(
        string key,
        CancellationToken ct = default,
        string? keyScope = null)
    {
        var encryptedPackage = await artifacts.ReadAllBytesAsync(ArtifactArea.Snapshots, key, ct);
        var compressedPackage = await DecryptAsync(encryptedPackage, ct, keyScope);
        using var input = new MemoryStream(compressedPackage, writable: false);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        var entry = zip.GetEntry(LayoutEntryName)
            ?? throw new InvalidDataException($"Snapshot package is missing {LayoutEntryName}.");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return await reader.ReadToEndAsync(ct);
    }

    public async Task<string?> MigrateLegacyJsonAsync(
        string legacyKey,
        CancellationToken ct = default,
        string? keyScope = null)
    {
        if (!IsLegacyJsonKey(legacyKey) || !await artifacts.ExistsAsync(ArtifactArea.Snapshots, legacyKey, ct))
            return null;

        var targetKey = ToPackageKey(legacyKey);
        var manifest = await LoadAsync(legacyKey, ct, keyScope);
        if (manifest is null)
            return null;

        await SaveAsync(manifest, targetKey, ct, keyScope);
        await artifacts.DeleteAsync(ArtifactArea.Snapshots, legacyKey, ct);
        logger.LogInformation("Migrated legacy plaintext snapshot {LegacySnapshot} to encrypted package {SnapshotPackage}",
            legacyKey, targetKey);
        return targetKey;
    }

    private async Task<byte[]> EncryptAsync(byte[] plaintext, CancellationToken ct, string? keyScope)
    {
        if (keyProvider is not null)
        {
            using var lease = await keyProvider.ResolveAsync(
                new KeyMaterialRequest(ResolveKeyScope(keyScope), KeyPurpose.Artifact), ct);
            return EncryptWithKey(
                plaintext, lease.Descriptor.Version, SHA256.HashData(lease.Bytes.Span));
        }

        // No portal-managed key: fall back to the same host-bound ENCRYPT=MACHINE protection dataset
        // caches use (DPAPI LocalMachine on Windows; authenticated AES-256-GCM keyed from the machine
        // id elsewhere). Host-bound, so the package is not portable across hosts. Detected on read by
        // the absence of the ETLSNAP1 magic header.
        if (string.IsNullOrWhiteSpace(config.Dataset.AtRestKey))
        {
            if (Interlocked.Exchange(ref _machineFallbackWarned, 1) == 0)
                logger.LogWarning(
                    "Portal:Dataset:AtRestKey is not set — snapshot packages are protected with host-bound " +
                    "machine encryption (not portable across hosts). Set Portal:Dataset:AtRestKey for portable, " +
                    "key-managed snapshot encryption.");
            return MachineBoundCrypto.Protect(plaintext);
        }

        var keyVersion = string.IsNullOrWhiteSpace(config.Dataset.AtRestKeyVersion)
            ? "v1"
            : config.Dataset.AtRestKeyVersion;
        var key = DeriveAesKey(config.Dataset.AtRestKey, "Portal:Dataset:AtRestKey");
        return EncryptWithKey(plaintext, keyVersion, key);
    }

    private static byte[] EncryptWithKey(byte[] plaintext, string keyVersion, ReadOnlySpan<byte> keyMaterial)
    {
        var keyVersionBytes = Encoding.UTF8.GetBytes(keyVersion);
        if (keyVersionBytes.Length > ushort.MaxValue)
            throw new InvalidOperationException("Artifact at-rest key version is too long.");
        if (keyMaterial.Length < 32)
            throw new InvalidOperationException("Artifact at-rest key must contain at least 256 bits.");
        var key = keyMaterial.ToArray();
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

    private async Task<byte[]> DecryptAsync(byte[] package, CancellationToken ct, string? keyScope)
    {
        // Packages written without a portal-managed key carry no ETLSNAP1 keyed envelope — they are
        // host-bound machine-encrypted (see Encrypt). Route those to MachineBoundCrypto.
        if (package.Length < Magic.Length || !package.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            try
            {
                return MachineBoundCrypto.Unprotect(package);
            }
            catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or ArgumentException or FormatException)
            {
                throw new InvalidDataException("Snapshot package is not a recognized ETL-SQL snapshot.", ex);
            }
        }

        if (package.Length < Magic.Length + 2 + NonceLength + TagLength)
            throw new InvalidDataException("Snapshot package is truncated.");

        var offset = Magic.Length;
        var keyVersionLength = BinaryPrimitives.ReadUInt16BigEndian(package.AsSpan(offset, 2));
        offset += 2;
        if (package.Length < offset + keyVersionLength + NonceLength + TagLength)
            throw new InvalidDataException("Snapshot package is truncated.");

        var keyVersion = Encoding.UTF8.GetString(package, offset, keyVersionLength);
        offset += keyVersionLength;
        var nonce = package.AsSpan(offset, NonceLength).ToArray();
        offset += NonceLength;
        var tag = package.AsSpan(offset, TagLength).ToArray();
        offset += TagLength;
        var ciphertext = package.AsSpan(offset).ToArray();
        var plaintext = new byte[ciphertext.Length];

        var key = keyProvider is null
            ? ResolveReadKey(keyVersion)
            : await ResolveProviderReadKeyAsync(keyVersion, ct, keyScope);
        using var aes = new AesGcm(key, TagLength);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, Encoding.UTF8.GetBytes(keyVersion));
        return plaintext;
    }

    private async Task<byte[]> ResolveProviderReadKeyAsync(
        string keyVersion,
        CancellationToken ct,
        string? keyScope)
    {
        using var lease = await keyProvider!.ResolveAsync(
            new KeyMaterialRequest(ResolveKeyScope(keyScope), KeyPurpose.Artifact, keyVersion), ct);
        return SHA256.HashData(lease.Bytes.Span);
    }

    private string ResolveKeyScope(string? explicitScope)
    {
        if (!string.IsNullOrWhiteSpace(explicitScope))
            return ETL_SQL.Core.Multitenancy.TenantId.FromTrustedSource(explicitScope).Value;
        if (config.SharedTenancy.Enabled)
            throw new UnauthorizedAccessException(
                "Shared snapshot encryption requires an explicit server-derived tenant scope.");
        return string.IsNullOrWhiteSpace(config.TenantId) ? "portal-host" : config.TenantId;
    }

    private void RequireSharedKeyProvider()
    {
        if (config.SharedTenancy.Enabled && keyProvider is null)
            throw new InvalidOperationException(
                "Shared snapshot encryption requires a tenant-aware Artifact key provider.");
    }

    private void RejectSharedLegacySnapshot()
    {
        if (config.SharedTenancy.Enabled)
            throw new InvalidDataException(
                "Legacy plaintext snapshots cannot be read on a Shared host because they have no authenticated tenant ownership.");
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
            throw new InvalidOperationException(
                $"{configName} is required to read this key-managed snapshot but is not configured.");

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

    private static async Task<byte[]> CreateCompressedPackageAsync(ReportManifest manifest, CancellationToken ct)
    {
        var (layout, tables) = await ExtractArrowTablesAsync(manifest, ct);
        var layoutJson = JsonSerializer.Serialize(layout, JsonOptions);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            await WriteEntryAsync(zip, MetadataEntryName, JsonSerializer.Serialize(new SnapshotPackageMetadata(
                Format: "etl-sql.snapshot",
                Version: 2,
                Layout: LayoutEntryName,
                CreatedAt: DateTimeOffset.UtcNow,
                Tables: tables.Select(t => t.Metadata).ToList())), ct);
            await WriteEntryAsync(zip, LayoutEntryName, layoutJson, ct);
            foreach (var table in tables)
            {
                await WriteBytesEntryAsync(zip, table.Metadata.Entry, table.Bytes, ct);
            }
        }
        return output.ToArray();
    }

    private static async Task WriteEntryAsync(ZipArchive zip, string name, string content, CancellationToken ct)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.SmallestSize);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, Encoding.UTF8);
        await writer.WriteAsync(content.AsMemory(), ct);
    }

    private static async Task WriteBytesEntryAsync(ZipArchive zip, string name, byte[] content, CancellationToken ct)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.SmallestSize);
        await using var stream = entry.Open();
        await stream.WriteAsync(content, ct);
    }

    private static async Task<(ReportManifest Layout, List<SnapshotArrowTable> Tables)> ExtractArrowTablesAsync(
        ReportManifest manifest,
        CancellationToken ct)
    {
        var cloneJson = JsonSerializer.Serialize(manifest, JsonOptions);
        var layout = JsonSerializer.Deserialize<ReportManifest>(cloneJson, JsonOptions)
            ?? throw new InvalidDataException("Snapshot manifest could not be cloned.");
        var tables = new List<SnapshotArrowTable>();

        for (var i = 0; i < layout.Visuals.Count; i++)
        {
            var visual = layout.Visuals[i];
            if (visual.Rows.Count < ArrowRowThreshold || visual.Columns.Count == 0)
                continue;

            var entryName = $"tables/visual-{i:D4}-{SanitizeEntryName(visual.Name)}.arrow";
            var rows = visual.Rows;
            var metadata = new SnapshotTableMetadata(
                VisualIndex: i,
                VisualName: visual.Name,
                Entry: entryName,
                RowCount: rows.Count,
                Columns: visual.Columns.ToList());
            var arrowBytes = await WriteArrowRowsAsync(visual.Columns, rows, ct);

            visual.Rows = new List<List<string?>>();
            tables.Add(new SnapshotArrowTable(metadata, arrowBytes));
        }

        return (layout, tables);
    }

    private static async Task<byte[]> WriteArrowRowsAsync(
        IReadOnlyList<string> columns,
        IReadOnlyList<List<string?>> rows,
        CancellationToken ct)
    {
        var fields = columns
            .Select(c => new Field(c, StringType.Default, nullable: true))
            .ToList();
        var schema = new Schema(fields, metadata: null);
        var arrays = new List<IArrowArray>(columns.Count);
        for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
        {
            var builder = new StringArray.Builder();
            builder.Reserve(rows.Count);
            foreach (var row in rows)
            {
                ct.ThrowIfCancellationRequested();
                if (columnIndex >= row.Count || row[columnIndex] is null)
                    builder.AppendNull();
                else
                    builder.Append(row[columnIndex]);
            }
            arrays.Add(builder.Build());
        }

        using var output = new MemoryStream();
        using var writer = new ArrowStreamWriter(output, schema, leaveOpen: true);
        await writer.WriteStartAsync(ct);
        await writer.WriteRecordBatchAsync(new RecordBatch(schema, arrays, rows.Count), ct);
        await writer.WriteEndAsync(ct);
        return output.ToArray();
    }

    private static async Task<ReportManifest> ReadManifestFromPackageAsync(byte[] compressedPackage, CancellationToken ct)
    {
        using var input = new MemoryStream(compressedPackage, writable: false);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        var metadata = await ReadPackageMetadataAsync(zip, ct);
        var manifest = await ReadLayoutManifestAsync(zip, ct);

        foreach (var table in metadata.Tables)
        {
            if (table.VisualIndex < 0 || table.VisualIndex >= manifest.Visuals.Count)
                throw new InvalidDataException($"Snapshot Arrow table references invalid visual index {table.VisualIndex}.");

            var tableEntry = zip.GetEntry(table.Entry)
                ?? throw new InvalidDataException($"Snapshot package is missing Arrow table {table.Entry}.");
            await using var tableStream = tableEntry.Open();
            using var tableBuffer = new MemoryStream();
            await tableStream.CopyToAsync(tableBuffer, ct);
            tableBuffer.Position = 0;
            manifest.Visuals[table.VisualIndex].Rows = await ReadArrowRowsAsync(tableBuffer, table.Columns, ct);
        }

        return manifest;
    }

    private static async Task<ReportManifest> ReadLightweightManifestFromPackageAsync(
        byte[] compressedPackage,
        Func<int, string> rowsUrlFactory,
        Func<int, string?>? arrowUrlFactory,
        CancellationToken ct)
    {
        using var input = new MemoryStream(compressedPackage, writable: false);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        var metadata = await ReadPackageMetadataAsync(zip, ct);
        var manifest = await ReadLayoutManifestAsync(zip, ct);

        foreach (var table in metadata.Tables)
        {
            if (table.VisualIndex < 0 || table.VisualIndex >= manifest.Visuals.Count)
                throw new InvalidDataException($"Snapshot Arrow table references invalid visual index {table.VisualIndex}.");

            var visual = manifest.Visuals[table.VisualIndex];
            visual.Rows = [];
            visual.RowsSource = new VisualRowsSourceManifest
            {
                Format = "json",
                Url = rowsUrlFactory(table.VisualIndex),
                ArrowUrl = arrowUrlFactory?.Invoke(table.VisualIndex),
                RowCount = table.RowCount,
                Columns = table.Columns.ToList()
            };
        }

        return manifest;
    }

    private static async Task<ReportManifest> ReadLayoutManifestAsync(ZipArchive zip, CancellationToken ct)
    {
        var entry = zip.GetEntry(LayoutEntryName)
            ?? throw new InvalidDataException($"Snapshot package is missing {LayoutEntryName}.");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        var layoutJson = await reader.ReadToEndAsync(ct);
        return JsonSerializer.Deserialize<ReportManifest>(layoutJson, JsonOptions)
            ?? throw new InvalidDataException("Snapshot package contains an invalid layout manifest.");
    }

    private static async Task<SnapshotPackageMetadata> ReadPackageMetadataAsync(ZipArchive zip, CancellationToken ct)
    {
        var entry = zip.GetEntry(MetadataEntryName);
        if (entry is null)
            return SnapshotPackageMetadata.Empty;

        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        var json = await reader.ReadToEndAsync(ct);
        var metadata = JsonSerializer.Deserialize<SnapshotPackageMetadata>(json, JsonOptions)
            ?? SnapshotPackageMetadata.Empty;
        return metadata.Tables is null ? metadata with { Tables = [] } : metadata;
    }

    private static async Task<List<List<string?>>> ReadArrowRowsAsync(
        Stream stream,
        IReadOnlyList<string> expectedColumns,
        CancellationToken ct)
    {
        var rows = new List<List<string?>>();
        using var arrowReader = new ArrowStreamReader(stream);
        while (true)
        {
            var batch = await arrowReader.ReadNextRecordBatchAsync(ct);
            if (batch is null)
                return rows;

            var arrays = expectedColumns
                .Select(column =>
                {
                    var index = batch.Schema.GetFieldIndex(column);
                    if (index < 0)
                        throw new InvalidDataException($"Snapshot Arrow table is missing column '{column}'.");
                    return (StringArray)batch.Arrays.ElementAt(index);
                })
                .ToList();

            for (var rowIndex = 0; rowIndex < batch.Length; rowIndex++)
            {
                ct.ThrowIfCancellationRequested();
                var row = new List<string?>(arrays.Count);
                foreach (var array in arrays)
                    row.Add(array.IsNull(rowIndex) ? null : array.GetString(rowIndex));
                rows.Add(row);
            }
        }
    }

    private static string SanitizeEntryName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "visual";

        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_')
                builder.Append(ch);
            else
                builder.Append('-');
        }
        return builder.Length == 0 ? "visual" : builder.ToString();
    }

    private sealed record SnapshotArrowTable(SnapshotTableMetadata Metadata, byte[] Bytes);

    private sealed record SnapshotPackageMetadata(
        [property: JsonPropertyName("format")] string Format,
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("layout")] string Layout,
        [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("tables")] List<SnapshotTableMetadata> Tables)
    {
        public static SnapshotPackageMetadata Empty { get; } = new(
            "etl-sql.snapshot",
            1,
            LayoutEntryName,
            DateTimeOffset.MinValue,
            []);
    }

    private sealed record SnapshotTableMetadata(
        [property: JsonPropertyName("visualIndex")] int VisualIndex,
        [property: JsonPropertyName("visualName")] string VisualName,
        [property: JsonPropertyName("entry")] string Entry,
        [property: JsonPropertyName("rowCount")] int RowCount,
        [property: JsonPropertyName("columns")] List<string> Columns);
}

public sealed record SnapshotVisualRows(
    [property: JsonPropertyName("columns")] List<string> Columns,
    [property: JsonPropertyName("rows")] List<List<string?>> Rows,
    [property: JsonPropertyName("rowCount")] int RowCount);
