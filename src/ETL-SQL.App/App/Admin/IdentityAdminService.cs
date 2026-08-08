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
