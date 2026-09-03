using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Analysis.Services;

/// <summary>
/// One <c>CREATE DATASET</c> as the script declares it.
/// </summary>
/// <param name="Encryption">
/// none | machine | password | keyfile. Reported, never edited here: a password or a key file is a
/// credential, and a surface that rewrites the clause holding one has read it, moved it through a
/// request, and written it back for no reason the author asked for.
/// </param>
/// <param name="Lifecycle">
/// The <c>REFRESH</c> / <c>EXPORT</c> / <c>PUBLISH</c> statements this script already declares for
/// this dataset, in written order.
/// </param>
public sealed record ScriptDataset(
    string Name,
    string Access,
    string? Ttl,
    bool Compress,
    string Encryption,
    int Line,
    IReadOnlyList<DatasetLifecycleStatement> Lifecycle);

/// <param name="Kind">refresh | export | publish</param>
/// <param name="Detail">The path an export writes or a publish reads, when it names one.</param>
public sealed record DatasetLifecycleStatement(string Kind, string Detail, int Line);

public sealed record ScriptDatasetLifecycle(
    bool Parsed,
    string? Error,
    IReadOnlyList<ScriptDataset> Datasets)
{
    public static ScriptDatasetLifecycle Failed(string error) => new(false, error, []);
}

public sealed record DatasetEditResult(bool Applied, string Script, string? Error = null)
{
    public static DatasetEditResult Ok(string script) => new(true, script);
    public static DatasetEditResult Refused(string script, string error) => new(false, script, error);
}

/// <summary>
/// Reads and edits the lifecycle a script declares for its datasets: who may see one, how long it
/// lives, and the refresh, export, and publish steps the script performs on it.
///
/// <para><b>Every edit is a span, never a regeneration.</b> A <c>CREATE DATASET</c> carries clauses
/// no authoring model represents — <c>COMPRESS</c>, <c>ENCRYPT</c>, <c>PASSWORD</c>,
/// <c>KEYFILE</c> — and the way to guarantee they survive an edit is to never write the bytes that
/// hold them. Changing the access level replaces the <c>ACCESS</c> clause and nothing else; there is
/// no path here that rewrites a whole statement.</para>
///
/// <para><b>Encryption is reported and not authored.</b> Setting it means writing a credential into
/// the script through a request that carries it, which is a journey a password has no reason to
/// make. The panel says what the script declares and leaves the author to write the clause where
/// they can see it.</para>
///
/// <para><b>Refresh, export and publish are statements, not buttons.</b> Writing
/// <c>REFRESH DATASET &amp;sales;</c> into the script is a durable declaration that runs every time
/// the script does; pressing a button in an authoring tool refreshes one copy once, and leaves no
/// trace of why. Both are useful, and they are different things — this service writes the statement,
/// and the host offers "do it now" separately against the registered dataset.</para>
/// </summary>
public sealed class ScriptDatasetLifecycleService
{
    public static IReadOnlyList<string> AccessLevels { get; } = ["PRIVATE", "PUBLIC"];

    /// <summary>The datasets this script declares, in script order.</summary>
    public ScriptDatasetLifecycle Read(string? scriptText)
    {
        var source = scriptText ?? string.Empty;
        if (!ScriptTextEditing.TryParse(source, out var ast, out var parseError))
            return ScriptDatasetLifecycle.Failed(parseError);

        var lifecycle = new Dictionary<string, List<DatasetLifecycleStatement>>(StringComparer.OrdinalIgnoreCase);
        void Record(string name, DatasetLifecycleStatement statement)
        {
            if (!lifecycle.TryGetValue(name, out var list))
                lifecycle[name] = list = [];
            list.Add(statement);
        }

        foreach (var statement in ScriptTextEditing.Flatten(ast.Statements))
        {
            switch (statement)
            {
                case RefreshDatasetStatement refresh:
                    Record(Normalize(refresh.DatasetName), new DatasetLifecycleStatement("refresh", string.Empty, refresh.Line));
                    break;
                case ExportDatasetStatement export:
                    Record(Normalize(export.DatasetName), new DatasetLifecycleStatement("export", export.TargetPath, export.Line));
                    break;
                case PublishDatasetStatement publish:
                    Record(Normalize(publish.DatasetName), new DatasetLifecycleStatement("publish", publish.SourcePath, publish.Line));
                    break;
            }
        }

        var datasets = ScriptTextEditing.Flatten(ast.Statements)
            .OfType<CreateDatasetStatement>()
            .Select(statement => new ScriptDataset(
                Normalize(statement.TempTableName),
                statement.AccessLevel.ToString().ToUpperInvariant(),
                statement.Ttl,
                statement.Compress,
                statement.EncryptionMode switch
                {
                    DatasetEncryptionMode.MachineBound => "machine",
                    DatasetEncryptionMode.Password => "password",
                    DatasetEncryptionMode.KeyFile => "keyfile",
                    _ => "none",
                },
                statement.Line,
                lifecycle.TryGetValue(Normalize(statement.TempTableName), out var found) ? found : []))
            .ToArray();

        return new ScriptDatasetLifecycle(true, null, datasets);
    }

