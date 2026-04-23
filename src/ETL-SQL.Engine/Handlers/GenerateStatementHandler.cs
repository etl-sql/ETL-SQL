using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using ETL_SQL.Core.Data;
using ETL_SQL.Engine.Storage;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the GENERATE statement, producing mock data based on rules and writing it to a target table or variable.
    /// </summary>
    public class GenerateStatementHandler(ILogger logger) : IStatementHandler
    {
        private readonly ILogger _logger = logger;

        public Type SupportedStatementType => typeof(GenerateStatement);

        /// <summary>Executes the GENERATE statement, creating the target data and writing it in batches.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (GenerateStatement)statement;
            _logger.Debug("Executing GENERATE for target {Target}", stmt.Target.TableName);

            // 1. Evaluate Row Count
            var rowCountObj = await context.EvaluationContext.EvaluateValue(stmt.RowCount, new Row());
            if (rowCountObj == null || !int.TryParse(rowCountObj.ToString(), out int rowCount) || rowCount < 0)
            {
                throw new ExecutionException($"Invalid or negative row count for GENERATE: {rowCountObj}");
            }

            // 2. Evaluate Seed from Options
            int? seed = null;
            if (stmt.Options != null && stmt.Options.TryGetValue("SEED", out var seedExpr))
            {
                var seedVal = await context.EvaluationContext.EvaluateValue(seedExpr, new Row());
                if (seedVal != null && int.TryParse(seedVal.ToString(), out int s)) seed = s;
            }

            // 3. Generate Data
            var generator = new DataGenerator(seed);
            var generatedRows = generator.GenerateRows(rowCount, stmt.Rules);

            // 4. Resolve Target DataSource
            // For GENERATE, we always overwrite the target (like SELECT INTO)
            IDataSource destination;
            if (stmt.Target.TableName.StartsWith("@"))
            {
                // Ensure variable exists as a table if it doesn't already
                destination = new VariableDataSource(stmt.Target.TableName, context);
            }
            else
            {
                // Ensure temp table exists if it starts with #
                if (stmt.Target.TableName.StartsWith("#") && !context.Connections.ContainsKey(stmt.Target.TableName))
                {
                    context.Connections[stmt.Target.TableName] = new InMemoryDataSource 
                    { 
                        Validator = context as IDataValidator,
                        ExecutionContext = context,
                        MaxInMemoryBatches = context.MaxInMemoryBatches
                    };
                }
                destination = await context.ResolveDataSourceAsync(stmt.Target);
            }

            if (destination == null)
            {
                throw new ExecutionException($"Could not resolve target data source: {stmt.Target.TableName}");
            }

            // 5. Truncate/Reset Target (overwrite behavior)
            await destination.TruncateAsync();

            // 6. Write Data in Batches
            var batch = new DataTable();
            var colNames = stmt.Rules.Select(r => r.ColumnName).ToList();
            batch.SetColumns(colNames);

            int currentBatchCount = 0;
            int totalWritten = 0;
            
            foreach (var rowDict in generatedRows)
            {
                var row = batch.NewRow();
                foreach (var kvp in rowDict)
                {
                    row[kvp.Key] = kvp.Value;
                }
                await batch.AddRowAsync(row);
                currentBatchCount++;
                totalWritten++;

                if (currentBatchCount >= context.BatchSize)
                {
                    await destination.WriteBatches(new[] { batch }.ToAsyncEnumerable());
                    batch = new DataTable();
                    batch.SetColumns(colNames);
                    currentBatchCount = 0;
                }
            }

            if (batch.Rows.Count > 0)
            {
                await destination.WriteBatches(new[] { batch }.ToAsyncEnumerable());
            }

            context.RowsProcessed += totalWritten;
            context.IncrementOperationCount(OperationType.MockData, count: totalWritten);
            
            _logger.Info("Generated {RowCount} rows into {Target}", totalWritten, stmt.Target.TableName);
        }
    }
}
