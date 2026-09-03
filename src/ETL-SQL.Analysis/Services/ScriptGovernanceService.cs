using System.Text;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Quality;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Analysis.Services;

/// <summary>
/// Where a tag came from, which is also what decides whether it can be edited here.
/// </summary>
public static class GovernanceTagOrigin
{
    /// <summary>A <c>/* @key: value */</c> comment on the projected column that declares it.</summary>
    public const string Inline = "inline";

    /// <summary>An <c>INSERT TAG</c> / <c>UPDATE TAG</c> statement naming this table or column.</summary>
    public const string Statement = "statement";

    /// <summary>A script header tag, which the engine applies to every lineage entry that lacks it.</summary>
    public const string Script = "script";

    /// <summary>
    /// Inherited at run time from the source this column is read from. Never editable here: the
    /// value does not exist anywhere in this script, and writing it would be a copy that stops
    /// tracking the thing it copied.
    /// </summary>
    public const string Derived = "derived";
}

/// <summary>Where a new tag for a scope would be written.</summary>
public static class GovernanceWriteTarget
{
    /// <summary>A tag comment on the SELECT column that projects it.</summary>
    public const string Inline = "inline";

    /// <summary>An <c>INSERT TAG FOR TABLE …</c> statement.</summary>
    public const string Statement = "statement";

    /// <summary>The script's own header tag comment.</summary>
    public const string Header = "header";
}

/// <summary>
/// One tag on one scope.
/// </summary>
/// <param name="DerivedFrom">
/// For <see cref="GovernanceTagOrigin.Derived"/>, the <c>table.column</c> the value is inherited
/// from — said out loud so a reader can tell an inherited classification from one somebody chose.
/// </param>
public sealed record GovernanceTag(
    string Name,
    string Value,
    string Origin,
    string? DerivedFrom = null,
    int Line = 0,
    bool Known = true,
    string? Problem = null)
{
    /// <summary>Derived values are the script's to change at the source, not here.</summary>
    public bool Editable => Origin != GovernanceTagOrigin.Derived;
}

/// <summary>
/// One thing in the script that can carry governance metadata.
/// </summary>
/// <param name="Id">
/// Stable across a reprojection of the same script, because the panel edits by id: it is built from
/// the kind and the object's own name, never from a position, so inserting a statement above does
/// not renumber it.
/// </param>
/// <param name="Kind">script | table | temp | dataset | column</param>
/// <param name="Producer">
/// The pipeline task label that writes this object, when a labelled task does. Purely a grouping —
/// see the note on <see cref="ScriptGovernanceService"/> about why a task carries no tags of its own.
/// </param>
public sealed record GovernanceScope(
    string Id,
    string Kind,
    string Name,
    string? Table,
    string WriteTarget,
    int Line,
    IReadOnlyList<GovernanceTag> Tags,
    IReadOnlyList<string> MissingRequired,
    string? Producer = null,
    string? Detail = null);

/// <summary>A governance lint finding, carried alongside the scope it lands on.</summary>
public sealed record GovernanceFinding(
    string Code,
    string Severity,
    string Message,
    int Line,
    int Column,
    string? ScopeId);

/// <param name="TaskScopes">
/// Pipeline task labels that write nothing this service can address, listed so the panel can say
/// why they have no tags rather than leaving them out and looking incomplete.
/// </param>
public sealed record ScriptGovernance(
    bool Parsed,
    string? Error,
    IReadOnlyList<GovernanceScope> Scopes,
    IReadOnlyList<GovernanceFinding> Findings,
    IReadOnlyList<string> TaskScopes)
{
    public static ScriptGovernance Failed(string error) => new(false, error, [], [], []);
}

/// <summary>
/// The outcome of a governance edit. A refusal carries its reason instead of the original script:
/// a panel that redraws unchanged after a refused edit looks exactly like one that applied it.
/// </summary>
public sealed record GovernanceEditResult(bool Applied, string Script, string? Error = null)
{
    public static GovernanceEditResult Ok(string script) => new(true, script);
    public static GovernanceEditResult Refused(string script, string error) => new(false, script, error);
}

