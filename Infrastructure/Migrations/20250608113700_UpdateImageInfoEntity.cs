using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateImageInfoEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "UploadedAt",
                table: "ImageInfos",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETUTCDATE()",
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "ImageInfos",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileExtension",
                table: "ImageInfos",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: ".png");

            migrationBuilder.AddColumn<string>(
                name: "Metadata",
                table: "ImageInfos",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "ImageInfos",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "ImageInfos");

            migrationBuilder.DropColumn(
                name: "FileExtension",
                table: "ImageInfos");

            migrationBuilder.DropColumn(
                name: "Metadata",
                table: "ImageInfos");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "ImageInfos");

            migrationBuilder.AlterColumn<DateTime>(
                name: "UploadedAt",
                table: "ImageInfos",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldDefaultValueSql: "GETUTCDATE()");
        }
    }
}
