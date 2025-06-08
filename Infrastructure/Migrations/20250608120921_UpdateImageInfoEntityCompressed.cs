using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateImageInfoEntityCompressed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CompressionType",
                table: "ImageInfos",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "none");

            migrationBuilder.AddColumn<bool>(
                name: "IsCompressed",
                table: "ImageInfos",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompressionType",
                table: "ImageInfos");

            migrationBuilder.DropColumn(
                name: "IsCompressed",
                table: "ImageInfos");
        }
    }
}
