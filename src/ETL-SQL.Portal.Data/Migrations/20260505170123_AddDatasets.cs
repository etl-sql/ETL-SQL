using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETL_SQL.Portal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDatasets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Datasets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    FolderPath = table.Column<string>(type: "TEXT", nullable: false),
                    ParquetFilePath = table.Column<string>(type: "TEXT", nullable: false),
                    OwningReportId = table.Column<int>(type: "INTEGER", nullable: true),
                    SourceQuery = table.Column<string>(type: "TEXT", nullable: false),
                    AccessLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    LastRefresh = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Ttl = table.Column<string>(type: "TEXT", nullable: true),
                    RefreshInterval = table.Column<string>(type: "TEXT", nullable: true),
                    RowCount = table.Column<long>(type: "INTEGER", nullable: false),
                    ColumnSchema = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Datasets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Datasets_Reports_OwningReportId",
                        column: x => x.OwningReportId,
                        principalTable: "Reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "DatasetAcls",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DatasetId = table.Column<int>(type: "INTEGER", nullable: false),
                    GroupId = table.Column<int>(type: "INTEGER", nullable: false),
                    Permission = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatasetAcls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DatasetAcls_Datasets_DatasetId",
                        column: x => x.DatasetId,
                        principalTable: "Datasets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DatasetAcls_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DatasetAcls_DatasetId",
                table: "DatasetAcls",
                column: "DatasetId");

            migrationBuilder.CreateIndex(
                name: "IX_DatasetAcls_GroupId",
                table: "DatasetAcls",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Datasets_FolderPath_Name",
                table: "Datasets",
                columns: new[] { "FolderPath", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Datasets_OwningReportId",
                table: "Datasets",
                column: "OwningReportId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DatasetAcls");

            migrationBuilder.DropTable(
                name: "Datasets");
        }
    }
}
