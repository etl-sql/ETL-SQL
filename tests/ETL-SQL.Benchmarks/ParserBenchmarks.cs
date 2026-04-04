using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using ETL_SQL.Core;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Benchmarks
{
    [MemoryDiagnoser]
    public class ParserBenchmarks
    {
        private string _smallScript;
        private string _largeScript;

        [GlobalSetup]
        public void Setup()
        {
            _smallScript = "SELECT * FROM Users WHERE Id = 42;";
            
            // Build a larger script
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 100; i++)
            {
                sb.AppendLine($"INSERT INTO Table{i} SELECT * FROM Source WHERE Col = {i};");
            }
            _largeScript = sb.ToString();
        }

        [Benchmark]
        public void ParseSmallScript()
        {
            var tokens = new Lexer(_smallScript).Tokenize();
            var script = new Parser(tokens).Parse();
        }

        [Benchmark]
        public void ParseLargeScript()
        {
            var tokens = new Lexer(_largeScript).Tokenize();
            var script = new Parser(tokens).Parse();
        }
    }
}
