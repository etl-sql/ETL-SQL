using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Services;
/// <summary>
/// Validates row-level constraints for engine-owned in-memory data sources.
/// </summary>
internal sealed class DataConstraintValidator(ExpressionEvaluator expressionEvaluator, IDictionary<string, IDataSource> connections) : IDataValidator
{
    private readonly ExpressionEvaluator _expressionEvaluator = expressionEvaluator;
    private readonly IDictionary<string, IDataSource> _connections = connections;

    public async Task<bool> ValidateCheckConstraint(Expression expression, Row row)
    {
        var result = await _expressionEvaluator.Evaluate(expression, row);
        return result != null && Convert.ToBoolean(result);
    }

    public async Task<bool> ValidateForeignKey(ForeignKeyReference reference, List<string> sourceColumns, Row row)
    {
        string connName = reference.Table.ConnectionName ?? reference.Table.TableName;
        if (!_connections.TryGetValue(connName, out var dataSource)) return true;

        var sourceValues = sourceColumns.Select(col => row[col]).ToList();
        if (sourceValues.All(v => v == null || v == DBNull.Value)) return true;

        return await dataSource.ExistsAsync(reference.Columns, sourceValues);
    }
}
