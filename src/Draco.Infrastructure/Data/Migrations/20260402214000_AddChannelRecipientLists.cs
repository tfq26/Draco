using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Draco.Infrastructure.Data.Migrations;

public partial class AddChannelRecipientLists : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "SmsRecipientsJson",
            table: "UserAccounts",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "WhatsAppRecipientsJson",
            table: "UserAccounts",
            type: "text",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "SmsRecipientsJson",
            table: "UserAccounts");

        migrationBuilder.DropColumn(
            name: "WhatsAppRecipientsJson",
            table: "UserAccounts");
    }
}
