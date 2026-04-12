using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Linting.Rules
{
    /// <summary>
    /// CQ-R1: Warns when a connection name is used in a statement that appears
    /// before the CREATE CONNECTION statement that defines it.
    /// </summary>
    public class ConnectionForwardReferenceRule : ILintRule
    {
        public string Name        => "ConnectionForwardReference";
        public string Description => "Warns when a connection is referenced before its CREATE CONNECTION statement.";

        public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
        {
            var results = new List<LintResult>();

            // Build map: connection name → line number of CREATE / ALTER CONNECTION
            var definitions = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var stmt in script.Statements)
            {
                if (stmt is CreateConnectionStatement cc)
                    definitions[cc.ConnectionName] = stmt.Line;
                else if (stmt is AlterConnectionStatement ac)
                    definitions.TryAdd(ac.ConnectionName, stmt.Line); // ALTER doesn't define; only add if not yet defined
            }

            // Walk all statements in order; flag any use of a connection whose CREATE comes later
            foreach (var stmt in script.Statements)
            {
                if (stmt is CreateConnectionStatement or AlterConnectionStatement) continue;

                foreach (var refName in CollectConnectionRefs(stmt))
                {
                    if (definitions.TryGetValue(refName, out var defLine) && defLine > stmt.Line)
                    {
                        results.Add(new LintResult
                        {
                            RuleName     = Name,
                            Severity     = LintSeverity.Warning,
                            Message      = $"Connection '{refName}' is used at line {stmt.Line} but is not defined until line {defLine}.",
                            LineNumber   = stmt.Line,
                            ColumnNumber = stmt.Column
                        });
                    }
                }
            }

            return Task.FromResult<IEnumerable<LintResult>>(results);
        }

        private static IEnumerable<string> CollectConnectionRefs(Statement stmt)
        {
            var names = new List<string>();

            // FROM <connection>.<table> — TableReference carries an optional ConnectionName
            if (stmt is SelectStatement sel)
            {
                if (sel.FromTable?.ConnectionName != null)
                    names.Add(sel.FromTable.ConnectionName);
                foreach (var join in sel.Joins)
                    if (join.Table.ConnectionName != null)
                        names.Add(join.Table.ConnectionName);
            }
            else if (stmt is InsertStatement ins && ins.TargetTable?.ConnectionName != null)
            {
                names.Add(ins.TargetTable.ConnectionName);
            }
            else if (stmt is AlterConnectionStatement alter)
            {
                names.Add(alter.ConnectionName);
            }

            return names;
        }
    }
}