/// <summary>
/// Reads and edits the governance metadata a script carries: stewardship tags on the script, on the
/// tables and datasets it builds, and on the columns it projects.
///
/// <para><b>Two authoring forms, one rule for choosing between them.</b> A column projected by a
/// <c>SELECT</c> is tagged inline, with a <c>/* @key: value */</c> comment on the column itself —
/// that is where the lint rules, the catalog, and the PII scanner read column metadata from, and it
/// keeps the tag beside the thing it describes. Everything else — a <c>#temp</c> table, a
/// <c>CREATE TABLE</c>, a dataset, a remote table this script only reads — is tagged with an
/// <c>INSERT TAG FOR TABLE …</c> statement, because those objects have no declaration site a comment
/// could attach to. The panel says which form it is about to write before it writes it.</para>
///
/// <para><b>Derived tags are shown, never written.</b> A column that reads
/// <c>customer_email</c> from a table tagged <c>@pii: true</c> inherits that tag at run time, and
/// this projection reports it the same way the engine computes it — table tags first, then the
/// source column's own, with the script header as the fallback the engine applies to any entry that
/// lacks a key. Inheritance is reported only where the column is a plain reference to a source this
/// projection can name; an expression, or an unqualified name that two sources could both supply,
/// yields nothing rather than a guess. Turning a derived tag off writes a <c>DELETE TAG</c>, which
/// is what the engine reads; it is never silently copied and edited.</para>
///
/// <para><b>A pipeline task carries no tags, deliberately.</b> There is no task tag in the language
/// and nothing that would read one, so a task appears here only as the producer of the tables it
/// writes. Inventing <c>@owner</c> on a label would put a word in the author's file that no lint
/// rule, no catalog, and no lineage query ever reads.</para>
/// </summary>
public sealed class ScriptGovernanceService
{
    private const string RuleTagDescription = "d";

    /// <summary>The governance metadata this script declares, in script order.</summary>
    public ScriptGovernance Read(string? scriptText)
    {
        var source = scriptText ?? string.Empty;
        if (!ScriptTextEditing.TryParse(source, out var ast, out var parseError))
            return ScriptGovernance.Failed(parseError);

        var projection = Project(source, ast);
        var findings = ReadFindings(ast, projection.Scopes);
        return new ScriptGovernance(true, null, projection.Scopes, findings, projection.TaskScopes);
    }

    /// <summary>
    /// Sets and removes tags on one scope. A null value removes the tag; every other value is
    /// validated against the standard tag catalog before a byte of the script moves.
    /// </summary>
    public GovernanceEditResult Write(string? scriptText, string scopeId, IReadOnlyDictionary<string, string?> tags)
    {
        var source = scriptText ?? string.Empty;
        ArgumentNullException.ThrowIfNull(tags);
        if (tags.Count == 0) return GovernanceEditResult.Ok(source);

        if (!ScriptTextEditing.TryParse(source, out var ast, out var parseError))
            return GovernanceEditResult.Refused(source, parseError);

        var projection = Project(source, ast);
        var scope = projection.Scopes.FirstOrDefault(s => string.Equals(s.Id, scopeId, StringComparison.OrdinalIgnoreCase));
        if (scope is null)
            return GovernanceEditResult.Refused(source, $"This script has nothing called '{scopeId}' to tag.");

        var writes = new List<KeyValuePair<string, string>>();
        var removals = new List<string>();
        foreach (var (name, value) in tags)
        {
            var trimmedName = (name ?? string.Empty).Trim().TrimStart('@');
            if (trimmedName.Length == 0)
                return GovernanceEditResult.Refused(source, "A tag needs a name.");

            if (value is null)
            {
                removals.Add(StewardshipTagCatalog.Canonicalize(trimmedName));
                continue;
            }

            if (ColumnRuleParser.IsRuleTagKey(trimmedName))
                return GovernanceEditResult.Refused(
                    source,
                    $"'@{trimmedName}' is written by the engine from this column's EXPECT rules. Edit the rule, not the tag.");

            var validation = StewardshipTagCatalog.Validate(trimmedName, value);
            if (!validation.IsValid)
                return GovernanceEditResult.Refused(source, validation.Message ?? $"'@{trimmedName}' is not a usable tag.");
            if (!StewardshipTagCatalog.IsKnownOrCustom(validation.CanonicalName))
                return GovernanceEditResult.Refused(
                    source,
                    $"'@{validation.CanonicalName}' is not a standard tag. Prefix an organisation-specific tag with org_, x_, or custom_.");

            writes.Add(new KeyValuePair<string, string>(validation.CanonicalName, value.Trim()));
        }

        var edited = scope.WriteTarget switch
        {
            GovernanceWriteTarget.Header => WriteHeaderTags(source, ast, writes, removals),
            GovernanceWriteTarget.Inline => WriteInlineTags(source, ast, scope, writes, removals),
            _ => WriteStatementTags(source, ast, projection, scope, writes, removals),
        };

        return edited is null ? GovernanceEditResult.Ok(source) : Commit(source, edited);
    }

