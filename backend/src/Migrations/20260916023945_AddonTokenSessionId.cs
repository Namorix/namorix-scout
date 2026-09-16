using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namorix.Scout.Migrations
{
    /// <inheritdoc />
    public partial class AddonTokenSessionId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tokens_ClientId_UserId",
                table: "Tokens");

            migrationBuilder.AddColumn<string>(
                name: "SessionId",
                table: "Tokens",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Tokens_ClientId_SessionId",
                table: "Tokens",
                columns: new[] { "ClientId", "SessionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tokens_ClientId_SessionId",
                table: "Tokens");

            migrationBuilder.DropColumn(
                name: "SessionId",
                table: "Tokens");

            migrationBuilder.CreateIndex(
                name: "IX_Tokens_ClientId_UserId",
                table: "Tokens",
                columns: new[] { "ClientId", "UserId" },
                unique: true);
        }
    }
}
