using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReqLens.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFieldProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Grounded",
                table: "Fields",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceText",
                table: "Fields",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Grounded",
                table: "Fields");

            migrationBuilder.DropColumn(
                name: "SourceText",
                table: "Fields");
        }
    }
}
