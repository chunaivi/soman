namespace SoMan.Models;

public enum ActionResult
{
    Success,
    Failed,
    Skipped
}

public class ActivityLog
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public ActionType ActionType { get; set; }
    public string? Target { get; set; }
    public ActionResult Result { get; set; }
    public string? Details { get; set; }
    public string? ScreenshotPath { get; set; }
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Account Account { get; set; } = null!;
}