    // ── Projection ───────────────────────────────────────────────────────────

    private sealed record Projection(
        IReadOnlyList<GovernanceScope> Scopes,
        IReadOnlyList<string> TaskScopes);

    /// <summary>Accumulated tag state as the engine would build it, in script order.</summary>
    private sealed class TagLedger
    {
        public Dictionary<string, Dictionary<string, string>> Table { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Dictionary<string, Dictionary<string, string>>> Column { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> TableTags(string table) =>
            Table.TryGetValue(table, out var tags) ? tags : [];

        public Dictionary<string, string> ColumnTags(string table, string column) =>
            Column.TryGetValue(table, out var columns) && columns.TryGetValue(column, out var tags) ? tags : [];

        public void SetTable(string table, IReadOnlyDictionary<string, string> tags)
        {
            if (!Table.TryGetValue(table, out var current))
                Table[table] = current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in tags) current[key] = value;
        }

        public void SetColumn(string table, string column, IReadOnlyDictionary<string, string> tags)
        {
            if (!Column.TryGetValue(table, out var columns))
                Column[table] = columns = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            if (!columns.TryGetValue(column, out var current))
                columns[column] = current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in tags) current[key] = value;
        }

        public void RemoveTable(string table, IEnumerable<string> names)
        {
            if (!Table.TryGetValue(table, out var current)) return;
            foreach (var name in names) current.Remove(name);
        }

        public void RemoveColumn(string table, string column, IEnumerable<string> names)
        {
            if (!Column.TryGetValue(table, out var columns) || !columns.TryGetValue(column, out var current)) return;
            foreach (var name in names) current.Remove(name);
        }
    }

