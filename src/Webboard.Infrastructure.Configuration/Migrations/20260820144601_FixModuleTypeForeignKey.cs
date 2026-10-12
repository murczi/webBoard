using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Webboard.Infrastructure.Configuration.Migrations
{
    /// <inheritdoc />
    public partial class FixModuleTypeForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Modules_ModuleTypes_Id",
                table: "Modules");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "Modules",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<int>(
                name: "ModuleTypeId",
                table: "Modules",
                type: "integer",
                nullable: true);

            // The initial FK used Modules.Id as the type reference. Preserve it.
            migrationBuilder.Sql("UPDATE \"Modules\" SET \"ModuleTypeId\" = \"Id\"");
            migrationBuilder.AlterColumn<int>(name: "ModuleTypeId", table: "Modules", type: "integer",
                nullable: false, oldClrType: typeof(int), oldType: "integer", oldNullable: true);
            migrationBuilder.Sql("SELECT setval(pg_get_serial_sequence('\"Modules\"', 'Id'), GREATEST(COALESCE(MAX(\"Id\"), 0), 1), COALESCE(MAX(\"Id\"), 0) > 0) FROM \"Modules\"");

            migrationBuilder.CreateIndex(
                name: "IX_Modules_ModuleTypeId",
                table: "Modules",
                column: "ModuleTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Modules_ModuleTypes_ModuleTypeId",
                table: "Modules",
                column: "ModuleTypeId",
                principalTable: "ModuleTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Modules_ModuleTypes_ModuleTypeId",
                table: "Modules");

            migrationBuilder.DropIndex(
                name: "IX_Modules_ModuleTypeId",
                table: "Modules");

            migrationBuilder.DropColumn(
                name: "ModuleTypeId",
                table: "Modules");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "Modules",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer")
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddForeignKey(
                name: "FK_Modules_ModuleTypes_Id",
                table: "Modules",
                column: "Id",
                principalTable: "ModuleTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
