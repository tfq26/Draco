using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Draco.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationDeliveryPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NotificationPreferencesJson",
                table: "UserAccounts",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotificationPreferencesJson",
                table: "UserAccounts");
        }
    }
}
