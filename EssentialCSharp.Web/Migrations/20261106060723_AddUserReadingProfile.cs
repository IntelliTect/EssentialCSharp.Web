using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EssentialCSharp.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddUserReadingProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserReadingProfiles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    TotalWordsRead = table.Column<long>(type: "bigint", nullable: false),
                    TotalActiveSeconds = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserReadingProfiles", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserReadingProfiles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserReadingProfiles");
        }
    }
}
