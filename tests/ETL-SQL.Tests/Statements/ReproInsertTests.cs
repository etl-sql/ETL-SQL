using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Common;

namespace ETL_SQL.Tests
{
    public class ReproInsertTests
    {
        [Fact]
        public void TestMultipleValuesParser()
        {
            var source = @"INSERT INTO Orders (OrderId, UserId, OrderDate) 
                           VALUES (1, 101, '2024-01-01'), 
                                  (2, 102, '2024-01-02'), 
                                  (3, 103, '2024-01-03');";
            
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new ETL_SQL.Core.Parser.Parser(tokens);
            var script = parser.Parse();
            
            Assert.Empty(script.Diagnostics);
            Assert.Single(script.Statements);
            var insert = Assert.IsType<InsertStatement>(script.Statements[0]);
            Assert.Equal("Orders", insert.TargetTable.TableName);
            Assert.Equal(3, insert.Values!.Count);
        }

        [Fact]
        public void TestMultipleValuesKeywordsParser()
        {
            var source = @"INSERT INTO Orders (OrderId) 
                           VALUES (1), (2) 
                           VALUES (3), (4);";
            
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new ETL_SQL.Core.Parser.Parser(tokens);
            var script = parser.Parse();
            
            Assert.Empty(script.Diagnostics);
            Assert.Single(script.Statements);
            var insert = Assert.IsType<InsertStatement>(script.Statements[0]);
            Assert.Equal(4, insert.Values!.Count);
        }

        [Fact]
        public void TestInsertWithCteParser()
        {
            var source = @"WITH SourceData AS (SELECT 1 AS ID)
                           INSERT INTO Orders (OrderId) SELECT ID FROM SourceData;";
            
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new ETL_SQL.Core.Parser.Parser(tokens);
            var script = parser.Parse();
            
            Assert.Empty(script.Diagnostics);
            Assert.Single(script.Statements);
            var insert = Assert.IsType<InsertStatement>(script.Statements[0]);
            Assert.NotNull(insert.Ctes);
            Assert.Single(insert.Ctes);
            Assert.Equal("SourceData", insert.Ctes[0].Name);
        }

        [Fact]
        public void TestMissingTableNameParser()
        {
            // Testing the exact string from TODO #3
            var source = "INSERT INTO (OrderId, UserId, OrderDate) VALUES (1, 101, '2024-01-01'), (2, 102, '2024-01-02'), (3, 103, '2024-01-03');";
            
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new ETL_SQL.Core.Parser.Parser(tokens);
            
            // This should throw or have diagnostics because 'Orders' is missing
            var script = parser.Parse();
            Assert.NotEmpty(script.Diagnostics);
        }
    }
}
