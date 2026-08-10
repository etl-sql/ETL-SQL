using ETL_SQL.Core.Common;
using ETL_SQL.Core.Security;
using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

public sealed record DatasetKeyRotationResult(
    string TargetVersion,
    int Rotated,
    int AlreadyCurrent,
    IReadOnlyList<string> FailedDatasets);

public sealed class DatasetAtRestKeyRotationService(
    PortalDbContext db,
    PortalConfig config,
    ILogger<DatasetAtRestKeyRotationService> log,
    IKeyMaterialProvider? keyProvider = null)
{
    public async Task<DatasetKeyRotationResult> RotateAsync(CancellationToken cancellationToken = default)
    {
        string targetKey;
        string targetVersion;
        if (keyProvider is null)
        {
            var validation = DatasetAtRestKeyValidator.Validate(config.Dataset);
            if (validation.Severity == DatasetAtRestKeyValidator.Severity.Fatal)
                throw new InvalidOperationException(validation.Message);
            targetKey = config.Dataset.AtRestKey
                ?? throw new InvalidOperationException("A portal at-rest key is required for rotation.");
            targetVersion = config.Dataset.AtRestKeyVersion;
        }
        else
        {
            using var target = await keyProvider.ResolveAsync(
                new KeyMaterialRequest(KeyScope, KeyPurpose.Dataset), cancellationToken);
            targetKey = Convert.ToBase64String(target.Bytes.Span);
            targetVersion = target.Descriptor.Version;
        }
        var datasets = await db.Datasets
            .Where(d => d.ParquetFilePath != "")
            .OrderBy(d => d.Id)
            .ToListAsync(cancellationToken);

        var rotated = 0;
        var alreadyCurrent = 0;
        var failures = new List<string>();

        foreach (var dataset in datasets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceVersion = ResolveSourceVersion(dataset);
            if (sourceVersion.Equals(targetVersion, StringComparison.OrdinalIgnoreCase))
            {
                if (dataset.AtRestKeyVersion == null)
                {
                    dataset.AtRestKeyVersion = targetVersion;
                    dataset.EncryptionMode = ETL_SQL.Core.DatasetEncryptionMode.MachineBound;
                    dataset.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(cancellationToken);
                }
                alreadyCurrent++;
                continue;
            }

            try
            {
                var sourceKey = await ResolveKeyAsync(sourceVersion, cancellationToken);
                await RotateOneAsync(dataset, sourceKey, targetKey, targetVersion, cancellationToken);
                rotated++;
            }
            catch (Exception ex)
            {
                db.Entry(dataset).State = EntityState.Unchanged;
                failures.Add(dataset.Name);
                log.LogError(
                    ex,
                    "Dataset key rotation failed for dataset {DatasetId} ({DatasetName}) at version {Version}.",
                    dataset.Id,
                    dataset.Name,
                    sourceVersion);
            }
        }

        return new DatasetKeyRotationResult(targetVersion, rotated, alreadyCurrent, failures);
    }

    private string ResolveSourceVersion(Dataset dataset) =>
        dataset.AtRestKeyVersion
        ?? config.Dataset.LegacyAtRestKeyVersion
        ?? (keyProvider is null ? config.Dataset.AtRestKeyVersion : null)
        ?? throw new InvalidOperationException(
            "An unstamped dataset requires LegacyAtRestKeyVersion before provider-backed rotation.");

    private async Task<string> ResolveKeyAsync(string version, CancellationToken cancellationToken)
    {
        if (keyProvider is not null)
        {
            using var lease = await keyProvider.ResolveAsync(
                new KeyMaterialRequest(KeyScope, KeyPurpose.Dataset, version), cancellationToken);
            return Convert.ToBase64String(lease.Bytes.Span);
        }
        if (version.Equals(config.Dataset.AtRestKeyVersion, StringComparison.OrdinalIgnoreCase))
            return config.Dataset.AtRestKey!;
        if (config.Dataset.PreviousAtRestKeys.TryGetValue(version, out var key))
            return key;
        throw new InvalidOperationException($"No at-rest key is configured for dataset key version '{version}'.");
    }

    private string KeyScope => string.IsNullOrWhiteSpace(config.TenantId)
        ? "portal-host"
        : config.TenantId;

    private async Task RotateOneAsync(
        Dataset dataset,
        string sourceKey,
        string targetKey,
        string targetVersion,
        CancellationToken cancellationToken)
    {
        if (!PortalPathGuard.TryResolveDataset(config, dataset.ParquetFilePath, out var path)
            || !File.Exists(path))
        {
            throw new InvalidOperationException("The managed dataset file is missing or outside DatasetRootPath.");
        }

        var directory = Path.GetDirectoryName(path)!;
        var token = Guid.NewGuid().ToString("N");
        var plainPath = Path.Combine(directory, $".rotate-plain-{token}.parquet");
        var stagedPath = Path.Combine(directory, $".rotate-{token}.parquet");
        var backupPath = Path.Combine(directory, $".rotate-backup-{token}.parquet");
        var sourceOptions = new EncryptionOptions(new Dictionary<string, string>
        {
            ["ENCRYPT"] = "PASSWORD",
            ["PASSWORD"] = sourceKey
        });
        var targetOptions = new EncryptionOptions(new Dictionary<string, string>
        {
            ["ENCRYPT"] = "PASSWORD",
            ["PASSWORD"] = targetKey
        });

        try
        {
            sourceOptions.DecryptFile(path, plainPath);
            targetOptions.EncryptFile(plainPath, stagedPath);
            if (!File.Exists(stagedPath) || new FileInfo(stagedPath).Length == 0)
                throw new InvalidDataException("Rotation did not produce a valid encrypted file.");

            File.Copy(path, backupPath, overwrite: true);
            File.Move(stagedPath, path, overwrite: true);
            try
            {
                dataset.AtRestKeyVersion = targetVersion;
                dataset.EncryptionMode = ETL_SQL.Core.DatasetEncryptionMode.MachineBound;
                dataset.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                File.Move(backupPath, path, overwrite: true);
                throw;
            }
        }
        finally
        {
            TryDelete(plainPath);
            TryDelete(stagedPath);
            TryDelete(backupPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // A later rotation retry or startup maintenance can remove abandoned files.
        }
    }
}
