using System;
using System.Collections.Generic;
using System.IO;
using ETL_SQL.Core;
using Xunit;

namespace ETL_SQL.Tests.Reporting
{
    public class DatasetExampleDeckTests
    {
        [Fact]
        public void DatasetDeckScripts_Parse()
        {
            var deck = FindDeckDirectory();
            var files = Directory.GetFiles(deck, "*.etlsql", SearchOption.TopDirectoryOnly);

            Assert.Equal(5, files.Length);
            foreach (var file in files)
            {
                var sql = File.ReadAllText(file);
                var exception = Record.Exception(() =>
                    new Parser(new Lexer(sql).Tokenize()).Parse());

                Assert.True(exception == null, $"{Path.GetFileName(file)}: {exception}");
            }
        }

        [Fact]
        public void TransferExample_CoversPasswordAndKeyFileWithoutMachineSpecificSecrets()
        {
            var sql = File.ReadAllText(Path.Combine(FindDeckDirectory(), "05_export_then_publish.etlsql"));

            Assert.Contains("ENCRYPT = PASSWORD", sql, StringComparison.Ordinal);
            Assert.Contains("ENCRYPT = KEYFILE", sql, StringComparison.Ordinal);
            Assert.Contains("id_rsa.pub", sql, StringComparison.Ordinal);
            Assert.Contains("id_rsa';", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("Portal:Dataset:AtRestKey", sql, StringComparison.OrdinalIgnoreCase);
        }

        private static string FindDeckDirectory()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                var candidate = Path.Combine(current.FullName, "samples", "08_Reporting", "datasets");
                if (Directory.Exists(candidate))
                    return candidate;
                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate samples/08_Reporting/datasets.");
        }
    }
}
