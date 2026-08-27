using System.IO;
using System.Linq;
using ETL_SQL.Core.Metadata;
using Xunit;

namespace ETL_SQL.LanguageServer.Tests;

public class SnippetLibraryTests
{
    // ── Loading ───────────────────────────────────────────────────────────────

    [Fact]
    public void Load_ReturnsAllSnippets()
    {
        var snippets = SnippetLibrary.Instance.GetAll();
        Assert.Equal(79, snippets.Count);
    }

    [Fact]
    public void Load_AllSnippetsHaveTriggerStartingWithDollar()
    {
        foreach (var s in SnippetLibrary.Instance.GetAll())
            Assert.StartsWith("$", s.Trigger);
    }

    [Fact]
    public void Load_AllSnippetsHaveNonEmptyBodies()
    {
        foreach (var s in SnippetLibrary.Instance.GetAll())
        {
            Assert.False(string.IsNullOrWhiteSpace(s.TuiBody), $"{s.Trigger} TuiBody is empty");
            Assert.False(string.IsNullOrWhiteSpace(s.LspBody), $"{s.Trigger} LspBody is empty");
        }
    }

    [Theory]
    [InlineData("$bar")]
    [InlineData("$line")]
    [InlineData("$kpi")]
    [InlineData("$pie")]
    [InlineData("$tbl")]
    [InlineData("$map")]
    [InlineData("$dataset")]
    [InlineData("$export-dataset")]
    [InlineData("$publish-dataset")]
    [InlineData("$proc")]
    [InlineData("$mssql")]
    [InlineData("$postgres")]
    [InlineData("$oracle")]
    [InlineData("$csv")]
    [InlineData("$excel")]
    [InlineData("$parquet")]
    [InlineData("$json")]
    [InlineData("$donut")]
    [InlineData("$hbar")]
    [InlineData("$gauge")]
    [InlineData("$scatter")]
    [InlineData("$heatmap")]
    [InlineData("$radar")]
    [InlineData("$funnel")]
    [InlineData("$waterfall")]
    [InlineData("$treemap")]
    [InlineData("$boxplot")]
    [InlineData("$sftp")]
    [InlineData("$ftp")]
    [InlineData("$blob")]
    [InlineData("$api")]
    [InlineData("$smtp")]
    [InlineData("$snowflake")]
    [InlineData("$bigquery")]
    [InlineData("$odbc")]
    [InlineData("$avro")]
    [InlineData("$xml")]
    [InlineData("$view")]
    [InlineData("$func")]
    [InlineData("$job")]
    [InlineData("$tag_header")]
    [InlineData("$tag_report")]
    [InlineData("$tag_table")]
    [InlineData("$tag_column")]
    [InlineData("$insert_tag")]
    [InlineData("$cascade")]
    [InlineData("$advanced_chart")]
    [InlineData("$html_visual")]
    [InlineData("$transform")]
    [InlineData("$transform-rolling")]
    [InlineData("$transform-mom")]
    [InlineData("$transform-share")]
    [InlineData("$transform-top-n")]
    [InlineData("$transform-fill-dates")]
    [InlineData("$transform-pivot")]
    [InlineData("$transform-interpolate")]
    [InlineData("$transform-normalize")]
    [InlineData("$transform-dedup")]
    public void Load_ExpectedTriggerExists(string trigger)
    {
        var snippets = SnippetLibrary.Instance.GetAll();
        Assert.Contains(snippets, s => s.Trigger == trigger);
    }

    // ── «» → ${N:text} conversion ─────────────────────────────────────────────

    [Fact]
    public void ConvertToLspTabStops_SinglePlaceholder_BecomesTabStop1()
    {
        var result = SnippetLibrary.ConvertToLspTabStops("SELECT «col» FROM t");
        Assert.Equal("SELECT ${1:col} FROM t", result);
    }

    [Fact]
    public void ConvertToLspTabStops_MultiplePlaceholders_NumberedInOrder()
    {
        var result = SnippetLibrary.ConvertToLspTabStops("CREATE VISUAL «Name» AS BAR (X = «cat», Y = «val»)");
        Assert.Equal("CREATE VISUAL ${1:Name} AS BAR (X = ${2:cat}, Y = ${3:val})", result);
    }

    [Fact]
    public void ConvertToLspTabStops_NoPlaceholders_BodyUnchanged()
    {
        var body = "SELECT 1 AS n;";
        Assert.Equal(body, SnippetLibrary.ConvertToLspTabStops(body));
    }

    [Fact]
    public void ConvertToLspTabStops_PreservesPlaceholderText()
    {
        var result = SnippetLibrary.ConvertToLspTabStops("AS MSSQL(SERVER = '«server»')");
        Assert.Equal("AS MSSQL(SERVER = '${1:server}')", result);
    }

    // ── GetByPrefix filtering ─────────────────────────────────────────────────

    [Fact]
    public void GetByPrefix_FullTrigger_ReturnsSingleMatch()
    {
        var matches = SnippetLibrary.Instance.GetByPrefix("$bar").ToList();
        Assert.Single(matches);
        Assert.Equal("$bar", matches[0].Trigger);
    }

