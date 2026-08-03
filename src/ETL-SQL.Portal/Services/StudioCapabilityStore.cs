using ETL_SQL.Portal.Data;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Resolves the Studio capabilities granted to a principal through group membership, and to a
/// service account through its own assignment.
///
/// Kept apart from <see cref="StudioAuthorizationService"/> deliberately: that service answers "does
/// this principal hold this capability?" from claims and configuration alone, with no database, so
/// the per-request check stays cheap. This one runs at sign-in and at token issue, where a database
/// read is already happening, and produces the claims that service then reads.
/// </summary>
public sealed class StudioCapabilityStore(PortalDbContext db)
{
    /// <summary>Capabilities the user's groups grant, deduplicated and ordered.</summary>
    public async Task<IReadOnlyList<string>> ResolveForUserAsync(int userId, CancellationToken ct = default)
    {
        var granted = await db.GroupStudioCapabilities
            .Where(grant => db.UserGroups.Any(membership =>
                membership.UserId == userId && membership.GroupId == grant.GroupId))
            .Select(grant => grant.Capability)
            .Distinct()
            .ToListAsync(ct);

        return Normalize(granted);
    }

    /// <summary>Capabilities currently granted to one group.</summary>
    public async Task<IReadOnlyList<string>> ResolveForGroupAsync(int groupId, CancellationToken ct = default) =>
        Normalize(await db.GroupStudioCapabilities
            .Where(grant => grant.GroupId == groupId)
            .Select(grant => grant.Capability)
            .ToListAsync(ct));

    /// <summary>
    /// Replaces a group's capabilities. Returns the unknown names rather than silently dropping
    /// them — a typo in a capability name would otherwise read as a successful grant that does
    /// nothing.
    /// </summary>
    public async Task<IReadOnlyList<string>> ReplaceForGroupAsync(
        int groupId, IEnumerable<string> capabilities, CancellationToken ct = default)
    {
        var requested = capabilities.Select(value => value.Trim()).Where(value => value.Length > 0).ToList();
        var unknown = requested
            .Where(value => !StudioCapabilities.All.Contains(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (unknown.Count > 0) return unknown;

        var canonical = Normalize(requested);
        var existing = await db.GroupStudioCapabilities.Where(g => g.GroupId == groupId).ToListAsync(ct);

        db.GroupStudioCapabilities.RemoveRange(
            existing.Where(row => !canonical.Contains(row.Capability, StringComparer.OrdinalIgnoreCase)));

        foreach (var capability in canonical)
        {
            if (existing.Any(row => string.Equals(row.Capability, capability, StringComparison.OrdinalIgnoreCase)))
                continue;
            db.GroupStudioCapabilities.Add(new GroupStudioCapability
            {
                GroupId = groupId,
                Capability = capability
            });
        }

        return [];
    }

    /// <summary>Canonical spelling from <see cref="StudioCapabilities.All"/>, ordered and deduplicated.</summary>
    private static IReadOnlyList<string> Normalize(IEnumerable<string> capabilities)
    {
        var canonical = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in capabilities)
        {
            var match = StudioCapabilities.All.FirstOrDefault(
                known => string.Equals(known, value, StringComparison.OrdinalIgnoreCase));
            if (match is not null) canonical.Add(match);
        }

        return [.. canonical.OrderBy(value => value, StringComparer.Ordinal)];
    }

    /// <summary>Parses the space-separated form service accounts store.</summary>
    public static IReadOnlyList<string> Parse(string? stored) =>
        Normalize((stored ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries));

    public static string Format(IEnumerable<string> capabilities) => string.Join(' ', Normalize(capabilities));
}
