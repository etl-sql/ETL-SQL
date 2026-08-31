using System;
using System.Collections.Generic;
using System.Globalization;

namespace ETL_SQL.Core.Quality;

/// <summary>
/// Projects a column's <c>EXPECT</c> clauses onto the two shapes the rest of the system already
/// speaks: runtime <see cref="ColumnRuleBinding"/>s, and the <c>expect</c>/<c>fail</c> stewardship
/// tags published to lineage.
/// <para>
/// The tag projection is what keeps rules steward-visible. Before Rev 11 the rules *were* tags, so
/// the catalog, lineage read side, Portal, and <c>SHOW DATA QUALITY RULES</c> all read them
/// straight out of a column's metadata dictionary. Rules are grammar now, but that was only ever an
/// input encoding: publishing the same tags from the AST keeps every one of those surfaces
/// rendering exactly what it rendered before, with no read-side change and no second source of
/// truth. Nothing parses a script by reading these back — they are an output.
/// </para>
/// </summary>
public static class ColumnExpectProjection
{
    /// <summary>The tag key a clause projects to: <c>expect</c>, then <c>expect_1</c>, …</summary>
    public static string ExpectKeyFor(int clauseIndex) =>
        clauseIndex == 0 ? "expect" : $"expect_{clauseIndex.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The action tag key paired with <see cref="ExpectKeyFor"/>.</summary>
    public static string FailKeyFor(int clauseIndex) =>
        clauseIndex == 0 ? "fail" : $"fail_{clauseIndex.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Runtime bindings for a column's clauses, in written order. Empty when it has none.</summary>
    public static IReadOnlyList<ColumnRuleBinding> ToBindings(SelectColumn column)
    {
        if (column.Expectations is not { Count: > 0 } clauses) return [];

        var bindings = new List<ColumnRuleBinding>(clauses.Count);
        for (int i = 0; i < clauses.Count; i++)
        {
            var clause = clauses[i];
            bindings.Add(new ColumnRuleBinding(
                ExpectKeyFor(i), clause.Rules, clause.Action, clause.ActionExplicit));
        }
        return bindings;
    }

    /// <summary>True when the column declares at least one expectation.</summary>
    public static bool HasRules(SelectColumn column) => column.Expectations is { Count: > 0 };

    /// <summary>
    /// The stewardship tags a column's clauses publish to lineage. The rule text is the clause as
    /// the author wrote it, so what a steward reads back is what the script says.
    /// </summary>
    public static void ProjectTags(SelectColumn column, IDictionary<string, string> target)
    {
        if (column.Expectations is not { Count: > 0 } clauses) return;

        for (int i = 0; i < clauses.Count; i++)
        {
            target[ExpectKeyFor(i)] = clauses[i].Text;

            // Only a written action is projected. A defaulted WARN stays absent so the read side
            // can still say "WARN (default)" — a steward reading the catalog should be able to tell
            // a deliberate WARN from one nobody chose.
            if (!clauses[i].ActionExplicit) continue;
            target[FailKeyFor(i)] = clauses[i].Action switch
            {
                FailAction.Throw => "THROW",
                FailAction.Quarantine => "QUARANTINE",
                _ => "WARN"
            };
        }
    }

    /// <summary>
    /// A column's metadata with its expectation tags merged in, or the metadata unchanged when it
    /// declares none. Never mutates the AST's dictionary: the projection is for the lineage record,
    /// and a rule written back onto the column would then look like an authored tag.
    /// </summary>
    public static Dictionary<string, string> WithProjectedTags(
        SelectColumn column, IDictionary<string, string> metadata)
    {
        var merged = new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase);
        ProjectTags(column, merged);
        return merged;
    }
}
