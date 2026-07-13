using ETL_SQL.Core;
using ETL_SQL.Core.Governance;
using ETL_SQL.Services;

namespace ETL_SQL.Engine.Governance;

/// <summary>
/// Enforces the enterprise mutation guardrails at statement dispatch:
/// <c>Security:RequireWhatIfForDestructiveStatements</c> and
/// <c>Security:RequireTransactionForMutations</c>. Session-local <c>#temp</c> targets and
/// unenrolled/standalone execution are exempt — the guardrails protect persistent data.
/// </summary>
public static class MutationGuardrailPolicy
{
    public static void Enforce(IExecutionContext context, Statement statement)
    {
        var target = MutationTarget(statement);
        if (target is null || target.StartsWith('#')) return;

        // Cheap enrollment gate keeps this off the hot path for standalone execution.
        if (!(context.ExecutionPolicy?.IsEnrolled ?? EnterprisePolicyRuntime.Current.IsEnrolled)) return;

        var snapshot = OperationPolicyBoundary.Refresh(context, $"<mutation:{StatementKind(statement)}>");
        if (!snapshot.IsEnrolled) return;

        // Under WHAT IF nothing is actually mutated, so neither guardrail applies.
        if (context.IsWhatIf) return;

        if (IsDestructive(statement)
            && GovernedFlag(snapshot, "Security:RequireWhatIfForDestructiveStatements"))
            throw Denied(snapshot, "Security:RequireWhatIfForDestructiveStatements", statement, target,
                $"Enterprise policy requires WHAT IF for destructive statements; run {StatementKind(statement)} on '{target}' under WHAT IF.");

        if (GovernedFlag(snapshot, "Security:RequireTransactionForMutations")
            && context.TranCount == 0)
            throw Denied(snapshot, "Security:RequireTransactionForMutations", statement, target,
                $"Enterprise policy requires mutations to run within a transaction; {StatementKind(statement)} on '{target}' has no open transaction.");
    }

    private static string? MutationTarget(Statement statement) => statement switch
    {
        InsertStatement s => s.TargetTable.TableName,
        UpdateStatement s => s.TargetTable.TableName,
        DeleteStatement s => s.TargetTable.TableName,
        MergeStatement s => s.TargetTable.TableName,
        TruncateTableStatement s => s.TargetTable.TableName,
        DropTableStatement s => s.TargetTable.TableName,
        _ => null
    };

    private static bool IsDestructive(Statement statement) =>
        statement is DeleteStatement or TruncateTableStatement or DropTableStatement;

    private static string StatementKind(Statement statement) => statement switch
    {
        InsertStatement => "INSERT",
        UpdateStatement => "UPDATE",
        DeleteStatement => "DELETE",
        MergeStatement => "MERGE",
        TruncateTableStatement => "TRUNCATE TABLE",
        DropTableStatement => "DROP TABLE",
        _ => statement.GetType().Name
    };

    private static bool GovernedFlag(ExecutionPolicySnapshot snapshot, string key) =>
        snapshot.GovernedValues.TryGetValue(key, out var raw)
        && bool.TryParse(raw, out var flag) && flag;

    private static OperationPolicyDeniedException Denied(
        ExecutionPolicySnapshot snapshot,
        string policyKey,
        Statement statement,
        string target,
        string reason) =>
        new(OperationPolicyDecision.Deny(snapshot, policyKey,
            $"{StatementKind(statement)}:{target}", "required mutation guardrail", reason));
}
