using ETL_SQL.Core;
using System.Linq;
using Xunit;

namespace ETL_SQL.Tests.Statements;

/// <summary>
/// File and directory statements used to parse any <c>WITH (KEY = value)</c> and drop the keys they
/// did not know, so <c>ENCRYPT FILE ... WITH (DELETE_SOURCE = ON)</c> or
/// <c>COMPRESS FILE ... WITH (FORMAT = 'GZIP')</c> ran as if the option were absent. An unknown key
/// is now a syntax error that names the keys the statement accepts (ROADMAP FO02).
/// </summary>
[Trait("CompatBreak", "0.22.0")]
public sealed class FileOperationWithOptionTests
{
    private static string[] Errors(string sql) =>
        new Parser(new Lexer(sql).Tokenize(), sql).Parse().Diagnostics.Select(d => d.Message).ToArray();

    [Theory]
    [InlineData("ENCRYPT FILE 'a.csv' TO 'a.enc' PASSWORD 'p' WITH (DELETE_SOURCE = ON);", "ENCRYPT FILE", "DELETE_SOURCE", "OVERWRITE")]
    [InlineData("ENCRYPT FILE 'a.csv' TO 'a.enc' PASSWORD 'p' WITH (DATE_SUFFIX = 'yyyyMMdd');", "ENCRYPT FILE", "DATE_SUFFIX", "OVERWRITE")]
    [InlineData("COMPRESS FILE 'a.csv' TO 'a.gz' WITH (FORMAT = 'GZIP');", "COMPRESS FILE", "FORMAT", "OVERWRITE")]
    [InlineData("COPY FILE 'a' TO 'b' WITH (OVERWRITE = ON, OVERWITE = ON);", "COPY FILE", "OVERWITE", "OVERWRITE, DATE_SUFFIX, SUFFIX_SEPARATOR")]
    [InlineData("COPY DIRECTORY 'a' TO 'b' WITH (RECURSE = ON);", "COPY DIRECTORY", "RECURSE", "OVERWRITE")]
    [InlineData("COPY DIRECTORY 'a' TO 'b' WITH (RECURSIVE = OFF);", "COPY DIRECTORY", "RECURSIVE", "OVERWRITE")]
    [InlineData("ENCRYPT DIRECTORY 'a' TO 'b' PASSWORD 'p' WITH (KEYFILE = 'k');", "ENCRYPT DIRECTORY", "KEYFILE", "OVERWRITE")]
    [InlineData("DELETE DIRECTORY_CONTENTS 'a' WITH (FORCE = ON);", "DELETE DIRECTORY_CONTENTS", "FORCE", "RECURSIVE")]
    [InlineData("CREATE DIRECTORY 'a' WITH (RECURSIVE = ON);", "CREATE DIRECTORY", "RECURSIVE", "OVERWRITE")]
    [InlineData("SEND FILE 'a' TO 'b' AT ftp WITH (DELETE_SOURCE = ON);", "SEND FILE", "DELETE_SOURCE", "OVERWRITE")]
    [InlineData("RECEIVE FILE FROM 'a' TO 'b' AT ftp WITH (OVERWRITE = ON, RETRIES = 3);", "RECEIVE FILE", "RETRIES", "OVERWRITE")]
    [InlineData("CONVERT FILE ENCODING 'a' TO 'b' WITH (FROM_ENCODING = 'UTF8', TO_ENCODING = 'ANSI', BOM = ON);", "CONVERT FILE ENCODING", "BOM", "FROM_ENCODING, TO_ENCODING, OVERWRITE")]
    [InlineData("SPLIT FILE 'a' TO 'b' WITH (LIMIT_TYPE = 'ROWS', LIMIT_VALUE = 2, HEADER = ON);", "SPLIT FILE", "HEADER", "LIMIT_TYPE, LIMIT_VALUE, PREFIX, OVERWRITE")]
    [InlineData("MERGE FILES 'a*' TO 'b' WITH (DELIMITER = ',');", "MERGE FILES", "DELIMITER", "HEADER, OVERWRITE")]
    [InlineData("SYNC DIRECTORY 'a' TO 'b' WITH (MIRROR = ON);", "SYNC DIRECTORY", "MIRROR", "DELETE_EXTRA, OVERWRITE, RECURSIVE")]
    public void UnknownOption_IsASyntaxErrorNamingTheAcceptedKeys(string sql, string statement, string key, string accepted)
    {
        var error = Assert.Single(Errors(sql));
        Assert.Contains($"{statement} has no WITH option '{key}'", error);
        Assert.Contains($"Accepted options: {accepted}.", error);
    }

