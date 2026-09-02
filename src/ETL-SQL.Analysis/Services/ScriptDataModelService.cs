using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Analysis.Services;

/// <summary>One column an entity in the data model carries.</summary>
/// <param name="IsKey">
/// True only when the database said so. A column the script merely joins on is not a key, and
/// calling it one would make an inferred relationship look like a declared one.
/// </param>
public sealed record DataModelColumn(string Name, string? Type = null, bool IsKey = false);

/// <summary>
/// One thing the model holds: a connection, a remote table, a <c>#temp</c> table, a CTE, or a
/// <c>&amp;dataset</c>.
/// </summary>
/// <param name="Kind">connection | table | temp | cte | dataset</param>
/// <param name="Line">Where the script first names it, so the diagram can jump to it.</param>
public sealed record DataModelEntity(
    string Id,
    string Name,
    string Kind,
    string? Connection,
    IReadOnlyList<DataModelColumn> Columns,
    int Line,
    string? Detail = null);

/// <summary>
/// One relationship between two entities.
/// </summary>
/// <param name="Kind">
/// join — an equality the script joins on; foreign-key — a constraint the database declares;
/// derivation — the model reads "<c>To</c> is built from <c>From</c>"; membership — a table belongs
/// to a connection.
/// </param>
/// <param name="Cardinality">
/// one-to-one | many-to-one | one-to-many | many-to-many | unknown. <b>unknown</b> is the honest and
/// the common answer: a join written in a script says two columns match, and nothing at all about
/// how many rows are on either side. Only a declared key or a declared foreign key upgrades it.
/// </param>
/// <param name="Evidence">
/// script — the relationship is written in the file; schema — the database declares it. Nothing else
/// produces a relationship, which is the whole rule this view is built on.
/// </param>
public sealed record DataModelRelationship(
    string Id,
    string From,
    string To,
    string Kind,
    string Cardinality,
    string Evidence,
    string? FromColumn = null,
    string? ToColumn = null,
    string? JoinType = null,
    int Line = 0);

/// <summary>
/// Keys and foreign keys a database declares, for the connections the script actually reads.
///
/// <para>Supplied by the host, because reading it means talking to a connector, and the projection
/// itself must stay a pure function of the script. Absent — a connector with no catalog, a host with
/// no live connection — the model is still complete as far as the script goes, and every cardinality
/// it reports is <c>unknown</c> rather than a guess dressed up as a fact.</para>
/// </summary>
/// <param name="KeyColumns">Key columns per qualified table name (<c>connection.table</c>).</param>
public sealed record DataModelSchemaEvidence(
    IReadOnlyDictionary<string, IReadOnlyCollection<string>> KeyColumns,
    IReadOnlyList<DataModelForeignKey> ForeignKeys)
{
    public static DataModelSchemaEvidence None { get; } =
        new(new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase), []);

    public bool IsEmpty => KeyColumns.Count == 0 && ForeignKeys.Count == 0;
}

/// <param name="FromTable">Qualified as <c>connection.table</c>, the same shape the entities use.</param>
public sealed record DataModelForeignKey(
    string FromTable,
    string FromColumn,
    string ToTable,
    string ToColumn);

/// <param name="HasSchemaEvidence">
/// Whether any database metadata reached the projection. The view says so out loud: "no keys were
/// available" is a different statement from "these tables have no keys", and a reader who cannot
/// tell them apart will read every unknown cardinality as a discovered fact.
/// </param>
public sealed record ScriptDataModel(
    bool Parsed,
    string? Error,
    IReadOnlyList<DataModelEntity> Entities,
    IReadOnlyList<DataModelRelationship> Relationships,
    bool HasSchemaEvidence)
{
    public static ScriptDataModel Empty { get; } = new(true, null, [], [], false);

    public static ScriptDataModel Failed(string error) => new(false, error, [], [], false);
}

