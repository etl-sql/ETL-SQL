using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

namespace ETL_SQL.Core.Linting.Rules
{
    public class UndeclaredVariableRule : ILintRule
    {
        public string Name => "UndeclaredVariable";
        public string Description => "Checks if a variable is used before it is declared.";

        public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
        {
            var results = new List<LintResult>();
            var declaredVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            foreach (var statement in script.Statements)
            {
                AnalyzeStatement(statement, declaredVariables, results);
            }
            return Task.FromResult<IEnumerable<LintResult>>(results);
        }

        private void AnalyzeStatement(Statement statement, HashSet<string> declaredVariables, List<LintResult> results)
        {
            if (statement is DeclareStatement declare)
            {
                if (declare.InitialValue != null) AnalyzeExpression(declare.InitialValue, declaredVariables, results);
                declaredVariables.Add(declare.VariableName);
            }
            else if (statement is SetVariableStatement setVar)
            {
                AnalyzeExpression(setVar.Target, declaredVariables, results);
                AnalyzeExpression(setVar.Value, declaredVariables, results);
            }
            else if (statement is SelectStatement select)
            {
                foreach (var col in select.Columns) AnalyzeExpression(col.Expression, declaredVariables, results);
                if (select.WhereClause != null) AnalyzeExpression(select.WhereClause, declaredVariables, results);
                if (select.HavingClause != null) AnalyzeExpression(select.HavingClause, declaredVariables, results);
                if (select.OrderBy != null) foreach (var o in select.OrderBy) AnalyzeExpression(o.Expression, declaredVariables, results);
                if (select.GroupBy != null) foreach (var g in select.GroupBy) AnalyzeExpression(g, declaredVariables, results);
                if (select.TopCount != null) AnalyzeExpression(select.TopCount, declaredVariables, results);
                if (select.LimitCount != null) AnalyzeExpression(select.LimitCount, declaredVariables, results);
                if (select.Offset != null) AnalyzeExpression(select.Offset, declaredVariables, results);
                
                if (select.FromTable?.Subquery != null) AnalyzeStatement(select.FromTable.Subquery, declaredVariables, results);
                foreach (var join in select.Joins)
                {
                    AnalyzeExpression(join.Condition, declaredVariables, results);
                    if (join.Table.Subquery != null) AnalyzeStatement(join.Table.Subquery, declaredVariables, results);
                }
            }
            else if (statement is InsertStatement insert)
            {
                if (insert.SelectQuery != null) AnalyzeStatement(insert.SelectQuery, declaredVariables, results);
                if (insert.Values != null) foreach (var row in insert.Values) foreach (var e in row) AnalyzeExpression(e, declaredVariables, results);
            }
            else if (statement is UpdateStatement update)
            {
                foreach (var assign in update.Assignments) AnalyzeExpression(assign.Value, declaredVariables, results);
                if (update.WhereClause != null) AnalyzeExpression(update.WhereClause, declaredVariables, results);
            }
            else if (statement is DeleteStatement delete)
            {
                if (delete.WhereClause != null) AnalyzeExpression(delete.WhereClause, declaredVariables, results);
            }
            else if (statement is BlockStatement block)
            {
                foreach (var s in block.Statements) AnalyzeStatement(s, declaredVariables, results);
            }
            else if (statement is IfStatement ifStmt)
            {
                AnalyzeExpression(ifStmt.Condition, declaredVariables, results);
                AnalyzeStatement(ifStmt.IfBody, declaredVariables, results);
                if (ifStmt.ElseIfClauses != null)
                {
                    foreach (var ei in ifStmt.ElseIfClauses)
                    {
                        AnalyzeExpression(ei.Condition, declaredVariables, results);
                        AnalyzeStatement(ei.Body, declaredVariables, results);
                    }
                }
                if (ifStmt.ElseBody != null) AnalyzeStatement(ifStmt.ElseBody, declaredVariables, results);
            }
            else if (statement is WhileStatement whileStmt)
            {
                AnalyzeExpression(whileStmt.Condition, declaredVariables, results);
                AnalyzeStatement(whileStmt.Body, declaredVariables, results);
            }
            else if (statement is ForStatement forStmt)
            {
                AnalyzeExpression(forStmt.StartValue, declaredVariables, results);
                AnalyzeExpression(forStmt.EndValue, declaredVariables, results);
                if (forStmt.StepValue != null) AnalyzeExpression(forStmt.StepValue, declaredVariables, results);
                
                var loopVars = new HashSet<string>(declaredVariables, StringComparer.OrdinalIgnoreCase);
                loopVars.Add(forStmt.VariableName); 
                AnalyzeStatement(forStmt.Body, loopVars, results);
            }
            else if (statement is ForeachStatement foreachStmt)
            {
                AnalyzeExpression(foreachStmt.ListExpression, declaredVariables, results);
                var loopVars = new HashSet<string>(declaredVariables, StringComparer.OrdinalIgnoreCase);
                loopVars.Add(foreachStmt.VariableName);
                AnalyzeStatement(foreachStmt.Body, loopVars, results);
            }
            else if (statement is PrintStatement print)
            {
                foreach (var expr in print.Arguments) AnalyzeExpression(expr, declaredVariables, results);
                if (print.ShowTimestamp != null) AnalyzeExpression(print.ShowTimestamp, declaredVariables, results);
                if (print.TimestampFormat != null) AnalyzeExpression(print.TimestampFormat, declaredVariables, results);
            }
            else if (statement is ExecStatement exec)
            {
                AnalyzeExpression(exec.SqlExpression, declaredVariables, results);
                if (exec.ConnectionName != null) AnalyzeExpression(exec.ConnectionName, declaredVariables, results);
            }
            else if (statement is RunScriptStatement run)
            {
                foreach (var p in run.Parameters) AnalyzeExpression(p.Value, declaredVariables, results);
            }
            else if (statement is CreateProcedureStatement proc)
            {
                var procVars = new HashSet<string>(declaredVariables, StringComparer.OrdinalIgnoreCase);
                foreach (var p in proc.Parameters) procVars.Add(p.Name);
                if (proc.Body != null) AnalyzeStatement(proc.Body, procVars, results);
            }
            else if (statement is CreateFunctionStatement func)
            {
                var funcVars = new HashSet<string>(declaredVariables, StringComparer.OrdinalIgnoreCase);
                foreach (var p in func.Parameters) funcVars.Add(p.Name);
                if (func.Body != null) AnalyzeStatement(func.Body, funcVars, results);
            }
            else if (statement is CreateSetsStatement createSets)
            {
                foreach (var assign in createSets.Assignments)
                {
                    AnalyzeExpression(assign.Value, declaredVariables, results);
                    string varName = assign.VariableName;
                    if (!varName.StartsWith("@")) varName = "@" + varName;
                    declaredVariables.Add(varName);
                }
            }
            else if (statement is UseSetsStatement useSets)
            {
                 // USE SETS !name - ideally we'd know what variables are in the set, 
                 // but for now we assume they were defined in a CREATE SETS earlier in this script.
            }
            else if (statement is AssertStatement assert)
            {
                AnalyzeExpression(assert.Condition, declaredVariables, results);
                if (assert.Message != null) AnalyzeExpression(assert.Message, declaredVariables, results);
            }
            else if (statement is FileOperationStatement fileOp)
            {
                AnalyzeExpression(fileOp.Source, declaredVariables, results);
                if (fileOp.Destination != null) AnalyzeExpression(fileOp.Destination, declaredVariables, results);
                if (fileOp.Overwrite != null) AnalyzeExpression(fileOp.Overwrite, declaredVariables, results);
                if (fileOp.Password != null) AnalyzeExpression(fileOp.Password, declaredVariables, results);
            }
            else if (statement is DirectoryOperationStatement dirOp)
            {
                AnalyzeExpression(dirOp.Path, declaredVariables, results);
                if (dirOp.Destination != null) AnalyzeExpression(dirOp.Destination, declaredVariables, results);
                if (dirOp.Overwrite != null) AnalyzeExpression(dirOp.Overwrite, declaredVariables, results);
                if (dirOp.Recursive != null) AnalyzeExpression(dirOp.Recursive, declaredVariables, results);
            }
            else if (statement is WaitForStatement wait)
            {
                AnalyzeExpression(wait.Expression, declaredVariables, results);
            }
            else if (statement is ParallelStatement parallel)
            {
                AnalyzeStatement(parallel.Body, declaredVariables, results);
            }
            else if (statement is ParallelForStatement pFor)
            {
                AnalyzeExpression(pFor.StartValue, declaredVariables, results);
                AnalyzeExpression(pFor.EndValue, declaredVariables, results);
                if (pFor.StepValue != null) AnalyzeExpression(pFor.StepValue, declaredVariables, results);
                
                var loopVars = new HashSet<string>(declaredVariables, StringComparer.OrdinalIgnoreCase);
                loopVars.Add(pFor.VariableName); 
                AnalyzeStatement(pFor.Body, loopVars, results);
            }
            else if (statement is BulkInsertStatement bulk)
            {
                foreach (var opt in bulk.Options.Values) AnalyzeExpression(opt, declaredVariables, results);
            }
            else if (statement is ExportReportStatement exportRpt)
            {
                AnalyzeExpression(exportRpt.ReportPath, declaredVariables, results);
                AnalyzeExpression(exportRpt.OutputPath, declaredVariables, results);
            }
            else if (statement is ExportStatement export)
            {
                AnalyzeExpression(export.Source, declaredVariables, results);
            }
            else if (statement is EmailStatement email)
            {
                AnalyzeExpression(email.To, declaredVariables, results);
                AnalyzeExpression(email.From, declaredVariables, results);
                AnalyzeExpression(email.Subject, declaredVariables, results);
                AnalyzeExpression(email.Body, declaredVariables, results);
                if (email.ConnectionName != null) AnalyzeExpression(email.ConnectionName, declaredVariables, results);
                if (email.Attachments != null) foreach (var a in email.Attachments) AnalyzeExpression(a, declaredVariables, results);
            }
            else if (statement is RaiseErrorStatement raise)
            {
                AnalyzeExpression(raise.Message, declaredVariables, results);
                AnalyzeExpression(raise.Severity, declaredVariables, results);
                foreach (var p in raise.Parameters) AnalyzeExpression(p, declaredVariables, results);
            }
            else if (statement is ThrowStatement thr)
            {
                if (thr.ErrorNumber != null) AnalyzeExpression(thr.ErrorNumber, declaredVariables, results);
                if (thr.Message != null) AnalyzeExpression(thr.Message, declaredVariables, results);
                if (thr.State != null) AnalyzeExpression(thr.State, declaredVariables, results);
            }
            else if (statement is ReturnStatement ret)
            {
                if (ret.ReturnValue != null) AnalyzeExpression(ret.ReturnValue, declaredVariables, results);
            }
            else if (statement is TryCatchStatement tryCatch)
            {
                AnalyzeStatement(tryCatch.TryBody, declaredVariables, results);
                AnalyzeStatement(tryCatch.CatchBody, declaredVariables, results);
            }
        }

