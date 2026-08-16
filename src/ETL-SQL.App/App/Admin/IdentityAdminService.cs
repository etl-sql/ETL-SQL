using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.App.Admin;

/// <summary>
/// <c>admin portal-whoami</c> and the identity read verbs (<c>admin user …</c>, <c>admin group …</c>,
/// <c>admin session list</c>) over the Portal's administration API.
///
/// <para>Every verb prints a human-readable table by default and a stable object under
/// <c>--json</c>, and reports a distinct exit code per failure kind so a runbook can branch. The
/// API is id-keyed while operators think in names, so names are resolved through the catalog
/// endpoints, with not-found and ambiguous-match kept distinct.</para>
/// </summary>
public static class IdentityAdminService
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static async Task<int> RunAsync(CliContext ctx, ILogger logger)
    {
        try
        {
            var url = PortalAdminCredentials.ResolveUrl(ctx.PortalUrl);
            var credentials = await PortalAdminCredentials.ResolveAsync(
                ResolveSecretProvider(), ctx.PortalClientId, CancellationToken.None);

            var client = PortalAdminClient.Create(url);
            await client.AuthenticateAsync(credentials, CancellationToken.None);

            return ctx.Command switch
            {
                "admin-portal-whoami" => WhoAmI(client, ctx, logger),
                "admin-user-list" => await UserListAsync(client, ctx, logger),
                "admin-user-show" => await UserShowAsync(client, ctx, logger),
                "admin-user-permissions" => await UserPermissionsAsync(client, ctx, logger),
                "admin-group-list" => await GroupListAsync(client, ctx, logger),
                "admin-group-members" => await GroupMembersAsync(client, ctx, logger),
                "admin-session-list" => await SessionListAsync(client, ctx, logger),
                "admin-session-disconnect" => await SessionDisconnectAsync(client, ctx, logger),
                "admin-user-create" => await UserCreateAsync(client, ctx, logger),
                "admin-user-delete" => await UserDeleteAsync(client, ctx, logger),
                "admin-user-enable" => await UserSetActiveAsync(client, ctx, logger, active: true),
                "admin-user-disable" => await UserSetActiveAsync(client, ctx, logger, active: false),
                "admin-user-revoke-tokens" => await UserRevokeTokensAsync(client, ctx, logger),
                "admin-group-create" => await GroupCreateAsync(client, ctx, logger),
                "admin-group-delete" => await GroupDeleteAsync(client, ctx, logger),
                "admin-group-add-member" => await GroupMemberChangeAsync(client, ctx, logger, add: true),
                "admin-group-remove-member" => await GroupMemberChangeAsync(client, ctx, logger, add: false),
                "admin-user-update" => await UserUpdateAsync(client, ctx, logger),
                "admin-user-reset-password" => await UserResetPasswordAsync(client, ctx, logger),
                "admin-group-update" => await GroupUpdateAsync(client, ctx, logger),
                "admin-group-capabilities" => await GroupCapabilitiesAsync(client, ctx, logger),
                "admin-group-set-capabilities" => await GroupSetCapabilitiesAsync(client, ctx, logger),
                "admin-access-simulate" => await AccessSimulateAsync(client, ctx, logger),
                "admin-service-account-list" => await ServiceAccountListAsync(client, ctx, logger),
                "admin-service-account-create" => await ServiceAccountCreateAsync(client, ctx, logger),
                "admin-service-account-update" => await ServiceAccountUpdateAsync(client, ctx, logger),
                "admin-service-account-rotate-secret" => await ServiceAccountRotateSecretAsync(client, ctx, logger),
                "admin-service-account-revoke" => await ServiceAccountRevokeAsync(client, ctx, logger),
                "admin-orchestrator-show" => await OrchestratorGrantShowAsync(client, ctx, logger),
                "admin-orchestrator-grant" => await OrchestratorGrantSetAsync(client, ctx, logger),
                "admin-orchestrator-revoke" => await OrchestratorGrantRevokeAsync(client, ctx, logger),
                "admin-orchestrator-set-owner" => await OrchestratorSetOwnerAsync(client, ctx, logger),
                "admin-orchestrator-unowned" => await OrchestratorUnownedAsync(client, ctx, logger),
                "admin-orchestrator-adopt" => await OrchestratorAdoptAsync(client, ctx, logger),
                _ => Fail(logger, AdminExitCode.ValidationError, $"Unknown identity command '{ctx.Command}'.")
            };
        }
        catch (AdminCliException ex)
        {
            return Fail(logger, ex.Code, ex.Message);
        }
        catch (OperationCanceledException)
        {
            return Fail(logger, AdminExitCode.Unreachable, "Cancelled.");
        }
    }

    private static int Fail(ILogger logger, AdminExitCode code, string message)
    {
        logger.WriteLine($"{message} (exit {(int)code}: {code})", ConsoleColor.Red);
        return (int)code;
    }

    // ── Verbs ────────────────────────────────────────────────────────────────────

    private static int WhoAmI(PortalAdminClient client, CliContext ctx, ILogger logger)
    {
        var identity = client.DescribeIdentity();
        if (ctx.IsJsonMode)
        {
            logger.WriteLine(JsonSerializer.Serialize(new
            {
                name = identity.Name,
                identityType = identity.IdentityType,
                roles = identity.Roles,
                scopes = identity.Scopes,
                expiresUtc = identity.ExpiresUtc
            }, Pretty));
            return 0;
        }

        logger.WriteLine($"Identity : {identity.Name} ({identity.IdentityType})");
        logger.WriteLine($"Roles    : {Join(identity.Roles)}");
        logger.WriteLine($"Scopes   : {Join(identity.Scopes)}");
        logger.WriteLine($"Expires  : {identity.ExpiresUtc?.ToString("u") ?? "(unknown)"}");
        return 0;
    }

    private static async Task<int> UserListAsync(PortalAdminClient client, CliContext ctx, ILogger logger)
    {
        var users = AsArray(await client.GetAsync("/api/admin/users", CancellationToken.None));
        var rows = users
            .Where(user => ctx.IncludeInactive || user?["isActive"]?.GetValue<bool>() != false)
            .Where(user => Matches(user?["userName"]?.GetValue<string>(), ctx.AdminFilter))
            .Where(user => ctx.AdminRole is null || Roles(user).Contains(ctx.AdminRole, StringComparer.OrdinalIgnoreCase))
            .ToList();

        return Emit(ctx, logger, rows,
            ["ID", "USERNAME", "EMAIL", "ROLES", "ACTIVE"],
            user =>
            [
                user?["id"]?.ToString() ?? "",
                user?["userName"]?.GetValue<string>() ?? "",
                user?["email"]?.GetValue<string>() ?? "",
                Join(Roles(user)),
                (user?["isActive"]?.GetValue<bool>() ?? false) ? "yes" : "no"
            ]);
    }

    private static async Task<int> UserShowAsync(PortalAdminClient client, CliContext ctx, ILogger logger)
    {
        var user = await ResolveUserAsync(client, ctx);
        if (ctx.IsJsonMode)
        {
            logger.WriteLine(user.ToJsonString(Pretty));
            return 0;
        }

        logger.WriteLine($"ID       : {user["id"]}");
        logger.WriteLine($"Username : {user["userName"]?.GetValue<string>()}");
        logger.WriteLine($"Email    : {user["email"]?.GetValue<string>()}");
        logger.WriteLine($"Active   : {((user["isActive"]?.GetValue<bool>() ?? false) ? "yes" : "no")}");
        logger.WriteLine($"Roles    : {Join(Roles(user))}");
        logger.WriteLine($"Provider : {user["provider"]?.GetValue<string>()}");
        return 0;
    }

    /// <summary>
    /// Answers "why can this person see this" without a browser — the highest-value read in the set.
    /// </summary>
    private static async Task<int> UserPermissionsAsync(PortalAdminClient client, CliContext ctx, ILogger logger)
    {
        var user = await ResolveUserAsync(client, ctx);
        var id = user["id"]!.GetValue<int>();
        var permissions = await client.GetAsync($"/api/admin/permissions/effective/user/{id}", CancellationToken.None);

        if (ctx.IsJsonMode)
        {
            logger.WriteLine(permissions?.ToJsonString(Pretty) ?? "null");
            return 0;
        }

        logger.WriteLine($"Effective permissions for {user["userName"]?.GetValue<string>()} (id {id}):");
        // The payload shape is owned by the Portal; render it generically rather than pinning a
        // schema the CLI would then have to track.
        Render(logger, permissions, indent: "  ");
        return 0;
    }

    private static async Task<int> GroupListAsync(PortalAdminClient client, CliContext ctx, ILogger logger)
    {
        var groups = AsArray(await client.GetAsync("/api/admin/groups", CancellationToken.None))
            .Where(group => Matches(group?["name"]?.GetValue<string>(), ctx.AdminFilter))
            .ToList();

        return Emit(ctx, logger, groups,
            ["ID", "NAME", "PROVIDER", "DESCRIPTION"],
            group =>
            [
                group?["id"]?.ToString() ?? "",
                group?["name"]?.GetValue<string>() ?? "",
                group?["provider"]?.GetValue<string>() ?? "",
                group?["description"]?.GetValue<string>() ?? ""
            ]);
    }

    private static async Task<int> GroupMembersAsync(PortalAdminClient client, CliContext ctx, ILogger logger)
    {
        var group = await ResolveGroupAsync(client, ctx);
        var id = group["id"]!.GetValue<int>();
        var members = AsArray(await client.GetAsync($"/api/admin/groups/{id}/members", CancellationToken.None));

        return Emit(ctx, logger, members,
            ["ID", "USERNAME", "EMAIL"],
            member =>
            [
                member?["id"]?.ToString() ?? "",
                member?["userName"]?.GetValue<string>() ?? "",
                member?["email"]?.GetValue<string>() ?? ""
            ]);
    }

    private static async Task<int> SessionListAsync(PortalAdminClient client, CliContext ctx, ILogger logger)
    {
        var sessions = AsArray(await client.GetAsync("/api/admin/sessions", CancellationToken.None))
            .Where(session => Matches(session?["userName"]?.GetValue<string>(), ctx.AdminFilter))
            .ToList();

        return Emit(ctx, logger, sessions,
            ["USERNAME", "STARTED", "LAST SEEN", "ADDRESS"],
            session =>
            [
                session?["userName"]?.GetValue<string>() ?? "",
                session?["createdAt"]?.ToString() ?? "",
                session?["lastSeenAt"]?.ToString() ?? "",
                session?["ipAddress"]?.GetValue<string>() ?? ""
            ]);
    }

    // ── Mutating verbs ───────────────────────────────────────────────────────────

    /// <summary>
    /// <c>--if-not-exists</c> makes a re-run a no-op rather than an error. That property is what
    /// makes the CLI worth having over the web UI: a provisioning runbook can be run twice, or
    /// resumed after a partial failure, without a human deciding which steps to skip.
    /// </summary>
    private static async Task<int> UserCreateAsync(PortalAdminClient client, CliContext ctx, ILogger logger)
    {
        Require(ctx.AdminUsername, "--username");

        if (ctx.IfNotExists && await FindAsync(client, "/api/admin/users", "userName", ctx.AdminUsername) is not null)
        {
            logger.WriteLine($"User '{ctx.AdminUsername}' already exists; nothing to do.");
            return 0;
        }

        var password = ReadPasswordFromStdin(ctx);
        var created = await client.PostAsync("/api/admin/users", new
        {
            username = ctx.AdminUsername,
            email = ctx.AdminEmail ?? "",
            role = ctx.AdminRole ?? "Viewer",
            password,
            provider = ctx.AdminProvider
        }, CancellationToken.None);

        logger.WriteLine($"Created user '{ctx.AdminUsername}' (id {created?["id"]}).", ConsoleColor.Green);
        return 0;
    }

    private static async Task<int> UserDeleteAsync(PortalAdminClient client, CliContext ctx, ILogger logger)
    {
        Require(ctx.AdminUsername, "--username");

        var user = await FindAsync(client, "/api/admin/users", "userName", ctx.AdminUsername);
        if (user is null)
        {
            if (ctx.IfExists)
            {
                logger.WriteLine($"User '{ctx.AdminUsername}' does not exist; nothing to do.");
                return 0;
            }
            throw new AdminCliException(AdminExitCode.NotFound, $"No user named '{ctx.AdminUsername}'.");
        }

        await client.DeleteAsync($"/api/admin/users/{user["id"]}", CancellationToken.None,
            VersionFor(ctx, user));
        logger.WriteLine($"Deleted user '{ctx.AdminUsername}'.", ConsoleColor.Green);
        return 0;
    }

    private static async Task<int> UserSetActiveAsync(
        PortalAdminClient client, CliContext ctx, ILogger logger, bool active)
    {
        var user = await ResolveUserAsync(client, ctx);
        if ((user["isActive"]?.GetValue<bool>() ?? false) == active)
        {
            logger.WriteLine($"User '{ctx.AdminUsername}' is already {(active ? "enabled" : "disabled")}; nothing to do.");
            return 0;
        }

        await client.PutAsync($"/api/admin/users/{user["id"]}", new { isActive = active },
            CancellationToken.None, VersionFor(ctx, user));
        logger.WriteLine($"{(active ? "Enabled" : "Disabled")} user '{ctx.AdminUsername}'.", ConsoleColor.Green);
        return 0;
    }

    private static async Task<int> UserRevokeTokensAsync(PortalAdminClient client, CliContext ctx, ILogger logger)
    {
        var user = await ResolveUserAsync(client, ctx);
        await client.PostAsync($"/api/admin/users/{user["id"]}/revoke-tokens", null, CancellationToken.None);
        logger.WriteLine($"Revoked tokens for '{ctx.AdminUsername}'.", ConsoleColor.Green);
        return 0;
    }

    private static async Task<int> SessionDisconnectAsync(PortalAdminClient client, CliContext ctx, ILogger logger)
    {
        var user = await ResolveUserAsync(client, ctx);
        await client.PostAsync($"/api/admin/users/{user["id"]}/disconnect", null, CancellationToken.None);
        logger.WriteLine($"Disconnected sessions for '{ctx.AdminUsername}'.", ConsoleColor.Green);
        return 0;
    }

    private static async Task<int> GroupCreateAsync(PortalAdminClient client, CliContext ctx, ILogger logger)
    {
        Require(ctx.AdminGroupName, "--name");

        if (ctx.IfNotExists && await FindAsync(client, "/api/admin/groups", "name", ctx.AdminGroupName) is not null)
        {
            logger.WriteLine($"Group '{ctx.AdminGroupName}' already exists; nothing to do.");
            return 0;
        }

        var created = await client.PostAsync("/api/admin/groups", new
        {
            name = ctx.AdminGroupName,
            description = ctx.AdminDescription
        }, CancellationToken.None);

        logger.WriteLine($"Created group '{ctx.AdminGroupName}' (id {created?["id"]}).", ConsoleColor.Green);
        return 0;
    }

    private static async Task<int> GroupDeleteAsync(PortalAdminClient client, CliContext ctx, ILogger logger)
    {
        Require(ctx.AdminGroupName, "--name");

        var group = await FindAsync(client, "/api/admin/groups", "name", ctx.AdminGroupName);
        if (group is null)
        {
            if (ctx.IfExists)
            {
                logger.WriteLine($"Group '{ctx.AdminGroupName}' does not exist; nothing to do.");
                return 0;
            }
            throw new AdminCliException(AdminExitCode.NotFound, $"No group named '{ctx.AdminGroupName}'.");
        }

        await client.DeleteAsync($"/api/admin/groups/{group["id"]}", CancellationToken.None,
            VersionFor(ctx, group));
        logger.WriteLine($"Deleted group '{ctx.AdminGroupName}'.", ConsoleColor.Green);
        return 0;
    }

    private static async Task<int> GroupMemberChangeAsync(
        PortalAdminClient client, CliContext ctx, ILogger logger, bool add)
    {
        var group = await ResolveGroupAsync(client, ctx);
        var user = await ResolveUserAsync(client, ctx);
        var groupId = group["id"]!.GetValue<int>();
        var userId = user["id"]!.GetValue<int>();

        var members = AsArray(await client.GetAsync($"/api/admin/groups/{groupId}/members", CancellationToken.None));
        var isMember = members.Any(member => member?["id"]?.GetValue<int>() == userId);

        // Membership changes are naturally idempotent, so they are treated that way unconditionally
        // rather than behind a flag: adding an existing member is what a re-run does.
        if (add == isMember)
        {
            logger.WriteLine($"'{ctx.AdminUsername}' is already {(add ? "a member of" : "absent from")} '{ctx.AdminGroupName}'; nothing to do.");
            return 0;
        }

        if (add)
            await client.PostAsync($"/api/admin/groups/{groupId}/members", new { userId }, CancellationToken.None);
        else
            await client.DeleteAsync($"/api/admin/groups/{groupId}/members/{userId}", CancellationToken.None);

        logger.WriteLine(
            $"{(add ? "Added" : "Removed")} '{ctx.AdminUsername}' {(add ? "to" : "from")} '{ctx.AdminGroupName}'.",
            ConsoleColor.Green);
        return 0;
    }

    /// <summary>
    /// Only the fields actually supplied are sent, so updating an email cannot silently blank a
    /// name the caller never mentioned.
    /// </summary>
    private static async Task<int> UserUpdateAsync(PortalAdminClient client, CliContext ctx, ILogger logger)
    {
        var user = await ResolveUserAsync(client, ctx);

        var changes = new Dictionary<string, object?>();
        if (ctx.AdminEmail is not null) changes["email"] = ctx.AdminEmail;
        if (ctx.AdminFirstName is not null) changes["firstName"] = ctx.AdminFirstName;
        if (ctx.AdminLastName is not null) changes["lastName"] = ctx.AdminLastName;
        if (ctx.AdminRole is not null) changes["role"] = ctx.AdminRole;

        if (changes.Count == 0)
            throw new AdminCliException(AdminExitCode.ValidationError,
                "Nothing to update. Supply at least one of --email, --first-name, --last-name, --role.");

        await client.PutAsync($"/api/admin/users/{user["id"]}", changes,
            CancellationToken.None, VersionFor(ctx, user));
        logger.WriteLine($"Updated user '{ctx.AdminUsername}' ({string.Join(", ", changes.Keys)}).", ConsoleColor.Green);
        return 0;
    }

    private static async Task<int> UserResetPasswordAsync(PortalAdminClient client, CliContext ctx, ILogger logger)
    {
        var user = await ResolveUserAsync(client, ctx);

        // No fallback to a prompt or a flag: the new password has exactly one source.
        if (!ctx.PasswordStdin)
            throw new AdminCliException(AdminExitCode.ValidationError,
                "--password-stdin is required. A password is never accepted as a command-line argument.");

        var password = ReadPasswordFromStdin(ctx);
        await client.PostAsync($"/api/admin/users/{user["id"]}/reset-password",
            new { newPassword = password }, CancellationToken.None, VersionFor(ctx, user));

        logger.WriteLine($"Reset the password for '{ctx.AdminUsername}'.", ConsoleColor.Green);
        return 0;
    }

    private static async Task<int> GroupUpdateAsync(PortalAdminClient client, CliContext ctx, ILogger logger)
    {
        var group = await ResolveGroupAsync(client, ctx);

        var changes = new Dictionary<string, object?>();
        if (ctx.AdminNewName is not null) changes["name"] = ctx.AdminNewName;
        if (ctx.AdminDescription is not null) changes["description"] = ctx.AdminDescription;

        if (changes.Count == 0)
            throw new AdminCliException(AdminExitCode.ValidationError,
                "Nothing to update. Supply --new-name or --description.");

        await client.PutAsync($"/api/admin/groups/{group["id"]}", changes,
            CancellationToken.None, VersionFor(ctx, group));
        logger.WriteLine($"Updated group '{ctx.AdminGroupName}' ({string.Join(", ", changes.Keys)}).", ConsoleColor.Green);
        return 0;
    }

    private static async Task<int> GroupCapabilitiesAsync(PortalAdminClient client, CliContext ctx, ILogger logger)
    {
        var group = await ResolveGroupAsync(client, ctx);
        var payload = await client.GetAsync(
            $"/api/admin/groups/{group["id"]}/studio-capabilities", CancellationToken.None);

        if (ctx.IsJsonMode)
        {
            logger.WriteLine(payload?.ToJsonString(Pretty) ?? "null");
            return 0;
        }

        var granted = ToStrings(payload?["capabilities"]);
        var available = ToStrings(payload?["available"]);
        logger.WriteLine($"Granted   : {Join(granted)}");
        logger.WriteLine($"Available : {Join(available)}");
        return 0;
    }

    /// <summary>
    /// Replaces the grant wholesale, matching the API. Stated plainly because "set" reading as
    /// "add" is the kind of misunderstanding that quietly removes someone's access.
    /// </summary>
    private static async Task<int> GroupSetCapabilitiesAsync(PortalAdminClient client, CliContext ctx, ILogger logger)
    {
        var group = await ResolveGroupAsync(client, ctx);
        var capabilities = (ctx.AdminCapabilities ?? []).ToArray();

        await client.PutAsync($"/api/admin/groups/{group["id"]}/studio-capabilities",
            new { capabilities }, CancellationToken.None);

        logger.WriteLine(
            capabilities.Length == 0
                ? $"Cleared all Studio capabilities for '{ctx.AdminGroupName}'."
                : $"Set Studio capabilities for '{ctx.AdminGroupName}' to: {string.Join(", ", capabilities)}.",
            ConsoleColor.Green);
        return 0;
    }

    private static async Task<int> AccessSimulateAsync(PortalAdminClient client, CliContext ctx, ILogger logger)
    {
        var user = await ResolveUserAsync(client, ctx);
        var result = await client.GetAsync(
            $"/api/admin/access-simulator/user/{user["id"]}", CancellationToken.None);

        if (ctx.IsJsonMode)
        {
            logger.WriteLine(result?.ToJsonString(Pretty) ?? "null");
            return 0;
        }

        logger.WriteLine($"Simulated access for {user["userName"]?.GetValue<string>()} (id {user["id"]}):");
        Render(logger, result, indent: "  ");
        return 0;
    }

    // ── Orchestrator object grants ───────────────────────────────────────────────
    //
    // Headless provisioning of the per-object model. These go through the Portal's proxy rather than
    // straight to the Orchestrator, so the CLI authenticates once, the same way it does for every
    // other admin command, and the Orchestrator sees a Portal-signed identity like any other caller.
    // A CLI that talked to the Orchestrator directly would need the shared signing secret on the
    // operator's machine, which is the thing the exchange shape exists to avoid.

    private static string GrantPath(CliContext ctx) =>
        $"/api/orchestrator/authorization/{Uri.EscapeDataString(RequireKind(ctx))}" +
        $"/{Uri.EscapeDataString(Required(ctx.GrantObjectName, "--object"))}";

    private static string GrantPrincipalPath(CliContext ctx) =>
        GrantPath(ctx) +
        $"/{Uri.EscapeDataString(RequirePrincipalKind(ctx))}" +
        $"/{Uri.EscapeDataString(Required(ctx.GrantPrincipalId, "--principal"))}";

    private static string RequireKind(CliContext ctx) =>
        Choice(ctx.GrantObjectKind, "--kind", "JOB", "SCHEDULE", "NOTIFICATION");

    private static string RequirePrincipalKind(CliContext ctx) =>
        Choice(ctx.GrantPrincipalKind, "--principal-kind", "USER", "GROUP", "SERVICE");

    /// <summary>Reuses the existing <c>Require</c> guard and returns the trimmed value.</summary>
    private static string Required(string? value, string option)
    {
        Require(value, option);
        return value!.Trim();
    }

    /// <summary>
    /// Validates a closed set locally so a typo is refused before it becomes an HTTP round trip that
    /// answers 400 — and, more usefully, so the message names the allowed values.
    /// </summary>
    private static string Choice(string? value, string option, params string[] allowed)
    {
        var normalized = Required(value, option).ToUpperInvariant();
        return allowed.Contains(normalized, StringComparer.OrdinalIgnoreCase)
            ? normalized
            : throw new AdminCliException(
                AdminExitCode.ValidationError,
                $"{option} must be one of {string.Join(", ", allowed)}.");
    }

    private static async Task<int> OrchestratorGrantShowAsync(
        PortalAdminClient client, CliContext ctx, ILogger logger)
    {
        var response = await client.GetAsync(GrantPath(ctx), CancellationToken.None);
        var grants = AsArray(response);
        if (ctx.IsJsonMode)
        {
            logger.WriteLine(response?.ToJsonString(Pretty) ?? "[]");
            return 0;
        }

        if (grants.Count == 0)
        {
            // Said plainly, because "no grants" and "you cannot see the grants" look identical in an
            // empty list, and only the first is normal. The route answers 403 for the second.
            logger.WriteLine($"{RequireKind(ctx)} '{ctx.GrantObjectName}' has no grants; only its owner and administrators can reach it.");
            return 0;
        }

        logger.WriteLine($"Grants on {RequireKind(ctx)} '{ctx.GrantObjectName}':");
        foreach (var grant in grants)
        {
            logger.WriteLine(
                $"  {grant?["principalKind"]}:{grant?["principalId"]} = {grant?["permission"]} " +
                $"(granted by {grant?["grantedBy"]})");
        }
        return 0;
    }

    private static async Task<int> OrchestratorGrantSetAsync(
        PortalAdminClient client, CliContext ctx, ILogger logger)
    {
        var permission = Choice(ctx.GrantPermission, "--permission", "READ", "EXECUTE", "OVERRIDE", "MANAGE");
        var result = await client.PutAsync(
            GrantPrincipalPath(ctx), new { permission }, CancellationToken.None);

        if (ctx.IsJsonMode)
        {
            logger.WriteLine(result?.ToJsonString(Pretty) ?? "null");
            return 0;
        }

        logger.WriteLine(
            $"Granted {permission} on {RequireKind(ctx)} '{ctx.GrantObjectName}' to " +
            $"{RequirePrincipalKind(ctx)}:{ctx.GrantPrincipalId}.");
        return 0;
    }

    private static async Task<int> OrchestratorGrantRevokeAsync(
        PortalAdminClient client, CliContext ctx, ILogger logger)
    {
        await client.DeleteAsync(GrantPrincipalPath(ctx), CancellationToken.None);
        if (!ctx.IsJsonMode)
        {
            logger.WriteLine(
                $"Revoked {RequirePrincipalKind(ctx)}:{ctx.GrantPrincipalId} on " +
                $"{RequireKind(ctx)} '{ctx.GrantObjectName}'.");
        }
        return 0;
    }

    // ── Orchestrator object ownership ────────────────────────────────────────────
    //
    // An owner may manage their own object, so ownership is the authority grants are administered
    // from; reassigning it is an administrator's act and the Orchestrator refuses anyone else. The
    // headless path exists for the two cases a UI is bad at: an owner who has left the organization,
    // and a box that has just attached a Portal and needs someone made accountable for everything it
    // already had.

    /// <summary>
    /// Owner principal kind. A group cannot own an object — ownership names who is accountable, and
    /// the decision path compares it against one caller's key, so a group owner would read as owned
    /// and behave as unowned.
    /// </summary>
    private static string RequireOwnerKind(CliContext ctx) =>
        Choice(ctx.GrantPrincipalKind, "--principal-kind", "USER", "SERVICE");

    private static async Task<int> OrchestratorSetOwnerAsync(
        PortalAdminClient client, CliContext ctx, ILogger logger)
    {
        var principalKind = RequireOwnerKind(ctx);
        var principalId = Required(ctx.GrantPrincipalId, "--principal");
        var result = await client.PutAsync(
            GrantPath(ctx) + "/owner", new { principalKind, principalId }, CancellationToken.None);

        if (ctx.IsJsonMode)
        {
            logger.WriteLine(result?.ToJsonString(Pretty) ?? "null");
            return 0;
        }

        // The previous owner is named because reassignment is not always a repair: on an object that
        // already had one, this is a transfer, and an operator who meant to adopt an orphan should see
        // that they moved someone else's object instead.
        var previous = result?["previousOwner"]?.GetValue<string>();
        logger.WriteLine(
            $"{RequireKind(ctx)} '{ctx.GrantObjectName}' is now owned by {principalKind.ToLowerInvariant()}:{principalId} " +
            $"(previously {previous ?? "unowned"}).");
        return 0;
    }

    private static async Task<int> OrchestratorUnownedAsync(
        PortalAdminClient client, CliContext ctx, ILogger logger)
    {
        var response = await client.GetAsync("/api/orchestrator/authorization/unowned", CancellationToken.None);
        var objects = AsArray(response);
        if (ctx.IsJsonMode)
        {
            logger.WriteLine(response?.ToJsonString(Pretty) ?? "[]");
            return 0;
        }

        if (objects.Count == 0)
        {
            logger.WriteLine("Every job, schedule, and notification has a recorded owner.");
            return 0;
        }

        logger.WriteLine($"{objects.Count} object(s) with no recorded owner — reachable only by administrators:");
        foreach (var entry in objects)
            logger.WriteLine($"  {entry?["kind"]} {entry?["name"]}");
        logger.WriteLine("Assign an owner with 'admin orchestrator adopt' or 'admin orchestrator set-owner'.");
        return 0;
    }

    private static async Task<int> OrchestratorAdoptAsync(
        PortalAdminClient client, CliContext ctx, ILogger logger)
    {
        var principalKind = RequireOwnerKind(ctx);
        var principalId = Required(ctx.GrantPrincipalId, "--principal");
        // --kind is optional here, unlike every other orchestrator verb: adoption's normal case is
        // "everything this box already had", and requiring a kind would make the normal case three
        // commands that each look complete on their own.
        var kind = string.IsNullOrWhiteSpace(ctx.GrantObjectKind) ? null : RequireKind(ctx);
        var result = await client.PostAsync(
            "/api/orchestrator/authorization/adopt",
            new { principalKind, principalId, kind },
            CancellationToken.None);

        if (ctx.IsJsonMode)
        {
            logger.WriteLine(result?.ToJsonString(Pretty) ?? "null");
            return 0;
        }

        var count = result?["count"]?.GetValue<int>() ?? 0;
        if (count == 0)
        {
            logger.WriteLine("No unowned objects to adopt.");
            return 0;
        }

        logger.WriteLine(
            $"{count} object(s) adopted by {principalKind.ToLowerInvariant()}:{principalId}:");
        foreach (var entry in AsArray(result?["adopted"]))
            logger.WriteLine($"  {entry?["kind"]} {entry?["name"]}");
        return 0;
    }

    // ── Service accounts ─────────────────────────────────────────────────────────

    private static async Task<int> ServiceAccountListAsync(
        PortalAdminClient client, CliContext ctx, ILogger logger)
    {
        var accounts = AsArray(await client.GetAsync("/api/admin/service-accounts", CancellationToken.None))
            .Where(account => Matches(account?["name"]?.GetValue<string>(), ctx.AdminFilter))
            .ToList();

        return Emit(ctx, logger, accounts,
            ["ID", "NAME", "CLIENT ID", "OWNER", "SCOPES", "ENABLED", "REVOKED", "VERSION"],
            account =>
            [
                account?["id"]?.ToString() ?? "",
                account?["name"]?.GetValue<string>() ?? "",
                account?["clientId"]?.GetValue<string>() ?? "",
                account?["ownerUserId"]?.ToString() ?? "",
                Join(ToStrings(account?["scopes"])),
                (account?["isEnabled"]?.GetValue<bool>() ?? false) ? "yes" : "no",
                account?["revokedAt"] is null ? "no" : "yes",
                account?["version"]?.ToString() ?? ""
            ]);
    }

    private static async Task<int> ServiceAccountCreateAsync(
        PortalAdminClient client, CliContext ctx, ILogger logger)
    {
        Require(ctx.ServiceAccountName, "--name");
        Require(ctx.ServiceAccountOwner, "--owner");
        if (ctx.IfNotExists
            && await FindAsync(client, "/api/admin/service-accounts", "name", ctx.ServiceAccountName) is not null)
        {
            logger.WriteLine($"Service account '{ctx.ServiceAccountName}' already exists; nothing to do.");
            return 0;
        }

        var scopes = Clean(ctx.ServiceAccountScopes);
        if (scopes.Length == 0)
            throw new AdminCliException(AdminExitCode.ValidationError,
                "At least one --scope is required.");

        var owner = await ResolveAsync(client, ctx, "/api/admin/users", "userName",
            ctx.ServiceAccountOwner, "user", "--owner");
        await using var output = OneTimeSecretFile.Reserve(ctx.ServiceAccountSecretOutput);
        var created = await client.PostAsync("/api/admin/service-accounts", new
        {
            name = ctx.ServiceAccountName,
            description = ctx.ServiceAccountDescription,
            ownerUserId = owner["id"]!.GetValue<int>(),
            scopes,
            roles = Clean(ctx.ServiceAccountRoles),
            expiresAt = ParseExpiry(ctx.ServiceAccountExpiresAt),
            studioCapabilities = Clean(ctx.ServiceAccountCapabilities)
        }, CancellationToken.None);

        var secret = created?["clientSecret"]?.GetValue<string>();
        await output.CommitAsync(secret ?? "", CancellationToken.None);
        return EmitSecretResult(ctx, logger, created?["account"], output.Path, "Created");
    }

    private static async Task<int> ServiceAccountUpdateAsync(
        PortalAdminClient client, CliContext ctx, ILogger logger)
    {
        var account = await ResolveServiceAccountAsync(client, ctx);
        if (ctx.ServiceAccountEnable && ctx.ServiceAccountDisable)
            throw new AdminCliException(AdminExitCode.ValidationError,
                "--enable and --disable are mutually exclusive.");
        if (ctx.ServiceAccountClearExpiry && ctx.ServiceAccountExpiresAt is not null)
            throw new AdminCliException(AdminExitCode.ValidationError,
                "--clear-expiry and --expires-at are mutually exclusive.");
        if (ctx.ServiceAccountClearCapabilities && ctx.ServiceAccountCapabilities is not null)
            throw new AdminCliException(AdminExitCode.ValidationError,
                "--clear-capabilities and --capability are mutually exclusive.");

        var changesRequested = ctx.ServiceAccountEnable || ctx.ServiceAccountDisable
            || ctx.ServiceAccountClearExpiry || ctx.ServiceAccountExpiresAt is not null
            || ctx.ServiceAccountScopes is not null || ctx.ServiceAccountCapabilities is not null
            || ctx.ServiceAccountClearCapabilities;
        if (!changesRequested)
            throw new AdminCliException(AdminExitCode.ValidationError,
                "Nothing to update. Supply --enable, --disable, --expires-at, --clear-expiry, " +
                "--scope, --capability, or --clear-capabilities.");

        var enabled = ctx.ServiceAccountEnable || (!ctx.ServiceAccountDisable
            && (account["isEnabled"]?.GetValue<bool>() ?? false));
        var expiresAt = ctx.ServiceAccountClearExpiry
            ? null
            : ctx.ServiceAccountExpiresAt is not null
                ? ParseExpiry(ctx.ServiceAccountExpiresAt)
                : ReadDate(account["expiresAt"]);
        var scopes = ctx.ServiceAccountScopes is null
            ? ToStrings(account["scopes"])
            : Clean(ctx.ServiceAccountScopes);
        if (scopes.Length == 0)
            throw new AdminCliException(AdminExitCode.ValidationError,
                "An update cannot clear every scope. Supply at least one --scope.");

        await client.PutAsync($"/api/admin/service-accounts/{account["id"]}", new
        {
            isEnabled = enabled,
            expiresAt,
            scopes,
            studioCapabilities = ctx.ServiceAccountClearCapabilities
                ? [] : ctx.ServiceAccountCapabilities is null
                    ? null : Clean(ctx.ServiceAccountCapabilities)
        }, CancellationToken.None, VersionFor(ctx, account));

        logger.WriteLine($"Updated service account '{ctx.ServiceAccountName}'.", ConsoleColor.Green);
        return 0;
    }

    private static async Task<int> ServiceAccountRotateSecretAsync(
        PortalAdminClient client, CliContext ctx, ILogger logger)
    {
        var account = await ResolveServiceAccountAsync(client, ctx);
        await using var output = OneTimeSecretFile.Reserve(ctx.ServiceAccountSecretOutput);
        var rotated = await client.PostAsync(
            $"/api/admin/service-accounts/{account["id"]}/rotate-secret", null,
            CancellationToken.None, VersionFor(ctx, account));
        var secret = rotated?["clientSecret"]?.GetValue<string>();
        await output.CommitAsync(secret ?? "", CancellationToken.None);
        return EmitSecretResult(ctx, logger, rotated?["account"], output.Path, "Rotated");
    }

    private static async Task<int> ServiceAccountRevokeAsync(
        PortalAdminClient client, CliContext ctx, ILogger logger)
    {
        var account = await ResolveServiceAccountAsync(client, ctx);
        if (account["revokedAt"] is not null)
        {
            logger.WriteLine($"Service account '{ctx.ServiceAccountName}' is already revoked; nothing to do.");
            return 0;
        }

        await client.PostAsync($"/api/admin/service-accounts/{account["id"]}/revoke", null,
            CancellationToken.None, VersionFor(ctx, account));
        logger.WriteLine($"Revoked service account '{ctx.ServiceAccountName}'.", ConsoleColor.Green);
        return 0;
    }

    private static int EmitSecretResult(
        CliContext ctx, ILogger logger, JsonNode? account, string outputPath, string action)
    {
        if (ctx.IsJsonMode)
        {
            logger.WriteLine(JsonSerializer.Serialize(new
            {
                account,
                secretWrittenTo = outputPath
            }, Pretty));
        }
        else
        {
            logger.WriteLine($"{action} service account '{account?["name"]}'.", ConsoleColor.Green);
            logger.WriteLine($"One-time secret written to: {outputPath}");
        }
        return 0;
    }

    private static Task<JsonObject> ResolveServiceAccountAsync(PortalAdminClient client, CliContext ctx) =>
        ResolveAsync(client, ctx, "/api/admin/service-accounts", "name",
            ctx.ServiceAccountName, "service account", "--name");

    private static string[] Clean(IEnumerable<string>? values) => (values ?? [])
        .Select(value => value.Trim()).Where(value => value.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static DateTime? ParseExpiry(string? value)
    {
        if (value is null) return null;
        if (!DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal |
                System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed))
            throw new AdminCliException(AdminExitCode.ValidationError,
                "--expires-at must be an ISO-8601 timestamp, for example 2027-01-31T23:59:59Z.");
        return parsed.UtcDateTime;
    }

    private static DateTime? ReadDate(JsonNode? node) => node is null
        ? null
        : DateTimeOffset.Parse(node.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal).UtcDateTime;

    // ── Mutation helpers ─────────────────────────────────────────────────────────

    private static void Require(string? value, string flag)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new AdminCliException(AdminExitCode.ValidationError, $"{flag} is required.");
    }

    /// <summary>
    /// The version to send in <c>If-Match</c>. <c>--if-version</c> pins an expected value so the
    /// write fails on drift; otherwise the version just read is carried through, which is still a
    /// detectable conflict rather than a blind overwrite.
    /// </summary>
    private static long? VersionFor(CliContext ctx, JsonNode record) =>
        ctx.IfVersion ?? record["version"]?.GetValue<long>();

    /// <summary>Returns the single match, null if absent. Ambiguity is still an error.</summary>
    private static async Task<JsonObject?> FindAsync(
        PortalAdminClient client, string path, string nameField, string? requested)
    {
        var matches = AsArray(await client.GetAsync(path, CancellationToken.None))
            .Where(item => string.Equals(item?[nameField]?.GetValue<string>(), requested, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return matches.Count switch
        {
            0 => null,
            1 => matches[0]!.AsObject(),
            _ => throw new AdminCliException(AdminExitCode.AmbiguousMatch,
                $"'{requested}' matched {matches.Count} records. Disambiguate by id.")
        };
    }

    /// <summary>
    /// Reads a password from standard input. Never from argv: a command line is readable by every
    /// process on the host and captured verbatim by CI logs.
    /// </summary>
    private static string? ReadPasswordFromStdin(CliContext ctx)
    {
        if (!ctx.PasswordStdin) return null;

        var password = Console.In.ReadToEnd()?.TrimEnd('\r', '\n');
        if (string.IsNullOrEmpty(password))
            throw new AdminCliException(AdminExitCode.ValidationError,
                "--password-stdin was given but standard input was empty.");
        return password;
    }

    // ── Name → id ────────────────────────────────────────────────────────────────

    private static Task<JsonObject> ResolveUserAsync(PortalAdminClient client, CliContext ctx) =>
        ResolveAsync(client, ctx, "/api/admin/users", "userName", ctx.AdminUsername, "user", "--username");

    private static Task<JsonObject> ResolveGroupAsync(PortalAdminClient client, CliContext ctx) =>
        ResolveAsync(client, ctx, "/api/admin/groups", "name", ctx.AdminGroupName, "group", "--name");

    /// <summary>
    /// Resolves a name to a record. Not-found and ambiguous-match get distinct exit codes because a
    /// runbook should retry the first and stop on the second — collapsing them into one generic
    /// failure is what makes an automation loop do the wrong thing quietly.
    /// </summary>
    private static async Task<JsonObject> ResolveAsync(
        PortalAdminClient client, CliContext ctx, string path, string nameField,
        string? requested, string noun, string flag)
    {
        if (string.IsNullOrWhiteSpace(requested))
            throw new AdminCliException(AdminExitCode.ValidationError, $"{flag} is required.");

        var matches = AsArray(await client.GetAsync(path, CancellationToken.None))
            .Where(item => string.Equals(item?[nameField]?.GetValue<string>(), requested, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return matches.Count switch
        {
            0 => throw new AdminCliException(AdminExitCode.NotFound, $"No {noun} named '{requested}'."),
            1 => matches[0]!.AsObject(),
            _ => throw new AdminCliException(AdminExitCode.AmbiguousMatch,
                $"'{requested}' matched {matches.Count} {noun}s. Disambiguate by id: " +
                string.Join(", ", matches.Select(match => match?["id"]?.ToString())))
        };
    }

    // ── Rendering ────────────────────────────────────────────────────────────────

    private static int Emit(
        CliContext ctx, ILogger logger, List<JsonNode?> rows,
        string[] headers, Func<JsonNode?, string[]> project)
    {
        if (ctx.IsJsonMode)
        {
            logger.WriteLine(new JsonArray(rows.Select(row => row?.DeepClone()).ToArray()).ToJsonString(Pretty));
            return 0;
        }

        if (rows.Count == 0)
        {
            logger.WriteLine("(none)");
            return 0;
        }

        var cells = rows.Select(project).ToList();
        var widths = headers
            .Select((header, index) => Math.Max(header.Length, cells.Max(row => row[index].Length)))
            .ToArray();

        logger.WriteLine(string.Join("  ", headers.Select((header, i) => header.PadRight(widths[i]))).TrimEnd());
        logger.WriteLine(string.Join("  ", widths.Select(width => new string('-', width))));
        foreach (var row in cells)
            logger.WriteLine(string.Join("  ", row.Select((cell, i) => cell.PadRight(widths[i]))).TrimEnd());

        return 0;
    }

    private static void Render(ILogger logger, JsonNode? node, string indent)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    if (value is JsonObject or JsonArray)
                    {
                        logger.WriteLine($"{indent}{key}:");
                        Render(logger, value, indent + "  ");
                    }
                    else logger.WriteLine($"{indent}{key}: {value}");
                }
                break;
            case JsonArray array:
                if (array.Count == 0) logger.WriteLine($"{indent}(none)");
                foreach (var item in array) Render(logger, item, indent);
                break;
            default:
                logger.WriteLine($"{indent}{node}");
                break;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static List<JsonNode?> AsArray(JsonNode? node) => node switch
    {
        JsonArray array => array.ToList(),
        // Paged endpoints wrap the rows; accept either shape.
        JsonObject obj when obj["items"] is JsonArray items => items.ToList(),
        JsonObject obj when obj["users"] is JsonArray users => users.ToList(),
        JsonObject obj when obj["groups"] is JsonArray groups => groups.ToList(),
        _ => []
    };

    private static string[] ToStrings(JsonNode? node) => node is JsonArray array
        ? array.Select(item => item?.GetValue<string>() ?? "").Where(item => item.Length > 0).ToArray()
        : [];

    private static string[] Roles(JsonNode? user) => user?["roles"] is JsonArray roles
        ? roles.Select(role => role?.GetValue<string>() ?? "").Where(role => role.Length > 0).ToArray()
        : [];

    private static bool Matches(string? value, string? filter) =>
        string.IsNullOrWhiteSpace(filter)
        || (value ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase);

    private static string Join(IEnumerable<string> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? "(none)" : string.Join(", ", list);
    }

    /// <summary>
    /// The machine-local secret store, used only to dereference a <c>SECRET:</c> credential. Null
    /// when unavailable, which surfaces as a clear auth error rather than a crash.
    /// </summary>
    private static ISecretProvider? ResolveSecretProvider()
    {
        try
        {
            return Program.ServiceProvider?.GetService(typeof(ISecretProvider)) as ISecretProvider;
        }
        catch
        {
            return null;
        }
    }
}
