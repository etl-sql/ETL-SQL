using ETL_SQL.Core;
using ETL_SQL.Core.Security;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Decides which statements a Portal ad hoc (designer) run may execute.
/// </summary>
/// <remarks>
/// The Portal executes under the logged-in user's identity against shared, ACL-resolved
/// connections, so this is a trust boundary rather than a convenience filter. The allow-list
/// is deliberately narrow and closed by default — anything not named here is rejected:
///
///   * read-only SELECT / set operations — the original single-statement contract;
///   * SELECT ... INTO #temp — writes only to session-local temp storage, which is what makes
///     a multi-step staging script (the common development shape) possible.
///
/// Everything else is refused, notably:
///   * CREATE CONNECTION — connections are injected by the server from the shared catalog after
///     an ACL check (see PortalDesignerRunService.BuildExecutionScriptAsync). Letting a script
///     declare its own would connect with script-supplied credentials and bypass that check.
///   * SET — the governance preamble sets the row cap, memory grant and session ceiling. A
///     script-supplied SET could raise them back up and defeat the limits.
///   * writes to a real connection, EXPORT, RUN SCRIPT, remote/docker blocks, and control flow.
/// </remarks>
public static class PortalInteractiveRunPolicy
{
    /// <summary>Returns null when the statement may run, or the reason it may not.</summary>
    public static string? Reject(Statement statement) => statement switch
    {
        SelectStatement { IntoTable: null } => null,
        SelectStatement { IntoTable: not null } select when IsTempTable(select.IntoTable!.TableName) => null,
        SelectStatement { IntoTable: not null } =>
            "SELECT ... INTO is limited to temp tables (#name) in an interactive run.",
        SetOperationStatement setOperation =>
            ReadOnlyQueryPolicy.IsReadOnly(setOperation) ? null : "Set operations must be read-only.",
        CreateConnectionStatement =>
            "CREATE CONNECTION is not allowed here; pick a shared connection instead.",
        _ => $"{DescribeStatement(statement)} is not allowed in an interactive run."
    };

    private static bool IsTempTable(string tableName) =>
        tableName.StartsWith('#');

    private static string DescribeStatement(Statement statement)
    {
        var name = statement.GetType().Name;
        return name.EndsWith("Statement", StringComparison.Ordinal)
            ? name[..^"Statement".Length]
            : name;
    }
}
