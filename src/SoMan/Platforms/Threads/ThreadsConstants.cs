namespace SoMan.Platforms.Threads;

public static class ThreadsConstants
{
    // ── URLs ──
    public const string BaseUrl = "https://www.threads.net";
    public const string FeedUrl = "https://www.threads.net";
    public const string SearchUrl = "https://www.threads.net/search";
    public const string ProfileUrl = "https://www.threads.net/@{0}"; // string.Format with username

    // ── Rate Limits (per hour) ──
    public const int MaxLikesPerHour = 30;
    public const int MaxCommentsPerHour = 15;
    public const int MaxFollowsPerHour = 20;
    public const int MaxUnfollowsPerHour = 20;
    public const int MaxPostsPerHour = 5;
    public const int MaxRepostsPerHour = 10;

    // ── Timeouts (ms) ──
    public const int PageLoadTimeout = 30000;
    public const int ElementWaitTimeout = 10000;
    public const int NavigationTimeout = 15000;
    public const int ShortWait = 2000;

    // ── Scroll settings ──
    public const int ScrollStepPx = 300;
    public const int ScrollStepMinDelayMs = 500;
    public const int ScrollStepMaxDelayMs = 1500;
    public const int MinPostsBeforeAction = 3; // scroll past at least N posts before acting
}
