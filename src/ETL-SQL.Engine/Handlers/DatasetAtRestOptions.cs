using System.Collections.Generic;
using ETL_SQL.Core;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Builds the at-rest encryption options for a portal dataset's Parquet cache.
    /// When a portal-managed key is supplied the cache is encrypted with that key
    /// (ENCRYPT=PASSWORD), making it portal-bound and portable across hosts; otherwise it falls
    /// back to host-bound ENCRYPT=MACHINE (the legacy, non-portable default).
    /// </summary>
    internal static class DatasetAtRestOptions
    {
        internal static void Apply(Dictionary<string, Expression> opts, string? atRestKey)
        {
            if (!string.IsNullOrWhiteSpace(atRestKey))
            {
                opts["ENCRYPT"]  = new LiteralExpression("PASSWORD", TokenType.STRING_LITERAL);
                opts["PASSWORD"] = new LiteralExpression(atRestKey, TokenType.STRING_LITERAL);
            }
            else
            {
                opts["ENCRYPT"] = new LiteralExpression("MACHINE", TokenType.STRING_LITERAL);
            }
        }
    }
}
