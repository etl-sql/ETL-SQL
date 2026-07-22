using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;

namespace ETL_SQL.Engine.Handlers;

public static class TagGovernanceRuntimePolicy
{
    private static readonly string[] RequiredStewardshipTags =
        StewardshipTagCatalog.RequiredStewardshipTags.ToArray();

    public static void EnforceDatasetPublish(
        string datasetName,
        DatasetAccessLevel accessLevel,
        IReadOnlyDictionary<string, string> tags,
        int line,
        int column)
    {
        if (HasTagValue(tags, "quality", "gold"))
            EnforceCompleteMetadata(
                tags,
                line,
                column,
                $"Dataset '{datasetName}' uses @quality=gold but is missing required stewardship metadata");

        if (accessLevel != DatasetAccessLevel.Public)
            return;

        EnforceCompleteMetadata(
            tags,
            line,
            column,
            $"Public dataset '{datasetName}' is missing required stewardship metadata");

        var classification = GetTag(tags, "classification");
        if (classification is not null
            && (classification.Equals("confidential", StringComparison.OrdinalIgnoreCase)
                || classification.Equals("restricted", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ExecutionException(
                $"Public dataset '{datasetName}' carries @classification={classification}. Restricted or confidential datasets must be private.",
                null, line, column);
        }
    }

    public static Dictionary<string, string> CollectDatasetTags(CreateDatasetStatement stmt, IExecutionContext context)
    {
        var tags = new Dictionary<string, string>(context.LineageTracker.GlobalMetadata, StringComparer.OrdinalIgnoreCase);

        foreach (var tag in context.LineageTracker.GetTableMetadata(stmt.TempTableName))
            tags[tag.Key] = tag.Value;

        foreach (var entry in context.LineageTracker.GetLineage(stmt.TempTableName))
            foreach (var tag in entry.Metadata)
                tags[tag.Key] = tag.Value;

        return tags;
    }

    public static Dictionary<string, string> CollectGlobalTags(IExecutionContext context) =>
        new(context.LineageTracker.GlobalMetadata, StringComparer.OrdinalIgnoreCase);

    private static void EnforceCompleteMetadata(
        IReadOnlyDictionary<string, string> tags,
        int line,
        int column,
        string messagePrefix)
    {
        var missing = RequiredStewardshipTags.Where(tag => !HasTag(tags, tag)).ToArray();
        if (missing.Length == 0)
            return;

        throw new ExecutionException(
            $"{messagePrefix}: {FormatTags(missing)}.",
            null, line, column);
    }

    private static bool HasTagValue(IReadOnlyDictionary<string, string> tags, string key, string value) =>
        GetTag(tags, key)?.Equals(value, StringComparison.OrdinalIgnoreCase) == true;

    private static bool HasTag(IReadOnlyDictionary<string, string> tags, string key) =>
        tags.Keys.Any(tag => tag.Equals(key, StringComparison.OrdinalIgnoreCase));

    private static string? GetTag(IReadOnlyDictionary<string, string> tags, string key)
    {
        foreach (var tag in tags)
            if (tag.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                return tag.Value;
        return null;
    }

    private static string FormatTags(IEnumerable<string> tags) =>
        string.Join(", ", tags.Select(tag => "@" + tag));
}