public interface IScriptDataModelProjection
{
    ScriptDataModel Project(string? scriptText, DataModelSchemaEvidence? evidence = null);
}

/// <summary>
/// Projects a script into the entity/relationship model behind Studio's data-model view.
///
/// <para>The rule the whole projection is built on: <b>nothing is drawn that the parser or the
/// database did not say</b>. A join in the script produces an edge because the author wrote it. A
/// declared foreign key produces an edge because the database declares it. Two tables that happen to
/// carry a column of the same name produce nothing — that is the inference every ER tool is tempted
/// to make, and it is the one that turns a diagram into a rumour. Cardinality follows the same rule:
/// it is <c>unknown</c> unless a key or a foreign key settles it.</para>
///
/// <para>Derivation edges are included as first-class relationships because in ETL-SQL most of the
/// model is not a set of tables at rest — it is a chain of <c>#temp</c> tables, each built from the
/// last. A diagram that showed only joins would show the smaller half of what a script does.</para>
/// </summary>
public sealed class ScriptDataModelService : IScriptDataModelProjection
{
    public ScriptDataModel Project(string? scriptText, DataModelSchemaEvidence? evidence = null)
    {
        if (string.IsNullOrWhiteSpace(scriptText)) return ScriptDataModel.Empty;

        Script script;
        try
        {
            script = new CoreParser(new Lexer(scriptText).Tokenize(), scriptText).Parse();
        }
        catch (Exception ex)
        {
            return ScriptDataModel.Failed($"Could not parse script for the data model: {ex.Message}");
        }

        if (script.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error) is { } diagnostic)
            return ScriptDataModel.Failed($"Could not parse script for the data model: {diagnostic.Message}");

