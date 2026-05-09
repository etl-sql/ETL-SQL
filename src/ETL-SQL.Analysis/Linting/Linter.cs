using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Analysis.Linting
{
    /// <summary>
    /// Orchestrates the linting process by executing multiple <see cref="ILintRule"/> instances.
    /// </summary>
    public class Linter
    {
        private readonly List<ILintRule> _rules = new();

        /// <summary>Adds a new linting rule to the linter.</summary>
        public void AddRule(ILintRule rule)
        {
            _rules.Add(rule);
        }

        /// <summary>Checks if a rule of the specified type exists.</summary>
        public bool HasRuleOfType(Type type) => _rules.Any(r => r.GetType() == type);

        /// <summary>Analyzes the script against all registered rules.</summary>
        public async Task<List<LintResult>> AnalyzeAsync(Script script, ILintContext context)
        {
            var results = new List<LintResult>();
            
            // 1. Discovery Pass (populate in-script metadata)
            var overlay = new ScriptMetadataOverlay(context.Metadata);
            DiscoverScriptMetadata(script, overlay);
            
            // 2. Wrap context with overlay
            if (context is DefaultLintContext defaultContext)
            {
                defaultContext.Metadata = overlay;
            }
            
            // 3. Execute Rules
            foreach (var rule in _rules)
            {
                var ruleResults = await rule.AnalyzeAsync(script, context);
                results.AddRange(ruleResults);
            }
            return results;
        }

        private void DiscoverScriptMetadata(Script script, ScriptMetadataOverlay overlay)
        {
            foreach (var statement in script.Statements)
            {
                DiscoverFromStatement(statement, overlay);
            }
        }

        private void DiscoverFromStatement(Statement statement, ScriptMetadataOverlay overlay)
        {
            if (statement.Ctes != null)
            {
                foreach (var cte in statement.Ctes)
                {
                    overlay.RegisterTable("DEFAULT", cte.Name);
                    if (cte.ColumnNames != null)
                    {
                        foreach (var col in cte.ColumnNames) overlay.RegisterColumn("DEFAULT", cte.Name, col);
                    }
                }
            }

            if (statement is CreateConnectionStatement conn)
            {
                overlay.RegisterConnection(conn.name, conn.type ?? "UNKNOWN");
            }
            else if (statement is CreateTableStatement create)
            {
                string connName = create.TargetTable.ConnectionName ?? "DEFAULT";
                string tableName = NormalizeName(create.TargetTable.TableName);
                overlay.RegisterTable(connName, tableName);
                foreach (var col in create.Columns)
                {
                    overlay.RegisterColumn(connName, tableName, col.ColumnName);
                }
            }
            else if (statement is ExecutePushdownStatement pushdown)
            {
                string connName = pushdown.ConnectionName is IdentifierExpression id ? id.Name : "DEFAULT";
                DiscoverFromNativeBlock(pushdown.SqlText, connName, overlay);
            }
            else if (statement is BlockStatement block)
            {
                foreach (var s in block.Statements) DiscoverFromStatement(s, overlay);
            }
            else if (statement is IfStatement ifStmt)
            {
                DiscoverFromStatement(ifStmt.IfBody, overlay);
                if (ifStmt.ElseIfClauses != null)
                {
                    foreach (var ei in ifStmt.ElseIfClauses) DiscoverFromStatement(ei.Body, overlay);
                }
                if (ifStmt.ElseBody != null) DiscoverFromStatement(ifStmt.ElseBody, overlay);
            }
            else if (statement is WhileStatement whileStmt)
            {
                DiscoverFromStatement(whileStmt.Body, overlay);
            }
            else if (statement is ForStatement forStmt)
            {
                DiscoverFromStatement(forStmt.Body, overlay);
            }
            else if (statement is ForeachStatement foreachStmt)
            {
                DiscoverFromStatement(foreachStmt.Body, overlay);
            }
        }

        private void DiscoverFromNativeBlock(string sql, string connectionName, ScriptMetadataOverlay overlay)
        {
            if (string.IsNullOrWhiteSpace(sql)) return;

            // 1. Try Parsing as ETL-SQL
            try
            {
                var lexer = new Lexer(sql);
                var tokens = lexer.Tokenize();
                var parser = new ETL_SQL.Core.Parser.Parser(tokens, sql);
                while (parser.Current.Type != TokenType.EOF)
                {
                    var stmt = parser.ParseStatement();
                    if (stmt is CreateTableStatement create)
                    {
                        string tableName = NormalizeName(create.TargetTable.TableName);
                        overlay.RegisterTable(connectionName, tableName);
                        foreach (var col in create.Columns) overlay.RegisterColumn(connectionName, tableName, col.ColumnName);
                    }
                }
                return; // Success
            }
            catch { /* Fallback to Regex */ }

            // 2. Regex Fallback for "Big Stuff" discovery
            // CREATE TABLE [dbo.]TableName
            var createMatches = Regex.Matches(sql, @"CREATE\s+TABLE\s+(?:[\w\[\]""]+\.)?([\w\[\]""]+)", RegexOptions.IgnoreCase);
            foreach (Match match in createMatches)
            {
                string tableName = NormalizeName(match.Groups[1].Value);
                overlay.RegisterTable(connectionName, tableName);
            }
        }

        private string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            // Strip brackets, quotes, and common schema prefixes like dbo.
            string clean = name.Trim('[', ']', '"');
            if (clean.Contains('.')) clean = clean.Split('.').Last();
            return clean;
        }
    }
}
