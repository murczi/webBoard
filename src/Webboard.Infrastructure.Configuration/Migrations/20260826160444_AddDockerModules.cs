using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Webboard.Infrastructure.Configuration.Migrations
{
    /// <inheritdoc />
    public partial class AddDockerModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContainerId",
                table: "Modules",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.InsertData(
                table: "ModuleTypes",
                columns: new[] { "Id", "Description", "Name" },
                values: new object[] { 2, "Docker container", "Docker" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE \"Modules\" SET \"TypeId\" = 1 WHERE \"TypeId\" = 2");

            migrationBuilder.DeleteData(
                table: "ModuleTypes",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DropColumn(
                name: "ContainerId",
                table: "Modules");
        }
    }
}
