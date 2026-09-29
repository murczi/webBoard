using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Webboard.Infrastructure.Configuration.Migrations
{
    /// <inheritdoc />
    public partial class AddSteamModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SteamQueryPlayers",
                table: "Modules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SteamQueryPort",
                table: "Modules",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SteamServerAddress",
                table: "Modules",
                type: "character varying(253)",
                maxLength: 253,
                nullable: true);

            migrationBuilder.InsertData(
                table: "ModuleTypes",
                columns: new[] { "Id", "Description", "Name" },
                values: new object[] { 5, "Steam A2S game server", "Steam" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Modules_SteamPort",
                table: "Modules",
                sql: "\"SteamQueryPort\" IS NULL OR \"SteamQueryPort\" BETWEEN 1 AND 65535");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Modules_SteamPort",
                table: "Modules");

            migrationBuilder.DeleteData(
                table: "ModuleTypes",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DropColumn(
                name: "SteamQueryPlayers",
                table: "Modules");

            migrationBuilder.DropColumn(
                name: "SteamQueryPort",
                table: "Modules");

            migrationBuilder.DropColumn(
                name: "SteamServerAddress",
                table: "Modules");
        }
    }
}
