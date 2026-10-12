using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Webboard.Infrastructure.Configuration.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeHostNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT lower("Name") FROM "Hosts" GROUP BY lower("Name") HAVING count(*) > 1) THEN
                        RAISE EXCEPTION 'Resolve case-insensitive duplicate host names before upgrading (including deleted hosts).';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropIndex(
                name: "IX_Hosts_Name",
                table: "Hosts");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "Hosts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                computedColumnSql: "lower(\"Name\")",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_Hosts_NormalizedName",
                table: "Hosts",
                column: "NormalizedName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Hosts_NormalizedName",
                table: "Hosts");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "Hosts");

            migrationBuilder.CreateIndex(
                name: "IX_Hosts_Name",
                table: "Hosts",
                column: "Name",
                unique: true);
        }
    }
}