        var schema = evidence ?? DataModelSchemaEvidence.None;
        var builder = new Builder(schema);
        foreach (var statement in Flatten(script.Statements)) builder.Visit(statement);
        return builder.Build();
    }

    /// <summary>
    /// Every statement in the script, including the ones nested inside blocks and control flow.
    ///
    /// <para>A model that stopped at the top level would miss the tables read inside an
    /// <c>EXECUTE ... BEGIN … END</c> block, which in a pipeline script is where nearly all of them
    /// are.</para>
    /// </summary>
    private static IEnumerable<Statement> Flatten(IEnumerable<Statement> statements)
    {
        foreach (var statement in statements)
        {
            yield return statement;

            IEnumerable<Statement> children = statement switch
            {
                BlockStatement block => block.Statements,
                IfStatement ifStatement => new[] { ifStatement.IfBody }
                    .Concat(ifStatement.ElseIfClauses?.Select(clause => clause.Body) ?? [])
                    .Concat(ifStatement.ElseBody is null ? [] : [ifStatement.ElseBody]),
                WhileStatement whileStatement => [whileStatement.Body],
                ForStatement forStatement => [forStatement.Body],
                ForeachStatement foreachStatement => [foreachStatement.Body],
                TryCatchStatement tryCatch => [tryCatch.TryBody, tryCatch.CatchBody],
                CreateProcedureStatement procedure => [procedure.Body],
                CreateFunctionStatement function => [function.Body],
                ParallelStatement parallel => [parallel.Body],
                ParallelForStatement parallelFor => [parallelFor.Body],
                ExecuteRemoteBlockStatement remote => [remote.Body],
                _ => [],
            };

            foreach (var child in Flatten(children)) yield return child;
        }
    }

    private sealed class Builder(DataModelSchemaEvidence schema)
    {
        private readonly Dictionary<string, DataModelEntity> _entities = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DataModelRelationship> _relationships = new(StringComparer.OrdinalIgnoreCase);

        public void Visit(Statement statement)
        {
            // A CTE is scoped to the statement that declares it, and inside that statement its name
            // is not a table. Registering the CTEs first is what stops `FROM recent` from also
            // conjuring a table entity called `recent` beside the CTE of the same name.
            var scope = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cte in statement.Ctes ?? [])
            {
                var cteId = EntityId("cte", cte.Name);
                Add(new DataModelEntity(
                    cteId, cte.Name, "cte", null,
                    (cte.ColumnNames ?? []).Select(name => new DataModelColumn(name)).ToList(),
                    cte.Line,
                    "Scoped to the statement that declares it"));
                if (cte.Query is SelectStatement cteQuery)
                {
                    VisitSelect(cteQuery, scope);
                    foreach (var source in SourcesOf(cteQuery, scope)) AddDerivation(source, cteId, cte.Line);
                }
                scope.Add(cte.Name);
            }

            switch (statement)
            {
                case CreateConnectionStatement connection:
                    Add(new DataModelEntity(
                        EntityId("connection", connection.ConnectionName),
                        connection.ConnectionName,
                        "connection",
                        null,
                        [],
                        connection.Line,
                        connection.ConnectionType));
                    break;

                case SelectStatement select:
                    VisitSelect(select, scope);
                    break;

                // Remote pushdown. The block's SQL is text handed to the database, not something
                // this parser reads, so the only honest source for what it produces is the
                // connection it was pushed to. Naming tables out of an unparsed string would be the
                // same invention this projection refuses everywhere else.
                case ExecutePushdownStatement pushdown when pushdown.IntoTable is { } into:
                    if (Reference(into, scope) is { } produced
                        && ConnectionNameOf(pushdown.ConnectionName) is { } pushedTo)
                    {
                        var connectionId = EntityId("connection", pushedTo);
                        Add(new DataModelEntity(connectionId, pushedTo, "connection", null, [], pushdown.Line));
                        AddDerivation(connectionId, produced, pushdown.Line);
                        _entities[produced] = _entities[produced] with
                        {
                            Detail = "Built by SQL pushed down to " + pushedTo,
                        };
                    }
                    break;
            }
        }

        private static string? ConnectionNameOf(Expression expression) => expression switch
        {
            IdentifierExpression identifier => identifier.Name,
            LiteralExpression { Value: string literal } => literal,
            _ => null,
        };

        private void VisitSelect(SelectStatement select, IReadOnlySet<string> scope)
        {
            var from = Reference(select.FromTable, scope);
            foreach (var join in select.Joins ?? [])
            {
                var target = Reference(join.Table, scope);
                if (from is null || target is null) continue;
                foreach (var (leftColumn, rightColumn) in EqualityColumns(join.Condition))
                {
                    var (left, right) = ResolveSides(select, join, leftColumn, rightColumn, scope);
                    if (left is null || right is null) continue;
                    AddJoin(left.Value, right.Value, join.JoinType, join.Line == 0 ? select.Line : join.Line);
                }
            }

            // SELECT … INTO #temp is the shape most of an ETL-SQL model is made of.
            if (select.IntoTable is { } into && Reference(into, scope) is { } target2)
            {
                foreach (var source in SourcesOf(select, scope)) AddDerivation(source, target2, select.Line);
            }
        }

        /// <summary>Every entity a select reads from, its joins included.</summary>
        private IEnumerable<string> SourcesOf(SelectStatement select, IReadOnlySet<string> scope)
        {
            if (Reference(select.FromTable, scope) is { } from) yield return from;
            foreach (var join in select.Joins ?? [])
                if (Reference(join.Table, scope) is { } joined) yield return joined;
        }

        /// <summary>
        /// Which entity each side of a join equality belongs to, by alias.
        ///
        /// <para>An unqualified column is left unresolved rather than assigned to the nearer table:
        /// an edge drawn on a guess about which side a bare <c>id</c> came from would be exactly the
        /// invented relationship this view exists to avoid.</para>
        /// </summary>
        private (string Entity, string Column)? Side(SelectStatement select, JoinClause join, string identifier, IReadOnlySet<string> scope)
        {
            var dot = identifier.LastIndexOf('.');
            if (dot < 0) return null;
            var qualifier = identifier[..dot];
            var column = identifier[(dot + 1)..];

            // The qualifier may itself be `connection.table`; match on the last segment, which is
            // what an alias-free reference reads as.
            var lastSegment = qualifier[(qualifier.LastIndexOf('.') + 1)..];
            foreach (var candidate in new[] { join.Table, select.FromTable })
            {
                var alias = candidate.Alias ?? candidate.TableName;
                if (!alias.Equals(lastSegment, StringComparison.OrdinalIgnoreCase)) continue;
                return Reference(candidate, scope) is { } id ? (id, column) : null;
            }
            return null;
        }

        private ((string Entity, string Column)? Left, (string Entity, string Column)? Right) ResolveSides(
            SelectStatement select, JoinClause join, string leftIdentifier, string rightIdentifier, IReadOnlySet<string> scope)
        {
            var first = Side(select, join, leftIdentifier, scope);
            var second = Side(select, join, rightIdentifier, scope);
            if (first is null || second is null || first.Value.Entity.Equals(second.Value.Entity, StringComparison.OrdinalIgnoreCase))
                return (null, null);

            // Orient the edge so the joined table is the right-hand side, which is how the reader
            // reads the statement.
            var joinedId = Reference(join.Table, scope);
            return second.Value.Entity.Equals(joinedId, StringComparison.OrdinalIgnoreCase)
                ? (first, second)
                : (second, first);
        }

        /// <summary>Column pairs an ON clause equates, walking through AND. Nothing else counts.</summary>
        private static IEnumerable<(string Left, string Right)> EqualityColumns(Expression? condition)
        {
            switch (condition)
            {
                case BinaryExpression { Operator: TokenType.EQUALS } equality
                    when equality.Left is IdentifierExpression left && equality.Right is IdentifierExpression right:
                    yield return (left.Name, right.Name);
                    break;
                case BinaryExpression { Operator: TokenType.AND } conjunction:
                    foreach (var pair in EqualityColumns(conjunction.Left)) yield return pair;
                    foreach (var pair in EqualityColumns(conjunction.Right)) yield return pair;
                    break;
            }
        }

        /// <summary>Registers the entity a table reference names, and returns its id.</summary>
        private string? Reference(TableReference? table, IReadOnlySet<string> scope)
        {
            if (table is null) return null;
            if (table.Subquery is not null || table.FunctionCall is not null || table.ValuesRows is not null)
                return null;

            var name = table.TableName;
            if (string.IsNullOrWhiteSpace(name)) return null;

            if (name.StartsWith('#'))
            {
                var tempId = EntityId("temp", name);
                Add(new DataModelEntity(tempId, name, "temp", null, [], table.Line));
                return tempId;
            }

            // A name a CTE in this statement declares resolves to that CTE, never to a table.
            if (scope.Contains(name)) return EntityId("cte", name);

            if (name.StartsWith('&'))
            {
                var datasetId = EntityId("dataset", name);
                Add(new DataModelEntity(datasetId, name, "dataset", null, [], table.Line));
                return datasetId;
            }

            var connection = table.ConnectionName;
            var qualified = connection is null ? name : $"{connection}.{name}";
            var id = EntityId("table", qualified);
            var keys = schema.KeyColumns.TryGetValue(qualified, out var declared) ? declared : [];
            Add(new DataModelEntity(
                id, name, "table", connection,
                keys.Select(key => new DataModelColumn(key, null, true)).ToList(),
                table.Line,
                table.SchemaName));

            if (connection is not null)
            {
                var connectionId = EntityId("connection", connection);
                // A connection the script reads but never declares still belongs on the diagram: it
                // is what the reader has to go and create, and a table floating with no owner reads
                // as a bug in the view rather than as a gap in the script.
                Add(new DataModelEntity(connectionId, connection, "connection", null, [], table.Line));
                AddRelationship(new DataModelRelationship(
                    $"member:{connectionId}:{id}", connectionId, id, "membership", "one-to-many", "script",
                    Line: table.Line));
            }

            return id;
        }

        private void AddJoin((string Entity, string Column) left, (string Entity, string Column) right, string joinType, int line)
        {
            var cardinality = Cardinality(left, right);
            AddRelationship(new DataModelRelationship(
                $"join:{left.Entity}.{left.Column}:{right.Entity}.{right.Column}",
                left.Entity, right.Entity, "join", cardinality, "script",
                left.Column, right.Column, joinType, line));
        }

        /// <summary>
        /// What the declared keys say about how many rows sit on each side, and nothing more.
        ///
        /// <para>A side whose joined column is a declared key holds at most one row per value. Both
        /// sides keyed is one-to-one; one side keyed is many-to-one toward it; neither keyed is
        /// <c>unknown</c>, which is also the answer whenever no schema reached us at all.</para>
        /// </summary>
        private string Cardinality((string Entity, string Column) left, (string Entity, string Column) right)
        {
            var leftKeyed = IsDeclaredKey(left);
            var rightKeyed = IsDeclaredKey(right);
            return (leftKeyed, rightKeyed) switch
            {
                (true, true) => "one-to-one",
                (false, true) => "many-to-one",
                (true, false) => "one-to-many",
                _ => "unknown",
            };
        }

        private bool IsDeclaredKey((string Entity, string Column) side) =>
            _entities.TryGetValue(side.Entity, out var entity)
            && entity.Columns.Any(column => column.IsKey
                && column.Name.Equals(side.Column, StringComparison.OrdinalIgnoreCase));

        private void AddDerivation(string source, string target, int line)
        {
            if (source.Equals(target, StringComparison.OrdinalIgnoreCase)) return;
            AddRelationship(new DataModelRelationship(
                $"derives:{source}:{target}", source, target, "derivation", "unknown", "script", Line: line));
        }

        private void Add(DataModelEntity entity)
        {
            if (!_entities.TryGetValue(entity.Id, out var existing))
            {
                _entities[entity.Id] = entity;
                return;
            }

            // Later mentions of the same table must not erase what an earlier one knew: a reference
            // carrying declared key columns or a connection is strictly better than a bare one.
            _entities[entity.Id] = existing with
            {
                Columns = existing.Columns.Count >= entity.Columns.Count ? existing.Columns : entity.Columns,
                Connection = existing.Connection ?? entity.Connection,
                Detail = existing.Detail ?? entity.Detail,
                Line = existing.Line == 0 ? entity.Line : Math.Min(existing.Line, entity.Line),
            };
        }

        private void AddRelationship(DataModelRelationship relationship) =>
            _relationships.TryAdd(relationship.Id, relationship);

        private static string EntityId(string kind, string name) => $"{kind}:{name.ToLowerInvariant()}";

        public ScriptDataModel Build()
        {
            // Declared foreign keys come last so they only ever connect tables the script reads. A
            // database has far more relationships than any one script touches, and drawing all of
            // them would bury the handful the author is actually working with.
            foreach (var foreignKey in schema.ForeignKeys)
            {
                var from = EntityId("table", foreignKey.FromTable);
                var to = EntityId("table", foreignKey.ToTable);
                if (!_entities.ContainsKey(from) || !_entities.ContainsKey(to)) continue;
                AddRelationship(new DataModelRelationship(
                    $"fk:{from}.{foreignKey.FromColumn}:{to}.{foreignKey.ToColumn}",
                    from, to, "foreign-key", "many-to-one", "schema",
                    foreignKey.FromColumn, foreignKey.ToColumn));
            }

            return new ScriptDataModel(
                true,
                null,
                _entities.Values.OrderBy(entity => entity.Line).ThenBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase).ToList(),
                _relationships.Values.ToList(),
                !schema.IsEmpty);
        }
    }
}
