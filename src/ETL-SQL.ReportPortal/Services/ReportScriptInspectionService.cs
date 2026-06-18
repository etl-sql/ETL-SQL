using System.Security.Cryptography;
using System.Text.Json;
using ETL_SQL.Analysis.Lineage;
using ETL_SQL.Core;
using ETL_SQL.Core.Storage;
using ETL_SQL.Core.Parser;
using ETL_SQL.Reporting;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Models;
using Microsoft.Extensions.Logging;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.ReportPortal.Services;

public class ReportScriptInspectionService(
    PortalConfig portalConfig,
    ILogger<ReportScriptInspectionService> logger,
    IArtifactStorage artifacts)
{
    public async Task<Dictionary<string, string>> ReadScriptMetadataAsync(string scriptPath)
    {
        // Resolve within the configured script root like the sibling methods, so a caller passing
        // a request-derived path cannot read arbitrary files (path traversal / arbitrary read).
        if (!PortalPathGuard.TryResolveScript(portalConfig, scriptPath, out var resolvedScriptPath))
        {
            logger.LogWarning("Rejected script metadata read outside the configured script root: {ScriptPath}", scriptPath);
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var scriptText = await System.IO.File.ReadAllTextAsync(resolvedScriptPath);
        var tokens = new Lexer(scriptText).Tokenize();
        var script = new CoreParser(tokens, scriptText).Parse();
        return new Dictionary<string, string>(script.Metadata, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<ReportScriptValidationDto> ValidateResolvedScriptAsync(string resolvedScriptPath)
    {
        if (!System.IO.File.Exists(resolvedScriptPath))
        {
            return new ReportScriptValidationDto(
                false,
                resolvedScriptPath,
                null,
                null,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                Array.Empty<ReportParameterDto>(),
                ["Script file not found."]);
        }

        if (!resolvedScriptPath.EndsWith(".rptsql", StringComparison.OrdinalIgnoreCase))
        {
            return new ReportScriptValidationDto(
                false,
                resolvedScriptPath,
                null,
                System.IO.File.GetLastWriteTimeUtc(resolvedScriptPath),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                Array.Empty<ReportParameterDto>(),
                ["Only .rptsql files may be published as reports."]);
        }

        var scriptText = await System.IO.File.ReadAllTextAsync(resolvedScriptPath);
        try
        {
            var tokens = new Lexer(scriptText).Tokenize();
            var script = new CoreParser(tokens, scriptText).Parse();
            var parameters = script.Statements
                .OfType<DeclareStatement>()
                .Where(d => d.IsInput)
                .Select(d => new ReportParameterDto(
                    d.VariableName,
                    d.DataType,
                    d.InitialValue is LiteralExpression lit ? lit.Value?.ToString() : null,
                    d.InitialValue is null,
                    d.Description))
                .ToList();
            var hash = "sha256:" + Convert.ToHexString(
                SHA256.HashData(await System.IO.File.ReadAllBytesAsync(resolvedScriptPath))).ToLowerInvariant();

            return new ReportScriptValidationDto(
                true,
                resolvedScriptPath,
                hash,
                System.IO.File.GetLastWriteTimeUtc(resolvedScriptPath),
                new Dictionary<string, string>(script.Metadata, StringComparer.OrdinalIgnoreCase),
                parameters,
                Array.Empty<string>());
        }
        catch (Exception ex)
        {
            // Log the full detail server-side; return a generic message to the client so internal
            // paths/offsets in the parser exception are not surfaced to the API consumer.
            logger.LogWarning(ex, "Script validation failed for {ScriptPath}", resolvedScriptPath);
            return new ReportScriptValidationDto(
                false,
                resolvedScriptPath,
                null,
                System.IO.File.GetLastWriteTimeUtc(resolvedScriptPath),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                Array.Empty<ReportParameterDto>(),
                ["The script could not be parsed. See server logs for details."]);
        }
    }

    public async Task<IReadOnlyList<ReportDependencyManifestDatasetDto>> ReadManifestDatasetsAsync(ReportSnapshot? snapshot)
    {
        if (snapshot is null) return Array.Empty<ReportDependencyManifestDatasetDto>();
        var manifestKey = PortalPathGuard.ToSnapshotKey(portalConfig, snapshot.ManifestPath);
        if (manifestKey is null)
            return Array.Empty<ReportDependencyManifestDatasetDto>();
        if (!await artifacts.ExistsAsync(ArtifactArea.Snapshots, manifestKey))
            return Array.Empty<ReportDependencyManifestDatasetDto>();

        try
        {
            var json = await artifacts.ReadAllTextAsync(ArtifactArea.Snapshots, manifestKey);
            var manifest = JsonSerializer.Deserialize<ReportManifest>(json);
            if (manifest is null) return Array.Empty<ReportDependencyManifestDatasetDto>();

            return manifest.Datasets
                .Select(d => new ReportDependencyManifestDatasetDto(
                    d.TempTableName,
                    d.RefreshInterval,
                    d.Ttl,
                    d.LastRefresh,
                    d.RowCount))
                .ToList();
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse report manifest at {ManifestPath}", snapshot.ManifestPath);
            return Array.Empty<ReportDependencyManifestDatasetDto>();
        }
    }

    public async Task<IReadOnlyList<string>> ReadScriptSourceTablesAsync(string scriptPath)
    {
        if (!PortalPathGuard.TryResolveScript(portalConfig, scriptPath, out var resolvedScriptPath))
            return Array.Empty<string>();
        if (!System.IO.File.Exists(resolvedScriptPath))
            return Array.Empty<string>();

        return ParseSourceTables(await System.IO.File.ReadAllTextAsync(resolvedScriptPath));
    }

    public async Task<IReadOnlyList<ReportDependencyLineageDto>> ReadScriptLineageAsync(string scriptPath)
    {
        if (!PortalPathGuard.TryResolveScript(portalConfig, scriptPath, out var resolvedScriptPath))
            return Array.Empty<ReportDependencyLineageDto>();
        if (!System.IO.File.Exists(resolvedScriptPath))
            return Array.Empty<ReportDependencyLineageDto>();

        try
        {
            var scriptText = await System.IO.File.ReadAllTextAsync(resolvedScriptPath);
            var tokens = new Lexer(scriptText).Tokenize();
            var script = new CoreParser(tokens, scriptText).Parse();
            var tracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
            new LineageAnalyzer(tracker).Analyze(script);

            return tracker.GetFullLineage()
                .Select(e => new ReportDependencyLineageDto(
                    e.TargetTable,
                    e.TargetColumn,
                    e.Operation,
                    e.SourceTables.ToList(),
                    e.SourceColumns.ToList(),
                    e.Metadata,
                    e.Line,
                    e.TransformationKind == TransformationKind.Unknown ? null : e.TransformationKind.ToString(),
                    e.TransformationExpression,
                    e.FunctionsApplied,
                    e.DerivedFromDescriptions))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Lineage extraction failed for {ScriptPath}; returning no lineage", resolvedScriptPath);
            return Array.Empty<ReportDependencyLineageDto>();
        }
    }

    public async Task<string?> ReadCurrentScriptHashAsync(string scriptPath)
    {
        if (!PortalPathGuard.TryResolveScript(portalConfig, scriptPath, out var resolvedScriptPath))
            return null;
        if (!System.IO.File.Exists(resolvedScriptPath))
            return null;

        var bytes = await System.IO.File.ReadAllBytesAsync(resolvedScriptPath);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public IReadOnlyList<string> ParseSourceTables(string? scriptText)
    {
        if (string.IsNullOrWhiteSpace(scriptText)) return Array.Empty<string>();
        try
        {
            var tokens = new Lexer(scriptText).Tokenize();
            var script = new CoreParser(tokens, scriptText).Parse();
            return script.Statements
                .SelectMany(s => s.GetSourceTables())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse source tables from script text; returning none");
            return Array.Empty<string>();
        }
    }

    public IReadOnlyList<ReportDependencySourceDto> BuildSourceDtos(IEnumerable<string> sources, string kind) =>
        sources
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(s =>
            {
                var parts = s.Split('.', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var connection = parts.Length == 2 && !parts[0].StartsWith("#", StringComparison.Ordinal) ? parts[0] : null;
                var objectName = parts.Length == 2 ? parts[1] : s;
                return new ReportDependencySourceDto(s, connection, objectName, kind);
            })
            .ToList();
}
