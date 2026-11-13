using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Services;

public class CteManager(ILogger logger)
{
    private readonly ILogger _logger = logger;

    /// <summary>
    /// Evaluates the CTEs and registers each one under its name in <see cref="IExecutionContext.LocalSources"/>.
    /// Dispose the returned scope when the statement finishes: it disposes the CTE results and puts back
    /// whatever the names meant before (a <c>#temp</c> table, an outer CTE), or removes them.
    /// </summary>
    public async Task<CteScope> RegisterCtes(List<CteDefinition> ctes, IExecutionContext context)
    {
        var scope = new CteScope(context);
        try
        {
            foreach (var cte in ctes)
            {
                if (IsRecursive(cte, out var anchor, out var recursive, out var isDistinct))
                {
                    await EvaluateRecursiveCte(cte, anchor!, recursive!, isDistinct, context, scope);
                }
                else
                {
                    await EvaluateStandardCte(cte, context, scope);
                }
            }
            return scope;
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }
    }

    private async Task EvaluateRecursiveCte(CteDefinition cte, Statement anchor, Statement recursive, bool isDistinct, IExecutionContext context, CteScope scope)
    {
        _logger.Debug("Evaluating RECURSIVE CTE: {CteName} ({UnionType})", cte.Name, isDistinct ? "UNION" : "UNION ALL");
        var finalResult = new DataTable();
        var currentStep = new DataTable();
        var seenKeys = isDistinct ? new HashSet<CompoundKey>() : null;

        // 1. Evaluate Anchor Member
        await foreach (var batch in context.ExecuteQuery(anchor))
        {
            if (cte.ColumnNames != null && cte.ColumnNames.Count > 0)
            {
                var oldNames = batch.ColumnNames.ToList();
                if (oldNames.Count != cte.ColumnNames.Count)
                    throw new ExecutionException($"CTE '{cte.Name}' has {cte.ColumnNames.Count} columns specified, but the anchor query returns {oldNames.Count} columns.", null, cte.Line, cte.Column);
                for (int i = 0; i < oldNames.Count; i++) batch.RenameColumn(oldNames[i], cte.ColumnNames[i]);
            }

            if (finalResult.ColumnNames.Count == 0) finalResult.SetColumns(batch.ColumnNames);
            if (currentStep.ColumnNames.Count == 0) currentStep.SetColumns(finalResult.ColumnNames);

            foreach (var r in batch.Rows)
            {
                await finalResult.AddRowAsync(r);
                await currentStep.AddRowAsync(r);
                seenKeys?.Add(MakeRowKey(r, finalResult.ColumnNames));
            }
        }

        // 2. Iterative Recursive Member
        int depth = 0;
        var colDefs = new List<ColumnDefinition>();
        var previousRecursiveDepth = context.CurrentRecursiveDepth;

        try
        {
            while (currentStep.Rows.Count > 0 && depth < context.MaxRecursiveDepth)
            {
                depth++;

                // Register currentStep as the CTE source for this iteration
                var mem = new InMemoryDataSource();
                mem.Validator = context as IDataValidator;
                mem.ExecutionContext = context;
                mem.MaxInMemoryBatches = context.MaxInMemoryBatches;

                // Type inference (only on first iteration to establish schema)
                if (depth == 1 && currentStep.Rows.Count > 0)
                {
                    var firstRow = currentStep.Rows[0];
                    foreach (var colName in currentStep.ColumnNames)
                    {
                        var val = firstRow[colName];
                        string type = "STRING";
                        // Intermediate iterations use DECIMAL for every numeric column. The schema is
                        // locked from the first iteration but later iterations can produce fractional
                        // values; typing INT here would Math.Truncate them mid-recursion (TypeConverter
                        // "INT" cast) and corrupt the result. DECIMAL holds integers losslessly. The
                        // final result is re-typed below once all values are known.
                        if (val is int || val is long || val is decimal || val is double || val is float) type = "DECIMAL";
                        else if (val is DateTime) type = "DATETIME";
                        else if (val is bool) type = "BOOLEAN";
                        colDefs.Add(new ColumnDefinition(colName, type, true));
                    }
                }
                else if (depth == 1)
                {
                    foreach (var colName in currentStep.ColumnNames) colDefs.Add(new ColumnDefinition(colName, "STRING", true));
                }

                mem.SetSchema(colDefs);
                await mem.WriteBatches(new[] { currentStep }.ToAsyncEnumerable());
                await scope.SetAsync(cte.Name, mem);

                var nextStep = new DataTable();
                nextStep.SetColumns(currentStep.ColumnNames);

                await foreach (var batch in context.ExecuteQuery(recursive))
                {
                    // Aligned by index to anchor schema
                    var alignedBatch = context.AlignColumns(new[] { batch }.ToAsyncEnumerable(), currentStep.ColumnNames.ToList());
                    await foreach (var aligned in alignedBatch)
                    {
                        foreach (var r in aligned.Rows)
                        {
                            if (isDistinct)
                            {
                                var key = MakeRowKey(r, nextStep.ColumnNames);
                                if (seenKeys!.Add(key))
                                {
                                    await finalResult.AddRowAsync(r);
                                    await nextStep.AddRowAsync(r);
                                }
                            }
                            else
                            {
                                await finalResult.AddRowAsync(r);
                                await nextStep.AddRowAsync(r);
                            }
                        }
                    }
                }
                currentStep = nextStep;
            }

            if (depth >= context.MaxRecursiveDepth && currentStep.Rows.Count > 0)
                throw new ExecutionException($"The maximum recursion {context.MaxRecursiveDepth} has been exhausted before statement completion for CTE '{cte.Name}'.", null, cte.Line, cte.Column);
        }
        finally
        {
            context.CurrentRecursiveDepth = previousRecursiveDepth;
        }

        var finalMem = new InMemoryDataSource();
        finalMem.Validator = context as IDataValidator;
        finalMem.ExecutionContext = context;
        finalMem.MaxInMemoryBatches = context.MaxInMemoryBatches;
        // Now that the full result is known, narrow each DECIMAL column to INT only if every
        // value across all iterations is integral. This keeps integer-valued recursive results
        // (e.g. hierarchy depths) displaying as integers while preserving any genuinely
        // fractional values as DECIMAL — without the mid-recursion truncation INT would cause.
        finalMem.SetSchema(NarrowIntegralColumns(colDefs, finalResult));
        await finalMem.WriteBatches(new[] { finalResult }.ToAsyncEnumerable());
        await scope.SetAsync(cte.Name, finalMem);
    }

