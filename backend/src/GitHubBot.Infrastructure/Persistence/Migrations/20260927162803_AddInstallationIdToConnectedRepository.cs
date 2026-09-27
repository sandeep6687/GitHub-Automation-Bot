using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GitHubBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInstallationIdToConnectedRepository : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "InstallationId",
                table: "connected_repositories",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InstallationId",
                table: "connected_repositories");
        }
    }
}
