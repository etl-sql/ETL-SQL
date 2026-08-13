using System.Text.Json;
using ETL_SQL.Core.Data;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

public sealed class SubscriptionQueryService(
    PortalDbContext db,
    PortalTenantCatalogScope? catalogScope = null)
{
    private IQueryable<Subscription> Subscriptions => catalogScope?.Subscriptions ?? db.Subscriptions;

    public async Task<IReadOnlyList<SubscriptionDto>> ListAsync(int currentUserId, bool isAdmin)
    {
        var subscriptions = await Subscriptions
            .AsNoTracking()
            .Include(s => s.Report)
            .Where(s => isAdmin || s.UserId == currentUserId)
            .ToListAsync();
        return subscriptions.Select(ToDto).ToList();
    }

    public async Task<PagedResult<SubscriptionDto>> GetAdminCatalogAsync(
        string? queryText,
        string? status,
        string? format,
        int page,
        int pageSize)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = Subscriptions
            .AsNoTracking()
            .Include(s => s.Report)
            .AsQueryable();
        if (string.Equals(status, "active", StringComparison.OrdinalIgnoreCase))
            query = query.Where(s => s.IsActive);
        else if (string.Equals(status, "paused", StringComparison.OrdinalIgnoreCase))
            query = query.Where(s => !s.IsActive);
        else if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
            query = query.Where(s => s.FailCount > 0);
        if (!string.IsNullOrWhiteSpace(format) &&
            Enum.TryParse<SubscriptionFormat>(format, true, out var parsedFormat))
            query = query.Where(s => s.Format == parsedFormat);

        if (!string.IsNullOrWhiteSpace(queryText))
        {
            var pattern = LikePattern(queryText.Trim());
            query = query.Where(s =>
                (s.Name != null && EF.Functions.Like(s.Name, pattern)) ||
                (s.Recipients != null && EF.Functions.Like(s.Recipients, pattern)) ||
                EF.Functions.Like(s.Report.Name, pattern));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(s => s.Report.Name).ThenBy(s => s.Recipients)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        return new PagedResult<SubscriptionDto>(items.Select(ToDto).ToList(), total, page, pageSize);
    }

    public async Task<Subscription?> LoadAsync(int id, bool track = false)
    {
        var query = Subscriptions.Include(s => s.Report).AsQueryable();
        if (!track)
            query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(s => s.Id == id);
    }

    public SubscriptionDto ToDto(Subscription s)
    {
        var parameters = DeserializeParams(s.ParametersJson);
        var summary = BuildParameterSummary(parameters);
        return new SubscriptionDto(
            s.Id, s.ReportId, s.Report?.Name ?? "",
            s.Name, s.Schedule, s.AtTime, s.DeliverOnRefresh, s.Format.ToString(),
            s.SmtpAlias, s.Recipients, s.LastSentAt, s.NextRunAt, s.FailCount, s.IsActive,
            parameters, summary, s.Version);
    }

    public static string? SerializeParams(Dictionary<string, string>? parameters) =>
        parameters is { Count: > 0 } ? JsonSerializer.Serialize(parameters) : null;

    private static Dictionary<string, string>? DeserializeParams(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json); }
        catch { return null; }
    }

    private static string? BuildParameterSummary(Dictionary<string, string>? parameters) =>
        parameters is { Count: > 0 }
            ? string.Join(", ", parameters.Select(kv => $"{kv.Key}={kv.Value}"))
            : null;

    private static string LikePattern(string query) => $"%{query}%";
}