        private void CheckVariable(string name, AstNode node, HashSet<string> declaredVariables, List<LintResult> results)
        {
            if (name.StartsWith("@") && !name.StartsWith("@@") && !declaredVariables.Contains(name))
            {
                results.Add(new LintResult
                {
                    RuleName = Name,
                    Severity = LintSeverity.Error,
                    Message = $"Variable '{name}' is used but not declared.",
                    LineNumber = node.Line,
                    ColumnNumber = node.Column
                });
            }
        }

        private void AnalyzeExpression(Expression expr, HashSet<string> declaredVariables, List<LintResult> results)
        {
            if (expr is VariableExpression varExpr)
            {
                CheckVariable(varExpr.Name, varExpr, declaredVariables, results);
            }
            else if (expr is BinaryExpression binary)
            {
                AnalyzeExpression(binary.Left, declaredVariables, results);
                AnalyzeExpression(binary.Right, declaredVariables, results);
            }
            else if (expr is UnaryExpression unary)
            {
                AnalyzeExpression(unary.Expression, declaredVariables, results);
            }
            else if (expr is FunctionCallExpression call)
            {
                foreach (var arg in call.Arguments) AnalyzeExpression(arg, declaredVariables, results);
                if (call.Window != null)
                {
                    if (call.Window.PartitionBy != null) foreach (var p in call.Window.PartitionBy) AnalyzeExpression(p, declaredVariables, results);
                    if (call.Window.OrderBy != null) foreach (var o in call.Window.OrderBy) AnalyzeExpression(o.Expression, declaredVariables, results);
                }
            }
            else if (expr is ListExpression list)
            {
                foreach (var item in list.Items) AnalyzeExpression(item, declaredVariables, results);
            }
            else if (expr is SubqueryExpression subquery)
            {
                AnalyzeStatement(subquery.Query, declaredVariables, results);
            }
            else if (expr is MemberAccessExpression member)
            {
                AnalyzeExpression(member.Expression, declaredVariables, results);
            }
        }
    }
}
