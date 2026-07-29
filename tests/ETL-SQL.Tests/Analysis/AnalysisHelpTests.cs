using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Analysis
{
    public class HelpSystemTests
    {
        [Fact]
        public async Task TestHelpFileOperations()
        {
            var serviceProvider = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator = serviceProvider.GetRequiredService<Evaluator>();

            // Capture messages
            evaluator.RedirectOutput = true;

            await evaluator.Evaluate(Parse("HELP SEND FILE;"));
            var output = string.Join("\n", evaluator.Messages);

            Assert.Contains("VERBOSE:", output);
            Assert.Contains("SHORTHAND:", output);
            Assert.Contains("OVERWRITE", output);

            evaluator.Messages.Clear();
            await evaluator.Evaluate(Parse("HELP RECEIVE FILE;"));
            output = string.Join("\n", evaluator.Messages);
            Assert.Contains("VERBOSE:", output);
            Assert.Contains("SHORTHAND:", output);

            evaluator.Messages.Clear();
            await evaluator.Evaluate(Parse("HELP SEND EMAIL;"));
            output = string.Join("\n", evaluator.Messages);
            Assert.Contains("VERBOSE:", output);
            Assert.Contains("SHORTHAND:", output);

            evaluator.Messages.Clear();
            await evaluator.Evaluate(Parse("HELP CONFIG;"));
            output = string.Join("\n", evaluator.Messages);
            Assert.Contains("HELP: CONFIG", output);
            Assert.Contains("inspect the configuration options", output);
            Assert.Contains("redacted", output);

            evaluator.Messages.Clear();
            await evaluator.Evaluate(Parse("HELP ENG;"));
            output = string.Join("\n", evaluator.Messages);
            Assert.Contains("HELP: ENG", output);
            Assert.Contains("eng.*", output);
            Assert.Contains("eng.connections", output);
        }

        private static Script Parse(string source)
        {
            var lexer = new global::ETL_SQL.Core.Parser.Lexer(source);
            return new global::ETL_SQL.Core.Parser.Parser(lexer.Tokenize()).Parse();
        }
    }
}
