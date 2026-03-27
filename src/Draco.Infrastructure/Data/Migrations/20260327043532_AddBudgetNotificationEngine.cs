using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Draco.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgetNotificationEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CostBudgets_UserId_Provider_SubscriptionId_Name",
                table: "CostBudgets");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastEvaluatedAt",
                table: "SystemNotifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Metadata",
                table: "SystemNotifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotificationKey",
                table: "SystemNotifications",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "SystemNotifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedAt",
                table: "SystemNotifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResourceId",
                table: "SystemNotifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Service",
                table: "SystemNotifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Severity",
                table: "SystemNotifications",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceRule",
                table: "SystemNotifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubscriptionId",
                table: "SystemNotifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BudgetSource",
                table: "CostBudgets",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "CurrentSpend",
                table: "CostBudgets",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalBudgetId",
                table: "CostBudgets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ForecastSpend",
                table: "CostBudgets",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSyncedAt",
                table: "CostBudgets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Scope",
                table: "CostBudgets",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_SystemNotifications_UserId_NotificationKey",
                table: "SystemNotifications",
                columns: new[] { "UserId", "NotificationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CostBudgets_UserId_Provider_SubscriptionId_Name_BudgetSource",
                table: "CostBudgets",
                columns: new[] { "UserId", "Provider", "SubscriptionId", "Name", "BudgetSource" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SystemNotifications_UserId_NotificationKey",
                table: "SystemNotifications");

            migrationBuilder.DropIndex(
                name: "IX_CostBudgets_UserId_Provider_SubscriptionId_Name_BudgetSource",
                table: "CostBudgets");

            migrationBuilder.DropColumn(
                name: "LastEvaluatedAt",
                table: "SystemNotifications");

            migrationBuilder.DropColumn(
                name: "Metadata",
                table: "SystemNotifications");

            migrationBuilder.DropColumn(
                name: "NotificationKey",
                table: "SystemNotifications");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "SystemNotifications");

            migrationBuilder.DropColumn(
                name: "ResolvedAt",
                table: "SystemNotifications");

            migrationBuilder.DropColumn(
                name: "ResourceId",
                table: "SystemNotifications");

            migrationBuilder.DropColumn(
                name: "Service",
                table: "SystemNotifications");

            migrationBuilder.DropColumn(
                name: "Severity",
                table: "SystemNotifications");

            migrationBuilder.DropColumn(
                name: "SourceRule",
                table: "SystemNotifications");

            migrationBuilder.DropColumn(
                name: "SubscriptionId",
                table: "SystemNotifications");

            migrationBuilder.DropColumn(
                name: "BudgetSource",
                table: "CostBudgets");

            migrationBuilder.DropColumn(
                name: "CurrentSpend",
                table: "CostBudgets");

            migrationBuilder.DropColumn(
                name: "ExternalBudgetId",
                table: "CostBudgets");

            migrationBuilder.DropColumn(
                name: "ForecastSpend",
                table: "CostBudgets");

            migrationBuilder.DropColumn(
                name: "LastSyncedAt",
                table: "CostBudgets");

            migrationBuilder.DropColumn(
                name: "Scope",
                table: "CostBudgets");

            migrationBuilder.CreateIndex(
                name: "IX_CostBudgets_UserId_Provider_SubscriptionId_Name",
                table: "CostBudgets",
                columns: new[] { "UserId", "Provider", "SubscriptionId", "Name" },
                unique: true);
        }
    }
}
