using System;
using System.Collections.Generic;
using System.Linq;

namespace ETL_SQL.Core.Analysis;
/// <summary>
/// Analyzes a statement tree to detect if any DML operations (INSERT, UPDATE, DELETE, MERGE)
/// are performed against a specific table. Also detects "opaque" calls (EXECUTE) where side effects are unknown.
/// </summary>
public class DmlDetector
{
    private readonly string? _targetTable;
    private readonly string? _targetConn;

    public bool IsDmlDetected { get; private set; }
    public bool HasOpaqueCalls { get; private set; }

    public DmlDetector(string? targetTable = null, string? targetConn = null)
    {
        _targetTable = targetTable;
        _targetConn = targetConn;
    }

    public void Analyze(Statement statement)
    {
        if (statement == null) return;

        if (statement is BlockStatement block)
        {
            foreach (var s in block.Statements) Analyze(s);
        }
        else if (statement is IfStatement ifStmt)
        {
            Analyze(ifStmt.IfBody);
            if (ifStmt.ElseIfClauses != null)
            {
                foreach (var ei in ifStmt.ElseIfClauses) Analyze(ei.Body);
            }
            if (ifStmt.ElseBody != null) Analyze(ifStmt.ElseBody);
        }
        else if (statement is WhileStatement whileStmt)
        {
            Analyze(whileStmt.Body);
        }
        else if (statement is ForStatement forStmt)
        {
            Analyze(forStmt.Body);
        }
        else if (statement is ForeachStatement foreachStmt)
        {
            Analyze(foreachStmt.Body);
        }
        else if (statement is TryCatchStatement tryCatch)
        {
            Analyze(tryCatch.TryBody);
            Analyze(tryCatch.CatchBody);
        }
        else if (statement is InsertStatement ins)
        {
            CheckTarget(ins.TargetTable);
        }
        else if (statement is UpdateStatement upd)
        {
            CheckTarget(upd.TargetTable);
        }
        else if (statement is DeleteStatement del)
        {
            CheckTarget(del.TargetTable);
        }
        else if (statement is MergeStatement merge)
        {
            CheckTarget(merge.TargetTable);
        }
        else if (statement is TruncateTableStatement trunc)
        {
            CheckTarget(trunc.TargetTable);
        }
        else if (statement is BulkInsertStatement bulk)
        {
            CheckTarget(bulk.TargetTable);
        }
        else if (statement is SelectStatement sel && sel.IntoTable != null)
        {
            CheckTarget(sel.IntoTable);
        }
        else if (statement is ExecStatement ||
                 statement is ExecuteRemoteBlockStatement ||
                 statement is ExecutePushdownStatement ||
                 statement is ExecuteStatement ||
                 statement is EmailStatement ||
                 statement is FileTransferStatement ||
                 statement is FileOperationStatement ||
                 statement is DirectoryOperationStatement ||
                 statement is DockerActionStatement)
        {
            // Side effects detected - trigger safe path
            HasOpaqueCalls = true;
            IsDmlDetected = true;
        }
    }

    private void CheckTarget(TableReference target)
    {
        if (IsDmlDetected) return;

        // If no target filter provided, any DML counts
        if (_targetTable == null)
        {
            IsDmlDetected = true;
            return;
        }

        bool connMatch = _targetConn == null || string.Equals(target.ConnectionName, _targetConn, StringComparison.OrdinalIgnoreCase);
        bool tableMatch = string.Equals(target.TableName, _targetTable, StringComparison.OrdinalIgnoreCase);

        if (connMatch && tableMatch)
        {
            IsDmlDetected = true;
        }
    }
}
