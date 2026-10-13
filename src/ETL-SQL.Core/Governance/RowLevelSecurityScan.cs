using System;
using System.Collections.Generic;
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

    public static bool ReferencesIdentity(string? scriptText) => IdentityReferences(scriptText).Count > 0;

    /// <summary>
    /// The identity tokens the script actually mentions, in a stable order.
    ///
    /// <see cref="ReferencesIdentity"/> answers the host's question — cache this per viewer or not.
    /// This answers an operator's: <em>which</em> identity does the report filter on? "Restricted by
    /// HAS_GROUP" is something an administrator can reason about; "identity-sensitive: true" is not.
    /// Same conservative raw-text scan, so the same over-flagging caveat applies.
    /// </summary>
    public static IReadOnlyList<string> IdentityReferences(string? scriptText)
    {
        if (string.IsNullOrEmpty(scriptText)) return [];
        var references = IdentityTokens.Where(token => scriptText.Contains(token, StringComparison.OrdinalIgnoreCase)).ToList();
        // COMPAT_BREAK: 0.21.0 — unresolved script dependencies cannot establish shared-cache safety.
        // Includes can be dynamic or change independently of their parent. Run such reports per
        // viewer even when no identity token is visible in the parent text.
        try
        {
            var tokens = new Parser.Lexer(scriptText).Tokenize();
            if (tokens.Zip(tokens.Skip(1)).Any(pair => pair.First.Type == Parser.TokenType.RUN && pair.Second.Type == Parser.TokenType.SCRIPT))
                references.Add("RUN SCRIPT");
        }
        catch (Exception)
        {
            references.Add("UNRESOLVED SCRIPT");
        }
        return references.OrderBy(token => token, StringComparer.Ordinal).ToArray();
    }
}