    /// <summary>Sets a dataset's declared access level, editing the <c>ACCESS</c> clause alone.</summary>
    public DatasetEditResult SetAccess(string? scriptText, string name, string access)
    {
        var source = scriptText ?? string.Empty;
        var level = (access ?? string.Empty).Trim().ToUpperInvariant();
        if (!AccessLevels.Contains(level))
            return DatasetEditResult.Refused(source, $"'{access}' is not an access level. Use PUBLIC or PRIVATE.");

        return EditClause(source, name, "ACCESS", level == "PRIVATE" ? null : $"ACCESS {level}",
            // PRIVATE is what a dataset with no ACCESS clause already is, so writing the word adds
            // nothing the reader did not know and removing it changes nothing about the dataset.
            allowRemoval: true);
    }

    /// <summary>Sets or clears a dataset's TTL, editing the <c>TTL</c> clause alone.</summary>
    public DatasetEditResult SetTtl(string? scriptText, string name, string? ttl)
    {
        var source = scriptText ?? string.Empty;
        var value = (ttl ?? string.Empty).Trim().Trim('\'');
        return EditClause(source, name, "TTL",
            value.Length == 0 ? null : $"TTL = '{value.Replace("'", "''")}'",
            allowRemoval: true);
    }

    /// <summary>
    /// Writes a lifecycle statement for a dataset, after the statement that creates it.
    ///
    /// <para>After, always: refreshing, exporting, or publishing a dataset the script has not
    /// created yet is a statement about nothing, and the run says so only when it runs.</para>
    /// </summary>
    public DatasetEditResult AddLifecycleStatement(
        string? scriptText,
        string name,
        string kind,
        string? path,
        string? encryption,
        string? secret,
        string? folder,
        string? access)
    {
        var source = scriptText ?? string.Empty;
        if (!ScriptTextEditing.TryParse(source, out var ast, out var parseError))
            return DatasetEditResult.Refused(source, parseError);

        var dataset = Normalize(name);
        var statement = ScriptTextEditing.Flatten(ast.Statements)
            .OfType<CreateDatasetStatement>()
            .FirstOrDefault(item => string.Equals(Normalize(item.TempTableName), dataset, StringComparison.OrdinalIgnoreCase));
        if (statement is null)
            return DatasetEditResult.Refused(source, $"This script creates no dataset called '{dataset}'.");

        string text;
        switch ((kind ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "refresh":
                text = $"REFRESH DATASET {dataset};";
                break;

            case "export":
                {
                    if (string.IsNullOrWhiteSpace(path))
                        return DatasetEditResult.Refused(source, "An export needs the file it writes.");
                    var transport = RenderTransport(encryption, secret);
                    if (transport is null)
                        return DatasetEditResult.Refused(
                            source,
                            "An export needs a transport credential: ENCRYPT = PASSWORD with a password, or ENCRYPT = KEYFILE with a key file. "
                            + "The exported file leaves this machine, so it cannot carry the at-rest key that only this machine has.");
                    text = $"EXPORT DATASET {dataset} TO '{Escape(path!)}' {transport};";
                    break;
                }

            case "publish":
                {
                    if (string.IsNullOrWhiteSpace(path))
                        return DatasetEditResult.Refused(source, "A publish needs the exported file it reads.");
                    var transport = RenderTransport(encryption, secret);
                    if (transport is null)
                        return DatasetEditResult.Refused(
                            source,
                            "A publish needs the transport credential the file was exported with: ENCRYPT = PASSWORD or ENCRYPT = KEYFILE.");
                    var into = string.IsNullOrWhiteSpace(folder) ? string.Empty : $" INTO '{Escape(folder!)}'";
                    var level = (access ?? string.Empty).Trim().ToUpperInvariant();
                    if (level.Length > 0 && !AccessLevels.Contains(level))
                        return DatasetEditResult.Refused(source, $"'{access}' is not an access level. Use PUBLIC or PRIVATE.");
                    var accessClause = level.Length == 0 ? string.Empty : $" ACCESS {level}";
                    text = $"PUBLISH DATASET {dataset} FROM '{Escape(path!)}'{into}{accessClause} {transport};";
                    break;
                }

            default:
                return DatasetEditResult.Refused(source, $"'{kind}' is not a dataset lifecycle step.");
        }

        var insertAt = ScriptTextEditing.EndOfLine(source, statement.EndOffset);
        var lineEnding = ScriptTextEditing.DetectLineEnding(source);
        var prefix = ScriptTextEditing.NeedsBlankLineBefore(source, insertAt) ? lineEnding : string.Empty;
        return Commit(source, ScriptTextEditing.Splice(source, insertAt, insertAt, prefix + text + lineEnding));
    }

    // ── Clause editing ───────────────────────────────────────────────────────

    /// <summary>
    /// Replaces, inserts, or removes one option clause of a <c>CREATE DATASET</c>, touching nothing
    /// else in the statement. The options before <c>AS</c> are order-free and none of them has an AST
    /// node of its own, so the clause is located by relexing the statement's own bytes.
    /// </summary>
    private static DatasetEditResult EditClause(string source, string name, string keyword, string? clause, bool allowRemoval)
    {
        if (!ScriptTextEditing.TryParse(source, out var ast, out var parseError))
            return DatasetEditResult.Refused(source, parseError);

        var dataset = Normalize(name);
        var statement = ScriptTextEditing.Flatten(ast.Statements)
            .OfType<CreateDatasetStatement>()
            .FirstOrDefault(item => string.Equals(Normalize(item.TempTableName), dataset, StringComparison.OrdinalIgnoreCase));
        if (statement is null)
            return DatasetEditResult.Refused(source, $"This script creates no dataset called '{dataset}'.");

        var options = OptionTokens(source, statement);
        if (options.Count == 0)
            return DatasetEditResult.Refused(source, $"The declaration of '{dataset}' could not be read.");

        var existing = FindClause(options, keyword);
        if (existing is { } span)
        {
            var start = statement.StartOffset + span.Start;
            var end = statement.StartOffset + span.End;
            if (clause is null)
            {
                if (!allowRemoval) return DatasetEditResult.Ok(source);
                while (start > 0 && source[start - 1] == ' ') start--;
                return Commit(source, ScriptTextEditing.Splice(source, start, end, string.Empty));
            }
            return Commit(source, ScriptTextEditing.Splice(source, start, end, clause));
        }

        if (clause is null) return DatasetEditResult.Ok(source);

        // A new clause goes straight after the &name, which is the one position that is legal
        // whatever else the statement already declares.
        var insertAt = statement.StartOffset + options[0].EndOffset;
        return Commit(source, ScriptTextEditing.Splice(source, insertAt, insertAt, " " + clause));
    }

    /// <summary>
    /// The span of an existing option clause, relative to the statement: its keyword, an optional
    /// <c>=</c>, and its value.
    /// </summary>
    private static (int Start, int End)? FindClause(IReadOnlyList<Token> options, string keyword)
    {
        for (var index = 1; index < options.Count; index++)
        {
            if (!options[index].Value.Equals(keyword, StringComparison.OrdinalIgnoreCase)) continue;

            var last = options[index];
            var next = index + 1;
            if (next < options.Count && options[next].Type == TokenType.EQUALS)
            {
                last = options[next];
                next++;
            }
            if (next < options.Count) last = options[next];
            return (options[index].Offset, last.EndOffset);
        }
        return null;
    }

    /// <summary>The tokens from the <c>&amp;name</c> up to <c>AS</c>, with statement-relative offsets.</summary>
    private static IReadOnlyList<Token> OptionTokens(string source, CreateDatasetStatement statement)
    {
        if (statement.StartOffset < 0
            || statement.EndOffset <= statement.StartOffset
            || statement.EndOffset > source.Length)
        {
            return [];
        }

        List<Token> tokens;
        try
        {
            tokens = new Lexer(source[statement.StartOffset..statement.EndOffset]).Tokenize();
        }
        catch
        {
            return [];
        }

        var start = tokens.FindIndex(token => token.Type == TokenType.DATASET);
        if (start < 0 || start + 1 >= tokens.Count) return [];

        var options = new List<Token>();
        for (var index = start + 1; index < tokens.Count; index++)
        {
            if (tokens[index].Type == TokenType.AS) break;
            options.Add(tokens[index]);
        }
        return options;
    }

    /// <summary>
    /// The transport credential clause for an export or a publish, or null when none was supplied.
    ///
    /// <para>Required rather than defaulted. An exported file leaves the machine that wrote it, so it
    /// cannot carry the at-rest key that only that machine holds; writing the statement without a
    /// transport credential produces a file that cannot be published anywhere, and finding that out
    /// is a run away.</para>
    /// </summary>
    private static string? RenderTransport(string? encryption, string? secret)
    {
        var mode = (encryption ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(secret)) return null;

        return mode switch
        {
            "PASSWORD" => $"ENCRYPT = PASSWORD PASSWORD = '{Escape(secret!)}'",
            "KEYFILE" => $"ENCRYPT = KEYFILE KEYFILE = '{Escape(secret!)}'",
            _ => null,
        };
    }

    private static string Normalize(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        return trimmed.StartsWith('&') ? trimmed : "&" + trimmed;
    }

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static DatasetEditResult Commit(string original, string edited) =>
        ScriptTextEditing.TryParse(edited, out _, out var error)
            ? DatasetEditResult.Ok(edited)
            : DatasetEditResult.Refused(original, $"That change would not parse: {error}");
}
