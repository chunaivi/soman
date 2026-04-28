using Microsoft.EntityFrameworkCore;
using SoMan.Data;
using SoMan.Models;
using SoMan.Services.Security;

namespace SoMan.Services.Proxy;

public interface IProxyManager
{
    Task<List<ProxyConfig>> GetAllAsync();
    Task<ProxyConfig?> GetByIdAsync(int id);
    Task<ProxyConfig> AddAsync(string name, ProxyType type, string host, int port, string? username = null, string? password = null);
    Task UpdateAsync(ProxyConfig proxy);
    Task DeleteAsync(int id);
    Task<List<ProxyConfig>> ImportBulkAsync(string text);
}

public class ProxyManager : IProxyManager
{
    private readonly IEncryptionService _encryption;

    public ProxyManager(IEncryptionService encryption)
    {
        _encryption = encryption;
    }

    private static SoManDbContext CreateDb() => new();

    public async Task<List<ProxyConfig>> GetAllAsync()
    {
        using var db = CreateDb();
        return await db.ProxyConfigs.AsNoTracking().OrderBy(p => p.Name).ToListAsync();
    }

    public async Task<ProxyConfig?> GetByIdAsync(int id)
    {
        using var db = CreateDb();
        return await db.ProxyConfigs.FindAsync(id);
    }

    public async Task<ProxyConfig> AddAsync(string name, ProxyType type, string host, int port, string? username = null, string? password = null)
    {
        using var db = CreateDb();
        var proxy = new ProxyConfig
        {
            Name = name,
            Type = type,
            Host = host,
            Port = port,
            Username = username,
            EncryptedPassword = password != null ? _encryption.Encrypt(password) : null,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.ProxyConfigs.Add(proxy);
        await db.SaveChangesAsync();
        return proxy;
    }

    public async Task UpdateAsync(ProxyConfig proxy)
    {
        using var db = CreateDb();
        db.ProxyConfigs.Update(proxy);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        using var db = CreateDb();
        var proxy = await db.ProxyConfigs.FindAsync(id);
        if (proxy != null)
        {
            db.ProxyConfigs.Remove(proxy);
            await db.SaveChangesAsync();
        }
    }

    public async Task<List<ProxyConfig>> ImportBulkAsync(string text)
    {
        using var db = CreateDb();
        var proxies = new List<ProxyConfig>();
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var line in lines)
        {
            var proxy = ParseProxyLine(line);
            if (proxy != null)
            {
                db.ProxyConfigs.Add(proxy);
                proxies.Add(proxy);
            }
        }

        if (proxies.Count > 0)
            await db.SaveChangesAsync();

        return proxies;
    }

    private ProxyConfig? ParseProxyLine(string line)
    {
        // Formats: host:port  |  host:port:user:pass  |  socks5://host:port:user:pass
        var type = ProxyType.Http;
        var cleaned = line;

        if (line.StartsWith("socks5://", StringComparison.OrdinalIgnoreCase))
        {
            type = ProxyType.Socks5;
            cleaned = line[9..];
        }
        else if (line.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = line[7..];
        }

        var parts = cleaned.Split(':');
        if (parts.Length < 2 || !int.TryParse(parts[1], out int port))
            return null;

        var proxy = new ProxyConfig
        {
            Name = $"{parts[0]}:{port}",
            Type = type,
            Host = parts[0],
            Port = port,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        if (parts.Length >= 4)
        {
            proxy.Username = parts[2];
            proxy.EncryptedPassword = _encryption.Encrypt(parts[3]);
        }

        return proxy;
    }
}
