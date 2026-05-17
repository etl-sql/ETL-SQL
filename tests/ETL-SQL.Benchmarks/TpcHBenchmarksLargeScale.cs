using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

namespace ETL_SQL.Benchmarks
{
    /// <summary>
    /// TPC-H benchmarks at SF=1 (600K lineitem rows). Intended for local profiling only —
    /// excluded from CI via <c>--filter Category!=LargeScale</c>.
    /// Run with: dotnet run --project tests/ETL-SQL.Benchmarks -c Release -- --filter *LargeScale*
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(launchCount: 1, warmupCount: 1, iterationCount: 3)]
    [BenchmarkCategory("LargeScale")]
    public class TpcHBenchmarksLargeScale
    {
        private readonly TpcHBenchmarks _inner = new(1.0);

        [GlobalSetup]
        public async Task Setup() => await _inner.Setup();

        [Benchmark]
        [BenchmarkCategory("LargeScale")]
        public async Task RunQ1_SF1() => await _inner.RunQ1();

        [Benchmark]
        [BenchmarkCategory("LargeScale")]
        public async Task RunQ6_SF1() => await _inner.RunQ6();

        [Benchmark]
        [BenchmarkCategory("LargeScale")]
        public async Task RunQ3_SF1() => await _inner.RunQ3();

        [Benchmark]
        [BenchmarkCategory("LargeScale")]
        public async Task RunQ5_SF1() => await _inner.RunQ5();

        [Benchmark]
        [BenchmarkCategory("LargeScale")]
        public async Task RunQ12_SF1() => await _inner.RunQ12();

        [Benchmark]
        [BenchmarkCategory("LargeScale")]
        public async Task RunQ14_SF1() => await _inner.RunQ14();
    }
}
