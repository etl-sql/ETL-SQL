using ETL_SQL.Core;
using ETL_SQL.Core.Governance;
using Xunit;

namespace ETL_SQL.Tests.Governance;

[Trait("CompatBreak", "0.23")]
public class SharedConnectionReferenceScanTests
{
    [Theory]
    [InlineData("CREATE CONNECTION allowed AS MOCKDB('SHARED:allowed'); CREATE CONNECTION hidden AS MOCKDB(@target);")]
    [InlineData("DECLARE @unused VARCHAR = 'SHARED:allowed'; CREATE CONNECTION hidden AS MOCKDB(@target);")]
    [InlineData("RUN SCRIPT 'child.etlsql';")]
    [InlineData("CREATE CONNECTION source AS FLATFILE(@path, HEADER=ON);")]
    public void UnresolvedDependenciesFailClosed(string script)
    {
        Assert.Empty(new Parser(new Lexer(script).Tokenize()).Parse().Diagnostics);
        Assert.False(SharedConnectionReferenceScan.ExtractAliases(script).IsResolved);
    }

    [Fact]
    public void ProcedureBodyContributesConnectionDependencies()
    {
        const string script = "CREATE PROCEDURE LoadData AS BEGIN CREATE CONNECTION hidden AS MOCKDB('SHARED:restricted'); END; EXECUTE LoadData;";
        Assert.Empty(new Parser(new Lexer(script).Tokenize()).Parse().Diagnostics);
        var result = SharedConnectionReferenceScan.ExtractAliases(script);
        Assert.True(result.IsResolved);
        Assert.Equal(["restricted"], result.Aliases);
    }
}
