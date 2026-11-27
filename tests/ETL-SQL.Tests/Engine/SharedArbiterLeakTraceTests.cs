using ETL_SQL.Core;
using ETL_SQL.Tests.Infrastructure;

namespace ETL_SQL.Tests.Engine;

/// <summary>
/// The spill lane traces tests that leave reservations on the process-wide
/// <see cref="MemoryGrantArbiter.Shared"/>: a leaked lease shrinks the budget for every later test
/// in the process, so a 2-row join fails with "exceeded the memory governor ceiling" far from the
/// test that leaked (Spill Certification, 2026-11-22).
/// </summary>
public sealed class SharedArbiterLeakTraceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "arbiter-trace-" + Guid.NewGuid().ToString("N"));
    private readonly string? _previousDir = Environment.GetEnvironmentVariable(SharedArbiterLeakTraceAttribute.DirectoryVariable);
    private readonly long _previousBudget = MemoryGrantArbiter.Shared.TotalBudgetBytes;

    public SharedArbiterLeakTraceTests()
    {
        Environment.SetEnvironmentVariable(SharedArbiterLeakTraceAttribute.DirectoryVariable, _dir);
        MemoryGrantArbiter.Shared.TotalBudgetBytes = 1L << 30;
    }

    [Fact]
    public void ALeaseLeftOpen_IsRecordedWithTheTestThatLeftIt()
    {
        var trace = new SharedArbiterLeakTraceAttribute();
        var method = typeof(SharedArbiterLeakTraceTests).GetMethod(nameof(ALeaseLeftOpen_IsRecordedWithTheTestThatLeftIt))!;

        trace.Before(method);
        using var lease = MemoryGrantArbiter.Shared.AcquireLease();
        Assert.False(lease.RegisterAndCheckSpill(4096));
        trace.After(method);

        var line = Assert.Single(Directory.GetFiles(_dir).SelectMany(File.ReadAllLines));
        Assert.Contains($"{nameof(SharedArbiterLeakTraceTests)}.{nameof(ALeaseLeftOpen_IsRecordedWithTheTestThatLeftIt)}", line);
        Assert.Contains("+4096", line);
    }

    [Fact]
    public void AReleasedLease_IsNotRecorded()
    {
        var trace = new SharedArbiterLeakTraceAttribute();
        var method = typeof(SharedArbiterLeakTraceTests).GetMethod(nameof(AReleasedLease_IsNotRecorded))!;

        trace.Before(method);
        using (var lease = MemoryGrantArbiter.Shared.AcquireLease())
            Assert.False(lease.RegisterAndCheckSpill(4096));
        trace.After(method);

        Assert.False(Directory.Exists(_dir) && Directory.GetFiles(_dir).Length > 0);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(SharedArbiterLeakTraceAttribute.DirectoryVariable, _previousDir);
        MemoryGrantArbiter.Shared.TotalBudgetBytes = _previousBudget;
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
