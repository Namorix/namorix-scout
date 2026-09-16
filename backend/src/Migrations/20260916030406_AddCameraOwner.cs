using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namorix.Scout.Migrations
{
    /// <inheritdoc />
    public partial class AddCameraOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "Cameras",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Cameras predating the owner column can only have belonged to the single user
            // this addon was able to serve, so they go to the most recent grant holder. With
            // no grant on record there is nobody to give them to and they stay at 0, which
            // no query matches.
            migrationBuilder.Sql(
                "UPDATE Cameras SET UserId = COALESCE(" +
                "(SELECT UserId FROM Tokens ORDER BY LastSeenAt DESC LIMIT 1), 0) WHERE UserId = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Cameras");
        }
    }
}
