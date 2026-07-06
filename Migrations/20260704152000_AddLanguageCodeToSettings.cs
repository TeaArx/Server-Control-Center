using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServerControlCenter.Migrations
{
    public partial class AddLanguageCodeToSettings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LanguageCode",
                table: "AppSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "en");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LanguageCode",
                table: "AppSettings");
        }
    }
}
