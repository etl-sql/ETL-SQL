using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConsolidateSavedViewState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // COMPAT_BREAK: 0.22 — preserve flat string maps once, then store only the envelope.
            // An existing envelope wins. Invalid legacy maps stop the transactional upgrade.
            // Preserve assignment order: database case folding differs from OrdinalIgnoreCase.
            migrationBuilder.Sql("""
                SELECT json(CASE WHEN
                    json_type(COALESCE(NULLIF(trim(parameters_json), ''), '{}')) <> 'object'
                    OR json_type(COALESCE(NULLIF(trim(filters_json), ''), '{}')) <> 'object'
                    OR EXISTS (SELECT 1 FROM json_each(COALESCE(NULLIF(trim(parameters_json), ''), '{}')) WHERE type NOT IN ('text', 'null'))
                    OR EXISTS (SELECT 1 FROM json_each(COALESCE(NULLIF(trim(filters_json), ''), '{}')) WHERE type NOT IN ('text', 'null'))
                    THEN 'Invalid saved-view flat map; repair it before upgrading'
                    ELSE 'null' END)
                FROM saved_report_views WHERE state_json IS NULL OR trim(state_json) = '';
                """);
            migrationBuilder.Sql("""
                UPDATE saved_report_views SET state_json = json_object(
                    'schemaVersion', 1, 'scriptHash', script_hash,
                    'parameters', json((
                        SELECT json_group_object(name, value) FROM (
                            SELECT name, value FROM (
                                SELECT name, value, priority, ordinal, row_number() OVER (
                                    PARTITION BY name COLLATE BINARY ORDER BY priority DESC, ordinal DESC) AS rank
                                FROM (
                                    SELECT CASE WHEN substr(key, 1, 1) = '@' THEN key ELSE '@' || key END AS name,
                                        value, id AS ordinal, 0 AS priority
                                    FROM json_each(COALESCE(NULLIF(trim(parameters_json), ''), '{}'))
                                    UNION ALL
                                    SELECT CASE WHEN substr(key, 1, 1) = '@' THEN key ELSE '@' || key END,
                                        value, id, 1
                                    FROM json_each(COALESCE(NULLIF(trim(filters_json), ''), '{}'))
                                )
                            ) WHERE rank = 1 ORDER BY priority, ordinal
                        )
                    )))
                WHERE state_json IS NULL OR trim(state_json) = '';
                """);
            migrationBuilder.DropColumn(
                name: "filters_json",
                table: "saved_report_views");

            migrationBuilder.DropColumn(
                name: "parameters_json",
                table: "saved_report_views");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "filters_json",
                table: "saved_report_views",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "parameters_json",
                table: "saved_report_views",
                type: "TEXT",
                nullable: true);
        }
    }
}
