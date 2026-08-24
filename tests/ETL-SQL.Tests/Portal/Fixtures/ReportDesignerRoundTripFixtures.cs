using ETL_SQL.Portal.Models;

namespace ETL_SQL.Tests.Portal;

/// <summary>
/// Shared Report-SQL source fixtures for validating surgical designer mutations. Future
/// nested grammar-of-graphics clauses should extend these fixtures before their first host rollout.
/// </summary>
internal static class ReportDesignerRoundTripFixtures
{
    internal const string NestedClauseTriviaScript = """
        -- preserve the complete CTE chain
        WITH orders AS (
            SELECT category, amount FROM source.orders
        ), ranked AS (
            SELECT category, amount, ROW_NUMBER() OVER (ORDER BY amount DESC) AS rn
            FROM orders
        )
        SELECT category, amount INTO #stage FROM ranked WHERE rn <= 10;

        CREATE VISUAL chart AS BAR (
            TITLE = 'Before',
            SOURCE = &data,
            MAPPINGS (
                -- category binding stays with the author
                X = category,
                /* amount binding */ Y = amount
            ),
            OPTIONS (
                -- retain option rationale
                LEGEND = 'ON'
            ),
            ACTIONS (
                -- retain action rationale
                ON_CLICK = SET_PARAMETER(@selected, category)
            ),
            INTERACTIONS (
                /* selection policy */ ON_SELECT = HIGHLIGHT
            ),
            STYLE (
                -- retain sizing rationale
                WIDTH = '100%', HEIGHT = '420px'
            )
        );

        CREATE PAGE [Dashboard] AS DASHBOARD (
            LAYOUT (STRUCTURE = 'A', MAP ('A' = chart))
        );
        """;

    internal static DesignerStateDto NestedClauseTriviaState(string title) => new(
        [new DesignerPageDto(
            "p1",
            "Dashboard",
            "Dashboard",
            [new DesignerVisualDto(
                "v1",
                "chart",
                "BAR",
                1,
                1,
                12,
                4,
                title,
                "data",
                new Dictionary<string, string> { ["X"] = "category", ["Y"] = "amount" },
                new Dictionary<string, string>
                {
                    ["LEGEND"] = "ON",
                    ["action:ON_CLICK"] = "SET_PARAMETER(@selected, category)",
                    ["interaction:ON_SELECT"] = "HIGHLIGHT",
                    ["WIDTH"] = "100%",
                    ["HEIGHT"] = "420px"
                })])],
        []);

    /// <summary>
    /// Rewrites <paramref name="script"/> so every line break is <paramref name="lineEnding"/>.
    /// </summary>
    /// <remarks>
    /// The sources are C# raw string literals, so on a checkout with <c>core.autocrlf=true</c> they
    /// already hold CRLF. Substituting LF for LF would leave every CR in place and the LF case would
    /// assert against a CRLF script — a false red on every Windows checkout. Normalizing to LF first
    /// makes the requested ending the only one present, per AGENTS.md §19.
    /// </remarks>
    internal static string WithLineEnding(string script, string lineEnding) =>
        script.Replace("\r\n", "\n", StringComparison.Ordinal)
              .Replace("\r", "\n", StringComparison.Ordinal)
              .Replace("\n", lineEnding, StringComparison.Ordinal);
}
