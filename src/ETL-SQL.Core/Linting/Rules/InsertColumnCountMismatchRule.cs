using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Linting.Rules
{
    public class InsertColumnCountMismatchRule : ILintRule
    {
        public string Name => "InsertColumnCountMismatch";
        public string Description => "Warns when an INSERT INTO omits the column list but the target table has more columns than the SELECT provides (silent null injection).";

        public async Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
        {
            var results = new List<LintResult>();
            if (context.Metadata == null) return results;

            foreach (var stmt in script.Statements)
            {
                if (stmt is InsertStatement insert)
                {
                    if (insert.Columns == null || insert.Columns.Count == 0)
                    {
                        if (insert.SelectQuery is SelectStatement select)
                        {
                            bool hasWildcard = select.Columns.Any(c => c.expression.ToSql().Contains("*"));
                            if (hasWildcard) continue; // Can't reliably check without full schema resolution

                            string? connName = insert.TargetTable.ConnectionName ?? "";
                            string? tableName = insert.TargetTable.TableName;

                            if (!string.IsNullOrEmpty(tableName))
                            {
                                var targetCols = await context.Metadata.GetColumnsAsync(connName, tableName);
                                var targetColList = targetCols?.ToList() ?? new List<string>();

                                if (targetColList.Count > 0 && targetColList.Count > select.Columns.Count)
                                {
                                    results.Add(new LintResult
                                    {
                                        RuleName = Name,
                                        Message = $"Target table '{tableName}' has {targetColList.Count} columns, but SELECT only provides {select.Columns.Count}. Explicitly declare the target columns to avoid silent null injection.",
                                        Severity = LintSeverity.Warning,
                                        LineNumber = insert.Line,
                                        ColumnNumber = insert.Column
                                    });
                                }
                            }
                        }
                    }
                }
            }
            return results;
        }
    }
}
