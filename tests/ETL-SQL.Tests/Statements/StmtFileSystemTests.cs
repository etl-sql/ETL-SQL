using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements.Statements
{
    public class FileSystemSyntaxTests
    {
        private async Task ExecuteAsync(string sql, Evaluator evaluator)
        {
            var lexer = new Lexer(sql);
            var parser = new Parser(lexer.Tokenize());
            var script = parser.Parse();
            await evaluator.Evaluate(script);
        }

        [Theory]
        [InlineData("COPY_FILE('a', 'b');", "COPY FILE")]
        [InlineData("MOVE_FILE('a', 'b');", "MOVE FILE")]
        [InlineData("DELETE_FILE('a');", "DELETE FILE")]
        [InlineData("CREATE_DIRECTORY('a');", "CREATE DIRECTORY")]
        [InlineData("SEND_FILE('a', conn, 'b');", "SEND FILE")]
        [InlineData("RECEIVE_FILE(conn, 'a', 'b');", "RECEIVE FILE")]
        [InlineData("FILE_SEND 'a', conn, 'b';", "SEND FILE")]
        [InlineData("FILE_RECEIVE conn, 'a', 'b';", "RECEIVE FILE")]
        public void RetiredFileAndEmailAliases_AreRejected(string sql, string replacement)
        {
            var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
            var diagnostic = Assert.Single(script.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Contains("retired", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(replacement, diagnostic.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DirectoryRecursiveClause_ReservedKeyword_ReachesDirectoryParser()
        {
            const string sql = "COPY DIRECTORY 'source' TO 'target' RECURSIVE ON;";
            var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();

            Assert.Empty(script.Diagnostics);
            Assert.IsType<DirectoryOperationStatement>(Assert.Single(script.Statements));
        }

        [Fact]
        public async Task TestCopyFileWithOverwriteOn()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            string src = "fs_test_src.txt";
            string dst = "fs_test_dst.txt";
            await File.WriteAllTextAsync(src, "source content");
            await File.WriteAllTextAsync(dst, "old destination content");

            try
            {
                // SQL-style syntax
                await ExecuteAsync($"COPY FILE '{src}' TO '{dst}' WITH(OVERWRITE=ON);", evaluator);

                string content = await File.ReadAllTextAsync(dst);
                Assert.Equal("source content", content);
            }
            finally
            {
                if (File.Exists(src)) File.Delete(src);
                if (File.Exists(dst)) File.Delete(dst);
            }
        }

        [Fact]
        public async Task TestCopyFileWithOverwriteOffThrows()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            string src = "fs_test_src_off.txt";
            string dst = "fs_test_dst_off.txt";
            await File.WriteAllTextAsync(src, "source content");
            await File.WriteAllTextAsync(dst, "destination existing");

            try
            {
                // Should throw if OVERWRITE=OFF
                await Assert.ThrowsAsync<ExecutionException>(async () =>
                {
                    await ExecuteAsync($"COPY FILE '{src}' TO '{dst}' WITH(OVERWRITE=OFF);", evaluator);
                });
            }
            finally
            {
                if (File.Exists(src)) File.Delete(src);
                if (File.Exists(dst)) File.Delete(dst);
            }
        }

        [Fact]
        public async Task TestMoveFileWithOverwriteOffThrows()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            string src = "fs_test_move_src.txt";
            string dst = "fs_test_move_dst.txt";
            await File.WriteAllTextAsync(src, "source");
            await File.WriteAllTextAsync(dst, "exists");

            try
            {
                await Assert.ThrowsAsync<ExecutionException>(async () =>
                {
                    await ExecuteAsync($"MOVE FILE '{src}' TO '{dst}' WITH(OVERWRITE=OFF);", evaluator);
                });
            }
            finally
            {
                if (File.Exists(src)) File.Delete(src);
                if (File.Exists(dst)) File.Delete(dst);
            }
        }

        [Fact]
        public async Task CopyFile_ToDirectoryWithDateSuffix_DerivesArchiveName()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            string src = "fs_suffix_source.csv";
            string archiveDir = "fs_suffix_archive";
            string expected = Path.Combine(archiveDir, $"fs_suffix_source_{DateTime.Now:yyyyMMdd}.csv");
            await File.WriteAllTextAsync(src, "id,name\n1,Alice");
            Directory.CreateDirectory(archiveDir);

            try
            {
                await ExecuteAsync($"COPY FILE '{src}' TO DIRECTORY '{archiveDir}' WITH(DATE_SUFFIX='yyyyMMdd');", evaluator);

                Assert.True(File.Exists(expected));
                Assert.True(File.Exists(src));
                Assert.Equal("id,name\n1,Alice", await File.ReadAllTextAsync(expected));
            }
            finally
            {
                if (File.Exists(src)) File.Delete(src);
                if (Directory.Exists(archiveDir)) Directory.Delete(archiveDir, true);
            }
        }

        [Fact]
        public async Task MoveFile_ToDirectoryWithDateSuffixAndSeparator_DerivesArchiveName()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            string src = "fs_suffix_move_source.csv";
            string archiveDir = "fs_suffix_move_archive";
            string expected = Path.Combine(archiveDir, $"fs_suffix_move_source-{DateTime.Now:yyyyMMdd}.csv");
            await File.WriteAllTextAsync(src, "id,name\n1,Alice");
            Directory.CreateDirectory(archiveDir);

            try
            {
                await ExecuteAsync($"MOVE FILE '{src}' TO DIRECTORY '{archiveDir}' WITH(DATE_SUFFIX='yyyyMMdd', SUFFIX_SEPARATOR='-');", evaluator);

                Assert.True(File.Exists(expected));
                Assert.False(File.Exists(src));
            }
            finally
            {
                if (File.Exists(src)) File.Delete(src);
                if (Directory.Exists(archiveDir)) Directory.Delete(archiveDir, true);
            }
        }

        [Fact]
        public async Task TestCreateDirectoryWithOverwrite()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            string dir = "fs_test_dir";
            if (Directory.Exists(dir)) Directory.Delete(dir, true);

            try
            {
                await ExecuteAsync($"CREATE DIRECTORY '{dir}';", evaluator);
                Assert.True(Directory.Exists(dir));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Fact]
        public async Task TestDeleteDirectoryContents()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            string dir = "fs_test_contents";
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "file1.txt"), "test");

            try
            {
                await ExecuteAsync($"DELETE DIRECTORY_CONTENTS '{dir}' WITH(RECURSIVE=ON);", evaluator);
                Assert.Empty(Directory.GetFiles(dir));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Fact]
        public async Task TestSqlStyleCopyFileOverwriteOff()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            string src = "fs_test_func_src.txt";
            string dst = "fs_test_func_dst.txt";
            await File.WriteAllTextAsync(src, "new");
            await File.WriteAllTextAsync(dst, "old");

            try
            {
                await Assert.ThrowsAsync<ExecutionException>(async () =>
                {
                    await ExecuteAsync($"COPY FILE '{src}' TO '{dst}' WITH(OVERWRITE=OFF);", evaluator);
                });
            }
            finally
            {
                if (File.Exists(src)) File.Delete(src);
                if (File.Exists(dst)) File.Delete(dst);
            }
        }

        [Fact]
        public async Task TestSqlStyleDeleteFile()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            string src = "fs_del_test.txt";
            await File.WriteAllTextAsync(src, "del me");

            try
            {
                await ExecuteAsync($"DELETE FILE '{src}';", evaluator);
                Assert.False(File.Exists(src));
            }
            finally
            {
                if (File.Exists(src)) File.Delete(src);
            }
        }

        [Fact]
        public async Task TestWaitForFileUnlocked()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            string src = "wait_test.txt";
            if (File.Exists(src)) File.Delete(src);

            try
            {
                // File doesn't exist, should timeout
                await Assert.ThrowsAsync<ExecutionException>(async () =>
                {
                    await ExecuteAsync($"WAITFOR FILE UNLOCKED '{src}' WITH (TIMEOUT = 1, POLL_INTERVAL_MS = 100);", evaluator);
                });

                await Assert.ThrowsAsync<ExecutionException>(async () =>
                {
                    await ExecuteAsync($"WAIT UNTIL FILE UNLOCKED '{src}' WITH (TIMEOUT = 1, POLL_INTERVAL_MS = 100);", evaluator);
                });

                // Create it, wait should pass immediately
                await File.WriteAllTextAsync(src, "here");
                await ExecuteAsync($"WAITFOR FILE UNLOCKED '{src}' WITH (TIMEOUT = 2, POLL_INTERVAL_MS = 100);", evaluator);
                await ExecuteAsync($"WAIT UNTIL FILE UNLOCKED '{src}' WITH (TIMEOUT = 2, POLL_INTERVAL_MS = 100);", evaluator);
            }
            finally
            {
                if (File.Exists(src)) File.Delete(src);
            }
        }

        [Theory]
        [InlineData("TIMEOUT = 0, POLL_INTERVAL_MS = 100")]
        [InlineData("TIMEOUT = 1, POLL_INTERVAL_MS = 0")]
        public async Task TestWaitForFileUnlockedRejectsNonPositiveTiming(string options)
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();

            await Assert.ThrowsAsync<ExecutionException>(async () =>
            {
                await ExecuteAsync($"WAITFOR FILE UNLOCKED 'wait_invalid.txt' WITH ({options});", evaluator);
            });
        }

        [Fact]
        public async Task TestConvertFileEncoding()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            string src = "enc_src.txt";
            string dst = "enc_dst.txt";
            if (File.Exists(src)) File.Delete(src);
            if (File.Exists(dst)) File.Delete(dst);

            try
            {
                await File.WriteAllTextAsync(src, "Hello World from UTF-8", Encoding.UTF8);

                // Convert to Latin1/ANSI
                await ExecuteAsync($"CONVERT FILE ENCODING '{src}' TO '{dst}' WITH (FROM_ENCODING = 'UTF8', TO_ENCODING = 'ANSI', OVERWRITE = ON);", evaluator);

                Assert.True(File.Exists(dst));
                string content = await File.ReadAllTextAsync(dst, Encoding.GetEncoding("ISO-8859-1"));
                Assert.Equal("Hello World from UTF-8", content);
            }
            finally
            {
                if (File.Exists(src)) File.Delete(src);
                if (File.Exists(dst)) File.Delete(dst);
            }
        }

        [Fact]
        public async Task TestSplitFile()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            string src = "split_src.txt";
            string destDir = "split_dest_dir";
            if (File.Exists(src)) File.Delete(src);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, true);

            try
            {
                await File.WriteAllLinesAsync(src, new[] { "line1", "line2", "line3", "line4", "line5" });

                await ExecuteAsync($"SPLIT FILE '{src}' TO '{destDir}' WITH (LIMIT_TYPE = 'ROWS', LIMIT_VALUE = 2, PREFIX = 'chunk_', OVERWRITE = ON);", evaluator);

                Assert.True(Directory.Exists(destDir));
                Assert.True(File.Exists(Path.Combine(destDir, "chunk_1.txt")));
                Assert.True(File.Exists(Path.Combine(destDir, "chunk_2.txt")));
                Assert.True(File.Exists(Path.Combine(destDir, "chunk_3.txt")));

                var lines1 = await File.ReadAllLinesAsync(Path.Combine(destDir, "chunk_1.txt"));
                var lines2 = await File.ReadAllLinesAsync(Path.Combine(destDir, "chunk_2.txt"));
                var lines3 = await File.ReadAllLinesAsync(Path.Combine(destDir, "chunk_3.txt"));

                Assert.Equal(2, lines1.Length);
                Assert.Equal("line1", lines1[0]);
                Assert.Equal("line2", lines1[1]);

                Assert.Equal(2, lines2.Length);
                Assert.Equal("line3", lines2[0]);
                Assert.Equal("line4", lines2[1]);

                Assert.Single(lines3);
                Assert.Equal("line5", lines3[0]);
            }
            finally
            {
                if (File.Exists(src)) File.Delete(src);
                if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
            }
        }

        [Fact]
        public async Task TestSplitFileRejectsUnsafePrefix()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            string src = "split_unsafe_src.txt";
            string destDir = "split_unsafe_dest";
            string escaped = "escape_1.txt";
            if (File.Exists(src)) File.Delete(src);
            if (File.Exists(escaped)) File.Delete(escaped);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, true);

            try
            {
                await File.WriteAllLinesAsync(src, new[] { "line1", "line2" });

                await Assert.ThrowsAsync<ExecutionException>(async () =>
                {
                    await ExecuteAsync($"SPLIT FILE '{src}' TO '{destDir}' WITH (LIMIT_TYPE = 'ROWS', LIMIT_VALUE = 1, PREFIX = '..\\escape_', OVERWRITE = ON);", evaluator);
                });

                Assert.False(File.Exists(escaped));
            }
            finally
            {
                if (File.Exists(src)) File.Delete(src);
                if (File.Exists(escaped)) File.Delete(escaped);
                if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
            }
        }

        [Fact]
        public async Task TestMergeFiles()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            string src1 = "merge_1.csv";
            string src2 = "merge_2.csv";
            string dst = "merge_dst.csv";
            if (File.Exists(src1)) File.Delete(src1);
            if (File.Exists(src2)) File.Delete(src2);
            if (File.Exists(dst)) File.Delete(dst);

            try
            {
                await File.WriteAllLinesAsync(src1, new[] { "ID,Name", "1,Alice", "2,Bob" });
                await File.WriteAllLinesAsync(src2, new[] { "ID,Name", "3,Charlie", "4,David" });

                // Merge keeping headers stripped from second
                await ExecuteAsync($"MERGE FILES 'merge_*.csv' TO '{dst}' WITH (HEADER = ON, OVERWRITE = ON);", evaluator);

                Assert.True(File.Exists(dst));
                var mergedLines = await File.ReadAllLinesAsync(dst);
                Assert.Equal(5, mergedLines.Length);
                Assert.Equal("ID,Name", mergedLines[0]);
                Assert.Equal("1,Alice", mergedLines[1]);
                Assert.Equal("2,Bob", mergedLines[2]);
                Assert.Equal("3,Charlie", mergedLines[3]);
                Assert.Equal("4,David", mergedLines[4]);
            }
            finally
            {
                if (File.Exists(src1)) File.Delete(src1);
                if (File.Exists(src2)) File.Delete(src2);
                if (File.Exists(dst)) File.Delete(dst);
            }
        }

        [Fact]
        public async Task TestMergeFilesNoMatchesPreservesDestination()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            string dst = "merge_preserve_dst.csv";
            if (File.Exists(dst)) File.Delete(dst);

            try
            {
                await File.WriteAllTextAsync(dst, "keep me");

                await Assert.ThrowsAsync<ExecutionException>(async () =>
                {
                    await ExecuteAsync($"MERGE FILES 'merge_no_match_*.csv' TO '{dst}' WITH (HEADER = ON, OVERWRITE = ON);", evaluator);
                });

                Assert.Equal("keep me", await File.ReadAllTextAsync(dst));
            }
            finally
            {
                if (File.Exists(dst)) File.Delete(dst);
            }
        }

        [Fact]
        public async Task TestSyncDirectory()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            string srcDir = "sync_src_dir";
            string destDir = "sync_dest_dir";
            if (Directory.Exists(srcDir)) Directory.Delete(srcDir, true);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, true);

            try
            {
                Directory.CreateDirectory(srcDir);
                Directory.CreateDirectory(destDir);

                await File.WriteAllTextAsync(Path.Combine(srcDir, "file1.txt"), "content1");
                await File.WriteAllTextAsync(Path.Combine(srcDir, "file2.txt"), "content2");
                await File.WriteAllTextAsync(Path.Combine(destDir, "extra.txt"), "extra content");

                // Sync without deleting extra
                await ExecuteAsync($"SYNC DIRECTORY '{srcDir}' TO '{destDir}' WITH (DELETE_EXTRA = OFF, OVERWRITE = ON, RECURSIVE = OFF);", evaluator);

                Assert.True(File.Exists(Path.Combine(destDir, "file1.txt")));
                Assert.True(File.Exists(Path.Combine(destDir, "file2.txt")));
                Assert.True(File.Exists(Path.Combine(destDir, "extra.txt"))); // Extra file should remain

                // Sync deleting extra
                await ExecuteAsync($"SYNC DIRECTORY '{srcDir}' TO '{destDir}' WITH (DELETE_EXTRA = ON, OVERWRITE = ON, RECURSIVE = OFF);", evaluator);

                Assert.True(File.Exists(Path.Combine(destDir, "file1.txt")));
                Assert.True(File.Exists(Path.Combine(destDir, "file2.txt")));
                Assert.False(File.Exists(Path.Combine(destDir, "extra.txt"))); // Extra file should be deleted
            }
            finally
            {
                if (Directory.Exists(srcDir)) Directory.Delete(srcDir, true);
                if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
            }
        }

        [Fact]
        public async Task TestVerifyFileIntegrity()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            string src = "verify_integrity_src.txt";
            string hashFile = "verify_integrity_src.sha256";
            if (File.Exists(src)) File.Delete(src);
            if (File.Exists(hashFile)) File.Delete(hashFile);

            try
            {
                await File.WriteAllTextAsync(src, "verify my contents");

                byte[] hashBytes;
                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes("verify my contents"));
                }
                string expectedHashStr = Convert.ToHexString(hashBytes).ToLowerInvariant();

                // Test direct expected hash
                await ExecuteAsync($"VERIFY FILE INTEGRITY '{src}' WITH (EXPECTED_HASH = '{expectedHashStr}', ALGORITHM = 'SHA256');", evaluator);

                // Test via hash file
                await File.WriteAllTextAsync(hashFile, $"{expectedHashStr}  {src}");
                await ExecuteAsync($"VERIFY FILE INTEGRITY '{src}' WITH (HASH_FILE = '{hashFile}', ALGORITHM = 'SHA256');", evaluator);

                // Test invalid hash throws
                await Assert.ThrowsAsync<ExecutionException>(async () =>
                {
                    await ExecuteAsync($"VERIFY FILE INTEGRITY '{src}' WITH (EXPECTED_HASH = 'invalidhash', ALGORITHM = 'SHA256');", evaluator);
                });
            }
            finally
            {
                if (File.Exists(src)) File.Delete(src);
                if (File.Exists(hashFile)) File.Delete(hashFile);
            }
        }

        private async Task<List<Row>> QueryRowsAsync(string sql, Evaluator evaluator)
        {
            var selectStmt = (SelectStatement)new Parser(new Lexer(sql).Tokenize()).Parse().Statements[0];
            var rows = new List<Row>();
            await foreach (var batch in evaluator.EvaluateSelect(selectStmt))
            {
                rows.AddRange(batch.Rows);
            }
            return rows;
        }

        [Fact]
        public async Task TestMoveFileConnectionToDirectoryConnection_UpdatesConnectionInPlace()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            string src = "test_conn_move_src.csv";
            string dir = "test_conn_move_dir";
            if (File.Exists(src)) File.Delete(src);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);

            try
            {
                Directory.CreateDirectory(dir);
                await File.WriteAllTextAsync(src, "id,name\n1,Alice\n2,Bob");

                await ExecuteAsync($"CREATE CONNECTION src_conn AS FLATFILE('{src}');", evaluator);
                await ExecuteAsync($"CREATE CONNECTION archive_dir AS DIRECTORY('{dir}');", evaluator);

                // MOVE FILE connection TO directory connection
                await ExecuteAsync("MOVE FILE src_conn TO archive_dir;", evaluator);

                string expectedNewPath = Path.Combine(dir, src);
                Assert.False(File.Exists(src));
                Assert.True(File.Exists(expectedNewPath));

                // Query src_conn to verify it was updated in-place and reads from the new location
                var result = await QueryRowsAsync("SELECT * FROM src_conn;", evaluator);
                Assert.Equal(2, result.Count);
                Assert.Equal(1m, Convert.ToDecimal(result[0]["id"]));
                Assert.Equal("Alice", result[0]["name"]);
            }
            finally
            {
                if (File.Exists(src)) File.Delete(src);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Fact]
        public async Task TestMoveFileConnectionToDirectoryConnectionWithDateSuffix()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            string src = "test_conn_suf_src.csv";
            string dir = "test_conn_suf_dir";
            if (File.Exists(src)) File.Delete(src);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);

            try
            {
                Directory.CreateDirectory(dir);
                await File.WriteAllTextAsync(src, "id,name\n1,Alice");

                await ExecuteAsync($"CREATE CONNECTION c_suf AS FLATFILE('{src}');", evaluator);
                await ExecuteAsync($"CREATE CONNECTION d_suf AS DIRECTORY('{dir}');", evaluator);

                await ExecuteAsync("MOVE FILE c_suf TO d_suf WITH (DATE_SUFFIX = 'yyyyMMdd');", evaluator);

                Assert.False(File.Exists(src));
                string dateStr = DateTime.Now.ToString("yyyyMMdd");
                string expectedNewPath = Path.Combine(dir, $"test_conn_suf_src_{dateStr}.csv");
                Assert.True(File.Exists(expectedNewPath));

                // Query c_suf to verify it reads from the date-suffixed location
                var result = await QueryRowsAsync("SELECT * FROM c_suf;", evaluator);
                Assert.Single(result);
                Assert.Equal("Alice", result[0]["name"]);
            }
            finally
            {
                if (File.Exists(src)) File.Delete(src);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Fact]
        public async Task TestMoveFileConnectionToFilePath_UpdatesConnectionInPlace()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            string src = "test_conn_fp_src.csv";
            string dst = "test_conn_fp_dst.csv";
            if (File.Exists(src)) File.Delete(src);
            if (File.Exists(dst)) File.Delete(dst);

            try
            {
                await File.WriteAllTextAsync(src, "id,name\n10,Charlie");

                await ExecuteAsync($"CREATE CONNECTION c_fp AS FLATFILE('{src}');", evaluator);
                await ExecuteAsync($"MOVE FILE c_fp TO '{dst}';", evaluator);

                Assert.False(File.Exists(src));
                Assert.True(File.Exists(dst));

                // Query c_fp
                var result = await QueryRowsAsync("SELECT * FROM c_fp;", evaluator);
                Assert.Single(result);
                Assert.Equal("Charlie", result[0]["name"]);
            }
            finally
            {
                if (File.Exists(src)) File.Delete(src);
                if (File.Exists(dst)) File.Delete(dst);
            }
        }

        [Fact]
        public async Task TestDeleteFileConnection_LeavesConnectionInPlace_AllowsSubsequentAlterInLoop()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            string src1 = "test_conn_del_1.csv";
            string src2 = "test_conn_del_2.csv";
            if (File.Exists(src1)) File.Delete(src1);
            if (File.Exists(src2)) File.Delete(src2);

            try
            {
                await File.WriteAllTextAsync(src1, "id,name\n1,FileOne");
                await File.WriteAllTextAsync(src2, "id,name\n2,FileTwo");

                await ExecuteAsync($"CREATE CONNECTION c_del AS FLATFILE('{src1}');", evaluator);

                // DELETE FILE connection
                await ExecuteAsync("DELETE FILE c_del;", evaluator);
                Assert.False(File.Exists(src1));

                // Connection remains registered
                Assert.True(evaluator.Connections.ContainsKey("c_del"));

                // Querying without alter returns 0 rows because the file does not exist
                var emptyResult = await QueryRowsAsync("SELECT * FROM c_del;", evaluator);
                Assert.Empty(emptyResult);

                // In a loop scenario: ALTER CONNECTION to next file works
                await ExecuteAsync($"ALTER CONNECTION c_del AS FLATFILE('{src2}');", evaluator);
                var result = await QueryRowsAsync("SELECT * FROM c_del;", evaluator);
                Assert.Single(result);
                Assert.Equal("FileTwo", result[0]["name"]);
            }
            finally
            {
                if (File.Exists(src1)) File.Delete(src1);
                if (File.Exists(src2)) File.Delete(src2);
            }
        }

        [Fact]
        public async Task TestCopyFileConnectionToDirectoryConnection_LeavesSourceConnectionUnchanged()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            string src = "test_conn_cp_src.csv";
            string dir = "test_conn_cp_dir";
            if (File.Exists(src)) File.Delete(src);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);

            try
            {
                Directory.CreateDirectory(dir);
                await File.WriteAllTextAsync(src, "id,name\n99,Preserved");

                await ExecuteAsync($"CREATE CONNECTION c_cp AS FLATFILE('{src}');", evaluator);
                await ExecuteAsync($"CREATE CONNECTION d_cp AS DIRECTORY('{dir}');", evaluator);

                await ExecuteAsync("COPY FILE c_cp TO d_cp;", evaluator);

                Assert.True(File.Exists(src));
                Assert.True(File.Exists(Path.Combine(dir, src)));

                // Source connection still points to src
                var result = await QueryRowsAsync("SELECT * FROM c_cp;", evaluator);
                Assert.Single(result);
                Assert.Equal("Preserved", result[0]["name"]);
            }
            finally
            {
                if (File.Exists(src)) File.Delete(src);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Fact]
        public async Task TestNonFileConnectionInFileOp_ThrowsExecutionException()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            await ExecuteAsync("CREATE CONNECTION non_file_db AS MSSQL('Server=localhost;Database=test;User Id=sa;Password=secret;');", evaluator);

            var ex = await Assert.ThrowsAsync<ExecutionException>(async () =>
            {
                await ExecuteAsync("MOVE FILE non_file_db TO 'dest.csv';", evaluator);
            });

            Assert.Contains("does not support file operations", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task TestWildcardPathInConnection_ThrowsExecutionException()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            await ExecuteAsync("CREATE CONNECTION wildcard_conn AS FLATFILE('data/*.csv');", evaluator);

            var ex = await Assert.ThrowsAsync<ExecutionException>(async () =>
            {
                await ExecuteAsync("MOVE FILE wildcard_conn TO 'dest.csv';", evaluator);
            });

            Assert.Contains("Wildcard patterns in connection 'wildcard_conn' are not supported", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
