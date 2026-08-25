using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace ETL_SQL.SqlLogicTests
{
    [Trait("Category", "SLT")]
    public class SltTests
    {
        [SltFact]
        public async Task RunAllSltTests()
        {
            var testFiles = GetTestFiles()
                .Select(f => (string)f[0])
                .ToList();
            var failures = new System.Collections.Concurrent.ConcurrentBag<(string FilePath, Exception Exception)>();

            var failLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "slt_failure_debug.log");
            if (File.Exists(failLog))
            {
                try { File.Delete(failLog); } catch { }
            }

            object fileLock = new object();
            int count = 0;

            await Parallel.ForEachAsync(testFiles, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (filePath, ct) =>
            {
                int currentCount;
                lock (fileLock)
                {
                    count++;
                    currentCount = count;
                }
                Console.WriteLine($"Running file {currentCount}/{testFiles.Count}: {Path.GetFileName(filePath)}");

                try
                {
                    using var runner = new SltRunner();
                    runner.CurrentFile = filePath;
                    var records = SltParser.ParseFile(filePath);

                    foreach (var record in records)
                    {
                        await runner.RunTestAsync(record);
                    }
                }
                catch (Exception ex)
                {
                    failures.Add((filePath, ex));
                    var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), filePath);

                    lock (fileLock)
                    {
                        try
                        {
                            var sb = new System.Text.StringBuilder();
                            sb.AppendLine($"=======================================================");
                            sb.AppendLine($"FAILED TEST FILE: {relativePath}");
                            sb.AppendLine($"Error: {ex.Message}");
                            sb.AppendLine($"=======================================================");
                            File.AppendAllText(failLog, sb.ToString());
                        }
                        catch { }
                    }
                }
            });

            if (failures.Any())
            {
                var summary = string.Join(Environment.NewLine, failures.Select(f => $"{Path.GetFileName(f.FilePath)}: {f.Exception.Message}"));
                throw new Exception($"SLT test run completed with {failures.Count} file failure(s) out of {testFiles.Count} total files.{Environment.NewLine}{Environment.NewLine}Failures:{Environment.NewLine}{summary}");
            }
        }

        public static IEnumerable<object[]> GetTestFiles()
        {
            // Resolve relative to the assembly directory first to support static discovery in xUnit
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var root = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "slt_data"));

            if (!Directory.Exists(root))
            {
                root = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "slt_data"));
            }

            if (!Directory.Exists(root)) return Enumerable.Empty<object[]>();

            // Exclusions (see docs/architecture/standards/slt-coverage.md for full rationale):
            //
            //   select4_debug.test — truncated artifact: same 1025 setup statements as select4.test
            //     but only 1019 of its 2832 queries (cuts off before complex join tests). No unique
            //     content; deleted from repo. Left here as a safety net in case it reappears.
            //
            //   slt_lang_aggfunc.test — SQLite-only by design. The file opens with "skipif sqlite; halt"
            //     meaning every non-SQLite engine should skip it. It tests total() (returns 0.0 not NULL
            //     for empty sets), group_concat() (not standard SQL), and non-numeric string coercion to 0
            //     in avg()/sum() — all SQLite-specific behaviors ETL-SQL intentionally does not emulate.
            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "select4_debug.test",
                "slt_lang_aggfunc.test",
            };

            // index/ subdirectory: real SLT index-optimization tests (10,000+ queries per file)
            // that use CREATE INDEX on regular (non-temp) tables — not supported in ETL-SQL.
            // ETL-SQL supports CREATE INDEX only on #temp tables.
            var indexDir = Path.Combine(root, "index") + Path.DirectorySeparatorChar;

            return Directory.GetFiles(root, "*.test", SearchOption.AllDirectories)
                .Where(f => new FileInfo(f).Length > 0)
                .Where(f => !excluded.Contains(Path.GetFileName(f)))
                .Where(f => !f.StartsWith(indexDir, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f)
                .Select(f => new object[] { f });
        }
    }
}
