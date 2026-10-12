using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Webboard.Infrastructure.Configuration.Migrations
{
    /// <inheritdoc />
    public partial class SeparateMonitoringVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "MonitoringVersion",
                table: "Modules",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "ConfigurationVersion",
                table: "LatestMonitoring",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MonitoringVersion",
                table: "Modules");

            migrationBuilder.DropColumn(
                name: "ConfigurationVersion",
                table: "LatestMonitoring");
        }
    }
}
