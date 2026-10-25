using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class ConsolidateSavedViewState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // COMPAT_BREAK: 0.22 — the pre-deployment upgrade backfills envelopes before dropping maps.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (
                        WITH candidates AS MATERIALIZED (SELECT * FROM saved_report_views WHERE state_json IS NULL OR btrim(state_json) = '')
                        SELECT 1 FROM candidates
                        WHERE (
                            json_typeof(COALESCE(NULLIF(btrim(parameters_json), ''), '{}')::json) <> 'object'
                            OR json_typeof(COALESCE(NULLIF(btrim(filters_json), ''), '{}')::json) <> 'object')
                    ) THEN RAISE EXCEPTION 'Invalid saved-view flat map; repair it before upgrading'; END IF;
                    IF EXISTS (
                        WITH candidates AS MATERIALIZED (SELECT * FROM saved_report_views WHERE state_json IS NULL OR btrim(state_json) = '')
                        SELECT 1 FROM candidates v,
                            LATERAL json_each(COALESCE(NULLIF(btrim(v.parameters_json), ''), '{}')::json) p
                        WHERE json_typeof(p.value) NOT IN ('string', 'null')
                        UNION ALL
                        SELECT 1 FROM candidates v,
                            LATERAL json_each(COALESCE(NULLIF(btrim(v.filters_json), ''), '{}')::json) p
                        WHERE json_typeof(p.value) NOT IN ('string', 'null')
                    ) THEN RAISE EXCEPTION 'Invalid saved-view flat map; repair it before upgrading'; END IF;
                END $$;
                """);
            migrationBuilder.Sql("""
                WITH candidates AS (
                    SELECT * FROM saved_report_views WHERE state_json IS NULL OR btrim(state_json) = ''
                ), maps AS (
                    SELECT v.id, CASE WHEN left(p.key, 1) = '@' THEN p.key ELSE '@' || p.key END AS name,
                        p.value, p.ordinality, 0 AS priority
                    FROM candidates v, LATERAL json_each(COALESCE(NULLIF(btrim(v.parameters_json), ''), '{}')::json) WITH ORDINALITY p
                    UNION ALL
                    SELECT v.id, CASE WHEN left(p.key, 1) = '@' THEN p.key ELSE '@' || p.key END,
                        p.value, p.ordinality, 1
                    FROM candidates v, LATERAL json_each(COALESCE(NULLIF(btrim(v.filters_json), ''), '{}')::json) WITH ORDINALITY p
                ), ranked AS (
                    SELECT *, row_number() OVER (PARTITION BY id, name COLLATE "C" ORDER BY priority DESC, ordinality DESC) AS rank FROM maps
                ), packed AS (
                    SELECT id, json_object_agg(name, value ORDER BY priority, ordinality) AS parameters FROM ranked WHERE rank = 1 GROUP BY id
                )
                UPDATE saved_report_views v SET state_json = json_build_object(
                    'schemaVersion', 1, 'scriptHash', v.script_hash, 'parameters', COALESCE(p.parameters, '{}'::json))::text
                FROM candidates c LEFT JOIN packed p ON p.id = c.id WHERE v.id = c.id;
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
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "parameters_json",
                table: "saved_report_views",
                type: "text",
                nullable: true);
        }
    }
}
