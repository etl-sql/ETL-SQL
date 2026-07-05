using ETL_SQL.Core.Governance;

namespace ETL_SQL.Tests.Core;

/// <summary>
/// Phase 3 completion-gate audit: every governed policy key must map to a named enforcement
/// boundary, or be explicitly recorded as not-yet-enforced. This test is the living artifact for
/// that gate — it fails when a governed key is added to <see cref="GovernancePolicyRegistry"/>
/// without a classification here, and when the set of unenforced keys changes without a deliberate
/// edit. It does not by itself prove each boundary is correct (the per-boundary enforcement tests
/// do that); it proves coverage is complete and the remaining gaps are acknowledged.
/// </summary>
public sealed class GovernanceEnforcementAuditTests
{
    private enum Boundary
    {
        /// <summary>Enforced directly against the captured ExecutionPolicySnapshot at a named code boundary.</summary>
        EnterpriseSnapshot,
        /// <summary>Enterprise value flows into effective config/engine state; enforced by a named local or Portal boundary.</summary>
        ConfigPrecedence,
        /// <summary>Flattened into policy values but not yet consumed by any enforcement boundary.</summary>
        DeclaredGap
    }

    // Each governed key → (how it is enforced, the named boundary). Keep in sync with
    // GovernancePolicyRegistry.DefaultDefinitions; the completeness test below fails otherwise.
    private static readonly IReadOnlyDictionary<string, (Boundary Kind, string Where)> Map =
        new Dictionary<string, (Boundary, string)>(StringComparer.OrdinalIgnoreCase)
        {
            // ── Enforced against the enterprise snapshot at a named boundary ──
            ["Security:ApprovedSafeZones"] = (Boundary.EnterpriseSnapshot, "FileSystemPolicyAuthorizer.EnforceEnterpriseRoots"),
            ["Security:AllowedWriteExtensions"] = (Boundary.EnterpriseSnapshot, "FileSystemPolicyAuthorizer.EnforceWriteExtensions"),
            ["Security:MaxFileOperationsPerScript"] = (Boundary.EnterpriseSnapshot, "Evaluator.IncrementOperationCount → OperationPolicyBoundary.EnforceCeiling"),
            ["Security:MaxRecursiveNestingDepth"] = (Boundary.EnterpriseSnapshot, "Evaluator.EnterRecursiveScope → OperationPolicyBoundary.EnforceCeiling"),
            ["Security:MaxSpillBytesPerScript"] = (Boundary.EnterpriseSnapshot, "SpillStore → OperationPolicyBoundary.EnforceSpillCeiling"),
            ["Security:AllowedHosts"] = (Boundary.EnterpriseSnapshot, "ConnectorPolicyAuthorizer.EnforceEnterpriseHosts (+ REST per-request)"),
            ["Security:AllowedDockerImages"] = (Boundary.EnterpriseSnapshot, "DockerStatementHandler → ProcessPolicyRules.EnforceDockerImage"),
            ["Security:MaxParallelDegree"] = (Boundary.EnterpriseSnapshot, "SetThresholdStatementHandler → OperationPolicyBoundary.EnforceCeiling"),
            ["Security:MaxSmtpEmailsPerScript"] = (Boundary.EnterpriseSnapshot, "Evaluator.RecordSmtpEmailSend → OperationPolicyBoundary.EnforceCeiling"),
            ["Connectors:AllowedTypes"] = (Boundary.EnterpriseSnapshot, "ConnectorPolicyAuthorizer.EnforceAllowedTypes"),
            ["Security:RequireWhatIfForDestructiveStatements"] = (Boundary.EnterpriseSnapshot, "MutationGuardrailPolicy.Enforce (statement dispatch)"),
            ["Security:RequireTransactionForMutations"] = (Boundary.EnterpriseSnapshot, "MutationGuardrailPolicy.Enforce (statement dispatch)"),
            ["Security:AllowedExecutionModes"] = (Boundary.EnterpriseSnapshot, "OperationPolicyBoundary.EnforceAllowedExecutionMode (execution start)"),

            // ── Enforced via config precedence by a named local/Portal boundary ──
            ["Security:PathProtectionMode"] = (Boundary.ConfigPrecedence, "SecurityService.ValidatePath (ProtectionMode)"),
            ["Security:AllowedEnvVars"] = (Boundary.ConfigPrecedence, "SecurityService.ValidateEnvVar"),
            ["Security:MaxStringResultSize"] = (Boundary.ConfigPrecedence, "SecurityService.ValidateStringSize (registry-only; not yet deliverable via policy document)"),
            ["Secrets:Provider"] = (Boundary.ConfigPrecedence, "SecretProviderFactory → ConnectionSecretResolver"),
            ["Audit:RemoteDeliveryRequired"] = (Boundary.ConfigPrecedence, "AuditFailClosedInterceptor / AuditDeliveryGate"),
            ["Audit:OutboxMaxBytes"] = (Boundary.ConfigPrecedence, "AuditDeliveryGate / AuditOutboxTransportService"),
            ["Engine:AllowPlaintextSecrets"] = (Boundary.ConfigPrecedence, "SetAllowPlaintextSecretsStatementHandler + secret-persistence guard"),
            ["Engine:NoSaveSensitive"] = (Boundary.ConfigPrecedence, "SetSavePolicyStatementHandlers + connection/secret save path"),
            ["Engine:NoSaveConnection"] = (Boundary.ConfigPrecedence, "SetSavePolicyStatementHandlers + connection save path"),
            ["Engine:ConnectionEncryption"] = (Boundary.ConfigPrecedence, "Connection save path (ConnectionEncryptionRule)"),

            // The boundary is named and tested; it is dormant only because no path produces a
            // Remote-mode snapshot yet — it enforces the moment one does.
            ["Security:RemoteExecutionMode"] = (Boundary.EnterpriseSnapshot, "OperationPolicyBoundary.EnforceRemoteExecutionMode (execution start)"),
        };

