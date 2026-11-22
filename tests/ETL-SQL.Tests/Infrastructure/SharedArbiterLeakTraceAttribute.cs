using System.Reflection;
using ETL_SQL.Core;
using Xunit.Sdk;

[assembly: ETL_SQL.Tests.Infrastructure.SharedArbiterLeakTrace]

namespace ETL_SQL.Tests.Infrastructure;

/// <summary>
/// Records each test that ends with more bytes reserved on the process-wide
/// <see cref="MemoryGrantArbiter.Shared"/> than it started with. A lease that is never disposed
/// keeps its bytes reserved for the rest of the process, so later tests hit the memory governor
/// ceiling on tiny inputs. Off unless <see cref="DirectoryVariable"/> names a folder (the spill
/// lane sets it to its evidence folder); it only records, it never fails a test. Tests run one at a
/// time (xunit.runner.json), so a delta belongs to the test that just ran.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method)]
public sealed class SharedArbiterLeakTraceAttribute : BeforeAfterTestAttribute
{
    public const string DirectoryVariable = "ETLSQL_ARBITER_TRACE_DIR";

    private static readonly object Gate = new();
    private static long _before;

    public override void Before(MethodInfo methodUnderTest) =>
        Interlocked.Exchange(ref _before, MemoryGrantArbiter.Shared.ReservedBytes);

    public override void After(MethodInfo methodUnderTest)
    {
        var after = MemoryGrantArbiter.Shared.ReservedBytes;
        var before = Interlocked.Read(ref _before);
        if (after <= before) return;
        var directory = Environment.GetEnvironmentVariable(DirectoryVariable);
        if (string.IsNullOrWhiteSpace(directory)) return;

        var line = string.Join('\t',
            DateTime.UtcNow.ToString("O"),
            $"{methodUnderTest.DeclaringType?.FullName}.{methodUnderTest.Name}",
            $"+{after - before}",
            $"reserved {after}",
            $"budget {MemoryGrantArbiter.Shared.TotalBudgetBytes}");
        lock (Gate)
        {
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, $"arbiter-leaks-{Environment.ProcessId}.log"), line + Environment.NewLine);
        }
    }
}
