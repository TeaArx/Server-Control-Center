using Microsoft.EntityFrameworkCore;
using ServerControlCenter.Data;
using ServerControlCenter.Models;

namespace ServerControlCenter.Services;

public class DashboardDataService
{
    private readonly IAppDbContextFactory contextFactory;

    public DashboardDataService(IAppDbContextFactory? contextFactory = null)
    {
        this.contextFactory = contextFactory ?? AppDbContextFactory.Shared;
    }

    public async Task<AppSettings> GetSettingsAsync()
    {
        using var db = contextFactory.CreateDbContext();
        var settings = await db.AppSettings.SingleOrDefaultAsync();

        if (settings is not null)
        {
            return settings;
        }

        settings = new AppSettings { Id = 1 };
        db.AppSettings.Add(settings);
        await db.SaveChangesAsync();
        return settings;
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        using var db = contextFactory.CreateDbContext();
        db.AppSettings.Update(settings);
        await db.SaveChangesAsync();
    }

    public async Task<UserProfile> GetProfileAsync()
    {
        using var db = contextFactory.CreateDbContext();
        var profile = await db.UserProfiles.SingleOrDefaultAsync();

        if (profile is not null)
        {
            return profile;
        }

        profile = new UserProfile { Id = 1 };
        db.UserProfiles.Add(profile);
        await db.SaveChangesAsync();
        return profile;
    }

    public async Task SaveProfileAsync(UserProfile profile)
    {
        using var db = contextFactory.CreateDbContext();
        db.UserProfiles.Update(profile);
        await db.SaveChangesAsync();
    }

    public async Task<List<ActivityLogEntry>> GetActivityLogsAsync(int take = 200)
    {
        using var db = contextFactory.CreateDbContext();

        return await db.ActivityLogs
            .OrderByDescending(x => x.CreatedAt)
            .Take(take)
            .ToListAsync();
    }

    public async Task AddActivityAsync(string action, string target, string details = "", string level = "info")
    {
        using var db = contextFactory.CreateDbContext();

        db.ActivityLogs.Add(new ActivityLogEntry
        {
            Action = SensitiveDataRedactor.Redact(action),
            Target = SensitiveDataRedactor.Redact(target),
            Details = SensitiveDataRedactor.Redact(details),
            Level = level,
            CreatedAt = DateTime.Now
        });

        await db.SaveChangesAsync();
    }

    public async Task ClearActivityLogsAsync()
    {
        using var db = contextFactory.CreateDbContext();
        await db.ActivityLogs.ExecuteDeleteAsync();
    }
}
