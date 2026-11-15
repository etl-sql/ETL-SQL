using System.Text.RegularExpressions;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Services;
using Microsoft.Extensions.Configuration;

namespace ETL_SQL.Core.Governance;

/// <summary>
/// Operation boundary for script-launched processes and containers.
/// </summary>
public static partial class ProcessPolicyRules
{
    /// <summary>
    /// Enforces explicit Docker image approval before a container is started or resumed.
    /// Enrolled executions require a matching entry in governed <c>Security:AllowedDockerImages</c>;
    /// standalone executions require explicit host-admin approval in
    /// <c>Governance:Docker:AllowedImages</c>. Absent or empty lists fail closed.
    /// </summary>
    public static void EnforceDockerImage(
        IExecutionContext context,
        string image,
        IConfiguration? configuration = null)
    {
        if (string.IsNullOrWhiteSpace(image))
            throw new ExecutionException("Docker image name cannot be null or empty.");

        var snapshot = OperationPolicyBoundary.Refresh(context, "<docker-run>");
        if (snapshot.IsEnrolled)
        {
            EnforceEnrolledDockerPolicy(snapshot, image, configuration);
            return;
        }

        EnforceStandaloneDockerPolicy(context, image, configuration);
    }

    /// <summary>
    /// Validates a catalog-defined <c>CONTAINER</c> tool image: requires a valid pinned
    /// <c>@sha256:&lt;64 hex&gt;</c> digest and enforces enrolled organization policy (plus any
    /// explicit host-admin Docker allowlist if configured).
    /// </summary>
    public static void EnforceContainerToolImage(
        IExecutionContext context,
        string image,
        IConfiguration? configuration = null)
    {
        if (!HasValidPinnedDigest(image))
        {
            throw new ExecutionException(
                $"Container tool image '{image}' must use a valid pinned @sha256:<64-hex> digest.");
        }

        var snapshot = OperationPolicyBoundary.Refresh(context, "<docker-tool>");
        if (snapshot.IsEnrolled)
        {
            EnforceEnrolledDockerPolicy(snapshot, image, configuration);
            return;
        }

        var hostAllowed = ReadHostAllowedImages(configuration);
        if (hostAllowed.Length > 0 && !hostAllowed.Any(pattern => DockerImageMatches(pattern, image)))
        {
            EmitDeniedDockerEvent(context, image, "Host Docker policy denied container tool image.");
            throw new ExecutionException(
                $"Docker image '{image}' is not permitted by host-admin Docker policy (Governance:Docker:AllowedImages).");
        }
    }

