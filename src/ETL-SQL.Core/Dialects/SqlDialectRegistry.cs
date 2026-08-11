using System;
using System.Collections.Concurrent;

namespace ETL_SQL.Core.Dialects;

public static class SqlDialectRegistry
{
    private static readonly ConcurrentDictionary<string, ISqlDialect> _dialects = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ISqlDialect _defaultDialect = new DefaultSqlDialect();

    static SqlDialectRegistry()
    {
        Register(new MssqlDialect());
        Register(new PostgresDialect());
        Register(new OracleDialect());
    }

    public static void Register(ISqlDialect dialect)
    {
        _dialects[dialect.Name] = dialect;
    }

    public static ISqlDialect GetDialect(string name)
    {
        if (string.IsNullOrEmpty(name)) return _defaultDialect;
        return _dialects.TryGetValue(name, out var dialect) ? dialect : _defaultDialect;
    }
}
