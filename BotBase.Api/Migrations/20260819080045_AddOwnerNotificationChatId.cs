using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotBase.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerNotificationChatId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_ScheduledAt",
                table: "Appointments");

            migrationBuilder.AddColumn<string>(
                name: "CrmWebhookUrl",
                table: "Businesses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "OwnerNotificationChatId",
                table: "Businesses",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Appointments",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CrmWebhookUrl",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "OwnerNotificationChatId",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Appointments");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ScheduledAt",
                table: "Appointments",
                column: "ScheduledAt");
        }
    }
}
