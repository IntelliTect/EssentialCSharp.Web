using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EssentialCSharp.Web.Migrations
{
    /// <inheritdoc />
    public partial class MakeNormalizedEmailUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Creating the unique index fails if two accounts already share an email. Stop with a clear message
            // (before dropping the old index) so the duplicates can be resolved by hand first.
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1 FROM [AspNetUsers]
                    WHERE [NormalizedEmail] IS NOT NULL
                    GROUP BY [NormalizedEmail]
                    HAVING COUNT(*) > 1)
                    THROW 50000, 'AspNetUsers has more than one account for the same NormalizedEmail. Resolve the duplicates before applying MakeNormalizedEmailUnique.', 1;
                """);

            migrationBuilder.DropIndex(
                name: "EmailIndex",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail",
                unique: true,
                filter: "[NormalizedEmail] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "EmailIndex",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");
        }
    }
}
