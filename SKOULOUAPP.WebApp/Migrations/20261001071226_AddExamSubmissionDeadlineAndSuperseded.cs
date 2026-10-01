using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HalloApp.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class AddExamSubmissionDeadlineAndSuperseded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSuperseded",
                table: "Exams",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmissionDueAtUtc",
                table: "Exams",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSuperseded",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "SubmissionDueAtUtc",
                table: "Exams");
        }
    }
}
