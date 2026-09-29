using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Webboard.Infrastructure.Configuration.Migrations
{
    /// <inheritdoc />
    public partial class AddModuleOperationsAndPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_UserCrudAccess_ReadOnlyResources",
                table: "UserCrudAccess");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserCrudAccess_Resource",
                table: "UserCrudAccess");

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

            migrationBuilder.AddColumn<bool>(
                name: "CanStop",
                table: "UserCrudAccess",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAt",
                table: "AuditLogs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExitCode",
                table: "AuditLogs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Failure",
                table: "AuditLogs",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Operation",
                table: "AuditLogs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OperationId",
                table: "AuditLogs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Outcome",
                table: "AuditLogs",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Output",
                table: "AuditLogs",
                type: "character varying(16384)",
                maxLength: 16384,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetSnapshot",
                table: "AuditLogs",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ModuleCommands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ModuleId = table.Column<int>(type: "integer", nullable: false),
                    HostId = table.Column<int>(type: "integer", nullable: false),
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

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserCrudAccess_ReadOnlyResources",
                table: "UserCrudAccess",
                sql: "\"Resource\" NOT IN ('ModuleTypes', 'AuditLogs', 'MonitoringHistory') OR (NOT \"CanCreate\" AND NOT \"CanUpdate\" AND NOT \"CanDelete\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserCrudAccess_Resource",
                table: "UserCrudAccess",
                sql: "\"Resource\" IN ('Users', 'Hosts', 'Modules', 'ModuleTypes', 'AuditLogs', 'MonitoringHistory')");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_OperationId",
                table: "AuditLogs",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModuleCommands_HostId",
                table: "ModuleCommands",
                column: "HostId");

            migrationBuilder.CreateIndex(
                name: "IX_ModuleCommands_ModuleId_HostId_CommandId",
                table: "ModuleCommands",
                columns: new[] { "ModuleId", "HostId", "CommandId" },
                unique: true);
            migrationBuilder.Sql("""
                INSERT INTO "UserCrudAccess" ("UserId", "Resource", "CanRead", "CanCreate", "CanUpdate", "CanDelete", "CanStart", "CanStop", "CanRestart", "CanEnable", "CanDisable", "CanExecuteCommand")
                SELECT "UserId", 'MonitoringHistory', true, false, false, false, false, false, false, false, false, false
                FROM "UserCrudAccess" WHERE "Resource" = 'Modules' AND "CanRead"
                ON CONFLICT ("UserId", "Resource") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM \"UserCrudAccess\" WHERE \"Resource\" = 'MonitoringHistory'");

            migrationBuilder.DropTable(
                name: "ModuleCommands");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserCrudAccess_Operations",
                table: "UserCrudAccess");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserCrudAccess_ReadOnlyResources",
                table: "UserCrudAccess");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserCrudAccess_Resource",
                table: "UserCrudAccess");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_OperationId",
                table: "AuditLogs");

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

            migrationBuilder.DropColumn(
                name: "CanStop",
                table: "UserCrudAccess");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "ExitCode",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "Failure",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "Operation",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "OperationId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "Outcome",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "Output",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "TargetSnapshot",
                table: "AuditLogs");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserCrudAccess_ReadOnlyResources",
                table: "UserCrudAccess",
                sql: "\"Resource\" NOT IN ('ModuleTypes', 'AuditLogs') OR (NOT \"CanCreate\" AND NOT \"CanUpdate\" AND NOT \"CanDelete\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserCrudAccess_Resource",
                table: "UserCrudAccess",
                sql: "\"Resource\" IN ('Users', 'Hosts', 'Modules', 'ModuleTypes', 'AuditLogs')");
        }
    }
}
