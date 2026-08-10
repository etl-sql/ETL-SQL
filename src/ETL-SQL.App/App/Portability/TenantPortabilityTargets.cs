using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.App.Portability;

/// <summary>
/// Replays a Portal configuration payload through the real parser and evaluator while binding the
/// exported <c>portal</c> alias to an authenticated service-account connection kept only in memory.
/// </summary>
public sealed class EnginePortalConfigurationTarget(
    IServiceProvider services, Func<IPortalAdminConnection> connectionFactory) : IPortalConfigurationTarget
{
    public async Task<IReadOnlyList<PortalPlanEntry>> PlanAsync(
        string script, IReadOnlyDictionary<string, string> bindings, CancellationToken ct)
    {
        var (evaluator, parsed, connection) = Prepare(script, bindings);
        await using (evaluator.ConfigureAwait(false))
        {
            var plan = new List<PortalPlanEntry>();
            foreach (var statement in EnumerateAdminStatements(parsed))
            {
                ct.ThrowIfCancellationRequested();
                var description = await connection
                    .PlanAdminStatementAsync(statement, evaluator, ct).ConfigureAwait(false)
                    ?? $"No read-only plan is available for {statement.GetType().Name}.";
                plan.Add(new PortalPlanEntry(
                    statement.GetType().Name,
                    description,
                    Classify(description)));
            }

            if (plan.Count == 0)
                throw new TenantBundleCompositionException(
                    "The Portal configuration payload contains no EXECUTE portal statements.");
            return plan;
        }
    }

    public async Task ApplyAsync(
        string script, IReadOnlyDictionary<string, string> bindings, CancellationToken ct)
    {
        var (evaluator, parsed, _) = Prepare(script, bindings);
        await using (evaluator.ConfigureAwait(false))
            await evaluator.Evaluate(parsed, ct).ConfigureAwait(false);
    }

    private (Evaluator Evaluator, Script Script, IPortalAdminConnection Connection) Prepare(
        string script, IReadOnlyDictionary<string, string> bindings)
    {
        var bound = BindSecretPlaceholders(script, bindings);
        var parsed = new Parser(new Lexer(bound).Tokenize(), bound).Parse();
        ValidatePortalOnlyScript(parsed);
        var evaluator = services.GetRequiredService<Evaluator>();
        var connection = connectionFactory();
        evaluator.Connections["portal"] = connection;
        return (evaluator, parsed, connection);
    }

    private static IEnumerable<Statement> EnumerateAdminStatements(Script parsed)
    {
        foreach (var outer in parsed.Statements)
        {
            switch (outer)
            {
                case ExecuteRemoteBlockStatement remote:
                    foreach (var inner in remote.Body.Statements) yield return inner;
                    break;
                case ExecutePushdownStatement pushdown:
                    var innerScript = new Parser(
                        new Lexer(pushdown.SqlText).Tokenize(), pushdown.SqlText).Parse();
                    foreach (var inner in innerScript.Statements) yield return inner;
                    break;
            }
        }
    }

    private static void ValidatePortalOnlyScript(Script parsed)
    {
        if (parsed.Statements.Count == 0)
            throw new TenantBundleCompositionException("The Portal configuration payload is empty.");

        foreach (var statement in parsed.Statements)
        {
            var connection = statement switch
            {
                ExecuteRemoteBlockStatement remote => remote.ConnectionName.ToSql(),
                ExecutePushdownStatement pushdown => pushdown.ConnectionName.ToSql(),
                _ => throw new TenantBundleCompositionException(
                    $"Portal configuration payloads may contain only EXECUTE portal blocks, not {statement.GetType().Name}.")
            };
            if (!string.Equals(connection.Trim('\'', '"', '[', ']'), "portal", StringComparison.OrdinalIgnoreCase))
                throw new TenantBundleCompositionException(
                    $"Portal configuration payloads may target only the 'portal' connection, not '{connection}'.");
        }
    }

    internal static string BindSecretPlaceholders(
        string script, IReadOnlyDictionary<string, string> bindings)
    {
        var result = script;
        foreach (var (source, target) in bindings)
        {
            var placeholder = source.StartsWith("SECRET:", StringComparison.OrdinalIgnoreCase)
                ? source["SECRET:".Length..]
                : source;
            if (!result.Contains("${" + placeholder + "}", StringComparison.Ordinal)) continue;
            if (!IsSecretReference(target))
                throw new TenantBundleCompositionException(
                    $"Binding '{source}' replaces a password placeholder and must target a SECRET:name reference.");
            result = result.Replace("${" + placeholder + "}", target, StringComparison.Ordinal);
        }

        var unresolved = result.IndexOf("${", StringComparison.Ordinal);
        if (unresolved >= 0)
            throw new TenantBundleCompositionException(
                "The Portal configuration payload still contains an unresolved ${...} secret placeholder.");
        return result;
    }

    private static bool IsSecretReference(string value)
    {
        if (!value.StartsWith("SECRET:", StringComparison.OrdinalIgnoreCase)) return false;
        var name = value["SECRET:".Length..];
        return name.Length > 0 && name.All(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.' or ':' or '/' or '@');
    }

    private static string Classify(string description)
    {
        if (description.StartsWith("No read-only plan", StringComparison.Ordinal)) return "Collision";
        if (description.Contains("would skip", StringComparison.OrdinalIgnoreCase)) return "Match";
        if (description.Contains("already", StringComparison.OrdinalIgnoreCase)
            || description.Contains("exists", StringComparison.OrdinalIgnoreCase)
            || description.Contains("would update", StringComparison.OrdinalIgnoreCase)
            || description.Contains("would delete", StringComparison.OrdinalIgnoreCase)
            || description.Contains("would remove", StringComparison.OrdinalIgnoreCase)
            || description.Contains("would enable", StringComparison.OrdinalIgnoreCase)
            || description.Contains("would disable", StringComparison.OrdinalIgnoreCase))
            return "Collision";
        return "Create";
    }
}

/// <summary>Production adapter over the local target deployment's provider-neutral stores.</summary>
public sealed class OrchestratorPackageTarget(
    IJobHistoryStore history, IJobCatalogStore catalog, ILineageCatalogStore lineage)
    : IOrchestratorPackageTarget
{
    public async Task<int> ImportAsync(
        OrchestratorPromotionPackageService.Package package, bool leaveDisabled, CancellationToken ct)
    {
        if (!leaveDisabled)
            throw new InvalidOperationException("Tenant imports must leave Orchestrator workloads disabled.");
        var result = await OrchestratorPromotionPackageService
            .ImportAsync(package, history, catalog, lineage, bindings: null, ct: ct)
            .ConfigureAwait(false);
        return result.Jobs + result.Schedules + result.Notifications
               + result.JobSchedules + result.JobNotifications + result.QualityRuns
               + result.QualityFailures + result.LineageEntries;
    }
}
