using System.Text.Json;
using Microsoft.Playwright;
using SoMan.Models;
using SoMan.Services.Browser;
using SoMan.Services.Delay;
using SoMan.Services.Logging;

namespace SoMan.Platforms.Threads;

/// <summary>
/// Orchestrator yang mengeksekusi ActionStep pada browser context akun.
/// Dipanggil oleh TaskEngine untuk setiap step dalam template.
/// </summary>
public class ThreadsAutomation
{
    private readonly IBrowserManager _browserManager;
    private readonly IDelayService _delay;
    private readonly IActivityLogger _logger;
    private readonly ThreadsActions _actions;

    public ThreadsAutomation(IBrowserManager browserManager, IDelayService delay, IActivityLogger logger)
    {
        _browserManager = browserManager;
        _delay = delay;
        _logger = logger;
        _actions = new ThreadsActions(delay);
    }

    /// <summary>
    /// Executes a single ActionStep on an account's browser page.
    /// Returns (success, message).
    /// </summary>
    public async Task<(bool Success, string Message)> ExecuteStepAsync(
        int accountId, ActionStep step, CancellationToken ct = default)
    {
        var page = _browserManager.GetPage(accountId);
        if (page == null)
            return (false, "Browser not open for this account.");

        // Check if still logged in
        if (!await _actions.IsLoggedInAsync(page))
            return (false, "Session expired — not logged in.");

        var parameters = ParseParameters(step.ParametersJson);

        try
        {
            return step.ActionType switch
            {
                ActionType.ScrollFeed => await ExecuteScrollAsync(page, accountId, parameters, ct),
                ActionType.Like => await ExecuteLikeAsync(page, accountId, parameters, ct),
                ActionType.Comment => await ExecuteCommentAsync(page, accountId, parameters, ct),
                ActionType.Follow => await ExecuteFollowAsync(page, accountId, parameters, ct),
                ActionType.Unfollow => await ExecuteUnfollowAsync(page, accountId, parameters, ct),
                ActionType.CreatePost => await ExecuteCreatePostAsync(page, accountId, parameters, ct),
                ActionType.Repost => await ExecuteRepostAsync(page, accountId, parameters, ct),
                ActionType.ViewProfile => await ExecuteViewProfileAsync(page, accountId, parameters, ct),
                ActionType.Search => await ExecuteSearchAsync(page, accountId, parameters, ct),
                ActionType.OpenRandomPost => await ExecuteOpenRandomPostAsync(page, accountId, parameters, ct),
                _ => (false, $"Unknown action type: {step.ActionType}")
            };
        }
        catch (TimeoutException)
        {
            await _logger.LogAsync(accountId, step.ActionType, null, ActionResult.Failed, "Timeout waiting for element");
            return (false, "Timeout — page element not found.");
        }
        catch (PlaywrightException ex)
        {
            await _logger.LogAsync(accountId, step.ActionType, null, ActionResult.Failed, ex.Message);
            return (false, $"Browser error: {ex.Message}");
        }
    }

    // ── Step Executors ──

    private async Task<(bool, string)> ExecuteScrollAsync(
        IPage page, int accountId, Dictionary<string, JsonElement> p, CancellationToken ct)
    {
        int duration = GetInt(p, "durationSeconds", 60);

        await _actions.WaitForFeedLoadAsync(page, ct);
        int scrolls = await _actions.ScrollFeedAsync(page, duration, ct);

        await _logger.LogAsync(accountId, ActionType.ScrollFeed, null, ActionResult.Success, $"Scrolled {scrolls} times in {duration}s");
        return (true, $"Scrolled {scrolls} times.");
    }

