using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ENote.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class MobileMoneyKamerun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MobileMoneyAnbieter",
                columns: new[] { "Id", "CreatedAtUtc", "Farbe", "IstAktiv", "Land", "Name", "Reihenfolge" },
                values: new object[,]
                {
                    { new Guid("7a0d0007-0000-4000-8000-000000000001"), new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), "#ffcb05", true, "Cameroun", "MTN MoMo", 1 },
                    { new Guid("7a0d0007-0000-4000-8000-000000000002"), new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), "#ff7900", true, "Cameroun", "Orange Money", 2 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "MobileMoneyAnbieter",
                keyColumn: "Id",
                keyValue: new Guid("7a0d0007-0000-4000-8000-000000000001"));

            migrationBuilder.DeleteData(
                table: "MobileMoneyAnbieter",
                keyColumn: "Id",
                keyValue: new Guid("7a0d0007-0000-4000-8000-000000000002"));
        }
    }
}
