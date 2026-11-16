using System;
using System.Collections.Generic;

namespace ETL_SQL.Core.Dialects;

public interface ISqlDialect
{
    string Name { get; }

    string RewriteIdentifier(string name);
    string RewriteFunctionCall(string functionName, IReadOnlyList<Expression> arguments, Func<Expression, string> compileArg);
    string FormatStringConcat(string left, string right);

    bool SupportsTop { get; }

    /// <summary>True when the source reads <c>NULLS FIRST</c> and <c>NULLS LAST</c> in <c>ORDER BY</c>.</summary>
    bool SupportsNullsOrdering { get; }
    string FormatTop(string compiledTop, bool percent, bool withTies);
    string FormatOffsetLimit(string? compiledOffset, string? compiledLimit);

    string FormatTableAlias(string alias);
}
