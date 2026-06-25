using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using ETL_SQL.Engine;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles the SHOW VARIABLES and SHOW LOCAL VARIABLES statements.
/// Lists all variables in the current execution scope as a DataTable.
/// </summary>
public class ShowVariablesStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(ShowVariablesStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (ShowVariablesStatement)statement;

        var table = new DataTable();
        table.AddColumn("Name");
        table.AddColumn("Value");
        table.AddColumn("DataType");
        table.AddColumn("Scope");
        table.AddColumn("IsSensitive");

        var variables = stmt.IsLocalOnly ? context.VarContext.CurrentVariables : context.VarContext.Variables;
        var metadata = stmt.IsLocalOnly ? context.VarContext.CurrentMetadata : context.VarContext.VariableMetadata;

        foreach (var variable in variables.OrderBy(v => v.Key))
        {
            var row = new Row();
            row["Name"] = variable.Key;

            bool isSensitive = false;
            if (metadata.TryGetValue(variable.Key, out var m))
            {
                isSensitive = m.IsSensitive;
            }

            object? value = variable.Value;
            if (isSensitive && !context.ShowPassword)
            {
                value = "*******";
            }

            row["Value"] = value;
            row["DataType"] = GetDataTypeName(value);
            row["Scope"] = stmt.IsLocalOnly ? "Local" : "Global";
            row["IsSensitive"] = isSensitive;

            await table.AddRowAsync(row);
        }

        if (stmt.IntoTable != null)
        {
            await WriteToTempTable(stmt.IntoTable, table, context);
        }
        else
        {
            if (table.Rows.Count == 0)
            {
                context.Log("0 rows returned.", ConsoleColor.Cyan);
            }
            else
            {
                if (!context.RedirectOutput)
                {
                    ResultFormatter.PrintTable(table);
                }
            }

            context.LastResult = table;
            context.LastResultSets.Add(table);
            context.OnResultSet?.Invoke(table);
        }
    }

    private string GetDataTypeName(object? value)
    {
        if (value == null) return "NULL";
        if (value is int || value is long) return "INT";
        if (value is bool) return "BOOLEAN";
        if (value is double || value is decimal) return "DECIMAL";
        if (value is DateTime) return "DATETIME";
        if (value is string) return "STRING";
        if (value is IEnumerable<object>) return "LIST";
        return value.GetType().Name.ToUpper();
    }

    private async Task WriteToTempTable(string tableName, DataTable table, IExecutionContext context)
    {
        if (!context.Connections.ContainsKey(tableName))
        {
            context.Connections[tableName] = new InMemoryDataSource();
        }
        var destination = await context.ResolveDataSourceAsync(new TableReference(tableName));
        await destination.WriteBatches(new[] { table }.ToAsyncEnumerable());
    }
}

