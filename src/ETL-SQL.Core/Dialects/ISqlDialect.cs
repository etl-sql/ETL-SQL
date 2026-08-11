using System;
using System.Collections.Generic;

namespace ETL_SQL.Core.Dialects;

public interface ISqlDialect
{
    string Name { get; }

    string RewriteIdentifier(string name);
    string RewriteFunctionCall(string functionName, IReadOnlyList<Expression> arguments, Func<Expression, string> compileArg);
    
    bool SupportsTop { get; }
    string FormatTop(string compiledTop, bool percent, bool withTies);
    string FormatOffsetLimit(string? compiledOffset, string? compiledLimit);
    
    string FormatTableAlias(string alias);
}
