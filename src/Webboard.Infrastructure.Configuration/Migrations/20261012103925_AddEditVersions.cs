using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Webboard.Infrastructure.Configuration.Migrations
{
    /// <inheritdoc />
    public partial class AddEditVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "PermissionVersion",
                table: "Users",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "Modules",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DateUpdated",
                table: "Hosts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "Hosts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
            migrationBuilder.Sql("UPDATE \"Hosts\" SET \"DateUpdated\" = \"DateCreated\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PermissionVersion",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Modules");

            migrationBuilder.DropColumn(
                name: "DateUpdated",
                table: "Hosts");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Hosts");
        }
    }
}