    private Projection Project(string source, Script ast)
    {
        var ledger = new TagLedger();
        var scopes = new List<GovernanceScope>();
        var scopeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var producers = ProducerLabels(source, ast);

        var scriptTags = ast.Metadata
            .Select(tag => Tag(tag.Key, tag.Value, GovernanceTagOrigin.Script, line: 1))
            .ToList();
        scopes.Add(BuildScope("script", "script", "This script", null, GovernanceWriteTarget.Header, 1, scriptTags, null,
            "Applied to every table and column this script records that does not carry the tag itself."));

        foreach (var statement in ScriptTextEditing.Flatten(ast.Statements))
        {
            switch (statement)
            {
                case CreateTagStatement tagStatement:
                    ApplyTagStatement(ledger, tagStatement);
                    break;

                case DeleteTagStatement deleteStatement:
                    ApplyDeleteStatement(ledger, deleteStatement);
                    break;

                case SelectStatement { IntoTable: not null } select:
                    AddTableScope(scopes, scopeIds, ledger, ast, select.IntoTable.TableName, select.Line,
                        producers.GetValueOrDefault(statement), null);
                    AddProjectionScopes(scopes, scopeIds, ledger, ast, select, select.IntoTable.TableName,
                        producers.GetValueOrDefault(statement));
                    break;

                case CreateTableStatement createTable:
                    AddTableScope(scopes, scopeIds, ledger, ast, createTable.TargetTable.TableName, createTable.Line,
                        producers.GetValueOrDefault(statement), null);
                    foreach (var column in createTable.Columns)
                    {
                        if (column.Metadata.Count > 0)
                            ledger.SetColumn(createTable.TargetTable.TableName, column.ColumnName, column.Metadata);
                        AddColumnScope(scopes, scopeIds, ledger, ast, createTable.TargetTable.TableName, column.ColumnName,
                            GovernanceWriteTarget.Statement, column.Line == 0 ? createTable.Line : column.Line,
                            column.Metadata, [], producers.GetValueOrDefault(statement));
                    }
                    break;

                case CreateDatasetStatement dataset:
                    AddDatasetScope(scopes, scopeIds, ledger, ast, dataset, producers.GetValueOrDefault(statement));
                    break;
            }
        }

        // A remote table the script only reads is taggable too — that is the case an INSERT TAG
        // statement exists for — but it earns a scope only once something has actually tagged it,
        // because every table in every FROM clause would otherwise fill the panel with rows nobody
        // asked about.
        foreach (var table in ledger.Table.Keys.Concat(ledger.Column.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
        {
            AddTableScope(scopes, scopeIds, ledger, ast, table, 0, null, "Tagged by a statement in this script.");

            if (!ledger.Column.TryGetValue(table, out var columns)) continue;
            foreach (var column in columns.Keys.ToArray())
                AddColumnScope(scopes, scopeIds, ledger, ast, table, column, GovernanceWriteTarget.Statement, 0,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), [], null);
        }

        return new Projection(scopes, producers.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private void AddProjectionScopes(
        List<GovernanceScope> scopes,
        HashSet<string> scopeIds,
        TagLedger ledger,
        Script ast,
        SelectStatement select,
        string target,
        string? producer)
    {
        var sources = SourceTables(select);
        foreach (var column in select.Columns)
        {
            var name = OutputName(column);
            if (name is null) continue;

            var derived = InheritedTags(ledger, sources, column);
            var authored = column.Metadata;

            // Mirrors the tracker: inherited first, the column's own tags on top.
            var effective = new Dictionary<string, string>(derived.Tags, StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in authored) effective[key] = value;
            ledger.SetColumn(target, name, effective);

            AddColumnScope(scopes, scopeIds, ledger, ast, target, name, GovernanceWriteTarget.Inline,
                column.Line, authored, derived.Tags.Select(tag => (tag.Key, tag.Value, derived.From)).ToArray(), producer);
        }
    }

    private void AddColumnScope(
        List<GovernanceScope> scopes,
        HashSet<string> scopeIds,
        TagLedger ledger,
        Script ast,
        string table,
        string column,
        string writeTarget,
        int line,
        IReadOnlyDictionary<string, string> authored,
        IReadOnlyList<(string Key, string Value, string? From)> derived,
        string? producer)
    {
        var id = ScopeId("column", table, column);
        if (!scopeIds.Add(id)) return;

        var tags = new List<GovernanceTag>();
        foreach (var (key, value) in authored)
            tags.Add(Tag(key, value, writeTarget == GovernanceWriteTarget.Inline
                ? GovernanceTagOrigin.Inline
                : GovernanceTagOrigin.Statement, line));

        foreach (var (key, value, from) in derived)
        {
            if (authored.ContainsKey(key)) continue;
            tags.Add(Tag(key, value, GovernanceTagOrigin.Derived, line, from));
        }

        foreach (var statementTag in ledger.ColumnTags(table, column))
        {
            if (tags.Any(tag => tag.Name.Equals(statementTag.Key, StringComparison.OrdinalIgnoreCase))) continue;
            tags.Add(Tag(statementTag.Key, statementTag.Value, GovernanceTagOrigin.Statement, line));
        }

        AddScriptFallback(tags, ast, line);
        scopes.Add(BuildScope(id, "column", column, table, writeTarget, line, tags, producer));
    }

    private void AddTableScope(
        List<GovernanceScope> scopes,
        HashSet<string> scopeIds,
        TagLedger ledger,
        Script ast,
        string table,
        int line,
        string? producer,
        string? detail)
    {
        var id = ScopeId("table", table, null);
        if (!scopeIds.Add(id)) return;

        var tags = ledger.TableTags(table)
            .Select(tag => Tag(tag.Key, tag.Value, GovernanceTagOrigin.Statement, line))
            .ToList();
        AddScriptFallback(tags, ast, line);

        var kind = table.StartsWith('#') ? "temp" : "table";
        scopes.Add(BuildScope(id, kind, table, table, GovernanceWriteTarget.Statement, line, tags, producer, detail));
    }

    private void AddDatasetScope(
        List<GovernanceScope> scopes,
        HashSet<string> scopeIds,
        TagLedger ledger,
        Script ast,
        CreateDatasetStatement dataset,
        string? producer)
    {
        var id = ScopeId("dataset", dataset.TempTableName, null);
        if (!scopeIds.Add(id)) return;

        var tags = ledger.TableTags(dataset.TempTableName)
            .Select(tag => Tag(tag.Key, tag.Value, GovernanceTagOrigin.Statement, dataset.Line))
            .ToList();
        AddScriptFallback(tags, ast, dataset.Line);

        scopes.Add(BuildScope(id, "dataset", dataset.TempTableName, dataset.TempTableName,
            GovernanceWriteTarget.Statement, dataset.Line, tags, producer,
            $"{dataset.AccessLevel} dataset."));

        if (dataset.SourceQuery is SelectStatement select)
            AddProjectionScopes(scopes, scopeIds, ledger, ast, select, dataset.TempTableName, producer);
    }

    /// <summary>
    /// A script header tag reaches anything that does not carry the key itself, so it is reported on
    /// every scope rather than only at the top — a reader looking at one column should not have to
    /// know the header exists to know what the column is classified as.
    /// </summary>
    private static void AddScriptFallback(List<GovernanceTag> tags, Script ast, int line)
    {
        foreach (var (key, value) in ast.Metadata)
        {
            if (tags.Any(tag => tag.Name.Equals(key, StringComparison.OrdinalIgnoreCase))) continue;
            tags.Add(new GovernanceTag(
                StewardshipTagCatalog.Canonicalize(key), value, GovernanceTagOrigin.Derived,
                DerivedFrom: "this script's header", Line: line,
                Known: StewardshipTagCatalog.IsKnownOrCustom(key)));
        }
    }

    private static GovernanceScope BuildScope(
        string id,
        string kind,
        string name,
        string? table,
        string writeTarget,
        int line,
        List<GovernanceTag> tags,
        string? producer,
        string? detail = null)
    {
        var missing = StewardshipTagCatalog.RequiredStewardshipTags
            .Where(required => !tags.Any(tag => tag.Name.Equals(required, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        return new GovernanceScope(id, kind, name, table, writeTarget, line,
            tags.OrderBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase).ToArray(), missing, producer, detail);
    }

    private static GovernanceTag Tag(string name, string value, string origin, int line, string? derivedFrom = null)
    {
        var canonical = StewardshipTagCatalog.Canonicalize(name);
        var validation = StewardshipTagCatalog.Validate(name, value);
        return new GovernanceTag(
            canonical,
            value,
            origin,
            derivedFrom,
            line,
            StewardshipTagCatalog.IsKnownOrCustom(name),
            validation.IsValid ? null : validation.Message);
    }

    private static string ScopeId(string kind, string name, string? column) =>
        column is null ? $"{kind}:{name}" : $"{kind}:{name}.{column}";

    // ── Inheritance, computed the way the tracker computes it ────────────────

    private sealed record Inherited(IReadOnlyDictionary<string, string> Tags, string? From);

    private static readonly Inherited NothingInherited =
        new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), null);

    /// <summary>
    /// What this column would inherit at run time, or nothing.
    ///
    /// <para>Only a plain column reference inherits, and only when exactly one source can supply it.
    /// An expression produces a value the source column's tags are no longer a statement about, and
    /// an unqualified name two joined tables could both supply is exactly the guess the data-model
    /// view refuses to make for the same reason.</para>
    /// </summary>
    private static Inherited InheritedTags(TagLedger ledger, IReadOnlyList<(string Table, string? Alias)> sources, SelectColumn column)
    {
        if (PlainReference(column.Expression) is not var (qualifier, name) || name is null) return NothingInherited;

        string? table = null;
        if (!string.IsNullOrEmpty(qualifier))
        {
            table = sources.FirstOrDefault(source =>
                string.Equals(source.Alias, qualifier, StringComparison.OrdinalIgnoreCase)
                || string.Equals(source.Table, qualifier, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Unqualified(source.Table), qualifier, StringComparison.OrdinalIgnoreCase)).Table;
        }
        else if (sources.Count == 1)
        {
            table = sources[0].Table;
        }

        if (string.IsNullOrEmpty(table)) return NothingInherited;

        var inherited = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in ledger.TableTags(table))
        {
            if (key.Equals(RuleTagDescription, StringComparison.OrdinalIgnoreCase)) continue;
            if (ColumnRuleParser.IsRuleTagKey(key)) continue;
            inherited[key] = value;
        }

        foreach (var (key, value) in ledger.ColumnTags(table, name))
        {
            if (key.Equals(RuleTagDescription, StringComparison.OrdinalIgnoreCase)) continue;
            if (ColumnRuleParser.IsRuleTagKey(key)) continue;
            inherited[key] = value;
        }

        return inherited.Count == 0
            ? NothingInherited
            : new Inherited(inherited, $"{table}.{name}");
    }

    private static IReadOnlyList<(string Table, string? Alias)> SourceTables(SelectStatement select)
    {
        var sources = new List<(string, string?)>();
        void Add(TableReference? reference)
        {
            if (reference is null || reference.Subquery is not null || reference.FunctionCall is not null) return;
            if (string.IsNullOrEmpty(reference.TableName)) return;
            sources.Add((QualifiedName(reference), reference.Alias));
        }

        Add(select.FromTable);
        foreach (var join in select.Joins) Add(join.Table);
        return sources;
    }

    /// <summary>
    /// The name the lineage tracker keys a table by: what the script writes, connection prefix
    /// included, because <c>demo.Orders</c> and <c>#orders</c> are different tables.
    /// </summary>
    private static string QualifiedName(TableReference reference) =>
        string.IsNullOrEmpty(reference.ConnectionName)
            ? reference.TableName
            : $"{reference.ConnectionName}.{reference.TableName}";

    private static string? OutputName(SelectColumn column)
    {
        if (!string.IsNullOrWhiteSpace(column.Alias)) return column.Alias;
        return PlainReference(column.Expression)?.Name;
    }

    /// <summary>
    /// The <c>[qualifier.]column</c> a plain reference names, or null for anything else. A star, a
    /// function call, an arithmetic expression — none of them is the source column whose tags could
    /// still be a true statement about the value, so none of them inherits.
    /// </summary>
    private static (string? Qualifier, string Name)? PlainReference(Expression expression)
    {
        if (expression is not IdentifierExpression identifier) return null;
        var name = identifier.Name;
        if (string.IsNullOrEmpty(name) || name.Contains('*')) return null;

        var parts = name.Split('.');
        return parts.Length == 1 ? (null, parts[0]) : (parts[^2], parts[^1]);
    }

    private static string Unqualified(string table)
    {
        var index = table.LastIndexOf('.');
        return index < 0 ? table : table[(index + 1)..];
    }

    private static void ApplyTagStatement(TagLedger ledger, CreateTagStatement statement)
    {
        var table = LiteralOf(statement.TableName);
        if (table is null) return;
        var column = statement.ColumnName is null ? null : LiteralOf(statement.ColumnName);

        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in statement.Tags)
        {
            var literal = LiteralOf(value);
            if (literal is not null) tags[StewardshipTagCatalog.Canonicalize(key)] = literal;
        }
        if (tags.Count == 0) return;

        if (column is null) ledger.SetTable(table, tags);
        else ledger.SetColumn(table, column, tags);
    }

    private static void ApplyDeleteStatement(TagLedger ledger, DeleteTagStatement statement)
    {
        var table = LiteralOf(statement.TableName);
        if (table is null) return;
        var column = statement.ColumnName is null ? null : LiteralOf(statement.ColumnName);
        var names = statement.TagNames.Select(StewardshipTagCatalog.Canonicalize).ToArray();

        if (column is null) ledger.RemoveTable(table, names);
        else ledger.RemoveColumn(table, column, names);
    }

    /// <summary>
    /// The written value of a tag statement operand, or null when it is computed.
    ///
    /// <para>A tag statement may name its table with a variable — <c>INSERT TAG FOR TABLE @t</c>
    /// inside a loop — and the value only exists at run time. Those are reported as untouched rather
    /// than shown against a guessed table, and the panel never offers to edit one.</para>
    /// </summary>
    private static string? LiteralOf(Expression expression) => expression switch
    {
        LiteralExpression literal => literal.Value?.ToString(),
        IdentifierExpression identifier => identifier.Name,
        _ => null,
    };

    // ── Findings ─────────────────────────────────────────────────────────────

    private static IReadOnlyList<GovernanceFinding> ReadFindings(Script ast, IReadOnlyList<GovernanceScope> scopes)
    {
        var linter = new Linter();
        linter.AddRule(new TagDrivenGovernancePolicyRule());
        linter.AddRule(new TagValueValidationRule());
        linter.AddRule(new UnknownTagLintRule());

        List<LintResult> results;
        try
        {
            results = linter.AnalyzeAsync(ast, new DefaultLintContext()).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            return [];
        }

        return results
            .Select(result => new GovernanceFinding(
                result.Code ?? result.RuleName,
                result.Severity.ToString().ToLowerInvariant(),
                result.Message,
                result.LineNumber,
                result.ColumnNumber,
                NearestScope(scopes, result.LineNumber)))
            .OrderBy(finding => finding.Line)
            .ToArray();
    }

    /// <summary>
    /// The scope a finding belongs to: the last one declared at or above its line. A finding whose
    /// line matches nothing keeps a null scope and is shown on its own rather than attached to
    /// whichever row happened to be nearest.
    /// </summary>
    private static string? NearestScope(IReadOnlyList<GovernanceScope> scopes, int line) =>
        scopes
            .Where(scope => scope.Line > 0 && scope.Line <= line)
            .OrderByDescending(scope => scope.Line)
            .FirstOrDefault(scope => scope.Kind != "script")
            ?.Id;

    // ── Writes ───────────────────────────────────────────────────────────────

    /// <summary>Rewrites the script's own header tag comment.</summary>
    private static string? WriteHeaderTags(
        string source,
        Script ast,
        IReadOnlyList<KeyValuePair<string, string>> writes,
        IReadOnlyList<string> removals)
    {
        var merged = new Dictionary<string, string>(ast.Metadata, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in writes) merged[name] = value;
        foreach (var name in removals) merged.Remove(name);

        var lineEnding = ScriptTextEditing.DetectLineEnding(source);
        var rendered = merged.Count == 0 ? string.Empty : RenderTagComment(merged) + lineEnding;

        // The header is the leading run of tag comments the parser folds into Script.Metadata; it
        // ends at the first token that is not one.
        var header = HeaderTags(source, TagTokens(source));
        if (header.Count == 0)
            return rendered.Length == 0 ? null : rendered + source;

        var start = header[0].Offset;
        var end = ScriptTextEditing.EndOfLine(source, header[^1].EndOffset);
        var suffix = rendered.Length == 0 && end < source.Length && source[end] == '\n' ? 1 : 0;
        return ScriptTextEditing.Splice(source, start, Math.Min(source.Length, end + suffix), rendered);
    }

    /// <summary>
    /// The leading run of tag comments the parser folds into <see cref="Script.Metadata"/>: each one
    /// separated from the last by whitespace alone. The first token that is not a tag ends the
    /// header, which is exactly the rule the parser applies when it reads them.
    /// </summary>
    private static IReadOnlyList<Token> HeaderTags(string source, IReadOnlyList<Token> tags)
    {
        var header = new List<Token>();
        var cursor = 0;
        foreach (var token in tags)
        {
            if (source[cursor..token.Offset].Any(character => !char.IsWhiteSpace(character))) break;
            header.Add(token);
            cursor = token.EndOffset;
        }
        return header;
    }

    /// <summary>Rewrites the tag comment on the SELECT column that projects this scope.</summary>
    private static string? WriteInlineTags(
        string source,
        Script ast,
        GovernanceScope scope,
        IReadOnlyList<KeyValuePair<string, string>> writes,
        IReadOnlyList<string> removals)
    {
        var located = FindProjectedColumn(source, ast, scope);
        if (located is null) return null;

        var (column, span) = located.Value;
        var merged = new Dictionary<string, string>(column.Metadata, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in writes) merged[name] = value;
        foreach (var name in removals) merged.Remove(name);

        var owned = TagTokens(source)
            .Where(token => token.Offset >= span.Start && token.EndOffset <= span.End)
            .ToArray();

        var rendered = merged.Count == 0 ? string.Empty : RenderTagComment(merged);

        if (owned.Length == 0)
            return rendered.Length == 0 ? null : ScriptTextEditing.Splice(source, span.End, span.End, " " + rendered);

        // Replace the last comment in place and drop the rest, so a column that carried its tags in
        // two comments ends up with one and nothing else on the line moves.
        var edited = source;
        for (var index = owned.Length - 1; index >= 0; index--)
        {
            var token = owned[index];
            var replacement = index == owned.Length - 1 ? rendered : string.Empty;
            var start = token.Offset;
            if (replacement.Length == 0 && start > 0 && edited[start - 1] == ' ') start--;
            edited = ScriptTextEditing.Splice(edited, start, token.EndOffset, replacement);
        }
        return edited;
    }

    private readonly record struct Span(int Start, int End);

    private static (SelectColumn Column, Span Span)? FindProjectedColumn(string source, Script ast, GovernanceScope scope)
    {
        foreach (var statement in ScriptTextEditing.Flatten(ast.Statements))
        {
            SelectStatement? select = null;
            string? target = null;
            if (statement is SelectStatement { IntoTable: not null } into)
            {
                select = into;
                target = into.IntoTable.TableName;
            }
            else if (statement is CreateDatasetStatement { SourceQuery: SelectStatement inner } dataset)
            {
                select = inner;
                target = dataset.TempTableName;
            }

            if (select is null || !string.Equals(target, scope.Table, StringComparison.OrdinalIgnoreCase)) continue;

            foreach (var column in select.Columns)
            {
                if (!string.Equals(OutputName(column), scope.Name, StringComparison.OrdinalIgnoreCase)) continue;
                var start = ScriptTextEditing.Offset(source, column.Line, column.Column);
                var end = ScriptTextEditing.Offset(source, column.EndLine, column.EndColumn);
                if (start < 0 || end < start) return null;
                return (column, new Span(start, end));
            }
        }
        return null;
    }

    /// <summary>Writes an <c>INSERT TAG</c> / <c>DELETE TAG</c> statement for a scope with no declaration site.</summary>
    private static string? WriteStatementTags(
        string source,
        Script ast,
        Projection projection,
        GovernanceScope scope,
        IReadOnlyList<KeyValuePair<string, string>> writes,
        IReadOnlyList<string> removals)
    {
        var lineEnding = ScriptTextEditing.DetectLineEnding(source);
        var table = scope.Table ?? scope.Name;
        var column = scope.Kind == "column" ? scope.Name : null;

        var text = new StringBuilder();
        if (writes.Count > 0)
        {
            var assignments = string.Join(", ", writes.Select(write => $"{write.Key} = {Quote(write.Value)}"));
            text.Append($"INSERT TAG FOR TABLE {table}{(column is null ? string.Empty : $" COLUMN {column}")} ({assignments});");
        }
        if (removals.Count > 0)
        {
            if (text.Length > 0) text.Append(lineEnding);
            text.Append($"DELETE TAG FOR TABLE {table}{(column is null ? string.Empty : $" COLUMN {column}")} ({string.Join(", ", removals)});");
        }
        if (text.Length == 0) return null;

        var insertAt = InsertPoint(source, ast, table);
        var prefix = ScriptTextEditing.NeedsBlankLineBefore(source, insertAt) ? lineEnding : string.Empty;
        var suffix = insertAt >= source.Length ? lineEnding : lineEnding;
        return ScriptTextEditing.Splice(source, insertAt, insertAt, prefix + text + suffix);
    }

    /// <summary>
    /// Where a tag statement for this table has to go.
    ///
    /// <para>After the statement that creates the table, when the script creates it — a tag applied
    /// before the table exists is applied to nothing. Otherwise before the first statement that reads
    /// it, because a tag on a source table is only inherited by the columns read after it is set.</para>
    /// </summary>
    private static int InsertPoint(string source, Script ast, string table)
    {
        foreach (var statement in ScriptTextEditing.Flatten(ast.Statements))
        {
            if (string.Equals(statement.GetCreatedTable(), table, StringComparison.OrdinalIgnoreCase))
                return ScriptTextEditing.EndOfLine(source, statement.EndOffset);
        }

        foreach (var statement in ScriptTextEditing.Flatten(ast.Statements))
        {
            if (statement.GetSourceTables().Any(read => string.Equals(read, table, StringComparison.OrdinalIgnoreCase)))
                return ScriptTextEditing.StartOfLine(source, statement.StartOffset);
        }

        return source.Length;
    }

    // ── Rendering and text mechanics ─────────────────────────────────────────

    private static string RenderTagComment(IReadOnlyDictionary<string, string> tags) =>
        "/* " + string.Join("; ", tags.Select(tag => $"@{tag.Key}: {InlineValue(tag.Value)}")) + " */";

    /// <summary>
    /// A tag comment ends at <c>*/</c> and its values end at <c>;</c>, so a value containing either
    /// is quoted — the comment layer reads a quoted value to its matching close quote.
    /// </summary>
    private static string InlineValue(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0) return "''";
        if (trimmed.StartsWith('\'') && trimmed.EndsWith('\'') && trimmed.Length > 1) return trimmed;
        if (trimmed.Contains(';') || trimmed.Contains(',') || trimmed.Contains('@') || trimmed.Contains("*/"))
            return "'" + trimmed.Replace("'", "''") + "'";
        return trimmed;
    }

    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

