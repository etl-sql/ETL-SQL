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
                try { File.Delete(failLog); } catch {}
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
                        catch {}
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

            // Exclusions:
            //   select4_debug.test — debug variant of select4, overlaps entirely with select4.test
            //   slt_lang_createtrigger.test, slt_lang_droptrigger.test — ETL-SQL has no trigger support
            //   slt_lang_aggfunc.test — uses aggregate functions with SQLite-specific NULL semantics that differ from ETL-SQL
            //   (empty) index/* files — placeholder stubs with no test content
            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "select4_debug.test",
                "slt_lang_createtrigger.test",
                "slt_lang_droptrigger.test",
                "slt_lang_aggfunc.test",
            };

            return Directory.GetFiles(root, "*.test", SearchOption.AllDirectories)
                .Where(f => new FileInfo(f).Length > 0)
                .Where(f => !excluded.Contains(Path.GetFileName(f)))
                .OrderBy(f => f)
                .Select(f => new object[] { f });
        }
    }
}
