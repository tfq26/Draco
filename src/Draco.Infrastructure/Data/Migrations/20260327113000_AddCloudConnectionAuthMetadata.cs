using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Draco.Infrastructure.Data.Migrations
{
    public partial class AddCloudConnectionAuthMetadata : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthType",
                table: "CloudConnections",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AwsRoleArn",
                table: "CloudConnections",
                type: "text",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AuthType",
                table: "CloudConnections");

            migrationBuilder.DropColumn(
                name: "AwsRoleArn",
                table: "CloudConnections");
        }
    }
}
