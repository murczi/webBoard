using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Webboard.Infrastructure.Configuration.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditActionsAndPagingIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_ActorId",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_HostId",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_ModuleId",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_UserId",
                table: "AuditLogs");

            migrationBuilder.AddColumn<int>(
                name: "Action",
                table: "AuditLogs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Action_DateCreated_Id",
                table: "AuditLogs",
                columns: new[] { "Action", "DateCreated", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ActorId_DateCreated_Id",
                table: "AuditLogs",
                columns: new[] { "ActorId", "DateCreated", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_DateCreated_Id",
                table: "AuditLogs",
                columns: new[] { "DateCreated", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_HostId_DateCreated_Id",
                table: "AuditLogs",
                columns: new[] { "HostId", "DateCreated", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ModuleId_DateCreated_Id",
                table: "AuditLogs",
                columns: new[] { "ModuleId", "DateCreated", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId_DateCreated_Id",
                table: "AuditLogs",
                columns: new[] { "UserId", "DateCreated", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_Action_DateCreated_Id",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_ActorId_DateCreated_Id",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_DateCreated_Id",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_HostId_DateCreated_Id",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_ModuleId_DateCreated_Id",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_UserId_DateCreated_Id",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "Action",
                table: "AuditLogs");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ActorId",
                table: "AuditLogs",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_HostId",
                table: "AuditLogs",
                column: "HostId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ModuleId",
                table: "AuditLogs",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId",
                table: "AuditLogs",
                column: "UserId");
        }
    }
}
