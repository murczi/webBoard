using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Webboard.Infrastructure.Configuration.Migrations
{
    /// <inheritdoc />
    public partial class UpdateConfigurationModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AuditLogs_Modules_ModuleId",
                table: "AuditLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_Modules_ManagedHosts_ManagedHostId",
                table: "Modules");

            migrationBuilder.DropForeignKey(
                name: "FK_Modules_ModuleTypes_ModuleTypeId",
                table: "Modules");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ManagedHosts",
                table: "ManagedHosts");

            migrationBuilder.DropIndex(
                name: "IX_Modules_ManagedHostId_SortOrder",
                table: "Modules");

            migrationBuilder.DropIndex(
                name: "IX_Modules_ModuleTypeId",
                table: "Modules");

            migrationBuilder.DropColumn(
                name: "ShowOnOverview",
                table: "Modules");

            migrationBuilder.DropColumn(
                name: "TargetIdentifier",
                table: "Modules");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "Modules");

            migrationBuilder.RenameTable(
                name: "ManagedHosts",
                newName: "Hosts");

            migrationBuilder.RenameColumn(
                name: "Type",
                table: "ModuleTypes",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "UpdatedAtUtc",
                table: "Modules",
                newName: "DateUpdated");

            migrationBuilder.RenameColumn(
                name: "ModuleTypeId",
                table: "Modules",
                newName: "TypeId");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "Modules",
                newName: "FriendlyName");

            migrationBuilder.RenameColumn(
                name: "ManagedHostId",
                table: "Modules",
                newName: "HostId");

            migrationBuilder.RenameColumn(
                name: "CreatedAtUtc",
                table: "Modules",
                newName: "DateCreated");

            migrationBuilder.RenameColumn(
                name: "AuditComment",
                table: "AuditLogs",
                newName: "Comment");

            migrationBuilder.RenameColumn(
                name: "ActorUserId",
                table: "AuditLogs",
                newName: "ActorId");

            migrationBuilder.RenameIndex(
                name: "IX_ManagedHosts_Name",
                table: "Hosts",
                newName: "IX_Hosts_Name");

            migrationBuilder.AlterColumn<bool>(
                name: "IsEnabled",
                table: "Modules",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AddColumn<bool>(
                name: "DeletionFlag",
                table: "Modules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "AuditLogs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HostId",
                table: "AuditLogs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DeletionFlag",
                table: "Hosts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Hosts",
                table: "Hosts",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DeletionFlag = table.Column<bool>(type: "boolean", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.AlterColumn<int>(
                name: "ActorId",
                table: "AuditLogs",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Modules_HostId",
                table: "Modules",
                column: "HostId");

            migrationBuilder.CreateIndex(
                name: "IX_Modules_TypeId",
                table: "Modules",
                column: "TypeId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ActorId",
                table: "AuditLogs",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_HostId",
                table: "AuditLogs",
                column: "HostId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId",
                table: "AuditLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Name",
                table: "Users",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AuditLogs_Hosts_HostId",
                table: "AuditLogs",
                column: "HostId",
                principalTable: "Hosts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AuditLogs_Modules_ModuleId",
                table: "AuditLogs",
                column: "ModuleId",
                principalTable: "Modules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AuditLogs_Users_ActorId",
                table: "AuditLogs",
                column: "ActorId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AuditLogs_Users_UserId",
                table: "AuditLogs",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Modules_Hosts_HostId",
                table: "Modules",
                column: "HostId",
                principalTable: "Hosts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Modules_ModuleTypes_TypeId",
                table: "Modules",
                column: "TypeId",
                principalTable: "ModuleTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AuditLogs_Hosts_HostId",
                table: "AuditLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_AuditLogs_Modules_ModuleId",
                table: "AuditLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_AuditLogs_Users_ActorId",
                table: "AuditLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_AuditLogs_Users_UserId",
                table: "AuditLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_Modules_Hosts_HostId",
                table: "Modules");

            migrationBuilder.DropForeignKey(
                name: "FK_Modules_ModuleTypes_TypeId",
                table: "Modules");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Hosts",
                table: "Hosts");

            migrationBuilder.DropIndex(
                name: "IX_Modules_HostId",
                table: "Modules");

            migrationBuilder.DropIndex(
                name: "IX_Modules_TypeId",
                table: "Modules");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_ActorId",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_HostId",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_UserId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "DeletionFlag",
                table: "Modules");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "HostId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "DeletionFlag",
                table: "Hosts");

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "Modules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "ModuleTypes",
                newName: "Type");

            migrationBuilder.RenameColumn(
                name: "TypeId",
                table: "Modules",
                newName: "ModuleTypeId");

            migrationBuilder.RenameColumn(
                name: "HostId",
                table: "Modules",
                newName: "ManagedHostId");

            migrationBuilder.RenameColumn(
                name: "FriendlyName",
                table: "Modules",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "DateUpdated",
                table: "Modules",
                newName: "UpdatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "DateCreated",
                table: "Modules",
                newName: "CreatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "ActorId",
                table: "AuditLogs",
                newName: "ActorUserId");

            migrationBuilder.RenameIndex(
                name: "IX_Hosts_Name",
                table: "Hosts",
                newName: "IX_ManagedHosts_Name");

            migrationBuilder.RenameTable(
                name: "Hosts",
                newName: "ManagedHosts");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ManagedHosts",
                table: "ManagedHosts",
                column: "Id");

            migrationBuilder.RenameColumn(
                name: "Comment",
                table: "AuditLogs",
                newName: "AuditComment");

            migrationBuilder.AlterColumn<bool>(
                name: "IsEnabled",
                table: "Modules",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowOnOverview",
                table: "Modules",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetIdentifier",
                table: "Modules",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Modules_ManagedHostId_SortOrder",
                table: "Modules",
                columns: new[] { "ManagedHostId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Modules_ModuleTypeId",
                table: "Modules",
                column: "ModuleTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_AuditLogs_Modules_ModuleId",
                table: "AuditLogs",
                column: "ModuleId",
                principalTable: "Modules",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Modules_ManagedHosts_ManagedHostId",
                table: "Modules",
                column: "ManagedHostId",
                principalTable: "ManagedHosts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Modules_ModuleTypes_ModuleTypeId",
                table: "Modules",
                column: "ModuleTypeId",
                principalTable: "ModuleTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
