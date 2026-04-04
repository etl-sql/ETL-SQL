using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Data;

namespace ETL_SQL.Tests
{
    public class DataTypeTests
    {
        [Fact]
        public async Task TestNumericTypes()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("DECLARE @t1 TINYINT = 255, @t2 BIGINT = 123456789012345;"));
            Assert.Equal((byte)255, ev.Variables["@t1"]);
            Assert.Equal(123456789012345L, ev.Variables["@t2"]);
        }

        [Fact]
        public async Task TestTimeType()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("DECLARE @time TIME = '14:30:05';"));
            Assert.Equal(new TimeSpan(14, 30, 5), ev.Variables["@time"]);
        }

        [Fact]
        public async Task TestUniqueIdentifier()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var guidStr = "612495f8-8e32-4c4d-993e-17e18a244e6b";
            await ev.Evaluate(Parse($"DECLARE @id UNIQUEIDENTIFIER = '{guidStr}';"));
            Assert.Equal(Guid.Parse(guidStr), ev.Variables["@id"]);
        }

        [Fact]
        public async Task TestBinaryTypes()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var base64 = "SGVsbG8="; // "Hello"
            await ev.Evaluate(Parse($"DECLARE @bin VARBINARY = '{base64}';"));
            var res = ev.Variables["@bin"] as byte[];
            Assert.NotNull(res);
            Assert.Equal("Hello", System.Text.Encoding.UTF8.GetString(res));
        }

        [Fact]
        public async Task TestJsonXmlTypes()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("DECLARE @j JSON = '{\"a\":1}', @x XML = '<root/>';"));
            Assert.Equal("{\"a\":1}", ev.Variables["@j"]);
            Assert.Equal("<root/>", ev.Variables["@x"]);
        }

        private static Script Parse(string source)
        {
            var tokens = new Lexer(source).Tokenize();
            return new Parser(tokens, source).Parse();
        }
    }
}
