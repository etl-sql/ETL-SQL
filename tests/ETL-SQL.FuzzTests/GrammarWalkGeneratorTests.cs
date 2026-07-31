using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Analysis.Linting.Grammar;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using Xunit;

namespace ETL_SQL.FuzzTests
{
    public class GrammarWalkGeneratorTests
    {
        [Fact]
        public void GenerateQuery_DefaultRawOutput_IsMostlyParserAccepted()
        {
            var tree = DefaultGrammar.Build();
            var generator = new GrammarWalkGenerator(tree, new Random(12345));
            generator.AddCustomSchema("FuzzTable", new[] { "ID", "Price", "Name", "TotalAmount" });

            const int iterations = 200;
            var rejected = new List<string>();

            for (int i = 0; i < iterations; i++)
            {
                var tokens = generator.GenerateQuery();
                var query = QueryMinimizer.Render(tokens);

                try
                {
                    var parsed = new Parser(tokens, query).Parse();
                    if (parsed.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                    {
                        rejected.Add(query);
                    }
                }
                catch
                {
                    rejected.Add(query);
                }
            }

            Assert.True(
                rejected.Count <= iterations * 2 / 5,
                $"Raw generator parser rejection rate was {rejected.Count}/{iterations}. Samples: {string.Join(" | ", rejected.Take(5))}");
        }
    }
}