    private async Task EvaluateStandardCte(CteDefinition cte, IExecutionContext context, CteScope scope)
    {
        var cteResult = new DataTable();
        await foreach (var batch in context.ExecuteQuery(cte.Query))
        {
            if (cte.ColumnNames != null && cte.ColumnNames.Count > 0)
            {
                var oldNames = batch.ColumnNames.ToList();
                if (oldNames.Count != cte.ColumnNames.Count)
                    throw new ExecutionException($"CTE '{cte.Name}' has {cte.ColumnNames.Count} columns specified, but the query returns {oldNames.Count} columns.", null, cte.Line, cte.Column);
                for (int i = 0; i < oldNames.Count; i++) batch.RenameColumn(oldNames[i], cte.ColumnNames[i]);
            }

            if (cteResult.Schema.ColumnCount == 0) cteResult.SetColumns(batch.ColumnNames);
            foreach (var r in batch.Rows) await cteResult.AddRowAsync(r);
        }
        var mem = new InMemoryDataSource();
        mem.Validator = context as IDataValidator;
        mem.ExecutionContext = context;
        mem.MaxInMemoryBatches = context.MaxInMemoryBatches;
        mem.SetSchema(cteResult.ColumnNames.Select(c => new ColumnDefinition(c, "STRING", false)));
        await mem.WriteBatches(new[] { cteResult }.ToAsyncEnumerable());
        await scope.SetAsync(cte.Name, mem);
    }

