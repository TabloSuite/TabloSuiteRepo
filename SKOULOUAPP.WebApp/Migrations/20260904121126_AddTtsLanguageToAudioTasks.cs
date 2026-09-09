using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HalloApp.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class AddTtsLanguageToAudioTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TtsLanguage",
                table: "Exercises",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "de-DE");

            migrationBuilder.AddColumn<string>(
                name: "TtsLanguage",
                table: "ExamQuestions",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "de-DE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TtsLanguage",
                table: "Exercises");

            migrationBuilder.DropColumn(
                name: "TtsLanguage",
                table: "ExamQuestions");
        }
    }
}
