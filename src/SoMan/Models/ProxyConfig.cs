namespace SoMan.Models;

public enum ProxyType
{
    Http,
    Socks5,
    Vpn
}

public class ProxyConfig
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ProxyType Type { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string? Username { get; set; }
    public string? EncryptedPassword { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<Account> Accounts { get; set; } = new List<Account>();
}
