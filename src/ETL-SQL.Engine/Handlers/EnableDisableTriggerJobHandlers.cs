using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;


namespace ETL_SQL.Engine.Handlers;

internal static class JobRoutingHelper
{
    public static async Task<bool> RouteToRemoteIfSpecified(Statement stmt, string? atConn, IExecutionContext context)
    {
        if (atConn == null) return false;

        IDataSource? conn = null;
        // 1. Exact match
        if (context.Connections.TryGetValue(atConn, out conn)) { }
        // 2. Case-insensitive match
        else
        {
            conn = context.Connections.FirstOrDefault(c => c.Key.Equals(atConn, StringComparison.OrdinalIgnoreCase)).Value;
        }

        if (conn == null)
        {
            var available = string.Join(", ", context.Connections.Keys);
            throw new ExecutionException($"Connection '{atConn}' not found in current session. Registered connections: [{available}]");
        }

        if (conn is not IPortalAdminConnection adminConn)
            throw new ExecutionException($"Connection '{atConn}' (Type: {conn.ConnectorType}) does not support orchestrator operations.");

        await adminConn.ExecuteAdminStatementAsync(stmt, context, context.CancellationToken);
        return true;
    }
}

/// <summary>
/// Handles ENABLE JOB and DISABLE JOB statements for the local job store.
/// </summary>
public class EnableJobStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(EnableJobStatement);
    private readonly IJobHistoryStore _store;

    public EnableJobStatementHandler(IJobHistoryStore store) => _store = store;

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (EnableJobStatement)statement;
        if (await JobRoutingHelper.RouteToRemoteIfSpecified(stmt, stmt.At, context))
            return;

        var existing = await _store.GetJobAsync(CatalogStatementSupport.ActingTenant(context), stmt.Name)
            ?? throw new ExecutionException($"ENABLE JOB failed: job '{stmt.Name}' not found.");
        await CatalogStatementSupport.DemandAsync(context, stmt, OrchestratorObjectKind.Job,
            stmt.Name, existing.Id, existing.TenantId,
            OrchestratorObjectPermission.Manage, existing.CreatedBy);
        await _store.SaveJobAsync(existing with
        {
            IsEnabled = true,
            ModifiedBy = CatalogStatementSupport.ActingIdentity(context)
        });
        CatalogStatementSupport.AuditMutation(
            context, "ENABLE_JOB", $"JOB:{stmt.Name}", $"Job '{stmt.Name}' enabled.");
        context.Log($"Job '{stmt.Name}' enabled.", ConsoleColor.Green);
    }
}

/// <summary>
/// Handles DISABLE JOB statements for the local job store.
/// </summary>
public class DisableJobStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(DisableJobStatement);
    private readonly IJobHistoryStore _store;

    public DisableJobStatementHandler(IJobHistoryStore store) => _store = store;

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (DisableJobStatement)statement;
        if (await JobRoutingHelper.RouteToRemoteIfSpecified(stmt, stmt.At, context))
            return;

        var existing = await _store.GetJobAsync(CatalogStatementSupport.ActingTenant(context), stmt.Name)
            ?? throw new ExecutionException($"DISABLE JOB failed: job '{stmt.Name}' not found.");
        await CatalogStatementSupport.DemandAsync(context, stmt, OrchestratorObjectKind.Job,
            stmt.Name, existing.Id, existing.TenantId,
            OrchestratorObjectPermission.Manage, existing.CreatedBy);
        await _store.SaveJobAsync(existing with
        {
            IsEnabled = false,
            ModifiedBy = CatalogStatementSupport.ActingIdentity(context)
        });
        CatalogStatementSupport.AuditMutation(
            context, "DISABLE_JOB", $"JOB:{stmt.Name}", $"Job '{stmt.Name}' disabled.");
        context.Log($"Job '{stmt.Name}' disabled.", ConsoleColor.Yellow);
    }
}

/// <summary>
/// Handles TRIGGER JOB for the local scheduler — enqueues execution through the attached
/// <see cref="IJobManager"/> or fails closed when no scheduler is attached.
/// </summary>
public class TriggerJobStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(TriggerJobStatement);
    private readonly IJobHistoryStore _store;
    private readonly IJobManager? _jobManager;

    public TriggerJobStatementHandler(IJobHistoryStore store, IJobManager? jobManager = null)
    {
        _store = store;
        _jobManager = jobManager;
    }

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (TriggerJobStatement)statement;
        if (await JobRoutingHelper.RouteToRemoteIfSpecified(stmt, stmt.At, context))
            return;

        var existing = await _store.GetJobAsync(CatalogStatementSupport.ActingTenant(context), stmt.Name)
            ?? throw new ExecutionException($"TRIGGER JOB failed: job '{stmt.Name}' not found.", null, stmt.Line, stmt.Column);
        await CatalogStatementSupport.DemandAsync(context, stmt, OrchestratorObjectKind.Job,
            stmt.Name, existing.Id, existing.TenantId,
            OrchestratorObjectPermission.Execute, existing.CreatedBy);

        if (_jobManager is null)
            throw new ExecutionException(
                $"TRIGGER JOB failed: no local scheduler is attached for job '{stmt.Name}'. " +
                $"Route to an Orchestrator connection with EXECUTE <conn> BEGIN TRIGGER JOB {stmt.Name}; END or TRIGGER JOB {stmt.Name} AT <conn>.",
                null, stmt.Line, stmt.Column);

        if (context.IsWhatIf)
        {
            context.Log($"WHAT IF: Would trigger job '{stmt.Name}' for immediate execution.", ConsoleColor.Yellow);
            return;
        }

        if (!await _jobManager.TriggerJobAsync(existing.Id))
            throw new ExecutionException(
                $"TRIGGER JOB failed: scheduler rejected immediate execution of job '{stmt.Name}'.",
                null, stmt.Line, stmt.Column);

        CatalogStatementSupport.AuditMutation(
            context, "TRIGGER_JOB", $"JOB:{stmt.Name}", $"Job '{stmt.Name}' queued for immediate execution.");
        context.Log($"Job '{stmt.Name}' triggered for immediate execution.", ConsoleColor.Green);
    }
}
