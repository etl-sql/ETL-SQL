using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Services;

public sealed record WatermarkConfig(
    string Column,
    string Key,
    string? InitialValue,
    bool Inclusive,
    string StateKey);

/// <summary>
/// Manages declarative incremental watermarks on SELECT queries (WITH (WATERMARK = 'column', ...)).
/// Handles state retrieval, WHERE filter predicate injection, and high-water mark tracking and persistence.
/// </summary>
public static class WatermarkManager
{
    public static bool HasWatermark(TableReference tableRef)
    {
        if (tableRef == null || tableRef.Options == null) return false;
        return tableRef.Options.ContainsKey("WATERMARK");
    }

    public static WatermarkConfig ParseConfig(TableReference tableRef, IExecutionContext context)
    {
        var watermarkExpr = tableRef.Options["WATERMARK"];
        string column = ExtractStringValue(watermarkExpr)
            ?? throw new SyntaxException("WITH (WATERMARK = ...) requires a column name string or identifier.");

        string? key = null;
        if (tableRef.Options.TryGetValue("KEY", out var keyExpr))
            key = ExtractStringValue(keyExpr);

        string? initial = null;
        if (tableRef.Options.TryGetValue("INITIAL", out var initialExpr))
            initial = ExtractStringValue(initialExpr);

        bool inclusive = false;
        if (tableRef.Options.TryGetValue("INCLUSIVE", out var incExpr))
        {
            var str = ExtractStringValue(incExpr);
            inclusive = bool.TryParse(str, out var b) && b;
        }
        else if (tableRef.Options.TryGetValue("STRICT", out var strictExpr))
        {
            var str = ExtractStringValue(strictExpr);
            if (bool.TryParse(str, out var strict))
                inclusive = !strict;
        }

        string tableKey = !string.IsNullOrEmpty(tableRef.FullyQualifiedName)
            ? tableRef.FullyQualifiedName
            : tableRef.TableName;
        string stateKey = !string.IsNullOrEmpty(key) ? key : $"{tableKey}:{column}";

        return new WatermarkConfig(column, key ?? stateKey, initial, inclusive, stateKey);
    }

    public static async Task<string?> GetCurrentWatermarkAsync(WatermarkConfig config, IExecutionContext context)
    {
        // 1. Check in-flight pending updates from earlier statements in this session
        if (context.PendingJobStateUpdates.TryGetValue(config.StateKey, out var pendingVal))
            return pendingVal;

        // 2. Check in-memory session state from committed runs in this session
        if (context.SessionJobState != null && context.SessionJobState.TryGetValue(config.StateKey, out var sessionVal))
            return sessionVal;

        // 3. Check persistent store or local state
        if (context.JobId.IsAssigned)
        {
            var store = context.ServiceProvider.GetService(typeof(IJobHistoryStore)) as IJobHistoryStore;
            if (store != null)
            {
                var val = await store.GetJobStateAsync(context.JobId, config.StateKey);
                if (!string.IsNullOrEmpty(val)) return val;
            }
        }
        else
        {
            var localVal = GetLocalJobState(context, config.StateKey);
            if (!string.IsNullOrEmpty(localVal)) return localVal;
        }

        // 3. Fallback to INITIAL value
        return config.InitialValue;
    }

    public static SelectStatement InjectWatermarkFilter(SelectStatement stmt, WatermarkConfig config, string? currentWatermark)
    {
        if (currentWatermark == null) return stmt;

        var colExpr = new IdentifierExpression(config.Column);
        var op = config.Inclusive ? TokenType.GREATER_EQUALS : TokenType.GREATER_THAN;

        Expression valExpr;
        if (decimal.TryParse(currentWatermark, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _))
            valExpr = new LiteralExpression(currentWatermark, TokenType.NUMBER);
        else
            valExpr = new LiteralExpression(currentWatermark, TokenType.STRING_LITERAL);

        var watermarkPredicate = new BinaryExpression(colExpr, op, valExpr);

        var newWhere = stmt.WhereClause != null
            ? new BinaryExpression(stmt.WhereClause, TokenType.AND, watermarkPredicate)
            : watermarkPredicate;

        return stmt with { WhereClause = newWhere };
    }

