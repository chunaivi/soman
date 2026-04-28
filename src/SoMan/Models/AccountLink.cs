namespace SoMan.Models;

public enum LinkType
{
    SamePerson,
    SameGroup,
    InteractWith,
    DoNotInteract,
    MasterSlave
}

public class AccountLink
{
    public int Id { get; set; }
    public int SourceAccountId { get; set; }
    public int TargetAccountId { get; set; }
    public LinkType LinkType { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Account SourceAccount { get; set; } = null!;
    public Account TargetAccount { get; set; } = null!;
}
