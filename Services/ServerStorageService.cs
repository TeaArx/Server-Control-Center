using Microsoft.EntityFrameworkCore;
using ServerControlCenter.Data;
using ServerControlCenter.Models;

namespace ServerControlCenter.Services;

public class ServerStorageService
{
    public async Task<List<ServerProfile>> GetAllAsync()
    {
        using var db = new AppDbContext();

        var servers = await db.Servers
            .AsTracking()
            .OrderBy(x => x.Name)
            .ToListAsync();

        var hasPlainTextPasswords = false;

        foreach (var server in servers)
        {
            if (!string.IsNullOrWhiteSpace(server.Password) &&
                !SecretProtector.IsProtected(server.Password))
            {
                server.Password = SecretProtector.Protect(server.Password);
                hasPlainTextPasswords = true;
            }
        }

        if (hasPlainTextPasswords)
        {
            await db.SaveChangesAsync();
        }

        foreach (var server in servers)
        {
            server.Password = SecretProtector.Unprotect(server.Password);
            server.IsOnline = false;
        }

        return servers;
    }

    public async Task AddAsync(ServerProfile server)
    {
        using var db = new AppDbContext();

        var storageCopy = CreateStorageCopy(server);
        db.Servers.Add(storageCopy);
        await db.SaveChangesAsync();

        server.Id = storageCopy.Id;
    }

    public async Task DeleteAsync(ServerProfile server)
    {
        using var db = new AppDbContext();

        await db.Servers
            .Where(x => x.Id == server.Id)
            .ExecuteDeleteAsync();
    }

    public async Task UpdateAsync(ServerProfile server)
    {
        using var db = new AppDbContext();

        db.Servers.Update(CreateStorageCopy(server));
        await db.SaveChangesAsync();
    }

    private static ServerProfile CreateStorageCopy(ServerProfile server)
    {
        return new ServerProfile
        {
            Id = server.Id,
            Name = server.Name.Trim(),
            Host = server.Host.Trim(),
            Port = server.Port,
            Username = server.Username.Trim(),
            Password = SecretProtector.Protect(server.Password),
            PrivateKeyPath = string.IsNullOrWhiteSpace(server.PrivateKeyPath) ? null : server.PrivateKeyPath.Trim(),
            Notes = string.IsNullOrWhiteSpace(server.Notes) ? null : server.Notes.Trim(),
            GroupName = string.IsNullOrWhiteSpace(server.GroupName) ? "Production" : server.GroupName.Trim(),
            OsName = string.IsNullOrWhiteSpace(server.OsName) ? null : server.OsName.Trim(),
            IpAddressDisplay = string.IsNullOrWhiteSpace(server.IpAddressDisplay) ? null : server.IpAddressDisplay.Trim(),
            IsFavorite = server.IsFavorite
        };
    }
}
