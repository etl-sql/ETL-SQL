using System.Collections.Generic;
using ETL_SQL.Core;

namespace ETL_SQL.Engine.Planning
{
    /// <summary>
    /// Lightweight logical query plan produced by <see cref="PredicatePushdownOptimizer"/>.
    /// Records the optimizer-rewritten statement, predicate classifications, and the required
    /// column set from <see cref="RequiredColumnAnalyzer"/>. Consumed by the execution engine
    /// for runtime decisions and by EXPLAIN ANALYZE for plan annotation.
    /// </summary>
    public record LogicalPlan
    {
        /// <summary>The rewritten statement (may differ from input after CROSS JOIN → INNER JOIN promotion).</summary>
        public required SelectStatement Statement { get; init; }

        /// <summary>Classified WHERE predicates. Empty when there is no WHERE or no JOIN.</summary>
        public List<LogicalPredicate> Predicates { get; init; } = new();

        /// <summary>
        /// Unqualified column names referenced anywhere in the statement.
        /// Null when analysis was not possible (e.g. SELECT *).
        /// </summary>
        public HashSet<string>? RequiredColumns { get; init; }
    }

    /// <summary>A single WHERE predicate with its scope classification.</summary>
    /// <param name="Predicate">The expression.</param>
    /// <param name="Scope">Which sources it references.</param>
    /// <param name="SourceAlias">
    /// The alias of the single referenced source for <see cref="PredicateScope.LeftSingle"/>
    /// and <see cref="PredicateScope.RightSingle"/> predicates; null otherwise.
    /// </param>
    public record LogicalPredicate(Expression Predicate, PredicateScope Scope, string? SourceAlias);

    public enum PredicateScope
    {
        /// <summary>References only the FROM / left source — safe to filter before any join.</summary>
        LeftSingle,

        /// <summary>References only one right-hand JOIN source — safe to pre-filter that source.</summary>
        RightSingle,

        /// <summary>References columns from two or more sources — must be applied post-join.</summary>
        MultiSource,

        /// <summary>Contains a subquery, non-deterministic expression, or unresolvable reference — keep post-join.</summary>
        Conservative,
    }
}