    // The governed keys deliberately not yet enforced. Adding a gap must be a conscious edit to both
    // the map above and this set. It is currently empty — every governed key names a boundary.
    private static readonly HashSet<string> ExpectedGaps = new(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void EveryGovernedRegistryKeyIsClassified()
    {
        var registryKeys = GovernancePolicyRegistry.CreateDefault().Definitions
            .Select(definition => definition.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unclassified = registryKeys.Where(key => !Map.ContainsKey(key)).OrderBy(k => k).ToArray();
        Assert.True(unclassified.Length == 0,
            "Governed keys with no enforcement-boundary classification (add them to the audit map): "
            + string.Join(", ", unclassified));
    }

    [Fact]
    public void AuditMapHasNoStaleKeys()
    {
        var registryKeys = GovernancePolicyRegistry.CreateDefault().Definitions
            .Select(definition => definition.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var stale = Map.Keys.Where(key => !registryKeys.Contains(key)).OrderBy(k => k).ToArray();
        Assert.True(stale.Length == 0,
            "Audit map names keys that are no longer in the governance registry: " + string.Join(", ", stale));
    }

    [Fact]
    public void EveryEnforcedKeyNamesARealBoundary()
    {
        foreach (var (key, entry) in Map)
        {
            if (entry.Kind == Boundary.DeclaredGap) continue;
            Assert.False(string.IsNullOrWhiteSpace(entry.Where),
                $"Governed key '{key}' is marked enforced but names no boundary.");
        }
    }

    [Fact]
    public void UnenforcedGapsMatchTheAcknowledgedSet()
    {
        var mapGaps = Map.Where(pair => pair.Value.Kind == Boundary.DeclaredGap)
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.True(mapGaps.SetEquals(ExpectedGaps),
            "The set of not-yet-enforced governed keys changed. Update ExpectedGaps deliberately. "
            + "Newly unenforced: " + string.Join(", ", mapGaps.Except(ExpectedGaps)) + ". "
            + "Newly enforced (remove from gaps): " + string.Join(", ", ExpectedGaps.Except(mapGaps)) + ".");
    }
}
