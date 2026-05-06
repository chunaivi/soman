using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using Microsoft.Playwright;
using SoMan.Models;
using SoMan.Services.Browser;
using SoMan.Services.Delay;
using SoMan.Services.Logging;
using SoMan.Services.Text;

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

    // Tracks each account's most-recently-created post URL within the currently
    // running executions. Used by ReplyToOwnLastPost to chain segments into a
    // Threads "utas". Entries live for the lifetime of the app; each fresh
    // CreatePost overwrites the previous URL for that account.
    private readonly ConcurrentDictionary<int, string> _lastOwnPostUrl = new();

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
                ActionType.ReplyToOwnLastPost => await ExecuteReplyToOwnLastPostAsync(page, accountId, parameters, ct),
                ActionType.CreateThreadFromText => await ExecuteCreateThreadFromTextAsync(page, accountId, parameters, ct),
                ActionType.AddToThread => await ExecuteAddToThreadAsync(page, accountId, parameters, ct),
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

        // If on a post detail page, comment there directly.
        if (page.Url.Contains("/post/"))
        {
            var rng = new Random();
            var text = texts[rng.Next(texts.Length)];
            var posts = page.Locator(ThreadsSelectors.PostArticle);
            int total = await posts.CountAsync();

            // When no <article> is rendered (e.g. the post detail uses a different
            // container), fall back to scoping at <body>. CommentOnPostAsync will
            // still find the global Reply button and run the same robust composer
            // flow (real keystrokes + dialog-scoped Post button + enable wait).
            ILocator scope = total > 0 ? posts.First : page.Locator("body");

            bool ok = false;
            try
            {
                ok = await _actions.CommentOnPostAsync(page, scope, text, ct);
            }
            catch
            {
                ok = false;
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

        var postUrl = await _actions.CreatePostAsync(page, text, ct);
        bool ok = postUrl != null;

        if (ok)
        {
            // Remember this URL so a later ReplyToOwnLastPost step can chain to it.
            _lastOwnPostUrl[accountId] = postUrl!;
        }

        await _logger.LogAsync(accountId, ActionType.CreatePost, postUrl,
            ok ? ActionResult.Success : ActionResult.Failed,
            ok ? $"Posted: {text[..Math.Min(50, text.Length)]}" : "Failed to post");

        return (ok, ok ? "Post created." : "Failed to create post.");
    }

    private async Task<(bool, string)> ExecuteReplyToOwnLastPostAsync(
        IPage page, int accountId, Dictionary<string, JsonElement> p, CancellationToken ct)
    {
        if (!_lastOwnPostUrl.TryGetValue(accountId, out var postUrl) || string.IsNullOrWhiteSpace(postUrl))
            return (false, "No previous CreatePost in this run — nothing to reply to.");

        string[] texts = GetStringArray(p, "texts", Array.Empty<string>());
        string? single = GetString(p, "text", null);
        if (texts.Length == 0 && !string.IsNullOrWhiteSpace(single))
            texts = new[] { single! };

        if (texts.Length == 0)
            return (false, "Reply text is required (provide `text` or `texts`).");

        var rng = new Random();
        string replyText = texts[rng.Next(texts.Length)];

        // Navigate to the target post
        if (!page.Url.TrimEnd('/').Equals(postUrl.TrimEnd('/')))
        {
            await page.GotoAsync(postUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = ThreadsConstants.PageLoadTimeout });
            await _delay.WaitAsync(1500, 3000, ct);
        }

        // Use the existing reply flow, scoped to the first article on the post page
        // (the target post itself). This uses the same nested-reply mechanism
        // that Comment uses for other users' posts.
        var target = page.Locator(ThreadsSelectors.PostArticle).First;
        if (await target.CountAsync() == 0)
            return (false, "Target post article not found after navigation.");

        try
        {
            bool ok = await _actions.CommentOnPostAsync(page, target, replyText, ct);
            await _logger.LogAsync(accountId, ActionType.ReplyToOwnLastPost, postUrl,
                ok ? ActionResult.Success : ActionResult.Failed,
                ok ? $"Replied: {replyText[..Math.Min(50, replyText.Length)]}" : "Reply submit failed");
            return (ok, ok ? "Reply posted to own last post." : "Reply submission failed.");
        }
        catch (Exception ex)
        {
            await _logger.LogAsync(accountId, ActionType.ReplyToOwnLastPost, postUrl, ActionResult.Failed, ex.Message);
            return (false, $"Reply failed: {ex.Message}");
        }
    }

    private async Task<(bool, string)> ExecuteAddToThreadAsync(
        IPage page, int accountId, Dictionary<string, JsonElement> p, CancellationToken ct)
    {
        // Required: URL of the post to append a reply to (head of an existing
        // thread, or any segment). User pastes this — can be our own thread
        // (re-engage followers) or someone else's (join the conversation).
        string? url = GetString(p, "url", null);
        if (string.IsNullOrWhiteSpace(url))
            return (false, "Target thread URL is required for AddToThread.");

        string[] texts = GetStringArray(p, "texts", Array.Empty<string>());
        string? single = GetString(p, "text", null);
        if (texts.Length == 0 && !string.IsNullOrWhiteSpace(single))
            texts = new[] { single! };
        if (texts.Length == 0)
            return (false, "Reply text is required (provide `text` or `texts`).");

        var rng = new Random();
        string replyText = texts[rng.Next(texts.Length)];

        // Navigate to the target post if not already there.
        if (!page.Url.TrimEnd('/').Equals(url!.TrimEnd('/')))
        {
            await page.GotoAsync(url!, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = ThreadsConstants.PageLoadTimeout });
            await _delay.WaitAsync(1500, 3000, ct);
        }

        // Reply to the first article on the page (that's the permalinked post);
        // reuses the verified nested-reply flow that Comment uses.
        var target = page.Locator(ThreadsSelectors.PostArticle).First;
        if (await target.CountAsync() == 0)
            return (false, "Target post article not found after navigation.");

        try
        {
            bool ok = await _actions.CommentOnPostAsync(page, target, replyText, ct);
            await _logger.LogAsync(accountId, ActionType.AddToThread, url,
                ok ? ActionResult.Success : ActionResult.Failed,
                ok ? $"Added to thread: {replyText[..Math.Min(50, replyText.Length)]}" : "AddToThread submit failed");
            return (ok, ok ? "Added to existing thread." : "AddToThread submission failed.");
        }
        catch (Exception ex)
        {
            await _logger.LogAsync(accountId, ActionType.AddToThread, url, ActionResult.Failed, ex.Message);
            return (false, $"AddToThread failed: {ex.Message}");
        }
    }

    private async Task<(bool, string)> ExecuteCreateThreadFromTextAsync(
        IPage page, int accountId, Dictionary<string, JsonElement> p, CancellationToken ct)
    {
        // Resolve text source: pasted text takes priority, fall back to file path.
        string? text = GetString(p, "text", null);
        string? filePath = GetString(p, "filePath", null);

        if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(filePath))
        {
            try
            {
                if (!File.Exists(filePath))
                    return (false, $"File not found: {filePath}");
                text = await File.ReadAllTextAsync(filePath, ct);
            }
            catch (Exception ex)
            {
                return (false, $"Failed to read file: {ex.Message}");
            }
        }

        if (string.IsNullOrWhiteSpace(text))
            return (false, "Thread text is required (paste `text` or provide `filePath`).");

        int maxChars = GetInt(p, "maxCharsPerSegment", ThreadTextSplitter.ThreadsMaxCharsPerPost);
        int delayMin = GetInt(p, "segmentDelayMinMs", 3000);
        int delayMax = GetInt(p, "segmentDelayMaxMs", 8000);
        if (delayMax < delayMin) delayMax = delayMin;

        var segments = ThreadTextSplitter.Split(text, maxChars);
        if (segments.Count == 0)
            return (false, "Text produced zero segments after split.");

        // Segment 1 → CreatePost (reuses the URL-capture logic so later
        // segments can reply to it).
        var headUrl = await _actions.CreatePostAsync(page, segments[0], ct);
        if (headUrl == null)
        {
            await _logger.LogAsync(accountId, ActionType.CreateThreadFromText, null,
                ActionResult.Failed, "Head post failed");
            return (false, "Failed to create head post of thread.");
        }

        _lastOwnPostUrl[accountId] = headUrl;
        int succeeded = 1;

        // Segments 2..N → reply to head (Threads renders chained self-replies
        // nested under the head post).
        for (int i = 1; i < segments.Count && !ct.IsCancellationRequested; i++)
        {
            await _delay.WaitAsync(delayMin, delayMax, ct);

            // Make sure we're on the head post before each reply — Threads'
            // composer resets state between replies.
            if (!page.Url.TrimEnd('/').Equals(headUrl.TrimEnd('/')))
            {
                await page.GotoAsync(headUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = ThreadsConstants.PageLoadTimeout });
                await _delay.WaitAsync(1500, 3000, ct);
            }

            var target = page.Locator(ThreadsSelectors.PostArticle).First;
            if (await target.CountAsync() == 0)
            {
                await _logger.LogAsync(accountId, ActionType.CreateThreadFromText, headUrl,
                    ActionResult.Failed, $"Segment {i + 1}/{segments.Count}: head article not found");
                return (false, $"Thread segment {i + 1}/{segments.Count} failed — head article not found.");
            }

            try
            {
                bool ok = await _actions.CommentOnPostAsync(page, target, segments[i], ct);
                if (!ok)
                {
                    await _logger.LogAsync(accountId, ActionType.CreateThreadFromText, headUrl,
                        ActionResult.Failed, $"Segment {i + 1}/{segments.Count}: reply submit failed");
                    return (false, $"Thread segment {i + 1}/{segments.Count} failed to submit.");
                }
                succeeded++;
            }
            catch (Exception ex)
            {
                await _logger.LogAsync(accountId, ActionType.CreateThreadFromText, headUrl,
                    ActionResult.Failed, $"Segment {i + 1}/{segments.Count}: {ex.Message}");
                return (false, $"Thread segment {i + 1}/{segments.Count} error: {ex.Message}");
            }
        }

        await _logger.LogAsync(accountId, ActionType.CreateThreadFromText, headUrl,
            ActionResult.Success, $"Posted thread: {succeeded}/{segments.Count} segments");
        return (true, $"Thread posted: {succeeded}/{segments.Count} segments.");
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

        // ClickRandomPostAsync waits for the feed itself, so no outer wait here.
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
