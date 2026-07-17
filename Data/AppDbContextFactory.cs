using Microsoft.EntityFrameworkCore;

namespace ServerControlCenter.Data;

public interface IAppDbContextFactory
{
    AppDbContext CreateDbContext();
}

public sealed class AppDbContextFactory : IAppDbContextFactory
{
    public static AppDbContextFactory Shared { get; } = new();

    private readonly string connectionString;

    public AppDbContextFactory(string? databasePath = null)
    {
        connectionString = $"Data Source={databasePath ?? AppDbContext.DatabasePath};Pooling=False";
    }

    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