    private static IReadOnlyList<Token> TagTokens(string source)
    {
        try
        {
            return new Lexer(source).Tokenize().Where(token => token.Type == TokenType.COLUMN_TAG).ToArray();
        }
        catch (Exception)
        {
            return [];
        }
    }


    /// <summary>
    /// Applies an edit only if the result still parses. An edit that would produce a script the
    /// parser rejects is refused with its reason, never written and never silently dropped.
    /// </summary>
    private static GovernanceEditResult Commit(string original, string edited) =>
        ScriptTextEditing.TryParse(edited, out _, out var error)
            ? GovernanceEditResult.Ok(edited)
            : GovernanceEditResult.Refused(original, $"That tag would not parse: {error}");
    /// <summary>
    /// The labelled task that writes each statement, for grouping only. A label introduces the
    /// statement that follows it, so the label in force is the last one seen at the same level.
    /// </summary>
    private static Dictionary<Statement, string> ProducerLabels(string source, Script ast)
    {
        var producers = new Dictionary<Statement, string>();
        string? current = null;
        foreach (var statement in ast.Statements)
        {
            if (statement is SectionLabelStatement label)
            {
                current = label.LabelName;
                continue;
            }
            if (current is not null) producers[statement] = current;
            current = null;
        }
        return producers;
    }
}