    [Theory]
    [InlineData("COPY FILE 'a' TO DIRECTORY 'b' WITH (overwrite = ON, Date_Suffix = 'yyyyMMdd', SUFFIX_SEPARATOR = '-');")]
    [InlineData("ENCRYPT DIRECTORY 'a' TO 'b' PASSWORD 'p' WITH (OVERWRITE = ON);")]
    [InlineData("ENCRYPT FILE 'a' TO 'b' PGP_KEY 'pub.asc';")]
    [InlineData("ENCRYPT FILE 'a' TO 'b' KEYFILE 'id_rsa.pub.pem';")]
    [InlineData("DECRYPT FILE 'a' TO 'b' PGP_KEY 'priv.asc' PASSWORD 'phrase';")]
    [InlineData("DECRYPT FILE 'a' TO 'b' KEYFILE 'id_rsa' PASSWORD('phrase') WITH (OVERWRITE = OFF);")]
    [InlineData("DELETE DIRECTORY_CONTENTS 'a' WITH(RECURSIVE=ON);")]
    [InlineData("CREATE DIRECTORY 'a' WITH (OVERWRITE = ON);")]
    [InlineData("SEND FILE 'a' TO 'b' AT ftp WITH (OVERWRITE = TRUE);")]
    [InlineData("CONVERT FILE ENCODING 'a' TO 'b' WITH (FROM_ENCODING = 'UTF8', TO_ENCODING = 'ANSI', OVERWRITE = ON);")]
    [InlineData("SPLIT FILE 'a' TO 'b' WITH (LIMIT_TYPE = 'ROWS', LIMIT_VALUE = 2, PREFIX = 'p_', OVERWRITE = ON);")]
    [InlineData("MERGE FILES 'a*' TO 'b' WITH (HEADER = ON, OVERWRITE = ON);")]
    [InlineData("SYNC DIRECTORY 'a' TO 'b' WITH (DELETE_EXTRA = ON, OVERWRITE = ON, RECURSIVE = OFF);")]
    public void KnownOptions_StillParse(string sql) => Assert.Empty(Errors(sql));

    /// <summary>
    /// The handler reads PASSWORD, KEYFILE and PGP_KEY only for ENCRYPT and DECRYPT FILE, picks one
    /// encryption method, and encrypts directories with a password only. A clause the run would drop,
    /// or that would fall back to the session password, is a syntax error.
    /// </summary>
    [Theory]
    [InlineData("COPY FILE 'a' TO 'b' PASSWORD 'p';", "COPY FILE does not take PASSWORD, KEYFILE or PGP_KEY")]
    [InlineData("COMPRESS FILE 'a' TO 'b.zip' KEYFILE 'k';", "COMPRESS FILE does not take PASSWORD, KEYFILE or PGP_KEY")]
    [InlineData("ENCRYPT FILE 'a' TO 'b' KEYFILE 'k' PGP_KEY 'p';", "ENCRYPT FILE takes KEYFILE (an SSH RSA key) or PGP_KEY, not both")]
    [InlineData("ENCRYPT FILE 'a' TO 'b' PGP_KEY 'pub.asc' PASSWORD 'p';", "ENCRYPT FILE with PGP_KEY encrypts to a public key and takes no PASSWORD")]
    [InlineData("ENCRYPT FILE 'a' TO 'b' PASSWORD 'p' KEYFILE 'k';", "ENCRYPT FILE with KEYFILE encrypts to a public key and takes no PASSWORD")]
    [InlineData("ENCRYPT DIRECTORY 'a' TO 'b' KEYFILE 'k';", "ENCRYPT DIRECTORY does not support KEYFILE or PGP_KEY")]
    [InlineData("DECRYPT DIRECTORY 'a' TO 'b' PGP_KEY 'k';", "DECRYPT DIRECTORY does not support KEYFILE or PGP_KEY")]
    [InlineData("COPY DIRECTORY 'a' TO 'b' PASSWORD 'p';", "COPY DIRECTORY does not take PASSWORD")]
    [InlineData("COPY DIRECTORY 'a' TO 'b' RECURSIVE OFF;", "COPY DIRECTORY does not take RECURSIVE")]
    public void IgnoredClause_IsASyntaxError(string sql, string expected) =>
        Assert.Contains(expected, Assert.Single(Errors(sql)));
}
