using ETL_SQL.Core.Governance;

namespace ETL_SQL.Tests.Governance;

/// <summary>
/// Slice D4 of the Secure Outbound Data Gateway: the typed operation contract and its durable
/// outcome ledger.
///
/// <para>The rule these tests exist for is the reconnect rule in §11.5 — <b>an ambiguous write is
/// never retried blindly nor reported as safely failed.</b> Both of those are the comfortable
/// answers and both are wrong: a blind retry can double-apply a write, and calling it failed tells
/// the caller the data is not there when it may be. This is the same judgement the sandbox
/// coordinator already makes about an ambiguous teardown, and the repository's own triage rule that
/// a wrong answer outranks a crash.</para>
/// </summary>
public sealed class GatewayOperationProtocolTests
{
    private const string Tenant = "tenant-acme";
    private const string OtherTenant = "tenant-globex";

    // ------------------------------------------------------------------ mandatory bounds

    [Fact]
    public void Bounds_HaveNoUnlimitedRepresentation()
    {
        GatewayOperationBounds.Default.Validate();

        Assert.Throws<GatewayProtocolException>(() =>
            (GatewayOperationBounds.Default with { TimeoutSeconds = 0 }).Validate());
        Assert.Throws<GatewayProtocolException>(() =>
            (GatewayOperationBounds.Default with { MaxRows = 0 }).Validate());
        Assert.Throws<GatewayProtocolException>(() =>
            (GatewayOperationBounds.Default with { MaxResponseBytes = 0 }).Validate());
        Assert.Throws<GatewayProtocolException>(() =>
            (GatewayOperationBounds.Default with { MaxConcurrentStreams = 0 }).Validate());
        Assert.Throws<GatewayProtocolException>(() =>
            (GatewayOperationBounds.Default with { BufferedBatchLimit = 0 }).Validate());
    }

    [Fact]
    public void Bounds_NarrowToAResourceAndNeverWiden()
    {
        // A registered resource's limits can only restrict. If a caller asks for more than the
        // resource permits, the resource wins; if it asks for less, its own smaller value stands.
        var generous = new GatewayOperationBounds(600, 1 << 20, 1L << 40, 10_000_000, 64, 16);
        var strict = new GatewayResourceLimits(MaxConcurrency: 2, MaxRows: 1000, MaxBytes: 4096, TimeoutSeconds: 30);

        var narrowed = generous.NarrowTo(strict);

        Assert.Equal(30, narrowed.TimeoutSeconds);
        Assert.Equal(1000, narrowed.MaxRows);
        Assert.Equal(4096, narrowed.MaxResponseBytes);
        Assert.Equal(2, narrowed.MaxConcurrentStreams);

        var alreadyTighter = new GatewayOperationBounds(10, 1 << 10, 1024, 100, 1, 4).NarrowTo(strict);
        Assert.Equal(10, alreadyTighter.TimeoutSeconds);
        Assert.Equal(100, alreadyTighter.MaxRows);
    }

    [Fact]
    public void Operation_RequiresAnIdTenantAndClass()
    {
        Assert.Throws<GatewayProtocolException>(() => Operation() with { OperationId = "" } is var op && Validate(op));
        Assert.Throws<GatewayProtocolException>(() => Validate(Operation() with { TenantId = "  " }));
        Assert.Throws<GatewayProtocolException>(() => Validate(Operation() with { Class = GatewayOperationClass.None }));
        Validate(Operation());
    }

    // ------------------------------------------------------- the ambiguous-write rule

    [Fact]
    public void AmbiguousWrite_IsNeitherRetriedBlindlyNorCalledFailed()
    {
        var ledger = new GatewayOutcomeLedger();
        var write = Operation() with { Effect = GatewayOperationEffect.Mutating };
        ledger.RecordDispatched(write);
        ledger.RecordTerminal(Tenant, write.OperationId, GatewayOutcomeState.Ambiguous);

        var action = ledger.DecideReconnect(Tenant, write.OperationId);

        Assert.Equal(GatewayReconnectAction.EscalateAmbiguous, action);
        Assert.NotEqual(GatewayReconnectAction.RetrySafely, action);
        Assert.NotEqual(GatewayReconnectAction.Dispatch, action);
    }

