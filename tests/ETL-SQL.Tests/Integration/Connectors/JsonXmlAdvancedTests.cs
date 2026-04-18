using Xunit;
using ETL_SQL.Engine.Engines;

using ETL_SQL.Core;
using ETL_SQL.Data;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Engine.Handlers;
using Microsoft.Extensions.DependencyInjection;
using System;
using ETL_SQL.App;

namespace ETL_SQL.Tests.Integration
{
    public class JsonXmlAdvancedTests
    {
        private async Task<DataTable> ExecuteSelect(string sql)
        {
            var serviceProvider = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator = serviceProvider.GetRequiredService<Evaluator>();
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var stmt = parser.ParseStatement();
            
            await evaluator.EvaluateStatement(stmt);
            return evaluator.LastResult;
        }

        [Fact]
        public async Task Select_ForJson_Path_WithoutArrayWrapper()
        {
            string sql = "SELECT 1 as [Id.Val], 'Test' as [Name] FOR JSON PATH, WITHOUT_ARRAY_WRAPPER";
            var result = await ExecuteSelect(sql);
            
            Assert.Single(result.Rows);
            string json = result.Rows[0].Columns.Values.First().ToString();
            Assert.DoesNotContain("[", json);
            Assert.DoesNotContain("]", json);
            Assert.Contains("\"Id\": {", json);
            Assert.Contains("\"Val\": 1", json);
        }

        [Fact]
        public async Task Select_ForXml_Raw_Elements()
        {
            string sql = "SELECT 1 as Id, 'Test' as Name FOR XML RAW, ELEMENTS";
            var result = await ExecuteSelect(sql);
            
            Assert.Single(result.Rows);
            string xml = result.Rows[0].Columns.Values.First().ToString();
            Assert.Contains("<Id>1</Id>", xml);
            Assert.Contains("<Name>Test</Name>", xml);
            Assert.DoesNotContain("Id=\"1\"", xml);
        }

        [Fact]
        public async Task Select_ForXml_Auto_Attributes()
        {
            string sql = "SELECT 1 as Id, 'Test' as Name FOR XML AUTO";
            var result = await ExecuteSelect(sql);
            
            Assert.Single(result.Rows);
            string xml = result.Rows[0].Columns.Values.First().ToString();
            Assert.Contains("Id=\"1\"", xml);
            Assert.Contains("Name=\"Test\"", xml);
            Assert.DoesNotContain("<Id>", xml);
        }

        [Fact]
        public async Task Select_ForXml_Path_Nesting()
        {
            string sql = "SELECT 1 as [User.Id], 'Alice' as [User.Name] FOR XML PATH('Root')";
            var result = await ExecuteSelect(sql);
            
            Assert.Single(result.Rows);
            string xml = result.Rows[0].Columns.Values.First().ToString();
            Assert.Contains("<User>", xml);
            Assert.Contains("<Id>1</Id>", xml);
            Assert.Contains("<Name>Alice</Name>", xml);
        }

        [Fact]
        public async Task Select_ForXml_IncludeNullValues_Xsinil()
        {
            // We need a way to select NULL with a name
            string sql = "SELECT 1 as Id, CAST(NULL AS STRING) as Name FOR XML RAW, ELEMENTS, INCLUDE_NULL_VALUES";
            var result = await ExecuteSelect(sql);
            
            Assert.Single(result.Rows);
            string xml = result.Rows[0].Columns.Values.First().ToString();
            Assert.Contains("xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"", xml);
            Assert.Contains("<Name xsi:nil=\"true\" />", xml);
        }
    }
}
