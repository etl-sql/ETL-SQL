using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETL_SQL.ReportPortal.Data.Migrations
{
    /// <inheritdoc />
    public partial class RecipientDeliveryIsolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SubscriptionDeliveries_SubscriptionId_TriggerKey",
                table: "SubscriptionDeliveries");

            migrationBuilder.AddColumn<string>(
                name: "RecipientKey",
                table: "SubscriptionDeliveries",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionDeliveries_SubscriptionId_TriggerKey_RecipientKey",
                table: "SubscriptionDeliveries",
                columns: new[] { "SubscriptionId", "TriggerKey", "RecipientKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SubscriptionDeliveries_SubscriptionId_TriggerKey_RecipientKey",
                table: "SubscriptionDeliveries");

            migrationBuilder.DropColumn(
                name: "RecipientKey",
                table: "SubscriptionDeliveries");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionDeliveries_SubscriptionId_TriggerKey",
                table: "SubscriptionDeliveries",
                columns: new[] { "SubscriptionId", "TriggerKey" },
                unique: true);
        }
    }
}
