using System.ComponentModel;

namespace SoMan.Models;

public class Account : INotifyPropertyChanged
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Platform Platform { get; set; }
    public string Username { get; set; } = string.Empty;
    public string EncryptedCookiesJson { get; set; } = string.Empty;
    public int? ProxyConfigId { get; set; }
    public AccountStatus Status { get; set; } = AccountStatus.Active;
    public string? Notes { get; set; }
    public bool IsHeadless { get; set; } = true;
    public DateTime? LastActiveAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // UI-only (not mapped to DB)
    private bool _isSelected;
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
    }

    private bool _isBrowserRunning;
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool IsBrowserRunning
    {
        get => _isBrowserRunning;
        set { _isBrowserRunning = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBrowserRunning))); }
    }

    // Navigation properties
    public ProxyConfig? ProxyConfig { get; set; }
    public ICollection<AccountCategoryMap> Categories { get; set; } = new List<AccountCategoryMap>();
    public ICollection<ActivityLog> ActivityLogs { get; set; } = new List<ActivityLog>();
    public ICollection<AccountLink> LinksAsSource { get; set; } = new List<AccountLink>();
    public ICollection<AccountLink> LinksAsTarget { get; set; } = new List<AccountLink>();

    public event PropertyChangedEventHandler? PropertyChanged;
}
