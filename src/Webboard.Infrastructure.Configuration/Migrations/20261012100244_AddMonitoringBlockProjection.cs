using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Webboard.Infrastructure.Configuration.Migrations
{
    /// <inheritdoc />
    public partial class AddMonitoringBlockProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MonitoringBlocks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ModuleId = table.Column<int>(type: "integer", nullable: false),
                    Start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    End = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonitoringBlocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MonitoringBlocks_Modules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "Modules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringBlocks_End",
                table: "MonitoringBlocks",
                column: "End");

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringBlocks_ModuleId_Start",
                table: "MonitoringBlocks",
                columns: new[] { "ModuleId", "Start" });
            migrationBuilder.Sql("""
                WITH source AS (
                    SELECT "ModuleId", "Timestamp" AS "Start",
                        LEAST("FreshUntil", LEAD("Timestamp", 1, "FreshUntil") OVER (PARTITION BY "ModuleId" ORDER BY "Timestamp", "Id")) AS "End", "State", "Message"
                    FROM "MonitoringResults"
                ), marked AS (
                    SELECT *, CASE WHEN "Start" = LAG("End") OVER w AND "State" = LAG("State") OVER w
                        AND "Message" = LAG("Message") OVER w THEN 0 ELSE 1 END AS boundary
                    FROM source WHERE "End" > "Start" WINDOW w AS (PARTITION BY "ModuleId" ORDER BY "Start")
                ), grouped AS (
                    SELECT *, SUM(boundary) OVER (PARTITION BY "ModuleId" ORDER BY "Start") AS island FROM marked
                ) INSERT INTO "MonitoringBlocks" ("ModuleId", "Start", "End", "State", "Message")
                    SELECT "ModuleId", MIN("Start"), MAX("End"), "State", "Message"
                    FROM grouped GROUP BY "ModuleId", island, "State", "Message";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MonitoringBlocks");
        }
    }
}
