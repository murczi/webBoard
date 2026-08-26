using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Webboard.Infrastructure.Configuration.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemdModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ServiceName",
                table: "Modules",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.InsertData(
                table: "ModuleTypes",
                columns: new[] { "Id", "Description", "Name" },
                values: new object[] { 3, "systemd service", "Systemd" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE \"Modules\" SET \"TypeId\" = 1 WHERE \"TypeId\" = 3");

            migrationBuilder.DeleteData(
                table: "ModuleTypes",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DropColumn(
                name: "ServiceName",
                table: "Modules");
        }
    }
}
