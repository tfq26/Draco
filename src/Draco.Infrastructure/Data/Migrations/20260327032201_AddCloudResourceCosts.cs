using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Draco.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCloudResourceCosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CloudResourceCosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceId = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    SubscriptionId = table.Column<string>(type: "text", nullable: false),
                    ResourceGroupName = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    Granularity = table.Column<string>(type: "text", nullable: false),
                    PeriodStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PeriodEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CostSource = table.Column<string>(type: "text", nullable: false),
                    RawData = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CloudResourceCosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CloudResourceCosts_UserAccounts_UserId",
                        column: x => x.UserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CloudResourceCosts_ResourceId",
                table: "CloudResourceCosts",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_CloudResourceCosts_UserId",
                table: "CloudResourceCosts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CloudResourceCosts_UserId_Provider_SubscriptionId_ResourceG~",
                table: "CloudResourceCosts",
                columns: new[] { "UserId", "Provider", "SubscriptionId", "ResourceGroupName", "PeriodEnd" });

            migrationBuilder.CreateIndex(
                name: "IX_CloudResourceCosts_UserId_ResourceId_PeriodEnd",
                table: "CloudResourceCosts",
                columns: new[] { "UserId", "ResourceId", "PeriodEnd" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CloudResourceCosts");
        }
    }
}
