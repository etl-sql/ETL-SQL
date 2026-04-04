using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Linting;
using ETL_SQL.Core.Linting.Rules;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the LINT statement, performing static analysis on script files to identify potential issues and best practice violations.
    /// </summary>
    public class LintStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(LintStatement);

        /// <summary>Executes the LINT statement, running all registered static analysis rules and returning the results as a table.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var lintStmt = (LintStatement)statement;
            string? sql = null;

            if (lintStmt.ScriptPath != null)
            {
                var fullPath = Path.IsPathRooted(lintStmt.ScriptPath) 
                    ? lintStmt.ScriptPath 
                    : Path.Combine(Directory.GetCurrentDirectory(), lintStmt.ScriptPath);

                if (!File.Exists(fullPath))
                {
                    throw new Exception($"Script file not found: {fullPath}");
                }
                sql = await File.ReadAllTextAsync(fullPath);
            }
            else
            {
                throw new Exception("LINT without a file path is not yet supported in this context. Use LINT 'path.sql';");
            }

            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            var parser = new ETL_SQL.Core.Parser.Parser(tokens, sql);
            var script = parser.Parse();

            var linter = new Linter();
            foreach (var type in typeof(ILintRule).Assembly.GetTypes()
                .Where(t => typeof(ILintRule).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract))
            {
                if (Activator.CreateInstance(type) is ILintRule rule)
                    linter.AddRule(rule);
            }

            var results = await linter.AnalyzeAsync(script, new DefaultLintContext());

            var table = new DataTable();
            table.AddColumn("Severity");
            table.AddColumn("Rule");
            table.AddColumn("Line");
            table.AddColumn("Message");

            foreach (var res in results.OrderBy(r => r.LineNumber))
            {
                var row = new Row();
                row["Severity"] = res.Severity.ToString();
                row["Rule"] = res.RuleName;
                row["Line"] = res.LineNumber;
                row["Message"] = res.Message;
                table.AddRow(row);
            }

            context.LastResult = table;
        }
    }
}
