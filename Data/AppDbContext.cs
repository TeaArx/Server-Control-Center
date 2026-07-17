using System.IO;
using Microsoft.EntityFrameworkCore;
using ServerControlCenter.Models;

namespace ServerControlCenter.Data;

public class AppDbContext : DbContext
{
    public AppDbContext()
    {
    }

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public static string DatabasePath
    {
        get
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ServerControlCenter"
            );

            Directory.CreateDirectory(appDataPath);

            return Path.Combine(appDataPath, "server_control_center.db");
        }
    }

    public DbSet<ServerProfile> Servers => Set<ServerProfile>();

    public DbSet<SavedCommand> SavedCommands => Set<SavedCommand>();

    public DbSet<AppSettings> AppSettings => Set<AppSettings>();

    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

    public DbSet<ActivityLogEntry> ActivityLogs => Set<ActivityLogEntry>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite($"Data Source={DatabasePath}");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ServerProfile>()
            .HasIndex(x => new { x.Host, x.Port, x.Username })
            .HasDatabaseName("IX_Servers_Connection");

        modelBuilder.Entity<ActivityLogEntry>()
            .HasIndex(x => x.CreatedAt)
            .HasDatabaseName("IX_ActivityLogs_CreatedAt");

        base.OnModelCreating(modelBuilder);
    }
}

