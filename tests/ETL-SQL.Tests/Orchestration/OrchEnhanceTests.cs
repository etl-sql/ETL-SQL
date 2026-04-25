using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using ETL_SQL.App;

namespace ETL_SQL.Tests.Orchestration
{
    public class OrchestrationEnhancementTests : IDisposable
    {
        private readonly string _testDir;

        public OrchestrationEnhancementTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "ETL_SQL_OrchTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
        }

        private Evaluator GetEvaluator()
        {
            var serviceProvider = DependencyInjectionSetup.BuildServiceProvider();
            return serviceProvider.GetRequiredService<Evaluator>();
        }

        public void Dispose()
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, true);
        }

        [Fact]
        public async Task RunScript_WithVariablePath_ShouldExecute()
        {
            // Arrange
            string scriptPath = Path.Combine(_testDir, "sub.etlsql");
            File.WriteAllText(scriptPath, "DECLARE @output INT = 42;");

            string mainScript = $@"
                DECLARE @p NVARCHAR(MAX) = '{scriptPath.Replace("\\", "\\\\")}';
                DECLARE @res INT;
                RUN SCRIPT @p WITH (@output = @res);
                PRINT @res;
            ";

            // Act
            var tokens = new Lexer(mainScript).Tokenize();
            var script = new Parser(tokens).Parse();
            var evaluator = GetEvaluator();
            await evaluator.Evaluate(script);

            // Assert
            Assert.Equal(42m, Convert.ToDecimal(evaluator.GetVariable("@res")));
        }

        [Fact]
        public async Task Foreach_WithDirectoryFunction_ShouldIterateFiles()
        {
            // Arrange
            File.WriteAllText(Path.Combine(_testDir, "file1.txt"), "test1");
            File.WriteAllText(Path.Combine(_testDir, "file2.log"), "test2");
            Directory.CreateDirectory(Path.Combine(_testDir, "sub"));
            File.WriteAllText(Path.Combine(_testDir, "sub", "file3.txt"), "test3");

            string scriptText = $@"
                DECLARE @count INT = 0;
                FOR EACH @f IN DIRECTORY('{_testDir.Replace("\\", "\\\\")}', false)
                BEGIN
                    SET @count = @count + 1;
                    PRINT @f.Name;
                END
                DECLARE @finalCount INT = @count;
            ";

            var tokens = new Lexer(scriptText).Tokenize();
            var script = new Parser(tokens).Parse();
            var evaluator = GetEvaluator();
            await evaluator.Evaluate(script);

            // Assert
            Assert.Equal(2m, Convert.ToDecimal(evaluator.GetVariable("@finalCount"))); // Only top directory
        }

        [Fact]
        public async Task Foreach_WithRecursiveDirectory_ShouldFindAllFiles()
        {
            // Arrange
            File.WriteAllText(Path.Combine(_testDir, "file1.txt"), "test1");
            Directory.CreateDirectory(Path.Combine(_testDir, "sub"));
            File.WriteAllText(Path.Combine(_testDir, "sub", "file2.txt"), "test2");

            string scriptText = $@"
                DECLARE @count INT = 0;
                FOR EACH @f IN DIRECTORY('{_testDir.Replace("\\", "\\\\")}', true)
                BEGIN
                    SET @count = @count + 1;
                END
                DECLARE @finalCount INT = @count;
            ";

            // Act
            var tokens = new Lexer(scriptText).Tokenize();
            var script = new Parser(tokens).Parse();
            var evaluator = GetEvaluator();
            await evaluator.Evaluate(script);

            // Assert
            Assert.Equal(2m, Convert.ToDecimal(evaluator.GetVariable("@finalCount")));
        }
    }
}
