using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Services;
using ETL_SQL.Core;

namespace ETL_SQL.Analysis.Linting.Rules;
/// <summary>
/// CQ-R2: Warns when a CREATE CONNECTION defines a connection name that is never
/// referenced in any FROM clause, INSERT target, ALTER CONNECTION, or DROP CONNECTION
/// within the same script.
/// </summary>
public class UnusedConnectionRule : ILintRule
{
    public string Name => "UnusedConnection";
    public string Description => "Warns when a connection is created but never used in the script.";

    public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
    {
        var results = new List<LintResult>();

        // Collect all defined connection names with their definition line
        var defined = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var stmt in ScriptTextEditing.Flatten(script.Statements))
        {
            if (stmt is CreateConnectionStatement cc && cc.Mode == ObjectCreationMode.Create)
                defined[cc.ConnectionName] = stmt.Line;
        }

        if (defined.Count == 0)
            return Task.FromResult<IEnumerable<LintResult>>(results);

        // Collect all connection names that are actually referenced
        var used = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var stmt in ScriptTextEditing.Flatten(script.Statements))
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
                    RuleName = Name,
                    Severity = LintSeverity.Warning,
                    Message = $"Connection '{name}' is defined at line {line} but never used in this script.",
                    LineNumber = line,
                    ColumnNumber = 0
                });
            }
        }

        return Task.FromResult<IEnumerable<LintResult>>(results);
    }

    internal static IEnumerable<string> CollectConnectionRefs(Statement stmt)
    {
        var names = new List<string>();

        if (stmt.Ctes != null)
        {
            foreach (var cte in stmt.Ctes)
            {
                if (cte.Query != null)
                    names.AddRange(CollectConnectionRefs(cte.Query));
            }
        }

        if (stmt is SelectStatement sel)
        {
            AddTableRef(sel.FromTable, names);
            if (sel.IntoTable != null) AddTableRef(sel.IntoTable, names);
            foreach (var join in sel.Joins)
            {
                AddTableRef(join.Table, names);
                AddExpressionRefs(join.Condition, names);
            }
            foreach (var col in sel.Columns)
                AddExpressionRefs(col.Expression, names);
            AddExpressionRefs(sel.WhereClause, names);
            AddExpressionRefs(sel.HavingClause, names);
            AddExpressionRefs(sel.QualifyClause, names);
        }
        else if (stmt is SetOperationStatement setOp)
        {
            if (setOp.Left != null) names.AddRange(CollectConnectionRefs(setOp.Left));
            if (setOp.Right != null) names.AddRange(CollectConnectionRefs(setOp.Right));
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
        else if (stmt is ExecStatement exec)
        {
            if (exec.ConnectionName is IdentifierExpression id) names.Add(id.Name);
            if (exec.IntoTable != null) AddTableRef(exec.IntoTable, names);
        }
        else if (stmt is ExecutePushdownStatement push)
        {
            if (push.ConnectionName is IdentifierExpression pid) names.Add(pid.Name);
            if (push.IntoTable != null) AddTableRef(push.IntoTable, names);
        }
        else if (stmt is ExecuteRemoteBlockStatement rem)
        {
            if (rem.ConnectionName is IdentifierExpression rid) names.Add(rid.Name);
            if (rem.IntoTable != null) AddTableRef(rem.IntoTable, names);
        }
        else if (stmt is DropConnectionStatement drop)
        {
            names.Add(drop.ConnectionName);
        }
        else if (stmt is AlterConnectionStatement alter)
        {
            names.Add(alter.ConnectionName);
        }
        else if (stmt is FileOperationStatement fileOp)
        {
            if (fileOp.Source is IdentifierExpression sid) names.Add(sid.Name);
            if (fileOp.Destination is IdentifierExpression did) names.Add(did.Name);
            if (!string.IsNullOrEmpty(fileOp.ConnectionName)) names.Add(fileOp.ConnectionName);
        }
        else if (stmt is DirectoryOperationStatement dirOp)
        {
            if (dirOp.Path is IdentifierExpression dpid) names.Add(dpid.Name);
            if (dirOp.Destination is IdentifierExpression did) names.Add(did.Name);
            if (!string.IsNullOrEmpty(dirOp.ConnectionName)) names.Add(dirOp.ConnectionName);
        }
        else if (stmt is FileTransferStatement trans)
        {
            if (trans.LocalPath is IdentifierExpression lid) names.Add(lid.Name);
            if (trans.RemotePath is IdentifierExpression rid) names.Add(rid.Name);
            if (!string.IsNullOrEmpty(trans.ConnectionName)) names.Add(trans.ConnectionName);
        }
        else if (stmt is EmailStatement email)
        {
            if (email.ConnectionName is IdentifierExpression eid) names.Add(eid.Name);
        }
        else if (stmt is ShowConnectionConfigStatement showConn)
        {
            names.Add(showConn.ConnectionName);
        }
        else if (stmt is TestConnectionStatement testConn)
        {
            names.Add(testConn.ConnectionName);
        }
        else if (stmt is ShowTablesStatement showTables)
        {
            if (!string.IsNullOrEmpty(showTables.ConnectionName)) names.Add(showTables.ConnectionName);
        }
        else if (stmt is CreateNotificationStatement notif)
        {
            if (!string.IsNullOrEmpty(notif.ConnectionName)) names.Add(notif.ConnectionName);
        }
        else if (stmt is AlterCatalogObjectStatement catalogObj)
        {
            if (!string.IsNullOrEmpty(catalogObj.ConnectionName)) names.Add(catalogObj.ConnectionName);
        }

        return names;
    }

    private static void AddTableRef(TableReference? table, List<string> names)
    {
        if (table == null) return;
        if (table.ConnectionName != null) names.Add(table.ConnectionName);
        else if (!string.IsNullOrEmpty(table.TableName) && !table.TableName.StartsWith("#"))
            names.Add(table.TableName);

        if (table.Subquery != null)
            names.AddRange(CollectConnectionRefs(table.Subquery));
    }

    private static void AddExpressionRefs(Expression? expr, List<string> names)
    {
        foreach (var query in ScriptTextEditing.EnumerateSubqueries(expr))
            names.AddRange(CollectConnectionRefs(query));
    }
}
