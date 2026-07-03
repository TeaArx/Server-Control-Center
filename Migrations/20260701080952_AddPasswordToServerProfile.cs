using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServerControlCenter.Migrations
{
    /// <inheritdoc />
    public partial class AddPasswordToServerProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Password",
                table: "Servers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Password",
                table: "Servers");
        }
    }
}
