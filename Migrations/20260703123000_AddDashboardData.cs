using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ServerControlCenter.Data;

#nullable disable

namespace ServerControlCenter.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260703123000_AddDashboardData")]
    public partial class AddDashboardData : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GroupName",
                table: "Servers",
                type: "TEXT",
                nullable: false,
                defaultValue: "Production");

            migrationBuilder.AddColumn<string>(
                name: "IpAddressDisplay",
                table: "Servers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFavorite",
                table: "Servers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "OsName",
                table: "Servers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "SavedCommands",
                type: "TEXT",
                nullable: false,
                defaultValue: "General");

            migrationBuilder.AddColumn<bool>(
                name: "IsFavorite",
                table: "SavedCommands",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "SavedCommands",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ActivityLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    Target = table.Column<string>(type: "TEXT", nullable: false),
                    Details = table.Column<string>(type: "TEXT", nullable: false),
                    Level = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MonitoringIntervalSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    DefaultRemoteFolder = table.Column<string>(type: "TEXT", nullable: false),
                    DefaultLogPath = table.Column<string>(type: "TEXT", nullable: false),
                    AutoRefreshMonitoring = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    Initials = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProfiles", x => x.Id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ActivityLogs");
            migrationBuilder.DropTable(name: "AppSettings");
            migrationBuilder.DropTable(name: "UserProfiles");

            migrationBuilder.DropColumn(name: "GroupName", table: "Servers");
            migrationBuilder.DropColumn(name: "IpAddressDisplay", table: "Servers");
            migrationBuilder.DropColumn(name: "IsFavorite", table: "Servers");
            migrationBuilder.DropColumn(name: "OsName", table: "Servers");
            migrationBuilder.DropColumn(name: "Category", table: "SavedCommands");
            migrationBuilder.DropColumn(name: "IsFavorite", table: "SavedCommands");
            migrationBuilder.DropColumn(name: "SortOrder", table: "SavedCommands");
        }
    }
}
