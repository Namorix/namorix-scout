using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Namorix.Scout.Migrations
{
    /// <inheritdoc />
    public partial class AddCameraLastUpdatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastUpdatedAt",
                table: "Cameras",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTimeOffset(1, 1, 1, 0, 0, 0, TimeSpan.Zero));

            // Rows written before this column existed have no edit history of their own;
            // CreatedAt is the honest stamp, and a year-0001 value would surface as-is.
            migrationBuilder.Sql("UPDATE \"Cameras\" SET \"LastUpdatedAt\" = \"CreatedAt\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastUpdatedAt",
                table: "Cameras");
        }
    }
}