    private async Task<(bool, string)> ExecuteLikeAsync(
        IPage page, int accountId, Dictionary<string, JsonElement> p, CancellationToken ct)
    {
        int count = GetInt(p, "count", 5);

        // Navigate to feed if not already there
        if (!page.Url.TrimEnd('/').EndsWith("threads.net"))
        {
            await page.GotoAsync(ThreadsConstants.FeedUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await _delay.WaitAsync(2000, 4000, ct);
        }

        await _actions.WaitForFeedLoadAsync(page, ct);
        int liked = await _actions.LikeFeedPostsAsync(page, count, ct);

        await _logger.LogAsync(accountId, ActionType.Like, "feed", ActionResult.Success, $"Liked {liked}/{count} posts");
        return (true, $"Liked {liked}/{count} posts.");
    }

    private async Task<(bool, string)> ExecuteCommentAsync(
        IPage page, int accountId, Dictionary<string, JsonElement> p, CancellationToken ct)
    {
        int count = GetInt(p, "count", 2);
        string[] texts = GetStringArray(p, "texts", new[] { "Nice!", "Great post! 🔥", "Interesting! 👍" });

        // If on a post detail page, comment there directly
        if (page.Url.Contains("/post/"))
        {
            var rng = new Random();
            var text = texts[rng.Next(texts.Length)];
            // Coba cari tombol reply secara global jika tidak ada article
            var posts = page.Locator(ThreadsSelectors.PostArticle);
            int total = await posts.CountAsync();
            bool ok = false;
            if (total > 0)
            {
                ok = await _actions.CommentOnPostAsync(page, posts.First, text, ct);
            }
            else
            {
                // Fallback: cari tombol reply secara global
                var replyBtn = page.Locator(ThreadsSelectors.ReplyButton).First;
                if (await replyBtn.CountAsync() > 0)
                {
                    await replyBtn.ClickAsync();
                    await _delay.WaitAsync(1500, 3000, ct);
                    var textArea = page.Locator(ThreadsSelectors.ReplyTextArea).Last;
                    await textArea.WaitForAsync(new() { Timeout = ThreadsConstants.ElementWaitTimeout });
                    await textArea.FillAsync(text);
                    await _delay.WaitAsync(1000, 2000, ct);
                    var postBtn = page.Locator(ThreadsSelectors.ReplyPostButton).First;
                    await postBtn.ClickAsync();
                    await _delay.WaitAsync(2000, 4000, ct);
                    ok = true;
                }
            }
            await _logger.LogAsync(accountId, ActionType.Comment, page.Url, 
                ok ? ActionResult.Success : ActionResult.Failed, ok ? $"Commented: {text}" : "Failed to comment");
            return (ok, ok ? "Commented on opened post." : "Failed to comment.");
        }

        // Navigate to feed if not already there
        if (!page.Url.TrimEnd('/').EndsWith("threads.net"))
        {
            await page.GotoAsync(ThreadsConstants.FeedUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await _delay.WaitAsync(2000, 4000, ct);
        }

        await _actions.WaitForFeedLoadAsync(page, ct);
        int commented = await _actions.CommentOnFeedPostsAsync(page, count, texts, ct);

        await _logger.LogAsync(accountId, ActionType.Comment, "feed", ActionResult.Success, $"Commented on {commented}/{count} posts");
        return (true, $"Commented on {commented}/{count} posts.");
    }

    private async Task<(bool, string)> ExecuteFollowAsync(
        IPage page, int accountId, Dictionary<string, JsonElement> p, CancellationToken ct)
    {
        int count = GetInt(p, "count", 5);
        string source = GetString(p, "source", "suggested");
        string? username = GetString(p, "username", null);

        if (username != null)
        {
            bool ok = await _actions.FollowUserAsync(page, username, ct);
            await _logger.LogAsync(accountId, ActionType.Follow, $"@{username}",
                ok ? ActionResult.Success : ActionResult.Skipped, ok ? "Followed" : "Already following or not found");
            return (ok, ok ? $"Followed @{username}." : $"Could not follow @{username}.");
        }

        int followed = await _actions.FollowFromSuggestedAsync(page, count, ct);
        await _logger.LogAsync(accountId, ActionType.Follow, source, ActionResult.Success, $"Followed {followed}/{count} users");
        return (true, $"Followed {followed}/{count} users from {source}.");
    }

    private async Task<(bool, string)> ExecuteUnfollowAsync(
        IPage page, int accountId, Dictionary<string, JsonElement> p, CancellationToken ct)
    {
        string? username = GetString(p, "username", null);
        if (username == null)
            return (false, "Username is required for unfollow.");

        bool ok = await _actions.UnfollowUserAsync(page, username, ct);
        await _logger.LogAsync(accountId, ActionType.Unfollow, $"@{username}",
            ok ? ActionResult.Success : ActionResult.Skipped, ok ? "Unfollowed" : "Not following or not found");
        return (ok, ok ? $"Unfollowed @{username}." : $"Could not unfollow @{username}.");
    }

    private async Task<(bool, string)> ExecuteCreatePostAsync(
        IPage page, int accountId, Dictionary<string, JsonElement> p, CancellationToken ct)
    {
        string? text = GetString(p, "text", null);
        if (string.IsNullOrWhiteSpace(text))
            return (false, "Post text is required.");

        bool ok = await _actions.CreatePostAsync(page, text, ct);
        await _logger.LogAsync(accountId, ActionType.CreatePost, null,
            ok ? ActionResult.Success : ActionResult.Failed, ok ? $"Posted: {text[..Math.Min(50, text.Length)]}" : "Failed to post");
        return (ok, ok ? "Post created." : "Failed to create post.");
    }

    private async Task<(bool, string)> ExecuteRepostAsync(
        IPage page, int accountId, Dictionary<string, JsonElement> p, CancellationToken ct)
    {
        int count = GetInt(p, "count", 2);

        await _actions.WaitForFeedLoadAsync(page, ct);
        int reposted = await _actions.RepostFeedPostsAsync(page, count, ct);

        await _logger.LogAsync(accountId, ActionType.Repost, "feed", ActionResult.Success, $"Reposted {reposted}/{count} posts");
        return (true, $"Reposted {reposted}/{count} posts.");
    }

    private async Task<(bool, string)> ExecuteViewProfileAsync(
        IPage page, int accountId, Dictionary<string, JsonElement> p, CancellationToken ct)
    {
        string? username = GetString(p, "username", null);
        if (username == null)
            return (false, "Username is required for view profile.");

        await _actions.ViewProfileAsync(page, username, ct);
        await _logger.LogAsync(accountId, ActionType.ViewProfile, $"@{username}", ActionResult.Success, "Profile viewed");
        return (true, $"Viewed profile @{username}.");
    }

    private async Task<(bool, string)> ExecuteSearchAsync(
        IPage page, int accountId, Dictionary<string, JsonElement> p, CancellationToken ct)
    {
        string keyword = GetString(p, "keyword", "threads") ?? "threads";
        bool interact = GetBool(p, "interactWithResults", false);

        int interactions = await _actions.SearchAndInteractAsync(page, keyword, interact, ct);
        await _logger.LogAsync(accountId, ActionType.Search, keyword, ActionResult.Success, $"Searched '{keyword}', {interactions} interactions");
        return (true, $"Searched '{keyword}' — {interactions} interactions.");
    }

    private async Task<(bool, string)> ExecuteOpenRandomPostAsync(
        IPage page, int accountId, Dictionary<string, JsonElement> p, CancellationToken ct)
    {
        // Navigate to feed if not already there
        if (!page.Url.TrimEnd('/').EndsWith("threads.net") && !page.Url.Contains("/search"))
        {
            await page.GotoAsync(ThreadsConstants.FeedUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await _delay.WaitAsync(2000, 4000, ct);
        }

        await _actions.WaitForFeedLoadAsync(page, ct);
        bool clicked = await _actions.ClickRandomPostAsync(page, ct);

        await _logger.LogAsync(accountId, ActionType.OpenRandomPost, page.Url,
            clicked ? ActionResult.Success : ActionResult.Failed,
            clicked ? "Clicked random post" : "No posts found to click");
        return (clicked, clicked ? "Clicked random post." : "No posts found.");
    }

    // ── Parameter Helpers ──

    private static Dictionary<string, JsonElement> ParseParameters(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
                   ?? new Dictionary<string, JsonElement>();
        }
        catch
        {
            return new Dictionary<string, JsonElement>();
        }
    }

    private static int GetInt(Dictionary<string, JsonElement> p, string key, int fallback)
    {
        if (p.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Number)
            return v.GetInt32();
        return fallback;
    }

    private static string? GetString(Dictionary<string, JsonElement> p, string key, string? fallback)
    {
        if (p.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String)
            return v.GetString();
        return fallback;
    }

    private static bool GetBool(Dictionary<string, JsonElement> p, string key, bool fallback)
    {
        if (p.TryGetValue(key, out var v))
        {
            if (v.ValueKind == JsonValueKind.True) return true;
            if (v.ValueKind == JsonValueKind.False) return false;
        }
        return fallback;
    }

    private static string[] GetStringArray(Dictionary<string, JsonElement> p, string key, string[] fallback)
    {
        if (p.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string>();
            foreach (var item in v.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    list.Add(item.GetString()!);
            }
            return list.Count > 0 ? list.ToArray() : fallback;
        }
        return fallback;
    }
}
