using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BotBase.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCrmLite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_BusinessId",
                table: "Appointments");

            migrationBuilder.AddColumn<Guid>(
                name: "MasterId",
                table: "WorkSchedules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RemindersEnabled",
                table: "Businesses",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ReviewRequestsEnabled",
                table: "Businesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ReviewUrl",
                table: "Businesses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeZone",
                table: "Businesses",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "Asia/Yerevan");

            migrationBuilder.AddColumn<int>(
                name: "WinbackAfterDays",
                table: "Businesses",
                type: "integer",
                nullable: false,
                defaultValue: 45);

            migrationBuilder.AddColumn<bool>(
                name: "WinbackEnabled",
                table: "Businesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClientConfirmedAt",
                table: "Appointments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClientId",
                table: "Appointments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MasterId",
                table: "Appointments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NoShow",
                table: "Appointments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "Price",
                table: "Appointments",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Rating",
                table: "Appointments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReminderSentAt",
                table: "Appointments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewRequestedAt",
                table: "Appointments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "Appointments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Clients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PhoneKey = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TelegramChatId = table.Column<long>(type: "bigint", nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    WinbackSentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Clients_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Masters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Masters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Masters_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MasterProcedures",
                columns: table => new
                {
                    MasterId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcedureId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MasterProcedures", x => new { x.MasterId, x.ProcedureId });
                    table.ForeignKey(
                        name: "FK_MasterProcedures_Masters_MasterId",
                        column: x => x.MasterId,
                        principalTable: "Masters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MasterProcedures_Procedures_ProcedureId",
                        column: x => x.ProcedureId,
                        principalTable: "Procedures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkSchedules_MasterId",
                table: "WorkSchedules",
                column: "MasterId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_BusinessId_ScheduledAt",
                table: "Appointments",
                columns: new[] { "BusinessId", "ScheduledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ClientId",
                table: "Appointments",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_MasterId",
                table: "Appointments",
                column: "MasterId");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_BusinessId_PhoneKey",
                table: "Clients",
                columns: new[] { "BusinessId", "PhoneKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MasterProcedures_ProcedureId",
                table: "MasterProcedures",
                column: "ProcedureId");

            migrationBuilder.CreateIndex(
                name: "IX_Masters_BusinessId",
                table: "Masters",
                column: "BusinessId");

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_Clients_ClientId",
                table: "Appointments",
                column: "ClientId",
                principalTable: "Clients",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_Masters_MasterId",
                table: "Appointments",
                column: "MasterId",
                principalTable: "Masters",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkSchedules_Masters_MasterId",
                table: "WorkSchedules",
                column: "MasterId",
                principalTable: "Masters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // Существующие записи → карточки клиентов (по цифрам телефона), цена из прайса.
            migrationBuilder.Sql(@"
INSERT INTO ""Clients"" (""Id"", ""BusinessId"", ""Name"", ""Phone"", ""PhoneKey"", ""TelegramChatId"", ""CreatedAt"")
SELECT gen_random_uuid(), x.""BusinessId"", x.""ClientName"", x.""ClientPhone"", x.key, x.chat, x.created
FROM (
    SELECT DISTINCT ON (a.""BusinessId"", left(regexp_replace(a.""ClientPhone"", '\D', '', 'g'), 30))
        a.""BusinessId"", a.""ClientName"", a.""ClientPhone"", left(regexp_replace(a.""ClientPhone"", '\D', '', 'g'), 30) AS key,
        c.""TelegramChatId"" AS chat, a.""CreatedAt"" AS created
    FROM ""Appointments"" a
    LEFT JOIN ""Conversations"" c ON c.""Id"" = a.""ConversationId""
    WHERE left(regexp_replace(a.""ClientPhone"", '\D', '', 'g'), 30) <> ''
    ORDER BY a.""BusinessId"", left(regexp_replace(a.""ClientPhone"", '\D', '', 'g'), 30), a.""CreatedAt"" DESC
) x;

UPDATE ""Appointments"" a SET ""ClientId"" = cl.""Id""
FROM ""Clients"" cl
WHERE cl.""BusinessId"" = a.""BusinessId"" AND cl.""PhoneKey"" = left(regexp_replace(a.""ClientPhone"", '\D', '', 'g'), 30);

UPDATE ""Appointments"" a SET ""Price"" = p.""Price""
FROM ""Procedures"" p
WHERE p.""BusinessId"" = a.""BusinessId"" AND p.""IsActive"" AND lower(trim(p.""Name"")) = lower(trim(a.""ProcedureName""));

-- Прошедшие визиты помечаем обработанными: автосообщения не должны писать клиентам о старых визитах.
UPDATE ""Appointments"" SET ""ReminderSentAt"" = now(), ""ReviewRequestedAt"" = now() WHERE ""ScheduledAt"" < now();
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_Clients_ClientId",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_Masters_MasterId",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkSchedules_Masters_MasterId",
                table: "WorkSchedules");

            migrationBuilder.DropTable(
                name: "Clients");

            migrationBuilder.DropTable(
                name: "MasterProcedures");

            migrationBuilder.DropTable(
                name: "Masters");

            migrationBuilder.DropIndex(
                name: "IX_WorkSchedules_MasterId",
                table: "WorkSchedules");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_BusinessId_ScheduledAt",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_ClientId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_MasterId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "MasterId",
                table: "WorkSchedules");

            migrationBuilder.DropColumn(
                name: "RemindersEnabled",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "ReviewRequestsEnabled",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "ReviewUrl",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "TimeZone",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "WinbackAfterDays",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "WinbackEnabled",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "ClientConfirmedAt",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "ClientId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "MasterId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "NoShow",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "Price",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "Rating",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "ReminderSentAt",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "ReviewRequestedAt",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Appointments");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_BusinessId",
                table: "Appointments",
                column: "BusinessId");
        }
    }
}
