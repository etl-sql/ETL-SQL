using System.Text.RegularExpressions;
using ETL_SQL.Portal.Services;

namespace ETL_SQL.Portal.Middleware;

/// <summary>
/// Enforces a deny-by-default method and route allowlist for native client sessions
/// (<c>identity_type = native_client</c>, <c>aud = etl-sql-portal-native-api</c>).
/// Runs in addition to existing user, role, capability, and object ACL checks.
/// </summary>
public sealed partial class NativeClientScopeMiddleware(RequestDelegate next)
{
    [GeneratedRegex("^/api/studio/reports/\\d+/summary$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StudioDocumentSummaryRegex();

    [GeneratedRegex("^/api/studio/pipelines/\\d+/runs$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PipelineRunsSubmitRegex();

    [GeneratedRegex("^/api/studio/pipelines/\\d+/revision$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PipelineRevisionRegex();

    [GeneratedRegex("^/api/studio/pipelines/\\d+/runs/\\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PipelineRunStatusRegex();

    [GeneratedRegex("^/api/studio/pipelines/\\d+/runs/by-request/[^/]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PipelineRunByRequestRegex();

    [GeneratedRegex("^/api/studio/pipelines/\\d+/runs/\\d+/cancel$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PipelineRunCancelRegex();

    [GeneratedRegex("^/api/studio/hosted-jobs/[^/]+/runs$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HostedJobRunsListRegex();

    [GeneratedRegex("^/api/studio/hosted-jobs/[^/]+/runs/\\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HostedJobRunDetailRegex();

    [GeneratedRegex("^/api/studio/hosted-jobs/[^/]+/trigger$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HostedJobTriggerRegex();

    [GeneratedRegex("^/api/studio/hosted-jobs/[^/]+/runs/\\d+/cancel$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HostedJobRunCancelRegex();

    public async Task InvokeAsync(HttpContext context)
    {
        var isNativeClient = string.Equals(
            context.User.FindFirst(TokenService.IdentityTypeClaim)?.Value,
            TokenService.NativeClientIdentityType,
            StringComparison.Ordinal);
        if (!isNativeClient)
        {
            await next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        var method = context.Request.Method;

        // Single-session native logout is always reachable by a valid native session regardless of
        // which Studio scopes were granted.
        if (HttpMethods.IsPost(method)
            && path.Equals("/api/auth/client/logout", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var requiredScope = RequiredScope(method, path);
        if (requiredScope is null)
        {
            await DenyAsync(context, null);
            return;
        }

        var grantedScopes = context.User.FindAll(TokenService.ScopeClaim)
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!grantedScopes.Contains(requiredScope))
        {
            await DenyAsync(context, requiredScope);
            return;
        }

        await next(context);
    }

    internal static string? RequiredScope(string method, string path)
    {
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method))
        {
            if (path.Equals("/api/studio/session", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/api/studio/catalog", StringComparison.OrdinalIgnoreCase)
                || StudioDocumentSummaryRegex().IsMatch(path)
                || path.Equals("/api/studio/hosted-jobs", StringComparison.OrdinalIgnoreCase)
                || HostedJobRunsListRegex().IsMatch(path)
                || HostedJobRunDetailRegex().IsMatch(path))
                return NativeClientScopes.StudioRead;

            if (path.Equals("/api/studio/pipelines/runnable", StringComparison.OrdinalIgnoreCase)
                || PipelineRevisionRegex().IsMatch(path)
                || PipelineRunStatusRegex().IsMatch(path)
                || PipelineRunByRequestRegex().IsMatch(path))
                return NativeClientScopes.StudioRun;

            return null;
        }

        if (HttpMethods.IsPost(method))
        {
            if (PipelineRunsSubmitRegex().IsMatch(path)
                || PipelineRunCancelRegex().IsMatch(path)
                || HostedJobTriggerRegex().IsMatch(path)
                || HostedJobRunCancelRegex().IsMatch(path))
                return NativeClientScopes.StudioRun;

            if (path.Equals("/api/designer/save", StringComparison.OrdinalIgnoreCase))
                return NativeClientScopes.StudioSave;

            return null;
        }

        return null;
    }

    private static async Task DenyAsync(HttpContext context, string? requiredScope)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "native_client_scope_denied",
            requiredScope
        });
    }
}
