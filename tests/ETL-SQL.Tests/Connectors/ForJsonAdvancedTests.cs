using ETL_SQL.Core;
using ETL_SQL.Engine;
using ETL_SQL.Data;

using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using System.Threading.Tasks;
using System.Linq;
using System;

namespace ETL_SQL.Tests.Connectors
{
    [Trait("Connector", "JSON")]
    [Trait("CertificationClass", "LocalRealIntegration")]
    public class ForJsonAdvancedTests
    {
        private readonly IServiceProvider _serviceProvider;

        public ForJsonAdvancedTests()
        {
            _serviceProvider = DependencyInjectionSetup.BuildServiceProvider();
        }

        [Fact]
        public async Task ForJson_IncludeNullValues()
        {
            var evaluator = _serviceProvider.GetRequiredService<Evaluator>();
            var script = @"
                CREATE TABLE #Nulls (Id INT, Name VARCHAR(50));
                INSERT INTO #Nulls (Id, Name) VALUES (1, NULL);
                SELECT * FROM #Nulls FOR JSON PATH, INCLUDE_NULL_VALUES;
            ";

            await evaluator.Evaluate(new Lexer(script).TokenizeToScript());
            var result = evaluator.LastResult;
            var json = result.Rows[0]["JSON_F52E2B61"]?.ToString();

            Assert.Contains("\"Name\": null", json);
        }

        [Fact]
        public async Task ForJson_OmitNullValues_Default()
        {
            var evaluator = _serviceProvider.GetRequiredService<Evaluator>();
            var script = @"
                CREATE TABLE #OmitNulls (Id INT, Name VARCHAR(50));
                INSERT INTO #OmitNulls (Id, Name) VALUES (1, NULL);
                SELECT * FROM #OmitNulls FOR JSON PATH;
            ";

            await evaluator.Evaluate(new Lexer(script).TokenizeToScript());
            var result = evaluator.LastResult;
            var json = result.Rows[0]["JSON_F52E2B61"]?.ToString();

            Assert.DoesNotContain("\"Name\"", json);
        }

        [Fact]
        public async Task ForJson_WithoutArrayWrapper_SingleRow()
        {
            var evaluator = _serviceProvider.GetRequiredService<Evaluator>();
            var script = @"
                CREATE TABLE #Single (Id INT);
                INSERT INTO #Single (Id) VALUES (42);
                SELECT * FROM #Single FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
            ";

            await evaluator.Evaluate(new Lexer(script).TokenizeToScript());
            var result = evaluator.LastResult;
            var json = result.Rows[0]["JSON_F52E2B61"]?.ToString();

            Assert.StartsWith("{", json);
            Assert.DoesNotContain("[", json);
        }

        [Fact]
        public async Task ForJson_WithoutArrayWrapper_MultipleRows()
        {
            var evaluator = _serviceProvider.GetRequiredService<Evaluator>();
            var script = @"
                CREATE TABLE #Multi (Id INT);
                INSERT INTO #Multi (Id) VALUES (1), (2);
                SELECT * FROM #Multi FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
            ";

            await evaluator.Evaluate(new Lexer(script).TokenizeToScript());
            var result = evaluator.LastResult;
            var json = result.Rows[0]["JSON_F52E2B61"]?.ToString();

            Assert.Contains("},", json);
            Assert.DoesNotContain("[", json);
        }

        [Fact]
        public async Task ForJson_Root()
        {
            var evaluator = _serviceProvider.GetRequiredService<Evaluator>();
            var script = @"
                CREATE TABLE #RootTest (Id INT);
                INSERT INTO #RootTest (Id) VALUES (1);
                SELECT * FROM #RootTest FOR JSON PATH, ROOT('Payload');
            ";

            await evaluator.Evaluate(new Lexer(script).TokenizeToScript());
            var result = evaluator.LastResult;
            var json = result.Rows[0]["JSON_F52E2B61"]?.ToString();

            Assert.Contains("\"Payload\": [", json);
        }
    }
}
