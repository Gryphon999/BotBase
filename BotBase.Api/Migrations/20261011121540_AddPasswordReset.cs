using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotBase.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPasswordReset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordResetExpiresAt",
                table: "Businesses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PasswordResetTokenHash",
                table: "Businesses",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PasswordResetExpiresAt",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "PasswordResetTokenHash",
                table: "Businesses");
        }
    }
}
