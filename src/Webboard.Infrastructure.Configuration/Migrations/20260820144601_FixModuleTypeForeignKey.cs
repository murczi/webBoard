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
                nullable: false,
                defaultValue: 0);

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