    public static IAsyncEnumerable<DataTable> TrackWatermarkStream(
        IAsyncEnumerable<DataTable> stream,
        WatermarkConfig config,
        IExecutionContext context,
        ILogger logger)
    {
        return TrackWatermarkStreamCore(stream, config, context, logger);
    }

    private static async IAsyncEnumerable<DataTable> TrackWatermarkStreamCore(
        IAsyncEnumerable<DataTable> stream,
        WatermarkConfig config,
        IExecutionContext context,
        ILogger logger)
    {
        object? maxVal = null;
        long rowCount = 0;

        await foreach (var batch in stream)
        {
            if (batch.Rows.Count > 0)
            {
                foreach (var row in batch.Rows)
                {
                    rowCount++;
                    object? cellVal = null;
                    if (row.Columns.TryGetValue(config.Column, out var val))
                    {
                        cellVal = val;
                    }
                    else
                    {
                        // Check case-insensitive column match
                        foreach (var kv in row.Columns)
                        {
                            if (string.Equals(kv.Key, config.Column, StringComparison.OrdinalIgnoreCase)
                                || kv.Key.EndsWith("." + config.Column, StringComparison.OrdinalIgnoreCase))
                            {
                                cellVal = kv.Value;
                                break;
                            }
                        }
                    }

                    if (cellVal != null && cellVal != DBNull.Value)
                    {
                        if (maxVal == null || CompareWatermarkValues(cellVal, maxVal) > 0)
                        {
                            maxVal = cellVal;
                        }
                    }
                }
            }
            yield return batch;
        }

        if (maxVal != null)
        {
            string formattedVal = FormatWatermarkValue(maxVal);
            context.PendingJobStateUpdates[config.StateKey] = formattedVal;
            logger.Debug($"[WATERMARK] Updated watermark '{config.StateKey}' to '{formattedVal}' ({rowCount} rows processed).");
        }
    }

    public static int CompareWatermarkValues(object a, object b)
    {
        if (a is DateTime da && b is DateTime db) return da.CompareTo(db);
        if (DateTime.TryParse(a.ToString(), out var parsedA) && DateTime.TryParse(b.ToString(), out var parsedB))
            return parsedA.CompareTo(parsedB);
        if (decimal.TryParse(a.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var decA)
            && decimal.TryParse(b.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var decB))
            return decA.CompareTo(decB);
        return string.Compare(a.ToString(), b.ToString(), StringComparison.Ordinal);
    }

    public static string FormatWatermarkValue(object val)
    {
        if (val is DateTime dt) return dt.ToString("yyyy-MM-dd HH:mm:ss.fff");
        if (val is DateTimeOffset dto) return dto.ToString("yyyy-MM-dd HH:mm:ss.fff zzz");
        return val.ToString() ?? "";
    }

    private static string? ExtractStringValue(Expression expr) => expr switch
    {
        LiteralExpression lit => lit.Value?.ToString(),
        IdentifierExpression id => id.Name,
        _ => expr.ToSql().Trim('\'', '"')
    };

    private static string? GetLocalJobState(IExecutionContext ctx, string key)
    {
        if (string.IsNullOrEmpty(ctx.CurrentScriptPath)) return null;
        try
        {
            var stateFile = Path.ChangeExtension(ctx.CurrentScriptPath, ".etlstate");
            if (File.Exists(stateFile))
            {
                var text = File.ReadAllText(stateFile);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(text);
                if (dict != null && dict.TryGetValue(key, out var val))
                    return val;
            }
        }
        catch (Exception ex)
        {
            ctx.Logger.Warning("Failed to read local job state: " + ex.Message);
        }
        return null;
    }
}
