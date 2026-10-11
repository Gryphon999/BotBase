using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotBase.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCrmAccessAndLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CrmEnabled",
                table: "Businesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "CrmUntil",
                table: "Businesses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MessagesLanguage",
                table: "Businesses",
                type: "text",
                nullable: false,
                defaultValue: "ru");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CrmEnabled",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "CrmUntil",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "MessagesLanguage",
                table: "Businesses");
        }
    }
}
