using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;

namespace ETL_SQL.Analysis.Linting.Rules;

public class TagDrivenGovernancePolicyRule : ILintRule
{
    public const string SensitiveExportCode = "ETLSQL-GOV-TAG-SENSITIVE-EXPORT";
    public const string RestrictedPublicDatasetCode = "ETLSQL-GOV-TAG-RESTRICTED-PUBLIC-DATASET";
    public const string MissingMetadataCode = "ETLSQL-GOV-TAG-MISSING-METADATA";
    public const string GoldQualityCode = "ETLSQL-GOV-TAG-GOLD-METADATA";

    private static readonly string[] RequiredStewardshipTags =
        StewardshipTagCatalog.RequiredStewardshipTags.ToArray();

    public string Name => "TagDrivenGovernancePolicy";
    public string Description => "Flags protected-data governance issues on dataset publish/export and quality promotion boundaries.";

    public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
    {
        var results = new List<LintResult>();
        var scriptTags = new Dictionary<string, string>(script.Metadata, StringComparer.OrdinalIgnoreCase);
        var datasetTags = BuildDatasetTagIndex(script, scriptTags);

        foreach (var statement in Flatten(script.Statements))
        {
            if (statement is CreateDatasetStatement createDataset)
            {
                var tags = MergeTags(scriptTags, ExtractTags(createDataset.SourceQuery));
                CheckPublishedDataset(createDataset.TempTableName, createDataset.AccessLevel, tags, createDataset.Line, createDataset.Column, results);
            }
            else if (statement is PublishDatasetStatement publishDataset)
            {
                var tags = datasetTags.TryGetValue(NormalizeDatasetName(publishDataset.DatasetName), out var known)
                    ? MergeTags(scriptTags, known)
                    : scriptTags;
                CheckPublishedDataset(publishDataset.DatasetName, publishDataset.AccessLevel, tags, publishDataset.Line, publishDataset.Column, results);
            }
            else if (statement is ExportDatasetStatement exportDataset
                     && datasetTags.TryGetValue(NormalizeDatasetName(exportDataset.DatasetName), out var tags)
                     && IsProtected(tags)
                     && exportDataset.EncryptionMode == DatasetEncryptionMode.None)
            {
                Add(results, SensitiveExportCode, LintSeverity.Error, exportDataset.Line, exportDataset.Column,
                    $"EXPORT DATASET {exportDataset.DatasetName} exports protected data without ENCRYPT = PASSWORD or ENCRYPT = KEYFILE.");
            }
        }

        foreach (var scope in ExtractTagScopes(script))
        {
            if (HasTagValue(scope.Tags, "quality", "gold"))
            {
                var missing = RequiredStewardshipTags
                    .Where(tag => !HasTag(scope.Tags, tag))
                    .ToArray();
                if (missing.Length > 0)
                    Add(results, GoldQualityCode, LintSeverity.Warning, scope.Line, scope.Column,
                        $"@quality=gold should include complete stewardship metadata. Missing: {FormatTags(missing)}.");
            }
        }

        return Task.FromResult<IEnumerable<LintResult>>(results);
    }

    private static void CheckPublishedDataset(
        string datasetName,
        DatasetAccessLevel accessLevel,
        IReadOnlyDictionary<string, string> tags,
        int line,
        int column,
        List<LintResult> results)
    {
        if (accessLevel != DatasetAccessLevel.Public)
            return;

        var missing = RequiredStewardshipTags
            .Where(tag => !HasTag(tags, tag))
            .ToArray();
        if (missing.Length > 0)
            Add(results, MissingMetadataCode, LintSeverity.Error, line, column,
                $"Public dataset {datasetName} is missing required stewardship metadata: {FormatTags(missing)}.");

        if (HasProtectedClassification(tags))
            Add(results, RestrictedPublicDatasetCode, LintSeverity.Error, line, column,
                $"Public dataset {datasetName} carries @classification={GetTag(tags, "classification")}; restricted/confidential datasets must not be published as public.");
    }