    [Fact]
    public void GetByPrefix_PartialPrefix_ReturnsAllMatching()
    {
        var matches = SnippetLibrary.Instance.GetByPrefix("$p").ToList();
        Assert.True(matches.Count >= 2); // $pie, $postgres, $parquet, $proc
        Assert.All(matches, s => Assert.StartsWith("$p", s.Trigger));
    }

    [Fact]
    public void GetByPrefix_JustDollar_ReturnsAll()
    {
        var matches = SnippetLibrary.Instance.GetByPrefix("$").ToList();
        Assert.Equal(79, matches.Count);
    }

    [Fact]
    public void GetByPrefix_NoMatch_ReturnsEmpty()
    {
        var matches = SnippetLibrary.Instance.GetByPrefix("$zzz").ToList();
        Assert.Empty(matches);
    }

    // ── Statement-start gate (ParseSnippet) ───────────────────────────────────

    [Fact]
    public void ParseSnippet_ValidFrontmatter_Parsed()
    {
        const string md = "---\ntrigger: $test\nlabel: Test snippet\ndescription: A test\n---\nSELECT «col» FROM t;\n";
        var def = SnippetLibrary.ParseSnippet(md);

        Assert.NotNull(def);
        Assert.Equal("$test", def!.Trigger);
        Assert.Equal("Test snippet", def.Label);
        Assert.Equal("A test", def.Description);
        Assert.Contains("«col»", def.TuiBody);
        Assert.Contains("${1:col}", def.LspBody);
    }

    [Fact]
    public void ParseSnippet_MissingTrigger_ReturnsNull()
    {
        const string md = "---\nlabel: No trigger\n---\nbody\n";
        Assert.Null(SnippetLibrary.ParseSnippet(md));
    }

    [Fact]
    public void ParseSnippet_MissingFrontmatter_ReturnsNull()
    {
        Assert.Null(SnippetLibrary.ParseSnippet("SELECT 1;"));
    }

    // ── User snippets (disk-based) ────────────────────────────────────────────

    [Fact]
    public void UserSnippets_LoadedFromDirectory_AppendedToBuiltIns()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "custom.md"),
                "---\ntrigger: $mysnippet\nlabel: My Custom\ndescription: Test\n---\nSELECT «col» FROM «tbl»;\n");

            var lib = new SnippetLibrary(dir);
            Assert.Contains(lib.GetAll(), s => s.Trigger == "$mysnippet");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void UserSnippets_SameTrigger_OverridesBuiltIn()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "bar.md"),
                "---\ntrigger: $bar\nlabel: Custom Bar\ndescription: Override\n---\nCUSTOM BODY;\n");

            var lib = new SnippetLibrary(dir);
            var bar = lib.GetAll().Single(s => s.Trigger == "$bar");
            Assert.Equal("Custom Bar", bar.Label);
            Assert.Equal("CUSTOM BODY;", bar.TuiBody);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void UserSnippets_MissingDirectory_LoadsBuiltInsOnly()
    {
        var lib = new SnippetLibrary(@"C:\nonexistent\path\that\does\not\exist");
        Assert.Equal(79, lib.GetAll().Count);
    }

    [Fact]
    public void UserSnippets_InvalidMarkdown_Skipped()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "bad.md"), "not valid frontmatter at all");
            File.WriteAllText(Path.Combine(dir, "good.md"),
                "---\ntrigger: $good\nlabel: Good\ndescription: ok\n---\nbody;\n");

            var lib = new SnippetLibrary(dir);
            Assert.Contains(lib.GetAll(), s => s.Trigger == "$good");
            Assert.DoesNotContain(lib.GetAll(), s => s.Trigger == "$bad");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ── Snippet body content spot-checks ─────────────────────────────────────

    [Fact]
    public void BarSnippet_ContainsCorrectKeywords()
    {
        var bar = SnippetLibrary.Instance.GetAll().First(s => s.Trigger == "$bar");
        Assert.Contains("CREATE VISUAL", bar.TuiBody);
        Assert.Contains("AS BAR", bar.TuiBody);
        Assert.Contains("MAPPINGS", bar.TuiBody);
    }

    [Fact]
    public void MssqlSnippet_ContainsMssqlConnector()
    {
        var mssql = SnippetLibrary.Instance.GetAll().First(s => s.Trigger == "$mssql");
        Assert.Contains("AS MSSQL(", mssql.TuiBody);
        Assert.Contains("SERVER", mssql.TuiBody);
        Assert.Contains("DATABASE", mssql.TuiBody);
    }

    [Fact]
    public void DatasetSnippet_ContainsAmpersand()
    {
        var ds = SnippetLibrary.Instance.GetAll().First(s => s.Trigger == "$dataset");
        Assert.Contains("CREATE DATASET &", ds.TuiBody);
        Assert.Contains("TTL =", ds.TuiBody);
    }
}
