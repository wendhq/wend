using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wend.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddBoardOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Clean slate: OwnerId is required and pre-existing boards have no owner, so they go.
            // Existing required FKs cascade to lists, cards, labels, join rows and checklist items.
            // On a fresh database (CI, every test, production) this is a no-op against an empty
            // table. It MUST run before AddColumn: the column is added with defaultValue "", and
            // the foreign key below would then fail against rows whose owner does not exist.
            migrationBuilder.Sql(@"DELETE FROM ""Boards"";");

            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                table: "Boards",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Boards_OwnerId",
                table: "Boards",
                column: "OwnerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Boards_AspNetUsers_OwnerId",
                table: "Boards",
                column: "OwnerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Boards_AspNetUsers_OwnerId",
                table: "Boards");

            migrationBuilder.DropIndex(
                name: "IX_Boards_OwnerId",
                table: "Boards");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Boards");
        }
    }
}