    [Fact]
    public void DroppedInFlightWrite_IsTreatedAsAmbiguousNotAsNeverHappened()
    {
        // The connection dropped with no terminal record. The tempting reading is "it never ran";
        // the safe reading is "unknown", because the Gateway may have applied it before the drop.
        var ledger = new GatewayOutcomeLedger();
        var write = Operation() with { Effect = GatewayOperationEffect.Mutating };
        ledger.RecordDispatched(write);

        Assert.Equal(GatewayReconnectAction.EscalateAmbiguous,
            ledger.DecideReconnect(Tenant, write.OperationId));
    }

    [Fact]
    public void DroppedInFlightRead_MaySimplyRun_BecauseRepeatingItChangesNothing()
    {
        var ledger = new GatewayOutcomeLedger();
        var read = Operation() with { Effect = GatewayOperationEffect.ReadOnly };
        ledger.RecordDispatched(read);

        Assert.Equal(GatewayReconnectAction.Dispatch, ledger.DecideReconnect(Tenant, read.OperationId));
    }

    [Fact]
    public void CommittedOutcome_IsReturnedRatherThanRepeated()
    {
        var ledger = new GatewayOutcomeLedger();
        var write = Operation() with { Effect = GatewayOperationEffect.Mutating };
        ledger.RecordDispatched(write);
        ledger.RecordTerminal(Tenant, write.OperationId, GatewayOutcomeState.Committed, rowsProduced: 7);

        Assert.Equal(GatewayReconnectAction.ReturnRecordedOutcome,
            ledger.DecideReconnect(Tenant, write.OperationId));
        Assert.Equal(7, ledger.Find(Tenant, write.OperationId)!.RowsProduced);
    }

    [Fact]
    public void FailedOutcome_IsSafeToRetry()
    {
        var ledger = new GatewayOutcomeLedger();
        var write = Operation() with { Effect = GatewayOperationEffect.Mutating };
        ledger.RecordDispatched(write);
        ledger.RecordTerminal(Tenant, write.OperationId, GatewayOutcomeState.Failed);

        Assert.Equal(GatewayReconnectAction.RetrySafely, ledger.DecideReconnect(Tenant, write.OperationId));
    }

    [Fact]
    public void CommittedOutcome_CannotBeDowngradedByALaterAmbiguousReport()
    {
        // A late or duplicated report must not turn a known-good write back into an unknown one.
        var ledger = new GatewayOutcomeLedger();
        var write = Operation() with { Effect = GatewayOperationEffect.Mutating };
        ledger.RecordDispatched(write);
        ledger.RecordTerminal(Tenant, write.OperationId, GatewayOutcomeState.Committed);

        Assert.Throws<GatewayProtocolException>(() =>
            ledger.RecordTerminal(Tenant, write.OperationId, GatewayOutcomeState.Ambiguous));
        Assert.Equal(GatewayOutcomeState.Committed, ledger.Find(Tenant, write.OperationId)!.State);
    }

    [Fact]
    public void TerminalOperation_CannotBeReDispatchedUnderTheSameId()
    {
        var ledger = new GatewayOutcomeLedger();
        var write = Operation() with { Effect = GatewayOperationEffect.Mutating };
        ledger.RecordDispatched(write);
        ledger.RecordTerminal(Tenant, write.OperationId, GatewayOutcomeState.Committed);

        Assert.Throws<GatewayProtocolException>(() => ledger.RecordDispatched(write));
    }

    [Fact]
    public void UnknownOperation_CannotBeGivenATerminalOutcome()
    {
        var ledger = new GatewayOutcomeLedger();
        Assert.Throws<GatewayProtocolException>(
            () => ledger.RecordTerminal(Tenant, "never-dispatched", GatewayOutcomeState.Committed));
    }

    // ------------------------------------------------------------------ tenant partitioning

