using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Data;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.App;


namespace ETL_SQL.Tests
{
    public class TableConstraintTests
    {
        [Fact]
        public async Task TestNotNullConstraint()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #NotNullTest (ID INT NOT NULL, Name STRING);"));
            
            // Should succeed
            await ev.Evaluate(Parse("INSERT INTO #NotNullTest (ID, Name) VALUES (1, 'Alice');"));
            
            // Should fail
            await Assert.ThrowsAsync<ExecutionException>(() => ev.Evaluate(Parse("INSERT INTO #NotNullTest (ID, Name) VALUES (NULL, 'Bob');")));
        }

        [Fact]
        public async Task TestPrimaryKeyConstraint()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #PkTest (ID INT PRIMARY KEY, Name STRING);"));
            
            await ev.Evaluate(Parse("INSERT INTO #PkTest (ID, Name) VALUES (1, 'Alice');"));
            
            // Duplicate PK should fail
            await Assert.ThrowsAsync<ExecutionException>(() => ev.Evaluate(Parse("INSERT INTO #PkTest (ID, Name) VALUES (1, 'Duplicate');")));
            
            // Null PK should fail
            await Assert.ThrowsAsync<ExecutionException>(() => ev.Evaluate(Parse("INSERT INTO #PkTest (ID, Name) VALUES (NULL, 'NullID');")));
        }

        [Fact]
        public async Task TestMultiColumnPrimaryKey()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #MultiPk (Part1 INT, Part2 INT, PRIMARY KEY (Part1, Part2));"));
            
            await ev.Evaluate(Parse("INSERT INTO #MultiPk VALUES (1, 1), (1, 2), (2, 1);"));
            
            // Duplicate combination should fail
            await Assert.ThrowsAsync<ExecutionException>(() => ev.Evaluate(Parse("INSERT INTO #MultiPk VALUES (1, 1);")));
        }

        [Fact]
        public async Task TestCheckConstraintColumnLevel()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #CheckCol (Age INT CHECK (Age >= 18));"));
            
            await ev.Evaluate(Parse("INSERT INTO #CheckCol VALUES (20);"));
            
            // Age < 18 should fail
            await Assert.ThrowsAsync<ExecutionException>(() => ev.Evaluate(Parse("INSERT INTO #CheckCol VALUES (15);")));
        }

        [Fact]
        public async Task TestCheckConstraintTableLevel()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #CheckTable (MinVal INT, MaxVal INT, CHECK (MaxVal > MinVal));"));
            
            await ev.Evaluate(Parse("INSERT INTO #CheckTable VALUES (10, 20);"));
            
            // MaxVal <= MinVal should fail
            await Assert.ThrowsAsync<ExecutionException>(() => ev.Evaluate(Parse("INSERT INTO #CheckTable VALUES (20, 10);")));
        }

        [Fact]
        public async Task TestUniqueConstraint()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #UniqueTest (Email STRING UNIQUE);"));
            
            await ev.Evaluate(Parse("INSERT INTO #UniqueTest VALUES ('test@example.com');"));
            
            // Duplicate email should fail
            await Assert.ThrowsAsync<ExecutionException>(() => ev.Evaluate(Parse("INSERT INTO #UniqueTest VALUES ('test@example.com');")));
        }

        [Fact]
        public void TestForeignKeyParsing()
        {
            // Just verify it parses and ToSql works
            var sql = "CREATE TABLE Child (ID INT, ParentID INT, FOREIGN KEY (ParentID) REFERENCES Parent(ID));";
            var script = Parse(sql);
            var stmt = (CreateTableStatement)script.Statements[0];
            
            Assert.Single(stmt.TableConstraints);
            var fk = Assert.IsType<TableForeignKeyConstraint>(stmt.TableConstraints[0]);
            Assert.Equal("ParentID", fk.Columns[0]);
            Assert.Equal("Parent", fk.Reference.Table.TableName);
        }

        private static Script Parse(string sql)
        {
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            return new Parser(tokens).Parse();
        }
    }
}
