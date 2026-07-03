using Microsoft.EntityFrameworkCore;
using ServerControlCenter.Data;
using ServerControlCenter.Models;

namespace ServerControlCenter.Services;

public class DashboardDataService
{
    public async Task<AppSettings> GetSettingsAsync()
    {
        using var db = new AppDbContext();
        var settings = await db.AppSettings.FirstOrDefaultAsync();

        if (settings is not null)
        {
            return settings;
        }

        settings = new AppSettings();
        db.AppSettings.Add(settings);
        await db.SaveChangesAsync();
        return settings;
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        using var db = new AppDbContext();
        db.AppSettings.Update(settings);
        await db.SaveChangesAsync();
    }

    public async Task<UserProfile> GetProfileAsync()
    {
        using var db = new AppDbContext();
        var profile = await db.UserProfiles.FirstOrDefaultAsync();

        if (profile is not null)
        {
            return profile;
        }

        profile = new UserProfile();
        db.UserProfiles.Add(profile);
        await db.SaveChangesAsync();
        return profile;
    }

    public async Task SaveProfileAsync(UserProfile profile)
    {
        using var db = new AppDbContext();
        db.UserProfiles.Update(profile);
        await db.SaveChangesAsync();
    }

    public async Task<List<ActivityLogEntry>> GetActivityLogsAsync(int take = 200)
    {
        using var db = new AppDbContext();

        return await db.ActivityLogs
            .OrderByDescending(x => x.CreatedAt)
            .Take(take)
            .ToListAsync();
    }

    public async Task AddActivityAsync(string action, string target, string details = "", string level = "info")
    {
        using var db = new AppDbContext();

        db.ActivityLogs.Add(new ActivityLogEntry
        {
            Action = action,
            Target = target,
            Details = details,
            Level = level,
            CreatedAt = DateTime.Now
        });

        await db.SaveChangesAsync();
    }

    public async Task ClearActivityLogsAsync()
    {
        using var db = new AppDbContext();
        await db.ActivityLogs.ExecuteDeleteAsync();
    }
}
