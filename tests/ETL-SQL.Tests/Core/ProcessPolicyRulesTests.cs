using ETL_SQL.Core.Common;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Tests.Core;

public sealed class ProcessPolicyRulesTests
{
    [Theory]
    [InlineData("postgres", "postgres:15", true)]           // tagless repo matches any tag
    [InlineData("postgres", "postgres", true)]
    [InlineData("postgres:15", "postgres:15", true)]        // exact
    [InlineData("postgres:15", "postgres:16", false)]       // tag mismatch
    [InlineData("postgres", "postgres-alpine", false)]      // not the same repo
    [InlineData("myreg.io/*", "myreg.io/team/app:1", true)] // registry prefix
    [InlineData("myreg.io/*", "other.io/app:1", false)]
    [InlineData("*", "anything:latest", true)]              // allow all
    [InlineData("registry:5000/app", "registry:5000/app:2", true)]  // port not mistaken for tag
    [InlineData("registry:5000/app:1", "registry:5000/app:2", false)]
    public void DockerImageMatches_HandlesTagsPortsAndWildcards(string pattern, string image, bool expected)
    {
        Assert.Equal(expected, ProcessPolicyRules.DockerImageMatches(pattern, image));
    }

    [Theory]
    [InlineData("USE DOCKER('postgres:15', '--privileged');")]
    [InlineData("USE DOCKER('postgres:15') WITH (NETWORK = 'host');")]
    [InlineData("USE DOCKER('postgres:15') AS pg WITH (VOLUME = '/:/host');")]
    public void DockerGrammar_RejectsEscapeOrientedRuntimeOptions(string sql)
    {
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();

        Assert.Contains(script.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
