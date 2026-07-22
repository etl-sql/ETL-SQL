using System;
using System.Collections.Generic;
using System.Linq;

namespace ETL_SQL.Core.Data;

public sealed record ProtectedLineageHistoryEntry(
    long Id,
    DateTime RunAt,
    string? JobName,
    string? ScriptPath,
    string TargetTable,
    string? TargetColumn,
    IReadOnlyList<string> SourceTables,
    string Operation,
    IReadOnlyDictionary<string, string> Tags,
    IReadOnlyList<string> ProtectionTags,
    string ProtectionReason,
    string? Owner,
    string? Steward,
    string? Contact,
    string? Domain,
    string? Classification,
    string? Quality,
    string? SourceFile,
    int Line);

public static class LineageProtectedData
{
    private static readonly string[] TruthyProtectedTags = ["pii", "phi", "pci", "sensitive"];
    private static readonly string[] ProtectedClassifications = ["confidential", "restricted"];

    public static IEnumerable<ProtectedLineageHistoryEntry> FromHistory(IEnumerable<LineageHistoryEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (!TryBuildProtection(entry.Tags, out var protectionTags, out var reason))
                continue;

            yield return new ProtectedLineageHistoryEntry(
                entry.Id,
                entry.RunAt,
                entry.JobName,
                entry.ScriptPath,
                entry.TargetTable,
                entry.TargetColumn,
                entry.SourceTables,
                entry.Operation,
                entry.Tags,
                protectionTags,
                reason,
                GetTag(entry.Tags, "owner"),
                GetTag(entry.Tags, "steward"),
                GetTag(entry.Tags, "contact"),
                GetTag(entry.Tags, "domain"),
                GetTag(entry.Tags, "classification"),
                GetTag(entry.Tags, "quality"),
                entry.SourceFile,
                entry.Line);
        }
    }

    public static bool IsProtected(IReadOnlyDictionary<string, string> tags) =>
        TryBuildProtection(tags, out _, out _);

    public static bool TryBuildProtection(
        IReadOnlyDictionary<string, string> tags,
        out IReadOnlyList<string> protectionTags,
        out string reason)
    {
        var matches = new List<string>();

        foreach (var tag in TruthyProtectedTags)
        {
            if (HasTruthyTag(tags, tag))
                matches.Add($"@{tag}=true");
        }

        var classification = GetTag(tags, "classification");
        if (!string.IsNullOrWhiteSpace(classification)
            && ProtectedClassifications.Contains(classification, StringComparer.OrdinalIgnoreCase))
        {
            matches.Add($"@classification={classification}");
        }

        protectionTags = matches;
        reason = string.Join(", ", matches);
        return matches.Count > 0;
    }

    private static bool HasTruthyTag(IReadOnlyDictionary<string, string> tags, string key)
    {
        var value = GetTag(tags, key);
        return value is not null
            && (value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || value.Equals("1", StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetTag(IReadOnlyDictionary<string, string> tags, string key)
    {
        foreach (var tag in tags)
        {
            if (tag.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                return tag.Value;
        }

        return null;
    }
}
