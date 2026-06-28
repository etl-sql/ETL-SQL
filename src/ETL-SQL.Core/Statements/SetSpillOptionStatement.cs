using System;

namespace ETL_SQL.Core;

public enum SpillOptionType
{
    Encryption,
    Compression
}

public record SetSpillOptionStatement : Statement
{
    public SpillOptionType Option { get; }
    public bool Enabled { get; }

    public SetSpillOptionStatement(SpillOptionType option, bool enabled)
    {
        Option = option;
        Enabled = enabled;
    }

    public override string ToSql() => $"SET SPILL_{Option.ToString().ToUpperInvariant()} {(Enabled ? "ON" : "OFF")};";
}
