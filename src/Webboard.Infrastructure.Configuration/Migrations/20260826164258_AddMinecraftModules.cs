using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Webboard.Infrastructure.Configuration.Migrations
{
    /// <inheritdoc />
    public partial class AddMinecraftModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MinecraftServerAddress",
                table: "Modules",
                type: "character varying(253)",
                maxLength: 253,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinecraftServerPort",
                table: "Modules",
                type: "integer",
                nullable: true);

            migrationBuilder.InsertData(
                table: "ModuleTypes",
                columns: new[] { "Id", "Description", "Name" },
                values: new object[] { 4, "Minecraft Java Edition server", "Minecraft" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE \"Modules\" SET \"TypeId\" = 1 WHERE \"TypeId\" = 4");

            migrationBuilder.DeleteData(
                table: "ModuleTypes",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DropColumn(
                name: "MinecraftServerAddress",
                table: "Modules");

            migrationBuilder.DropColumn(
                name: "MinecraftServerPort",
                table: "Modules");
        }
    }
}
