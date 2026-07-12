using ETL_SQL.Core;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Tests.Core;

/// <summary>
/// Proves the enterprise mutation guardrails: destructive statements on persistent targets require
/// WHAT IF, and mutations require an open transaction, when the enrolled policy sets them. Session
/// <c>#temp</c> targets and unenrolled standalone execution are exempt.
/// </summary>
public sealed class MutationGuardrailEnforcementTests : IDisposable
{
    public void Dispose() => EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);

    [Fact]
    public async Task DestructiveOnPersistentTarget_RequiresWhatIf_IsDenied()
    {
        var eventSink = new RecordingSecurityEventSink();
        using var eventScope = SecurityEventRuntime.UseSinkForScope(eventSink);
        EnterprisePolicyRuntime.SetCurrent(Enrolled(requireWhatIf: true, requireTransaction: false));

        var denied = await Assert.ThrowsAnyAsync<Exception>(() =>
            ExecuteAsync("DELETE FROM customers;"));
        Assert.Contains("WHAT IF", denied.ToString(), StringComparison.OrdinalIgnoreCase);
        var securityEvent = Assert.Single(eventSink.Events);
        Assert.Equal(SecurityEventType.OperationDenied, securityEvent.Type);
        Assert.Equal("DELETE:customers", securityEvent.SanitizedTarget);
    }

    [Fact]
    public async Task DestructiveOnTempTarget_IsExempt()
    {
        EnterprisePolicyRuntime.SetCurrent(Enrolled(requireWhatIf: true, requireTransaction: false));

        // #temp data is ephemeral, so the guardrail does not fire; the statement instead fails
        // (or succeeds) on its own merits, never on the WHAT IF guardrail.
        var error = await Record.ExceptionAsync(() =>
            ExecuteAsync("CREATE TABLE #t (id INT); DELETE FROM #t;"));
        Assert.DoesNotContain("WHAT IF", error?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MutationOutsideTransaction_RequiresTransaction_IsDenied()
    {
        EnterprisePolicyRuntime.SetCurrent(Enrolled(requireWhatIf: false, requireTransaction: true));

        var denied = await Assert.ThrowsAnyAsync<Exception>(() =>
            ExecuteAsync("INSERT INTO customers (id) VALUES (1);"));
        Assert.Contains("transaction", denied.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Standalone_Unenrolled_MutationsUnrestricted()
    {
        EnterprisePolicyRuntime.SetCurrent(EffectiveEnterprisePolicy.Standalone);

        // No enterprise policy: the guardrail never fires (the statement may still fail on a missing
        // table, but never on a guardrail).
        var error = await Record.ExceptionAsync(() => ExecuteAsync("DELETE FROM customers;"));
        var text = error?.ToString() ?? "";
        Assert.DoesNotContain("WHAT IF", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("requires mutations to run within a transaction", text,
            StringComparison.OrdinalIgnoreCase);
    }

    private static async Task ExecuteAsync(string sql)
    {
        var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        await evaluator.Evaluate(script);
    }

    private static EffectiveEnterprisePolicy Enrolled(bool requireWhatIf, bool requireTransaction)
    {
        var document = new OrganizationPolicyDocument
        {
            MutationGuardrails = new MutationGuardrailPolicySection
            {
                RequireWhatIfForDestructiveStatements = requireWhatIf,
                RequireTransactionForMutations = requireTransaction
            }
        };
        return new EffectiveEnterprisePolicy(true, true, "Live", "v1", "test",
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1),
            DateTimeOffset.UtcNow, document,
            EnterprisePolicyConfiguration.Flatten(document.ToPolicyValues()));
    }

    private sealed class RecordingSecurityEventSink : ISecurityEventSink
    {
        public List<SecurityEvent> Events { get; } = [];
        public void Emit(SecurityEvent securityEvent) => Events.Add(securityEvent);
    }
}
