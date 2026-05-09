using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ETL_SQL.Analysis.Linting.Rules
{
    /// <summary>
    /// CQ-R2: Warns when a CREATE CONNECTION defines a connection name that is never
    /// referenced in any FROM clause, INSERT target, ALTER CONNECTION, or DROP CONNECTION
    /// within the same script.
    /// </summary>
    public class UnusedConnectionRule : ILintRule
    {
        public string Name        => "UnusedConnection";
        public string Description => "Warns when a connection is created but never used in the script.";

        public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
        {
            var results = new List<LintResult>();

            // Collect all defined connection names with their definition line
            var defined = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var stmt in script.Statements)
            {
                if (stmt is CreateConnectionStatement cc && cc.Mode == ObjectCreationMode.Create)
                    defined[cc.ConnectionName] = stmt.Line;
            }

            if (defined.Count == 0)
                return Task.FromResult<IEnumerable<LintResult>>(results);

            // Collect all connection names that are actually referenced
            var used = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var stmt in script.Statements)
            {
                if (stmt is CreateConnectionStatement) continue;
                foreach (var name in CollectConnectionRefs(stmt))
                    used.Add(name);
            }

            foreach (var (name, line) in defined)
            {
                if (!used.Contains(name))
                {
                    results.Add(new LintResult
                    {
                        RuleName     = Name,
                        Severity     = LintSeverity.Warning,
                        Message      = $"Connection '{name}' is defined at line {line} but never used in this script.",
                        LineNumber   = line,
                        ColumnNumber = 0
                    });
                }
            }

            return Task.FromResult<IEnumerable<LintResult>>(results);
        }

        private static IEnumerable<string> CollectConnectionRefs(Statement stmt)
        {
            var names = new List<string>();

            if (stmt is SelectStatement sel)
            {
                AddTableRef(sel.FromTable, names);
                if (sel.IntoTable != null) AddTableRef(sel.IntoTable, names);
                foreach (var join in sel.Joins)
                    AddTableRef(join.Table, names);
            }
            else if (stmt is InsertStatement ins)
            {
                AddTableRef(ins.TargetTable, names);
                if (ins.SelectQuery != null) names.AddRange(CollectConnectionRefs(ins.SelectQuery));
            }
            else if (stmt is UpdateStatement upd)
            {
                AddTableRef(upd.TargetTable, names);
                if (upd.FromTable != null) AddTableRef(upd.FromTable, names);
                if (upd.Joins != null) foreach (var j in upd.Joins) AddTableRef(j.Table, names);
            }
            else if (stmt is DeleteStatement del)
            {
                AddTableRef(del.TargetTable, names);
            }
            else if (stmt is MergeStatement mrg)
            {
                AddTableRef(mrg.TargetTable, names);
                AddTableRef(mrg.SourceTable, names);
            }
            else if (stmt is ExecStatement exec && exec.ConnectionName is IdentifierExpression id)
            {
                names.Add(id.Name);
            }
            else if (stmt is ExecutePushdownStatement push && push.ConnectionName is IdentifierExpression pid)
            {
                names.Add(pid.Name);
            }
            else if (stmt is DropConnectionStatement drop)
            {
                names.Add(drop.ConnectionName);
            }
            else if (stmt is AlterConnectionStatement alter)
            {
                names.Add(alter.ConnectionName);
            }

            return names;
        }

        private static void AddTableRef(TableReference? table, List<string> names)
        {
            if (table == null) return;
            if (table.ConnectionName != null) names.Add(table.ConnectionName);
            else if (!string.IsNullOrEmpty(table.TableName) && !table.TableName.StartsWith("#")) 
                names.Add(table.TableName);
        }
    }
}
