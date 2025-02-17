using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EssentialCSharp.Web.Migrations
{
    /// <inheritdoc />
    public partial class ReferralCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReferralCount",
                table: "AspNetUsers",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReferralCount",
                table: "AspNetUsers");
        }
    }
}
