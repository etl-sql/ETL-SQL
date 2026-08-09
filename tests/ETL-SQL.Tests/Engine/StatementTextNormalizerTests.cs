using ETL_SQL.Core.Profiling;
using Xunit;

namespace ETL_SQL.Tests.Engine;

/// <summary>
/// Statement text is about to become durable history read by operators who are a different principal
/// from whoever ran the script. The invariant these protect is that the recorded text says which
/// statement ran and never what data it ran on.
/// </summary>
public class StatementTextNormalizerTests
{
    // ── The security boundary ────────────────────────────────────────────────────

    [Fact]
    public void StringLiteralsAreReplaced()
    {
        var text = StatementTextNormalizer.Normalize(
            "SELECT * FROM Users WHERE email = 'alice@corp.local'");

        Assert.DoesNotContain("alice@corp.local", text);
        Assert.Contains("'?'", text);
        Assert.Contains("email", text);
    }

    /// <summary>The literal case that matters most: a credential inline in a connection string.</summary>
    [Fact]
    public void ACredentialInAConnectionStringDoesNotSurvive()
    {
        var text = StatementTextNormalizer.Normalize(
            "CREATE CONNECTION db AS MSSQL('Server=x;User Id=sa;Password=hunter2;')");

        Assert.DoesNotContain("hunter2", text);
        Assert.DoesNotContain("Password=", text);
    }

    /// <summary>An escaped quote must not end the literal early and leak the rest.</summary>
    [Fact]
    public void AnEscapedQuoteDoesNotTerminateTheLiteralEarly()
    {
        var text = StatementTextNormalizer.Normalize(
            "SELECT * FROM t WHERE name = 'O''Brien secret' AND x = 1");

        Assert.DoesNotContain("Brien", text);
        Assert.DoesNotContain("secret", text);
    }

    [Fact]
    public void NumericLiteralsAreReplaced()
    {
        var text = StatementTextNormalizer.Normalize(
            "SELECT * FROM Accounts WHERE balance > 15000.75 AND id = 4815162342");

        Assert.DoesNotContain("15000.75", text);
        Assert.DoesNotContain("4815162342", text);
        Assert.Contains("balance", text);
    }

    [Fact]
    public void ExponentNotationLeavesNoResidue()
    {
        var text = StatementTextNormalizer.Normalize("SELECT * FROM t WHERE x > 1.5e10");

        Assert.DoesNotContain("1.5", text);
        Assert.DoesNotContain("e10", text);
    }

    /// <summary>Comments are free text and can hold anything someone pasted.</summary>
    [Theory]
    [InlineData("SELECT 1 -- password is hunter2")]
    [InlineData("SELECT 1 /* password is hunter2 */")]
    [InlineData("/* password is hunter2 */ SELECT 1")]
    public void CommentBodiesAreDropped(string sql)
    {
        var text = StatementTextNormalizer.Normalize(sql);

        Assert.DoesNotContain("hunter2", text);
        Assert.DoesNotContain("password", text);
    }

    [Fact]
    public void AnUnterminatedCommentDoesNotLeakTheRemainder()
    {
        var text = StatementTextNormalizer.Normalize("SELECT 1 /* trailing hunter2");

        Assert.DoesNotContain("hunter2", text);
    }

    // ── What must survive, or the record is useless ──────────────────────────────

    [Fact]
    public void TheShapeOfTheStatementIsStillRecognisable()
    {
        var text = StatementTextNormalizer.Normalize(
            "INSERT INTO hospital.dbo.Patient (name, dob) SELECT name, dob FROM pats.FILE WHERE gender = 'F'");

        Assert.Contains("INSERT INTO hospital.dbo.Patient", text);
        Assert.Contains("FROM pats.FILE", text);
    }

    /// <summary>A quoted or bracketed name is schema, not data — stripping it hides which table ran.</summary>
    [Theory]
    [InlineData("SELECT \"First Name\" FROM t", "First Name")]
    [InlineData("SELECT [Order Total] FROM t", "Order Total")]
    public void QuotedAndBracketedIdentifiersArePreserved(string sql, string identifier)
    {
        Assert.Contains(identifier, StatementTextNormalizer.Normalize(sql));
    }

    /// <summary>A digit inside a name is part of the name, not a literal.</summary>
    [Fact]
    public void DigitsWithinAnIdentifierAreNotTreatedAsLiterals()
    {
        var text = StatementTextNormalizer.Normalize("SELECT col2, x1y FROM table3");

        Assert.Contains("col2", text);
        Assert.Contains("x1y", text);
        Assert.Contains("table3", text);
    }

    // ── Payload size, which is the other half of the job ─────────────────────────

    /// <summary>The envelope is parsed as one line, so a multi-line statement must collapse.</summary>
    [Fact]
    public void WhitespaceCollapsesToASingleLine()
    {
        var text = StatementTextNormalizer.Normalize("SELECT a,\n       b\r\nFROM   t");

        Assert.DoesNotContain('\n', text);
        Assert.DoesNotContain('\r', text);
        Assert.DoesNotContain("  ", text);
    }

    [Fact]
    public void OverlongTextIsCappedAndSaysSo()
    {
        var text = StatementTextNormalizer.Normalize("SELECT " + new string('a', 5000), maxLength: 100);

        Assert.True(text.Length <= 100 + StatementTextNormalizer.TruncationMarker.Length);
        Assert.EndsWith(StatementTextNormalizer.TruncationMarker, text);
    }

    /// <summary>A very long literal must be replaced, not merely truncated mid-value.</summary>
    [Fact]
    public void ALongLiteralIsReplacedRatherThanTruncated()
    {
        var secret = new string('s', 4000);
        var text = StatementTextNormalizer.Normalize($"SELECT * FROM t WHERE k = '{secret}'");

        Assert.DoesNotContain("ssss", text);
        Assert.Contains("'?'", text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyInputYieldsEmptyOutput(string? sql) =>
        Assert.Equal(string.Empty, StatementTextNormalizer.Normalize(sql));
}
