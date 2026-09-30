using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Webboard.Infrastructure.Configuration.Migrations
{
    /// <inheritdoc />
    public partial class RestrictModuleOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModuleCommands");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserCrudAccess_Operations",
                table: "UserCrudAccess");

            migrationBuilder.DropColumn(
                name: "CanDisable",
                table: "UserCrudAccess");

            migrationBuilder.DropColumn(
                name: "CanEnable",
                table: "UserCrudAccess");

            migrationBuilder.DropColumn(
                name: "CanExecuteCommand",
                table: "UserCrudAccess");

            migrationBuilder.DropColumn(
                name: "CanRestart",
                table: "UserCrudAccess");

            migrationBuilder.DropColumn(
                name: "CanStart",
                table: "UserCrudAccess");

            migrationBuilder.DropColumn(name: "CanStop", table: "UserCrudAccess");

            // Consolidation must not broaden any existing user's permissions.
            migrationBuilder.AddColumn<bool>(
                name: "CanOperate", table: "UserCrudAccess", type: "boolean",
                nullable: false, defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserCrudAccess_Operations",
                table: "UserCrudAccess",
                sql: "\"Resource\" = 'Modules' OR NOT \"CanOperate\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_UserCrudAccess_Operations",
                table: "UserCrudAccess");

            migrationBuilder.DropColumn(name: "CanOperate", table: "UserCrudAccess");
            migrationBuilder.AddColumn<bool>(
                name: "CanStop", table: "UserCrudAccess", type: "boolean",
                nullable: false, defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanDisable",
                table: "UserCrudAccess",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanEnable",
                table: "UserCrudAccess",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanExecuteCommand",
                table: "UserCrudAccess",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanRestart",
                table: "UserCrudAccess",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanStart",
                table: "UserCrudAccess",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ModuleCommands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HostId = table.Column<int>(type: "integer", nullable: false),
                    ModuleId = table.Column<int>(type: "integer", nullable: false),
                    CommandId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModuleCommands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModuleCommands_Hosts_HostId",
                        column: x => x.HostId,
                        principalTable: "Hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ModuleCommands_Modules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "Modules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserCrudAccess_Operations",
                table: "UserCrudAccess",
                sql: "\"Resource\" = 'Modules' OR (NOT \"CanStart\" AND NOT \"CanStop\" AND NOT \"CanRestart\" AND NOT \"CanEnable\" AND NOT \"CanDisable\" AND NOT \"CanExecuteCommand\")");

            migrationBuilder.CreateIndex(
                name: "IX_ModuleCommands_HostId",
                table: "ModuleCommands",
                column: "HostId");

            migrationBuilder.CreateIndex(
                name: "IX_ModuleCommands_ModuleId_HostId_CommandId",
                table: "ModuleCommands",
                columns: new[] { "ModuleId", "HostId", "CommandId" },
                unique: true);
        }
    }
}
