using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HalloApp.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class AllowSchoollessProfileImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "SchoolId",
                table: "BlobMediaAssets",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE Users SET ProfilBildAssetId = NULL WHERE ProfilBildAssetId IN (SELECT Id FROM BlobMediaAssets WHERE SchoolId IS NULL)");
            migrationBuilder.Sql("DELETE FROM BlobMediaAssets WHERE SchoolId IS NULL");
            migrationBuilder.AlterColumn<Guid>(
                name: "SchoolId",
                table: "BlobMediaAssets",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
