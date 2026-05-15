using BenchmarkDotNet.Running;

namespace ETL_SQL.Benchmarks
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var summary = BenchmarkRunner.Run<TpcHBenchmarks>();
        }
    }
}
