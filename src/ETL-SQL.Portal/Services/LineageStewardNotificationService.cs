using System.Text.Json;
using ETL_SQL.Core;

namespace ETL_SQL.Portal.Services;

public sealed class LineageStewardNotificationService(
    LineageImpactService impact,
    AuditService audit,
    ILogger<LineageStewardNotificationService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task NotifyAsync(
        int? actorUserId,
        int? reportId,
        string? jobName,
        string? scriptPath,
        IReadOnlyCollection<LineageEntry> entries,
        CancellationToken cancellationToken = default)
    {
        if (entries.Count == 0)
            return;

        var targets = entries
            .SelectMany(e => e.SourceTables.Append(e.TargetTable))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();
        if (targets.Count == 0)
            return;

        var impactedStewards = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (entry.Metadata.TryGetValue("steward", out var directSteward) && !string.IsNullOrWhiteSpace(directSteward))
                impactedStewards.Add(directSteward.Trim());
        }

        foreach (var target in targets)
        {
            try
            {
                var result = await impact.AnalyzeAsync(
                    "table",
                    target,
                    null,
                    "downstream",
                    4,
                    100,
                    isAdmin: true,
                    currentUserId: actorUserId ?? 0,
                    cancellationToken);
                foreach (var steward in result.Stewards.Where(s => s.Type == "Steward"))
                    impactedStewards.Add(steward.Name);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to analyze steward lineage impact for {Target}", target);
            }
        }

        foreach (var steward in impactedStewards)
        {
            var detail = JsonSerializer.Serialize(new
            {
                reportId,
                jobName,
                scriptPath,
                targets,
                entryCount = entries.Count
            }, JsonOptions);

            try
            {
                await audit.LogAsync(
                    actorUserId,
                    "STEWARD_LINEAGE_IMPACT",
                    "Steward",
                    steward,
                    detail);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to audit steward lineage impact for {Steward}", steward);
            }
        }
    }
}
