using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkoulouApp.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class DirektionErfasstUndAntwortTon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ErfasstVonId",
                table: "TeacherLessons",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AntwortTonAus",
                table: "StudentProfiles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ErfasstVonId",
                table: "Lessons",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ErfasstVonId",
                table: "Exams",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeacherLessons_ErfasstVonId",
                table: "TeacherLessons",
                column: "ErfasstVonId");

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_ErfasstVonId",
                table: "Lessons",
                column: "ErfasstVonId");

            migrationBuilder.CreateIndex(
                name: "IX_Exams_ErfasstVonId",
                table: "Exams",
                column: "ErfasstVonId");

            migrationBuilder.AddForeignKey(
                name: "FK_Exams_AspNetUsers_ErfasstVonId",
                table: "Exams",
                column: "ErfasstVonId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Lessons_AspNetUsers_ErfasstVonId",
                table: "Lessons",
                column: "ErfasstVonId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TeacherLessons_AspNetUsers_ErfasstVonId",
                table: "TeacherLessons",
                column: "ErfasstVonId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Exams_AspNetUsers_ErfasstVonId",
                table: "Exams");

            migrationBuilder.DropForeignKey(
                name: "FK_Lessons_AspNetUsers_ErfasstVonId",
                table: "Lessons");

            migrationBuilder.DropForeignKey(
                name: "FK_TeacherLessons_AspNetUsers_ErfasstVonId",
                table: "TeacherLessons");

            migrationBuilder.DropIndex(
                name: "IX_TeacherLessons_ErfasstVonId",
                table: "TeacherLessons");

            migrationBuilder.DropIndex(
                name: "IX_Lessons_ErfasstVonId",
                table: "Lessons");

            migrationBuilder.DropIndex(
                name: "IX_Exams_ErfasstVonId",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "ErfasstVonId",
                table: "TeacherLessons");

            migrationBuilder.DropColumn(
                name: "AntwortTonAus",
                table: "StudentProfiles");

            migrationBuilder.DropColumn(
                name: "ErfasstVonId",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "ErfasstVonId",
                table: "Exams");
        }
    }
}
