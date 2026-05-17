using BenchmarkDotNet.Running;

namespace ETL_SQL.Benchmarks
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // Run all benchmarks in this assembly.
            // To exclude LargeScale (SF=1) benchmarks: --filter Category!=LargeScale
            // To run only LargeScale benchmarks:       --filter *LargeScale*
            // To export JSON for CI comparison:        --exporters json
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        }
    }
}
