using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETL_SQL.ReportPortal.Data.Migrations
{
    [DbContext(typeof(PortalDbContext))]
    [Migration("20260524000001_AddDatasetEncryptionMode")]
    public partial class AddDatasetEncryptionMode : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EncryptionMode",
                table: "Datasets",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1); // MachineBound is the standard default
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "EncryptionMode", table: "Datasets");
        }
    }
}