    private static CompoundKey MakeRowKey(Row r, IList<string> columnNames)
    {
        var values = new object?[columnNames.Count];
        for (int i = 0; i < columnNames.Count; i++)
            values[i] = r[columnNames[i]];
        return new CompoundKey(values);
    }

    private static bool IsIntegralValue(object? value) => value switch
    {
        int or long => true,
        decimal d => d == Math.Truncate(d),
        double db => db == Math.Truncate(db),
        float f => f == Math.Truncate(f),
        _ => false
    };

    /// <summary>
    /// Returns a copy of <paramref name="colDefs"/> where each DECIMAL column whose every
    /// non-null value in <paramref name="rows"/> is integral is narrowed to INT. Columns with
    /// any fractional value stay DECIMAL so no data is lost.
    /// </summary>
    private static List<ColumnDefinition> NarrowIntegralColumns(List<ColumnDefinition> colDefs, DataTable rows)
    {
        var result = new List<ColumnDefinition>(colDefs.Count);
        foreach (var col in colDefs)
        {
            if (col.DataType == "DECIMAL" && AllValuesIntegral(rows, col.ColumnName))
                result.Add(new ColumnDefinition(col.ColumnName, "INT", col.IsNullable));
            else
                result.Add(col);
        }
        return result;
    }

    private static bool AllValuesIntegral(DataTable rows, string columnName)
    {
        foreach (var row in rows.Rows)
        {
            var val = row[columnName];
            if (val is null) continue;
            if (!IsIntegralValue(val)) return false;
        }
        return true;
    }

    private bool IsRecursive(CteDefinition cte, out Statement? anchor, out Statement? recursive, out bool isDistinct)
    {
        anchor = null;
        recursive = null;
        isDistinct = false;
        if (cte.Query is SetOperationStatement setOp && (setOp.Operation == SetOpType.UNION_ALL || setOp.Operation == SetOpType.UNION))
        {
            isDistinct = setOp.Operation == SetOpType.UNION;
            if (setOp.Right.GetSourceTables().Contains(cte.Name, StringComparer.OrdinalIgnoreCase))
            {
                anchor = setOp.Left;
                recursive = setOp.Right;
                return true;
            }
        }
        return false;
    }
}

/// <summary>
/// The CTE names one statement registered. Disposing it disposes the CTE results and restores the
/// entries the names shadowed, so a CTE is visible only to its own statement.
/// </summary>
public sealed class CteScope(IExecutionContext context) : IAsyncDisposable
{
    private readonly IExecutionContext _context = context;
    // Name -> (the entry it shadowed, if any; the CTE result registered now). Ordered so restore is LIFO.
    private readonly List<(string Name, IDataSource? Shadowed, IDataSource Current)> _entries = [];
    private bool _disposed;

    /// <summary>Registers <paramref name="source"/> under <paramref name="name"/>, disposing an earlier result of this scope.</summary>
    public async Task SetAsync(string name, IDataSource source)
    {
        int i = _entries.FindIndex(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        if (i >= 0)
        {
            var entry = _entries[i];
            if (!ReferenceEquals(entry.Current, source)) await entry.Current.DisposeAsync();
            _entries[i] = entry with { Current = source };
        }
        else
        {
            _context.LocalSources.TryGetValue(name, out var shadowed);
            _entries.Add((name, shadowed, source));
        }
        _context.LocalSources[name] = source;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            var (name, shadowed, current) = _entries[i];
            // Put the name back only if the statement left our entry in place.
            if (_context.LocalSources.TryGetValue(name, out var now) && ReferenceEquals(now, current))
            {
                if (shadowed != null) _context.LocalSources[name] = shadowed;
                else _context.LocalSources.Remove(name);
            }
            try { await current.DisposeAsync(); } catch { /* cleanup must not mask the statement's own error */ }
        }
        _entries.Clear();
    }
}
