using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace EssentialCSharp.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddReadingTimeTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReadingActivities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    PageKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActiveSeconds = table.Column<int>(type: "int", nullable: false),
                    WordsRead = table.Column<int>(type: "int", nullable: false),
                    Completed = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadingActivities_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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

            migrationBuilder.CreateIndex(
                name: "IX_ReadingActivities_UserId_PageKey",
                table: "ReadingActivities",
                columns: new[] { "UserId", "PageKey" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingActivities_UserId_RecordedAtUtc",
                table: "ReadingActivities",
                columns: new[] { "UserId", "RecordedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReadingActivities");

            migrationBuilder.DropTable(
                name: "UserReadingProfiles");
        }
    }
}
