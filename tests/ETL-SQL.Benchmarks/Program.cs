using System;
using System.IO;
using System.Linq;
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
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(WithTimestampedArtifacts(args));
        }

        private static string[] WithTimestampedArtifacts(string[] args)
        {
            if (args.Any(a =>
                    a.Equals("--artifacts", StringComparison.OrdinalIgnoreCase) ||
                    a.Equals("--artifactsPath", StringComparison.OrdinalIgnoreCase)))
            {
                return args;
            }

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var artifactsPath = Path.Combine("BenchmarkDotNet.Artifacts", "runs", stamp);
            return args.Concat(new[] { "--artifacts", artifactsPath }).ToArray();
        }
    }
}
