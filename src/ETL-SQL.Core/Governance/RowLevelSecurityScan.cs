using System;
using System.Linq;

namespace ETL_SQL.Core.Governance;

/// <summary>
/// Conservative static detection of row-level-security identity references in a script. Used by the
/// host to decide that a report is identity-sensitive (runs per viewer, never shared-cached).
/// Scans raw text, so comments/strings may over-flag — deliberate, because over-flagging only costs
/// caching while under-flagging would leak one viewer's filtered rows to another.
/// See Docs/Design/RowLevelSecurity.md.
/// </summary>
public static class RowLevelSecurityScan
{
    private static readonly string[] IdentityTokens =
        { "@@CURRENT_USER", "@@REAL_USER", "@@IS_ADMIN", "HAS_GROUP", "HAS_ROLE", "USER_GROUPS", "USER_ROLES" };

    public static bool ReferencesIdentity(string? scriptText) =>
        !string.IsNullOrEmpty(scriptText)
        && IdentityTokens.Any(token => scriptText.Contains(token, StringComparison.OrdinalIgnoreCase));
}