    [Fact]
    public void EqualOperationIdsAcrossTenantsAreDifferentOperations()
    {
        var ledger = new GatewayOutcomeLedger();
        var mine = Operation() with { Effect = GatewayOperationEffect.Mutating };
        var theirs = mine with { TenantId = OtherTenant };

        ledger.RecordDispatched(mine);
        ledger.RecordDispatched(theirs);
        ledger.RecordTerminal(Tenant, mine.OperationId, GatewayOutcomeState.Committed);

        // Committing mine must not resolve theirs, and neither tenant can read the other's outcome.
        Assert.Equal(GatewayReconnectAction.ReturnRecordedOutcome, ledger.DecideReconnect(Tenant, mine.OperationId));
        Assert.Equal(GatewayReconnectAction.EscalateAmbiguous, ledger.DecideReconnect(OtherTenant, mine.OperationId));
        Assert.Equal(GatewayOutcomeState.InFlight, ledger.Find(OtherTenant, mine.OperationId)!.State);
    }

    [Fact]
    public void TenantAndOperationIdCannotCollideByConcatenation()
    {
        // ("a", "bc") and ("ab", "c") must stay distinct. A string key built by concatenating them
        // without a separator would merge the two into one another's outcome.
        var ledger = new GatewayOutcomeLedger();
        var first = Operation() with { TenantId = "a", OperationId = "bc", Effect = GatewayOperationEffect.Mutating };
        var second = Operation() with { TenantId = "ab", OperationId = "c", Effect = GatewayOperationEffect.Mutating };

        ledger.RecordDispatched(first);
        ledger.RecordDispatched(second);
        ledger.RecordTerminal("a", "bc", GatewayOutcomeState.Committed);

        Assert.Equal(GatewayOutcomeState.Committed, ledger.Find("a", "bc")!.State);
        Assert.Equal(GatewayOutcomeState.InFlight, ledger.Find("ab", "c")!.State);
    }

    [Fact]
    public void UnknownOperationForAnotherTenantLooksLikeAnUnknownOperation()
    {
        var ledger = new GatewayOutcomeLedger();
        var mine = Operation() with { Effect = GatewayOperationEffect.Mutating };
        ledger.RecordDispatched(mine);

        Assert.Null(ledger.Find(OtherTenant, mine.OperationId));
        Assert.Throws<GatewayProtocolException>(
            () => ledger.RecordTerminal(OtherTenant, mine.OperationId, GatewayOutcomeState.Committed));
    }

    [Fact]
    public void DurableLedgerPreservesCommittedAndAmbiguousWriteDecisionsAcrossRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"gateway-ledger-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "outcomes.json");
        try
        {
            var committed = Operation() with
            {
                OperationId = "committed-write",
                Effect = GatewayOperationEffect.Mutating
            };
            var ambiguous = committed with { OperationId = "ambiguous-write" };
            var firstProcess = new GatewayOutcomeLedger(path);
            firstProcess.RecordDispatched(committed);
            firstProcess.RecordTerminal(Tenant, committed.OperationId, GatewayOutcomeState.Committed, 3);
            firstProcess.RecordDispatched(ambiguous);
            firstProcess.RecordTerminal(Tenant, ambiguous.OperationId, GatewayOutcomeState.Ambiguous);

            var restarted = new GatewayOutcomeLedger(path);
            Assert.Equal(
                GatewayReconnectAction.ReturnRecordedOutcome,
                restarted.DecideReconnect(Tenant, committed.OperationId));
            Assert.Equal(3, restarted.Find(Tenant, committed.OperationId)?.RowsProduced);
            Assert.Equal(
                GatewayReconnectAction.EscalateAmbiguous,
                restarted.DecideReconnect(Tenant, ambiguous.OperationId));
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    // --------------------------------------------------------------------------- helpers

    private static bool Validate(GatewayOperation operation)
    {
        operation.Validate();
        return true;
    }

    private static GatewayOperation Operation() => new(
        OperationId: "op-1",
        TenantId: Tenant,
        GatewayId: "hq-gateway",
        ResourceId: "corp-sql-sales",
        Class: GatewayOperationClass.Read,
        Effect: GatewayOperationEffect.ReadOnly,
        Bounds: GatewayOperationBounds.Default,
        CorrelationId: "corr-1");
}
