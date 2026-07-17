using System.IO;
using Microsoft.EntityFrameworkCore;
using ServerControlCenter.Data;
using ServerControlCenter.Services;
using Xunit;

namespace ServerControlCenter.UnitTests;

public sealed class MigrationTests
{
    [Fact]
    public async Task FreshDatabaseAppliesEntireMigrationChain()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"scc-migration-{Guid.NewGuid():N}.db");
        var factory = new AppDbContextFactory(databasePath);

        try
        {
            var service = new DatabaseMigrationService(factory, databasePath);
            await service.MigrateAsync();

            await using (var db = factory.CreateDbContext())
            {
                Assert.Empty(await db.Database.GetPendingMigrationsAsync());
                Assert.True(await db.Database.CanConnectAsync());
            }
        }
        finally
        {
            foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}
