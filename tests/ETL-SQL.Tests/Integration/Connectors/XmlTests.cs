using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Data;
using ETL_SQL.Connectors.Xml;
using Spectre.Console;
using ETL_SQL.Common;

namespace ETL_SQL.Tests.Integration
{
    [Trait("Connector", "XML")]
    [Trait("CertificationClass", "LocalRealIntegration")]
    public class XmlTests
    {
        [Fact]
        public async Task TestBasicXmlRead()
        {
            string tempXml = "test_data.xml";
            await File.WriteAllTextAsync(tempXml, @"
<Root>
    <Item><ID>1</ID><Name>Record 1</Name></Item>
    <Item><ID>2</ID><Name>Record 2</Name></Item>
</Root>");

            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse($"CREATE CONNECTION x AS XML('{tempXml}');"));
            var res = await ev.ExecuteQuery(Parse("SELECT * FROM x;").Statements[0]).FirstAsync();
            
            Assert.Equal(2, res.Rows.Count);
            Assert.Equal("1", res.Rows[0]["ID"]?.ToString());

            try { if (File.Exists(tempXml)) File.Delete(tempXml); } catch (IOException) { }
        }

        [Fact]
        public async Task TestXmlWithAttributes()
        {
            string tempXml = "test_attr.xml";
            await File.WriteAllTextAsync(tempXml, @"
<Root>
    <Item id='100' status='Active'>Value 1</Item>
    <Item id='200' status='Inactive'>Value 2</Item>
</Root>");

            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse($"CREATE CONNECTION x AS XML('{tempXml}');"));
            var res = await ev.ExecuteQuery(Parse("SELECT * FROM x;").Statements[0]).FirstAsync();
            
            Assert.Equal(2, res.Rows.Count);
            Assert.Equal("100", res.Rows[0]["id"]?.ToString());
            Assert.Equal("Active", res.Rows[0]["status"]?.ToString());

            try { if (File.Exists(tempXml)) File.Delete(tempXml); } catch (IOException) { }
        }

        [Fact]
        public async Task TestXmlRootPath()
        {
            string tempXml = "test_nested.xml";
            await File.WriteAllTextAsync(tempXml, @"
<Response>
    <Status>Success</Status>
    <Data>
        <Product><ID>P1</ID></Product>
        <Product><ID>P2</ID></Product>
    </Data>
</Response>");

            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse($"CREATE CONNECTION x AS XML('{tempXml}', ROOT_PATH='Response.Data');"));
            var res = await ev.ExecuteQuery(Parse("SELECT * FROM x;").Statements[0]).FirstAsync();
            
            Assert.Equal(2, res.Rows.Count);
            Assert.Equal("P2", res.Rows[1]["ID"]?.ToString());

            try { if (File.Exists(tempXml)) File.Delete(tempXml); } catch (IOException) { }
        }

        private static Script Parse(string source)
        {
            var lexer = new Lexer(source);
            return new Parser(lexer.Tokenize()).Parse();
        }
    }
}
