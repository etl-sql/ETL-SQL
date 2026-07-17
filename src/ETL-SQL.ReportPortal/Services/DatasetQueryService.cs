using System.Text.Json;
using ETL_SQL.Core.Data;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Services;

public sealed class DatasetQueryService(
    PortalDbContext db,
    DatasetPermissionService datasetPermissions)
{
    public async Task<IReadOnlyList<DatasetDto>> GetAllAsync(int currentUserId, bool isAdmin)
    {
        var datasets = await db.Datasets
            .AsNoTracking()
            .Include(d => d.OwningReport)
            .Include(d => d.Acls)
            .ToListAsync();

        var permissions = await datasetPermissions.GetEffectivePermissionsAsync(datasets, currentUserId, isAdmin);

        return datasets
            .Where(d => DatasetPermissionService.CanView(permissions[d.Id]))
            .Select(ToDto)
            .ToList();
    }

    public async Task<Dataset?> LoadDatasetAsync(int id) =>
        await db.Datasets
            .Include(d => d.OwningReport)
            .Include(d => d.Acls)
            .FirstOrDefaultAsync(d => d.Id == id);

    public async Task<Dataset?> LoadDatasetWithAclGroupsAsync(int id)
    {
        return await db.Datasets
            .AsNoTracking()
            .Include(d => d.OwningReport)
            .Include(d => d.Acls).ThenInclude(a => a.Group)
            .FirstOrDefaultAsync(d => d.Id == id);
    }

    public IReadOnlyList<DatasetAclEntryDto> ToAclEntries(Dataset dataset) =>
        dataset.Acls
            .Select(a => new DatasetAclEntryDto(a.GroupId, a.Group.Name, a.Permission.ToString()))
            .ToList();

    public DatasetPreviewDto ToPreview(Dataset dataset) =>
        new(ParseColumnSchema(dataset.ColumnSchema), dataset.RowCount);

    public DatasetDto ToDto(Dataset d) => new(
        d.Id, d.Name, d.FolderPath,
        d.AccessLevel.ToString(),
        d.RowCount, IsStale(d),
        d.LastRefresh, d.Ttl, d.RefreshInterval,
        IsEncrypted: !string.IsNullOrWhiteSpace(d.ParquetFilePath)
            && d.EncryptionMode != ETL_SQL.Core.DatasetEncryptionMode.None,
        d.CreatedAt, d.UpdatedAt,
        d.OwningReport?.Name,
        d.OwningReportId,
        d.Version);

    private static bool IsStale(Dataset d)
    {
        if (!d.LastRefresh.HasValue) return true;
        if (string.IsNullOrWhiteSpace(d.Ttl)) return false;
        var ttl = ParseDuration(d.Ttl);
        return ttl.HasValue && d.LastRefresh.Value + ttl.Value < DateTime.UtcNow;
    }

    private static TimeSpan? ParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(
            value.Trim(),
            @"^(\d+)([smhd])$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        int amount = int.Parse(match.Groups[1].Value);
        return match.Groups[2].Value.ToUpperInvariant() switch
        {
            "S" => TimeSpan.FromSeconds(amount),
            "M" => TimeSpan.FromMinutes(amount),
            "H" => TimeSpan.FromHours(amount),
            "D" => TimeSpan.FromDays(amount),
            _ => null
        };
    }

    private static IEnumerable<DatasetColumnDto> ParseColumnSchema(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
            return doc.RootElement.EnumerateArray()
                .Select(e => new DatasetColumnDto(
                    e.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                    e.TryGetProperty("type", out var type) ? type.GetString() ?? "unknown" : "unknown"))
                .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                .ToList();
        }
        catch { return []; }
    }
}
