using Microsoft.EntityFrameworkCore;
using ServerControlCenter.Data;
using ServerControlCenter.Models;

namespace ServerControlCenter.Services;

public class SavedCommandService
{
    public async Task<List<SavedCommand>> GetAllAsync()
    {
        using var db = new AppDbContext();

        return await db.SavedCommands
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Title)
            .ToListAsync();
    }

    public async Task AddAsync(SavedCommand command)
    {
        using var db = new AppDbContext();

        db.SavedCommands.Add(command);
        await db.SaveChangesAsync();
    }

    public async Task AddRangeAsync(IEnumerable<SavedCommand> commands)
    {
        using var db = new AppDbContext();

        db.SavedCommands.AddRange(commands);
        await db.SaveChangesAsync();
    }

    public async Task UpdateAsync(SavedCommand command)
    {
        using var db = new AppDbContext();

        db.SavedCommands.Update(command);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(SavedCommand command)
    {
        using var db = new AppDbContext();

        db.SavedCommands.Remove(command);
        await db.SaveChangesAsync();
    }
}