    private static void EnforceEnrolledDockerPolicy(
        ExecutionPolicySnapshot snapshot,
        string image,
        IConfiguration? configuration)
    {
        var allowed = snapshot.GovernedValues
            .Where(pair => pair.Key.StartsWith("Security:AllowedDockerImages:",
                StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => pair.Value!.Trim())
            .ToArray();

        if (allowed.Length == 0)
        {
            var emptyDecision = OperationPolicyDecision.Deny(snapshot, "Process:AllowedDockerImages",
                image, "non-empty Security:AllowedDockerImages list",
                $"Enterprise policy denied Docker image '{image}': no allowed Docker images are configured.");
            throw new OperationPolicyDeniedException(emptyDecision);
        }

        if (RequiresPinnedDigest(snapshot, configuration) && !HasValidPinnedDigest(image))
        {
            var unpinnedDecision = OperationPolicyDecision.Deny(snapshot, "Process:AllowedDockerImages",
                image, "image reference pinned with @sha256:<64-hex>",
                $"Enterprise policy requires a pinned @sha256 digest for Docker image '{image}'.");
            throw new OperationPolicyDeniedException(unpinnedDecision);
        }

        if (allowed.Any(pattern => DockerImageMatches(pattern, image)))
            return;

        var decision = OperationPolicyDecision.Deny(snapshot, "Process:AllowedDockerImages",
            image, $"permitted images: [{string.Join(", ", allowed)}]",
            $"Enterprise policy denied Docker image '{image}'.");
        throw new OperationPolicyDeniedException(decision);
    }

    private static void EnforceStandaloneDockerPolicy(
        IExecutionContext context,
        string image,
        IConfiguration? configuration)
    {
        var allowed = ReadHostAllowedImages(configuration);
        if (allowed.Length == 0)
        {
            EmitDeniedDockerEvent(context, image, "No host-admin Docker image approvals are configured.");
            throw new ExecutionException(
                $"Docker image '{image}' is denied: explicit host-admin approval in Governance:Docker:AllowedImages is required.");
        }

        if (RequiresPinnedDigest(snapshot: null, configuration) && !HasValidPinnedDigest(image))
        {
            EmitDeniedDockerEvent(context, image, "Pinned @sha256 digest required by Governance:Docker:RequirePinnedDigest.");
            throw new ExecutionException(
                $"Docker image '{image}' is denied: host policy requires an immutable @sha256 digest.");
        }

        if (allowed.Any(pattern => DockerImageMatches(pattern, image)))
            return;

        EmitDeniedDockerEvent(context, image, $"Image not in approved Governance:Docker:AllowedImages list.");
        throw new ExecutionException(
            $"Docker image '{image}' is not permitted by host-admin Docker policy (Governance:Docker:AllowedImages).");
    }

    private static bool RequiresPinnedDigest(ExecutionPolicySnapshot? snapshot, IConfiguration? configuration)
    {
        if (snapshot?.GovernedValues.TryGetValue("Security:RequirePinnedDockerImages", out var governed) == true
            && bool.TryParse(governed, out var parsed) && parsed)
        {
            return true;
        }

        return configuration?.GetValue<bool>("Governance:Docker:RequirePinnedDigest") == true
            || configuration?.GetValue<bool>("Security:RequirePinnedDockerImages") == true;
    }

    private static string[] ReadHostAllowedImages(IConfiguration? configuration)
    {
        if (configuration == null)
            return [];

        var results = new List<string>();
        foreach (var path in new[] { "Governance:Docker:AllowedImages", "Security:AllowedDockerImages" })
        {
            var section = configuration.GetSection(path);
            foreach (var child in section.GetChildren())
            {
                if (!string.IsNullOrWhiteSpace(child.Value))
                    results.Add(child.Value.Trim());
            }

            var scalar = configuration[path];
            if (!string.IsNullOrWhiteSpace(scalar))
            {
                results.AddRange(scalar.Split([',', ';'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
        }

        return results.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void EmitDeniedDockerEvent(IExecutionContext context, string image, string reason)
    {
        var actor = context.ExecutionIdentity?.RealUser ?? context.ExecutionPolicy?.Actor ?? ProcessActor.Resolve(Environment.UserName);
        var effective = context.ExecutionIdentity?.EffectiveUser ?? actor;
        SecurityEventRuntime.Emit(SecurityEventContract.Create(
            SecurityEventSeverity.Warning,
            SecurityEventType.OperationDenied,
            actor,
            effective,
            $"Docker:{image}",
            SecurityEventDecision.Denied,
            reason) with
        {
            ScriptHash = context.ExecutionPolicy?.ScriptHash,
            JobId = context.ExecutionPolicy?.JobId,
            CorrelationId = context.ExecutionPolicy?.CorrelationId,
            PolicyVersion = context.ExecutionPolicy?.PolicyVersion,
            PolicyHash = context.ExecutionPolicy?.PolicyHash
        });
    }

    public static bool HasValidPinnedDigest(string? image)
    {
        if (string.IsNullOrWhiteSpace(image))
            return false;

        var trimmed = image.Trim();
        var atIndex = trimmed.IndexOf('@', StringComparison.Ordinal);
        if (atIndex <= 0 || atIndex != trimmed.LastIndexOf('@'))
            return false;

        return PinnedSha256Regex().IsMatch(trimmed[(atIndex + 1)..]);
    }

    /// <summary>
    /// Matches an image reference against an allowlist entry: <c>*</c> allows any, a <c>prefix/*</c>
    /// entry matches a registry/namespace prefix, a tagless <c>repo</c> entry matches that repo with
    /// any tag/digest, <c>repo:tag</c> matches that repo and tag, and a digest-pinned pattern
    /// <c>repo[:tag]@sha256:...</c> requires an exact digest match.
    /// </summary>
    public static bool DockerImageMatches(string pattern, string image)
    {
        if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(image))
            return false;

        pattern = pattern.Trim();
        image = image.Trim();
        if (image.Any(char.IsWhiteSpace) || image.Contains("..", StringComparison.Ordinal))
            return false;

        if (pattern == "*")
            return true;

        if (pattern.EndsWith("/*", StringComparison.Ordinal))
            return image.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);

        if (!TrySplitImage(pattern, out var patternRepo, out var patternTag, out var patternDigest))
            return false;
        if (!TrySplitImage(image, out var imageRepo, out var imageTag, out var imageDigest))
            return false;

        if (!string.Equals(patternRepo, imageRepo, StringComparison.OrdinalIgnoreCase))
            return false;

        if (patternDigest != null && !string.Equals(patternDigest, imageDigest, StringComparison.OrdinalIgnoreCase))
            return false;

        if (patternTag != null && !string.Equals(patternTag, imageTag, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static bool TrySplitImage(string value, out string repo, out string? tag, out string? digest)
    {
        repo = string.Empty;
        tag = null;
        digest = null;

        var atIndex = value.IndexOf('@', StringComparison.Ordinal);
        string repoAndTag;
        if (atIndex >= 0)
        {
            if (atIndex == 0 || atIndex != value.LastIndexOf('@'))
                return false;
            digest = value[(atIndex + 1)..];
            if (!PinnedSha256Regex().IsMatch(digest))
                return false;
            repoAndTag = value[..atIndex];
        }
        else
        {
            repoAndTag = value;
        }

        if (string.IsNullOrWhiteSpace(repoAndTag))
            return false;

        var slash = repoAndTag.LastIndexOf('/');
        var colon = repoAndTag.LastIndexOf(':');
        if (colon > slash)
        {
            repo = repoAndTag[..colon];
            tag = repoAndTag[(colon + 1)..];
            return !string.IsNullOrWhiteSpace(repo) && !string.IsNullOrWhiteSpace(tag);
        }

        repo = repoAndTag;
        return true;
    }

    [GeneratedRegex(@"^sha256:[0-9a-fA-F]{64}$")]
    private static partial Regex PinnedSha256Regex();
}
