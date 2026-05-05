namespace SoMan.Models;

public enum ActionType
{
    ScrollFeed,
    Like,
    Comment,
    Follow,
    Unfollow,
    CreatePost,
    Repost,
    ViewProfile,
    Search,
    OpenRandomPost,
    // Replies to the account's most-recently-created post in this run —
    // used to build a Threads "utas" (thread chain) step-by-step.
    ReplyToOwnLastPost
}

public class ActionTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Platform Platform { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<ActionStep> Steps { get; set; } = new List<ActionStep>();
    public ICollection<ScheduledTask> ScheduledTasks { get; set; } = new List<ScheduledTask>();
}

public class ActionStep
{
    public int Id { get; set; }
    public int ActionTemplateId { get; set; }
    public int Order { get; set; }
    public ActionType ActionType { get; set; }
    public string ParametersJson { get; set; } = "{}";
    public int DelayMinMs { get; set; } = 3000;
    public int DelayMaxMs { get; set; } = 10000;

    // Navigation
    public ActionTemplate ActionTemplate { get; set; } = null!;
}
