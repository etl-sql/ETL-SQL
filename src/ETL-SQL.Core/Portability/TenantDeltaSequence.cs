namespace ETL_SQL.Core.Portability;

public sealed record TenantDeltaSequenceResult(
    bool IsValid, string FinalConsistencyPointDigest, IReadOnlyList<string> Errors);

/// <summary>Validates ordered, tenant-bound delta application and rejects replay/mixing.</summary>
public static class TenantDeltaSequence
{
    public static TenantDeltaSequenceResult Validate(
        string tenantId, string certifiedBaseDigest, IEnumerable<TenantBundleManifest> deltas)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(certifiedBaseDigest);
        ArgumentNullException.ThrowIfNull(deltas);
        var errors = new List<string>();
        var expectedBase = certifiedBaseDigest;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { certifiedBaseDigest };
        var array = deltas.ToArray();
        for (var index = 0; index < array.Length; index++)
        {
            var delta = array[index];
            if (delta.ExportMode is not (TenantBundleExportMode.IncrementalDelta or TenantBundleExportMode.FinalCutoverDelta))
                errors.Add($"Bundle {index} is not an incremental/final delta.");
            if (!string.Equals(delta.TenantExportIdentity, tenantId, StringComparison.Ordinal))
                errors.Add($"Bundle {index} belongs to another tenant.");
            if (!string.Equals(delta.BaseConsistencyPointDigest, expectedBase, StringComparison.OrdinalIgnoreCase))
                errors.Add($"Bundle {index} does not continue from consistency point '{expectedBase}'.");
            var next = delta.DeclaredConsistencyPoint?.Digest;
            if (string.IsNullOrWhiteSpace(next) || !TenantExportConsistencyCoordinator.Verify(delta.DeclaredConsistencyPoint!))
                errors.Add($"Bundle {index} has no valid resulting consistency point.");
            else
            {
                if (!seen.Add(next)) errors.Add($"Bundle {index} replays consistency point '{next}'.");
                expectedBase = next;
            }
            if (delta.ExportMode == TenantBundleExportMode.FinalCutoverDelta && index != array.Length - 1)
                errors.Add("A final cutover delta must be the last bundle in the sequence.");
        }
        return new TenantDeltaSequenceResult(errors.Count == 0, expectedBase, errors);
    }
}
