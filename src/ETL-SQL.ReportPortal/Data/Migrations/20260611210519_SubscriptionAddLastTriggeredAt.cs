using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETL_SQL.ReportPortal.Data.Migrations
{
    /// <inheritdoc />
    public partial class SubscriptionAddLastTriggeredAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastTriggeredAt",
                table: "Subscriptions",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastTriggeredAt",
                table: "Subscriptions");
        }
    }
}
