using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ServerControlCenter.Data;

#nullable disable

namespace ServerControlCenter.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260717130000_HardenLocalData")]
public sealed class HardenLocalData : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DELETE FROM AppSettings WHERE Id NOT IN (SELECT MIN(Id) FROM AppSettings);");
        migrationBuilder.Sql("DELETE FROM UserProfiles WHERE Id NOT IN (SELECT MIN(Id) FROM UserProfiles);");
        migrationBuilder.Sql("CREATE UNIQUE INDEX IF NOT EXISTS IX_AppSettings_Singleton ON AppSettings ((1));");
        migrationBuilder.Sql("CREATE UNIQUE INDEX IF NOT EXISTS IX_UserProfiles_Singleton ON UserProfiles ((1));");
        migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS IX_Servers_Connection ON Servers (Host, Port, Username);");
        migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS IX_ActivityLogs_CreatedAt ON ActivityLogs (CreatedAt);");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS IX_ActivityLogs_CreatedAt;");
        migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Servers_Connection;");
        migrationBuilder.Sql("DROP INDEX IF EXISTS IX_UserProfiles_Singleton;");
        migrationBuilder.Sql("DROP INDEX IF EXISTS IX_AppSettings_Singleton;");
    }
}
