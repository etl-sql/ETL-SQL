using Xunit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Data;

namespace ETL_SQL.Tests
{
    public class EdgeCaseTests
    {
        [Fact]
        public async Task TestNestedRunScript()
        {
            string childPath = "child.sql";
            string parentPath = "parent.sql";
            
            // Child script declares @p as OUTPUT
            File.WriteAllText(childPath, "DECLARE @p INT = @in * 2 OUTPUT;");
            File.WriteAllText(parentPath, $"DECLARE @p INT = 5; RUN SCRIPT '{childPath}' WITH (@in = @p);");

            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(TestHelpers.Parse(File.ReadAllText(parentPath)));

            Assert.True(ev.Variables.ContainsKey("@p"), "Variable @p should be in scope");
            Assert.Equal(10m, Convert.ToDecimal(ev.Variables["@p"]));

            File.Delete(childPath);
            File.Delete(parentPath);
        }

        [Fact]
        public async Task TestTryCatchRuntimeError()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            string script = @"
                DECLARE @err STRING;
                BEGIN TRY
                    SET @err = 1 / 0; -- Runtime error
                END
                BEGIN CATCH
                    SET @err = 'CAUGHT';
                END
            ";
            await ev.Evaluate(TestHelpers.Parse(script));
            Assert.Equal("CAUGHT", ev.Variables["@err"]);
        }

        [Fact]
        public async Task TestTryCatchThrow()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            string script = @"
                DECLARE @marker STRING = 'NONE';
                BEGIN TRY
                    BEGIN TRY
                        SET @marker = 1 / 0;
                    END
                    BEGIN CATCH
                        SET @marker = 'INNER';
                        THROW; -- Re-throw
                    END
                END
                BEGIN CATCH
                    SET @marker = 'OUTER';
                END
            ";
            await ev.Evaluate(TestHelpers.Parse(script));
            Assert.Equal("OUTER", ev.Variables["@marker"]);
        }

        [Fact]
        public async Task TestExplainRemote()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            // Using MockSqlDataSource to simulate remote plan
            await ev.Evaluate(TestHelpers.Parse("CREATE CONNECTION RemotePlan ON MSSQL('Server=Remote') WITH(PASSWORD='abc');"));
            
            var res = await ev.ExecuteQuery(TestHelpers.Parse("EXPLAIN SELECT * FROM RemotePlan.Users;").Statements[0]).FirstAsync();
            
            Assert.NotEmpty(res.Rows);
            // Verify it mentions remote execution
            bool foundRemote = false;
            foreach (var row in res.Rows)
            {
                if (row.Columns.Values.Any(v => v?.ToString()?.Contains("Remote", StringComparison.OrdinalIgnoreCase) == true))
                {
                    foundRemote = true;
                    break;
                }
            }
            Assert.True(foundRemote, "Explain should mention remote source");
        }

        [Fact]
        public async Task TestDirectoryDeleteRecursive()
        {
            string root = "test_dir_delete";
            string sub = Path.Combine(root, "sub");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, "file.txt"), "data");

            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            
            await ev.Evaluate(TestHelpers.Parse($"DELETE_DIRECTORY('{root.Replace("\\", "/")}');"));
            
            Assert.False(Directory.Exists(root));
        }
    }
}
