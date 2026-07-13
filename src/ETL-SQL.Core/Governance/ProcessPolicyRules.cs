using ETL_SQL.Services;

namespace ETL_SQL.Core.Governance;

/// <summary>
/// Operation boundary for script-launched processes. Currently gates Docker image references a
/// script may run; the same pattern extends to external executables when that surface is added.
/// </summary>
public static class ProcessPolicyRules
{
    /// <summary>
    /// Enforces the enterprise Docker image allowlist before a container is started. No-op when
    /// standalone/unenrolled or when the policy declares no allowlist. Refreshes the snapshot so a
    /// revoked/expired policy fails promptly.
    /// </summary>
    public static void EnforceDockerImage(IExecutionContext context, string image)
    {
        var snapshot = OperationPolicyBoundary.Refresh(context, "<docker-run>");
        if (!snapshot.IsEnrolled) return;

        var allowed = snapshot.GovernedValues
            .Where(pair => pair.Key.StartsWith("Security:AllowedDockerImages:",
                StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => pair.Value!.Trim())
            .ToArray();
        if (allowed.Length == 0) return;

        if (allowed.Any(pattern => DockerImageMatches(pattern, image))) return;

        var decision = OperationPolicyDecision.Deny(snapshot, "Process:AllowedDockerImages",
            image, $"permitted images: [{string.Join(", ", allowed)}]",
            $"Enterprise policy denied Docker image '{image}'.");
        throw new OperationPolicyDeniedException(decision);
    }

    /// <summary>
    /// Matches an image reference against an allowlist entry: <c>*</c> allows any, a <c>prefix/*</c>
    /// entry matches a registry/namespace prefix, a tagless <c>repo</c> entry matches that repo with
    /// any tag, and an exact <c>repo:tag</c> matches only that reference.
    /// </summary>
    public static bool DockerImageMatches(string pattern, string image)
    {
        pattern = pattern.Trim();
        image = image.Trim();
        if (pattern == "*") return true;
        if (pattern.EndsWith("/*", StringComparison.Ordinal))
            return image.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);
        if (string.Equals(pattern, image, StringComparison.OrdinalIgnoreCase)) return true;
        if (!HasTag(pattern))
            return string.Equals(pattern, RepoOf(image), StringComparison.OrdinalIgnoreCase);
        return false;
    }

    // The tag is the segment after the last ':' only when that ':' follows the last '/', so a
    // registry port (registry:5000/repo) is not mistaken for a tag.
    private static bool HasTag(string image)
    {
        var slash = image.LastIndexOf('/');
        var colon = image.LastIndexOf(':');
        return colon > slash;
    }

    private static string RepoOf(string image)
    {
        var slash = image.LastIndexOf('/');
        var colon = image.LastIndexOf(':');
        return colon > slash ? image[..colon] : image;
    }
}
