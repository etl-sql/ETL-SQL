using System.Text.Json.Nodes;

namespace ETL_SQL.Portal.Models;

/// <param name="ContentHash">
/// Identifies exactly what a reviewer saw, so the download can check an acknowledgement against it.
/// Excludes the generation time: otherwise every review would be stale the moment it was made and
/// the check would become noise an operator learns to bypass.
/// </param>
/// <param name="Excluded">
/// What the bundle deliberately does not contain. A support artifact that does not say what it left
/// out invites the assumption that it left nothing out.
/// </param>
public sealed record SupportBundleContentDto(
    DateTime GeneratedUtc,
    IReadOnlyList<SupportBundleSectionDto> Sections,
    string ContentHash,
    string RedactionNote,
    IReadOnlyList<string> Excluded);

/// <param name="VolatileCounts">
/// True for sections whose values move on their own — health timings, live counters. Excluded from
/// <see cref="SupportBundleContentDto.ContentHash"/> so a review stays valid while the deployment it
/// describes has not changed.
/// </param>
public sealed record SupportBundleSectionDto(
    string Key, string Title, JsonNode Payload, bool VolatileCounts = false);

public sealed record CreateSupportAccessApprovalRequest(
    string? PlatformActor,
    string? Purpose,
    string? AcknowledgedContent,
    int LifetimeMinutes = 30);
