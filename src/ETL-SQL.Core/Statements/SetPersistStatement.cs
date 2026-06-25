using System;

namespace ETL_SQL.Core;
public record SetPersistStatement : Statement
{
    public bool Enabled { get; init; }
}