    private static Dictionary<string, Dictionary<string, string>> BuildDatasetTagIndex(
        Script script,
        IReadOnlyDictionary<string, string> scriptTags)
    {
        var datasets = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var statement in Flatten(script.Statements).OfType<CreateDatasetStatement>())
        {
            datasets[NormalizeDatasetName(statement.TempTableName)] =
                MergeTags(scriptTags, ExtractTags(statement.SourceQuery));
        }
        return datasets;
    }

    private static Dictionary<string, string> ExtractTags(Statement? statement)
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scope in ExtractTagScopes(statement))
            foreach (var tag in scope.Tags)
                tags[tag.Key] = tag.Value;
        return tags;
    }

    private static IEnumerable<TagScope> ExtractTagScopes(Script script)
    {
        if (script.Metadata.Count > 0)
            yield return new TagScope(script.Metadata, script.Line, script.Column);

        foreach (var statement in script.Statements)
            foreach (var scope in ExtractTagScopes(statement))
                yield return scope;
    }

    private static IEnumerable<TagScope> ExtractTagScopes(Statement? statement)
    {
        if (statement is null)
            yield break;

        switch (statement)
        {
            case SelectStatement select:
                foreach (var column in select.Columns)
                    if (column.Metadata.Count > 0)
                        yield return new TagScope(column.Metadata, column.Line, column.Column);
                if (select.FromTable.Metadata.Count > 0)
                    yield return new TagScope(select.FromTable.Metadata, select.FromTable.Line, select.FromTable.Column);
                foreach (var join in select.Joins)
                    if (join.Table.Metadata.Count > 0)
                        yield return new TagScope(join.Table.Metadata, join.Table.Line, join.Table.Column);
                if (select.FromTable.Subquery is not null)
                    foreach (var scope in ExtractTagScopes(select.FromTable.Subquery))
                        yield return scope;
                foreach (var join in select.Joins)
                    if (join.Table.Subquery is not null)
                        foreach (var scope in ExtractTagScopes(join.Table.Subquery))
                            yield return scope;
                break;

            case CreateTableStatement createTable:
                if (createTable.TargetTable.Metadata.Count > 0)
                    yield return new TagScope(createTable.TargetTable.Metadata, createTable.TargetTable.Line, createTable.TargetTable.Column);
                foreach (var column in createTable.Columns)
                    if (column.Metadata.Count > 0)
                        yield return new TagScope(column.Metadata, column.Line, column.Column);
                break;

            case BulkInsertStatement bulkInsert:
                if (bulkInsert.Metadata.Count > 0)
                    yield return new TagScope(bulkInsert.Metadata, bulkInsert.Line, bulkInsert.Column);
                break;

            case CreateDatasetStatement dataset:
                foreach (var scope in ExtractTagScopes(dataset.SourceQuery))
                    yield return scope;
                break;

            case CreateVisualStatement visual when visual.Source.IsInlineSelect:
                foreach (var scope in ExtractTagScopes(visual.Source.InlineSelect))
                    yield return scope;
                break;

            case InsertStatement insert when insert.SelectQuery is not null:
                foreach (var scope in ExtractTagScopes(insert.SelectQuery))
                    yield return scope;
                break;

            case SetOperationStatement setOperation:
                foreach (var scope in ExtractTagScopes(setOperation.Left))
                    yield return scope;
                foreach (var scope in ExtractTagScopes(setOperation.Right))
                    yield return scope;
                break;
        }
    }

    private static IEnumerable<Statement> Flatten(IEnumerable<Statement> statements)
    {
        foreach (var statement in statements)
        {
            yield return statement;

            IEnumerable<Statement> children = statement switch
            {
                BlockStatement block => block.Statements,
                IfStatement ifStmt => new[] { ifStmt.IfBody }
                    .Concat(ifStmt.ElseIfClauses?.Select(c => c.Body) ?? [])
                    .Concat(ifStmt.ElseBody is null ? [] : [ifStmt.ElseBody]),
                WhileStatement whileStmt => [whileStmt.Body],
                ForStatement forStmt => [forStmt.Body],
                ForeachStatement foreachStmt => [foreachStmt.Body],
                TryCatchStatement tryCatch => [tryCatch.TryBody, tryCatch.CatchBody],
                CreateProcedureStatement proc => [proc.Body],
                CreateFunctionStatement func => [func.Body],
                ParallelStatement parallel => [parallel.Body],
                ParallelForStatement parallelFor => [parallelFor.Body],
                _ => []
            };

            foreach (var child in Flatten(children))
                yield return child;
        }
    }

    private static Dictionary<string, string> MergeTags(params IReadOnlyDictionary<string, string>[] tagSets)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tagSet in tagSets)
            foreach (var tag in tagSet)
                merged[tag.Key] = tag.Value;
        return merged;
    }

    private static bool IsProtected(IReadOnlyDictionary<string, string> tags) =>
        HasTruthyTag(tags, "pii")
        || HasTruthyTag(tags, "phi")
        || HasTruthyTag(tags, "pci")
        || HasTruthyTag(tags, "sensitive")
        || HasProtectedClassification(tags);

    private static bool HasProtectedClassification(IReadOnlyDictionary<string, string> tags)
    {
        var classification = GetTag(tags, "classification");
        return classification is not null
            && (classification.Equals("confidential", StringComparison.OrdinalIgnoreCase)
                || classification.Equals("restricted", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasTruthyTag(IReadOnlyDictionary<string, string> tags, string key)
    {
        var value = GetTag(tags, key);
        return value is not null
            && (value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || value.Equals("1", StringComparison.OrdinalIgnoreCase));
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

    private static string NormalizeDatasetName(string name) => name.TrimStart('&');

    private static string FormatTags(IEnumerable<string> tags) =>
        string.Join(", ", tags.Select(tag => "@" + tag));

    private static void Add(
        List<LintResult> results,
        string code,
        LintSeverity severity,
        int line,
        int column,
        string message) =>
        results.Add(new LintResult
        {
            RuleName = "TagDrivenGovernancePolicy",
            Code = code,
            Severity = severity,
            Message = message,
            LineNumber = line,
            ColumnNumber = column
        });

    private sealed record TagScope(IReadOnlyDictionary<string, string> Tags, int Line, int Column);
}
