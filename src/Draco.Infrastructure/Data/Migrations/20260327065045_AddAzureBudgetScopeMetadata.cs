using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Draco.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAzureBudgetScopeMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NotificationSettingsJson",
                table: "CostBudgets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScopeDisplayName",
                table: "CostBudgets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScopeType",
                table: "CostBudgets",
                type: "text",
                nullable: false,
                defaultValue: "Subscription");

            migrationBuilder.Sql("""
                UPDATE "CostBudgets"
                SET "ScopeType" = 'Subscription'
                WHERE "ScopeType" IS NULL OR "ScopeType" = '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotificationSettingsJson",
                table: "CostBudgets");

            migrationBuilder.DropColumn(
                name: "ScopeDisplayName",
                table: "CostBudgets");

            migrationBuilder.DropColumn(
                name: "ScopeType",
                table: "CostBudgets");
        }
    }
}
