using System;
using System.Threading.Tasks;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles USE SETS !&lt;name&gt; — applies a stored set of variable assignments to the current context.
    /// When the set was created with SET WITH_PROMPT ON, prompts for confirmation in interactive mode
    /// (i.e. when context.OnPrompt is wired up). Non-interactive / batch mode auto-proceeds.
    /// </summary>
    public class UseSetsStatementHandler : IStatementHandler
    {
        public Type SupportedStatementType => typeof(UseSetsStatement);

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (UseSetsStatement)statement;

            if (!context.NamedSets.TryGetValue(stmt.Name, out var set))
                throw new System.Exception($"Named set '!{stmt.Name}' does not exist. Use CREATE SETS to define it.");

            if (set.WithPrompt && context.OnPrompt != null)
            {
                bool proceed = await context.OnPrompt(
                    $"You are about to apply !{stmt.Name}. This may change production settings. Continue? (Y/N)");
                if (!proceed)
                {
                    context.Log($"USE SETS !{stmt.Name} aborted by user.");
                    return;
                }
            }

            foreach (var assignment in set.Assignments)
            {
                var value = await context.EvaluateValue(assignment.Value, new Row());
                var varName = $"@{assignment.VariableName}";
                if (!context.VarContext.ContainsVariable(varName))
                    context.VarContext.DeclareVariable(varName, value);
                else
                    context.VarContext.SetVariable(varName, value);
            }

            context.Log($"Applied set !{stmt.Name} — {set.Assignments.Count} variable(s) set.");
        }
    }
}

