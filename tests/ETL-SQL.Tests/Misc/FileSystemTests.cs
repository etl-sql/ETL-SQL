using Xunit;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Engine;
using ETL_SQL.Core;
using ETL_SQL.App;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Tests
{
    public class FileSystemTests
    {
        private Evaluator GetEvaluator()
        {
            return DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        }

        [Fact]
        public async Task TestFileExists()
        {
            string fileName = "exists_test.txt";
            File.WriteAllText(fileName, "test");
            var evaluator = GetEvaluator();

            try
            {
                await evaluator.Evaluate(new Lexer($"SELECT FILE_EXISTS('{fileName}') as Result;").TokenizeToScript());
                Assert.True(Convert.ToBoolean(evaluator.LastResult?.Rows[0]["Result"]));

                await evaluator.Evaluate(new Lexer("SELECT FILE_EXISTS('non_existent.txt') as Result;").TokenizeToScript());
                Assert.False(Convert.ToBoolean(evaluator.LastResult?.Rows[0]["Result"]));
            }
            finally
            {
                if (File.Exists(fileName)) File.Delete(fileName);
            }
        }

        [Fact]
        public async Task TestDirectoryExists()
        {
            string dirName = "exists_dir";
            if (Directory.Exists(dirName)) Directory.Delete(dirName, true);
            Directory.CreateDirectory(dirName);
            var evaluator = GetEvaluator();

            try
            {
                await evaluator.Evaluate(new Lexer($"SELECT DIRECTORY_EXISTS('{dirName}') as Result;").TokenizeToScript());
                Assert.True(Convert.ToBoolean(evaluator.LastResult?.Rows[0]["Result"]));

                await evaluator.Evaluate(new Lexer("SELECT DIRECTORY_EXISTS('non_existent_dir') as Result;").TokenizeToScript());
                Assert.False(Convert.ToBoolean(evaluator.LastResult?.Rows[0]["Result"]));
            }
            finally
            {
                if (Directory.Exists(dirName)) Directory.Delete(dirName, true);
            }
        }

        [Fact]
        public async Task TestFileList()
        {
            string dirName = "list_test_dir";
            if (Directory.Exists(dirName)) Directory.Delete(dirName, true);
            Directory.CreateDirectory(dirName);
            File.WriteAllText(Path.Combine(dirName, "file1.txt"), "1");
            File.WriteAllText(Path.Combine(dirName, "file2.log"), "2");
            
            var evaluator = GetEvaluator();

            try
            {
                var script = $@"
                    DECLARE @count INT = 0;
                    FOREACH @f IN FILE_LIST('{dirName}')
                    BEGIN
                        SET @count = @count + 1;
                    END
                    SELECT @count as TotalCount;";

                await evaluator.Evaluate(new Lexer(script).TokenizeToScript());
                var count = Convert.ToInt32(evaluator.LastResult?.Rows[0]["TotalCount"]);
                Assert.Equal(2, count);
            }
            finally
            {
                if (Directory.Exists(dirName)) Directory.Delete(dirName, true);
            }
        }

        [Fact]
        public async Task TestCopyDirectory()
        {
            string src = "copy_src";
            string dest = "copy_dest";
            if (Directory.Exists(src)) Directory.Delete(src, true);
            if (Directory.Exists(dest)) Directory.Delete(dest, true);
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "test.txt"), "hello");
            
            var evaluator = GetEvaluator();

            try
            {
                // Note: The parser for directory operations uses a different path than basic statements in some cases,
                // but ParseStatement handles them. We need to make sure we use the right syntax.
                // Syntax from StatementParser: CREATE_DIRECTORY(path, extra), etc.
                await evaluator.Evaluate(new Lexer($"COPY_DIRECTORY('{src}', '{dest}');").TokenizeToScript());
                Assert.True(File.Exists(Path.Combine(dest, "test.txt")));
            }
            finally
            {
                if (Directory.Exists(src)) Directory.Delete(src, true);
                if (Directory.Exists(dest)) Directory.Delete(dest, true);
            }
        }

        [Fact]
        public async Task TestDeleteDirectoryContents()
        {
            string dir = "delete_contents_test";
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "keep_me.txt"), "stay");
            Directory.CreateDirectory(Path.Combine(dir, "sub"));
            
            var evaluator = GetEvaluator();

            try
            {
                await evaluator.Evaluate(new Lexer($"DELETE_DIRECTORY_CONTENTS('{dir}');").TokenizeToScript());
                Assert.True(Directory.Exists(dir));
                Assert.Empty(Directory.GetFiles(dir));
                Assert.Empty(Directory.GetDirectories(dir));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
        [Fact]
        public async Task TestFileOperations()
        {
            string src = "file_src.txt";
            string dest = "file_dest.txt";
            if (File.Exists(src)) File.Delete(src);
            if (File.Exists(dest)) File.Delete(dest);
            if (File.Exists("file_renamed.txt")) File.Delete("file_renamed.txt");
            
            await File.WriteAllTextAsync(src, "test");

            var evaluator = GetEvaluator();
            try
            {
                await evaluator.Evaluate(new Lexer($"COPY_FILE('{src}', '{dest}');").TokenizeToScript());
                Assert.True(File.Exists(dest), "COPY_FILE failed");

                await evaluator.Evaluate(new Lexer($"DELETE_FILE('{src}');").TokenizeToScript());
                Assert.False(File.Exists(src), "DELETE_FILE failed");

                await evaluator.Evaluate(new Lexer($"RENAME_FILE('{dest}', 'file_renamed.txt');").TokenizeToScript());
                Assert.True(File.Exists("file_renamed.txt"), "RENAME_FILE failed");
            }
            finally
            {
                if (File.Exists(src)) File.Delete(src);
                if (File.Exists(dest)) File.Delete(dest);
                if (File.Exists("file_renamed.txt")) File.Delete("file_renamed.txt");
            }
        }

        [Fact]
        public async Task TestDirectoryOperations()
        {
            string dir = "DirOps_Test";
            string renamedDir = "DirOps_Renamed";
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            if (Directory.Exists(renamedDir)) Directory.Delete(renamedDir, true);
            
            var evaluator = GetEvaluator();
            try
            {
                await evaluator.Evaluate(new Lexer($"CREATE_DIRECTORY('{dir}');").TokenizeToScript());
                Assert.True(Directory.Exists(dir), "CREATE_DIRECTORY failed");

                await evaluator.Evaluate(new Lexer($"RENAME_DIRECTORY('{dir}', '{renamedDir}');").TokenizeToScript());
                Assert.True(Directory.Exists(renamedDir), "RENAME_DIRECTORY failed");

                await evaluator.Evaluate(new Lexer($"DELETE_DIRECTORY('{renamedDir}');").TokenizeToScript());
                Assert.False(Directory.Exists(renamedDir), "DELETE_DIRECTORY failed");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                if (Directory.Exists(renamedDir)) Directory.Delete(renamedDir, true);
            }
        }
    }
}
