using Xunit;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Tests.Statements
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
        public async Task TestUnderscoreFunctionOverwrite()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            string src = "fs_test_func_src.txt";
            string dst = "fs_test_func_dst.txt";
            await File.WriteAllTextAsync(src, "new");
            await File.WriteAllTextAsync(dst, "old");

            try
            {
                // Traditional syntax with 3rd param (OFF)
                await Assert.ThrowsAsync<ExecutionException>(async () => 
                {
                    await ExecuteAsync($"COPY_FILE('{src}', '{dst}', OFF);", evaluator);
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
    }
}
