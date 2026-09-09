using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HalloApp.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class AddLessonPublishingAndManualExerciseReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExerciseSubmissionDueAtUtc",
                table: "Lessons",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPublished",
                table: "Lessons",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "ManualFeedback",
                table: "ExerciseAttempts",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ManuallyGradedAtUtc",
                table: "ExerciseAttempts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ManuallyGradedByUserId",
                table: "ExerciseAttempts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ManuallyGradedIsCorrect",
                table: "ExerciseAttempts",
                type: "bit",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExerciseSubmissionDueAtUtc",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "IsPublished",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "ManualFeedback",
                table: "ExerciseAttempts");

            migrationBuilder.DropColumn(
                name: "ManuallyGradedAtUtc",
                table: "ExerciseAttempts");

            migrationBuilder.DropColumn(
                name: "ManuallyGradedByUserId",
                table: "ExerciseAttempts");

            migrationBuilder.DropColumn(
                name: "ManuallyGradedIsCorrect",
                table: "ExerciseAttempts");
        }
    }
}
