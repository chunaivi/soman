namespace SoMan.Models;

public class AccountCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#2196F3";
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<AccountCategoryMap> Accounts { get; set; } = new List<AccountCategoryMap>();
}

public class AccountCategoryMap
{
    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public int AccountCategoryId { get; set; }
    public AccountCategory AccountCategory { get; set; } = null!;
}
