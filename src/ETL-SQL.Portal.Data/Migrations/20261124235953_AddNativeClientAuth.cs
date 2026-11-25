using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETLSQL.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNativeClientAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "native_client_auth_codes",
                columns: table => new
                {
                    code_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    transaction_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    tenant_id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    canonical_origin = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    client_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    redirect_uri = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    code_challenge = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    code_challenge_method = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    scopes = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    user_id = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    expires_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    consumed_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    issued_session_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_native_client_auth_codes", x => x.code_hash);
                    table.ForeignKey(
                        name: "fk_native_client_auth_codes_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "native_client_auth_transactions",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    tenant_id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    canonical_origin = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    client_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    redirect_uri = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    code_challenge = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    code_challenge_method = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    client_state = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    requested_scopes = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    csrf_token_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    user_id = table.Column<int>(type: "INTEGER", nullable: true),
                    status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    expires_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    completed_at = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_native_client_auth_transactions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "native_client_sessions",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    tenant_id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    user_id = table.Column<int>(type: "INTEGER", nullable: false),
                    client_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    scopes = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    last_refreshed_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    expires_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    revoked_reason = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_native_client_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_native_client_sessions_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "native_client_refresh_tokens",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    session_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    tenant_id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    user_id = table.Column<int>(type: "INTEGER", nullable: false),
                    token_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    expires_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_native_client_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_native_client_refresh_tokens_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_native_client_refresh_tokens_native_client_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "native_client_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_native_client_auth_codes_expires_at",
                table: "native_client_auth_codes",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_native_client_auth_codes_tenant_id_user_id",
                table: "native_client_auth_codes",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_native_client_auth_codes_user_id",
                table: "native_client_auth_codes",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_native_client_auth_transactions_expires_at",
                table: "native_client_auth_transactions",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_native_client_auth_transactions_tenant_id_status",
                table: "native_client_auth_transactions",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_native_client_refresh_tokens_expires_at",
                table: "native_client_refresh_tokens",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_native_client_refresh_tokens_session_id",
                table: "native_client_refresh_tokens",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_native_client_refresh_tokens_tenant_id_session_id",
                table: "native_client_refresh_tokens",
                columns: new[] { "tenant_id", "session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_native_client_refresh_tokens_token_hash",
                table: "native_client_refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_native_client_refresh_tokens_user_id",
                table: "native_client_refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_native_client_sessions_expires_at",
                table: "native_client_sessions",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_native_client_sessions_tenant_id_user_id",
                table: "native_client_sessions",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_native_client_sessions_user_id",
                table: "native_client_sessions",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "native_client_auth_codes");

            migrationBuilder.DropTable(
                name: "native_client_auth_transactions");

            migrationBuilder.DropTable(
                name: "native_client_refresh_tokens");

            migrationBuilder.DropTable(
                name: "native_client_sessions");
        }
    }
}
