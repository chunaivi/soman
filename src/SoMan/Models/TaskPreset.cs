namespace SoMan.Models;

/// <summary>
/// A saved combination of a template and a list of account IDs, so the user
/// can replay a recurring "run X on this batch of accounts" workflow without
/// re-picking from scratch every time.
/// </summary>
public class TaskPreset
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public int? ActionTemplateId { get; set; }
    public ActionTemplate? ActionTemplate { get; set; }

    /// <summary>JSON array of account IDs (e.g. "[1,2,5]").</summary>
    public string AccountIdsJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
