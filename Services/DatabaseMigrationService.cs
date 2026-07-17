using Microsoft.EntityFrameworkCore;
using ServerControlCenter.Data;
using System.IO;

namespace ServerControlCenter.Services;

public sealed class DatabaseMigrationService
{
    private readonly IAppDbContextFactory contextFactory;
    private readonly string databasePath;

    public DatabaseMigrationService(IAppDbContextFactory contextFactory, string? databasePath = null)
    {
        this.contextFactory = contextFactory;
        this.databasePath = databasePath ?? AppDbContext.DatabasePath;
    }

    public async Task<string?> MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var db = contextFactory.CreateDbContext();
        var pendingMigrations = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();

        if (pendingMigrations.Length == 0)
        {
            return null;
        }

        var backupPath = await CreateBackupAsync(db, cancellationToken);

        try
        {
            await db.Database.MigrateAsync(cancellationToken);
            return backupPath;
        }
        catch
        {
            await db.Database.CloseConnectionAsync();

            if (backupPath is not null && File.Exists(backupPath))
            {
                File.Copy(backupPath, databasePath, overwrite: true);
            }

            throw;
        }
    }

    private async Task<string?> CreateBackupAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        if (!File.Exists(databasePath))
        {
            return null;
        }

        await db.Database.OpenConnectionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);", cancellationToken);
        await db.Database.CloseConnectionAsync();

        var backupDirectory = Path.Combine(Path.GetDirectoryName(databasePath)!, "backups");
        Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(
            backupDirectory,
            $"server_control_center-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.db");
        File.Copy(databasePath, backupPath, overwrite: false);
        RotateBackups(backupDirectory, keep: 5);
        return backupPath;
    }

    private static void RotateBackups(string directory, int keep)
    {
        foreach (var oldBackup in Directory.EnumerateFiles(directory, "server_control_center-*.db")
                     .OrderByDescending(File.GetCreationTimeUtc)
                     .Skip(keep))
        {
            File.Delete(oldBackup);
        }
    }
}
