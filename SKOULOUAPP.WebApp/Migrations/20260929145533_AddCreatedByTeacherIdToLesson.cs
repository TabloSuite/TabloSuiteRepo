using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HalloApp.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class AddCreatedByTeacherIdToLesson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByTeacherId",
                table: "Lessons",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_CreatedByTeacherId",
                table: "Lessons",
                column: "CreatedByTeacherId");

            migrationBuilder.AddForeignKey(
                name: "FK_Lessons_AspNetUsers_CreatedByTeacherId",
                table: "Lessons",
                column: "CreatedByTeacherId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Lessons_AspNetUsers_CreatedByTeacherId",
                table: "Lessons");

            migrationBuilder.DropIndex(
                name: "IX_Lessons_CreatedByTeacherId",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "CreatedByTeacherId",
                table: "Lessons");
        }
    }
}
