using System.Text;
using Microsoft.Playwright;
using SoMan.Services.Delay;

namespace SoMan.Platforms.Threads;

/// <summary>
/// Low-level Threads actions — each method performs one atomic browser action.
/// </summary>
public class ThreadsActions
{
    private readonly IDelayService _delay;

    public ThreadsActions(IDelayService delay)
    {
        _delay = delay;
    }

    // ── Scroll Feed ──

    public async Task<int> ScrollFeedAsync(IPage page, int durationSeconds, CancellationToken ct = default)
    {
        int scrolled = 0;
        var end = DateTime.UtcNow.AddSeconds(durationSeconds);

        while (DateTime.UtcNow < end && !ct.IsCancellationRequested)
        {
            await page.Mouse.WheelAsync(0, ThreadsConstants.ScrollStepPx);
            scrolled++;
            await _delay.WaitAsync(
                ThreadsConstants.ScrollStepMinDelayMs,
                ThreadsConstants.ScrollStepMaxDelayMs, ct);
        }

        return scrolled;
    }

    // ── Like ──

    public async Task<bool> LikePostAsync(IPage page, ILocator postArticle, CancellationToken ct = default)
    {
        var likeBtn = postArticle.Locator(ThreadsSelectors.LikeButton).First;
        if (await likeBtn.CountAsync() == 0)
            return false;

        // Already liked? (Unlike button present = already liked)
        var unlikeBtn = postArticle.Locator(ThreadsSelectors.UnlikeButton).First;
        if (await unlikeBtn.CountAsync() > 0)
            return false;

        await likeBtn.ClickAsync();
        await _delay.WaitAsync(1000, 2000, ct);
        return true;
    }

    public async Task<int> LikeFeedPostsAsync(IPage page, int count, CancellationToken ct = default)
    {
        int liked = 0;

        // Scroll a bit first to load posts
        await ScrollFeedAsync(page, 3, ct);

        var posts = page.Locator(ThreadsSelectors.PostArticle);
        int total = await posts.CountAsync();

        for (int i = 0; i < total && liked < count && !ct.IsCancellationRequested; i++)
        {
            var post = posts.Nth(i);

            // Scroll post into view
            await post.ScrollIntoViewIfNeededAsync();
            await _delay.WaitAsync(500, 1000, ct);

            if (await LikePostAsync(page, post, ct))
            {
                liked++;
                await _delay.WaitAsync(3000, 8000, ct);
            }

            // Scroll down to next posts
            if (i >= total - 2 && liked < count)
            {
                await page.Mouse.WheelAsync(0, 500);
                await _delay.WaitAsync(2000, 4000, ct);
                total = await posts.CountAsync(); // recount after scroll
            }
        }

        return liked;
    }

    // ── Comment ──

    public async Task<bool> CommentOnPostAsync(IPage page, ILocator postArticle, string text, CancellationToken ct = default)
    {
        var dbg = new StringBuilder();

        try
        {
            dbg.Append("[Comment] start; ");

            var replyBtn = postArticle.Locator(ThreadsSelectors.ReplyButton).First;
            var replyCount = await replyBtn.CountAsync();
            dbg.Append($"replyCount={replyCount}; ");

            if (replyCount == 0)
                throw new Exception($"{dbg}reply button not found");

            await replyBtn.ClickAsync();
            dbg.Append("replyClicked; ");
            await _delay.WaitAsync(1500, 3000, ct);

            // Wait for reply textbox
            var textArea = page.Locator(ThreadsSelectors.ReplyTextArea).Last;
            await textArea.WaitForAsync(new() { Timeout = ThreadsConstants.ElementWaitTimeout });
            dbg.Append("textAreaReady; ");

            await textArea.FillAsync(text);
            dbg.Append($"textFilled(len={text.Length}); ");
            await _delay.WaitAsync(1000, 2000, ct);

            // Try multiple post button selectors explicitly for debug
            var selector1 = "button:has-text('Post')";
            var selector2 = "[role='button']:has-text('Post')";
            var selector3 = "div[role='button']:has-text('Post')";
            var selector4 = "span:has-text('Post')";

            var c1 = await page.Locator(selector1).CountAsync();
            var c2 = await page.Locator(selector2).CountAsync();
            var c3 = await page.Locator(selector3).CountAsync();
            var c4 = await page.Locator(selector4).CountAsync();
            dbg.Append($"postCandidates(button={c1},roleBtn={c2},divRole={c3},span={c4}); ");

            // Prefer bottom black "Post" button in active reply composer (scoped first)
            ILocator composer = textArea.Locator("xpath=ancestor::*[self::div or self::form][1]");
            ILocator postBtn = composer.Locator("button:has-text('Post')").Last;

            if (await postBtn.CountAsync() == 0)
                postBtn = composer.Locator("[role='button']:has-text('Post')").Last;
            if (await postBtn.CountAsync() == 0)
                postBtn = page.Locator("button:has-text('Post')").Last;
            if (await postBtn.CountAsync() == 0)
                postBtn = page.Locator("[role='button']:has-text('Post')").Last;
            if (await postBtn.CountAsync() == 0)
                postBtn = page.Locator(ThreadsSelectors.ReplyPostButton).Last;

            await postBtn.WaitForAsync(new() { Timeout = ThreadsConstants.ElementWaitTimeout });
            await postBtn.ScrollIntoViewIfNeededAsync();
            dbg.Append("postBtnReady; ");

            bool clicked = false;

            // Size-based targeting (from inspector: ~61x36) to lock on real Post button
            ILocator sizeMatchedBtn = postBtn;
            try
            {
                const double targetW = 61;
                const double targetH = 36;
                const double tolW = 24;
                const double tolH = 18;

                var scopedCandidates = composer.Locator("button:has-text('Post'), [role='button']:has-text('Post')");
                int scopedCount = await scopedCandidates.CountAsync();
                dbg.Append($"scopedCandidates={scopedCount}; ");

                double bestScore = double.MaxValue;
                ILocator? best = null;

                for (int i = 0; i < scopedCount; i++)
                {
                    var cand = scopedCandidates.Nth(i);
                    var box = await cand.BoundingBoxAsync();
                    if (box == null) continue;

                    var dw = Math.Abs(box.Width - targetW);
                    var dh = Math.Abs(box.Height - targetH);
                    dbg.Append($"cand#{i}=({box.Width:F1}x{box.Height:F1}); ");

                    if (dw <= tolW && dh <= tolH)
                    {
                        var score = dw + dh;
                        if (score < bestScore)
                        {
                            bestScore = score;
                            best = cand;
                        }
                    }
                }

                if (best != null)
                {
                    sizeMatchedBtn = best;
                    dbg.Append("sizeMatch=found; ");
                }
                else
                {
                    dbg.Append("sizeMatch=none; ");
                }
            }
            catch (Exception exSize)
            {
                dbg.Append($"sizeMatchError({exSize.Message}); ");
            }

            // Attempt 1: normal click on size-matched button
            try
            {
                await sizeMatchedBtn.ClickAsync(new() { Timeout = 5000 });
                dbg.Append("click=normal_ok; ");
                clicked = true;
            }
            catch (Exception ex1)
            {
                dbg.Append($"click=normal_fail({ex1.Message}); ");
            }

            // Attempt 2: force click
            if (!clicked)
            {
                try
                {
                    await sizeMatchedBtn.ClickAsync(new() { Force = true, Timeout = 5000 });
                    dbg.Append("click=force_ok; ");
                    clicked = true;
                }
                catch (Exception ex2)
                {
                    dbg.Append($"click=force_fail({ex2.Message}); ");
                }
            }

            // Attempt 3: click by coordinate center on size-matched button
            if (!clicked)
            {
                try
                {
                    var box = await sizeMatchedBtn.BoundingBoxAsync();
                    if (box != null)
                    {
                        var cx = box.X + (box.Width / 2);
                        var cy = box.Y + (box.Height / 2);
                        await page.Mouse.ClickAsync(cx, cy);
                        dbg.Append($"click=coord_ok({cx:F1},{cy:F1},{box.Width:F1}x{box.Height:F1}); ");
                        clicked = true;
                    }
                    else
                    {
                        dbg.Append("click=coord_fail(no_bbox); ");
                    }
                }
                catch (Exception ex3)
                {
                    dbg.Append($"click=coord_fail({ex3.Message}); ");
                }
            }

            // Attempt 4: keyboard enter on textarea
            if (!clicked)
            {
                try
                {
                    await textArea.PressAsync("Enter");
                    dbg.Append("click=enter_on_textarea_ok; ");
                    clicked = true;
                }
                catch (Exception ex4)
                {
                    dbg.Append($"click=enter_on_textarea_fail({ex4.Message}); ");
                }
            }

            // Give time for submit transition
            await _delay.WaitAsync(2000, 4000, ct);

            // Ensure editor closes / returns to post page state
            bool editorStillVisible = false;
            string currentValue = string.Empty;
            try
            {
                var editor = page.Locator(ThreadsSelectors.ReplyTextArea).Last;
                editorStillVisible = await editor.IsVisibleAsync();
                currentValue = await editor.InputValueAsync();
            }
            catch
            {
                // editor likely gone (good)
            }

            bool uiReturnedToPost = !editorStillVisible || string.IsNullOrWhiteSpace(currentValue);
            dbg.Append($"uiReturnedToPost={uiReturnedToPost}; editorVisible={editorStillVisible}; textLen={currentValue.Length}; ");

            if (!clicked || !uiReturnedToPost)
                throw new Exception($"{dbg}post submit click/transition not confirmed");

            // Strict success criteria:
            // Wait until posted comment text appears on page (exact match), max 30s
            string escaped = EscapeForTextSelector(text);
            var postedComment = page.Locator($"text=\"{escaped}\"").First;

            bool appeared = false;
            var waitUntil = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < waitUntil && !ct.IsCancellationRequested)
            {
                try
                {
                    if (await postedComment.CountAsync() > 0 && await postedComment.IsVisibleAsync())
                    {
                        appeared = true;
                        break;
                    }
                }
                catch
                {
                    // ignore transient DOM issues
                }

                await _delay.WaitAsync(800, 1500, ct);
            }

            dbg.Append($"commentAppeared={appeared}; ");

            if (!appeared)
                throw new Exception($"{dbg}comment not visible after submit within timeout");

            return true;
        }
        catch (Exception ex)
        {
            throw new Exception($"{dbg}error={ex.Message}", ex);
        }
    }

    public async Task<int> CommentOnFeedPostsAsync(IPage page, int count, string[] texts, CancellationToken ct = default)
    {
        int commented = 0;
        var rng = new Random();

        await ScrollFeedAsync(page, 3, ct);

        var posts = page.Locator(ThreadsSelectors.PostArticle);
        int total = await posts.CountAsync();

        for (int i = 0; i < total && commented < count && !ct.IsCancellationRequested; i++)
        {
            var post = posts.Nth(i);
            await post.ScrollIntoViewIfNeededAsync();
            await _delay.WaitAsync(500, 1000, ct);

            var text = texts[rng.Next(texts.Length)];
            try
            {
                if (await CommentOnPostAsync(page, post, text, ct))
                {
                    commented++;
                    await _delay.WaitAsync(5000, 12000, ct);
                }
            }
            catch
            {
                // Comment dialog may fail — skip and continue
                await page.Keyboard.PressAsync("Escape");
                await _delay.WaitAsync(1000, 2000, ct);
            }
        }

        return commented;
    }

    // ── Follow ──

    public async Task<bool> FollowUserAsync(IPage page, string username, CancellationToken ct = default)
    {
        var profileUrl = string.Format(ThreadsConstants.ProfileUrl, username);
        await page.GotoAsync(profileUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = ThreadsConstants.PageLoadTimeout });
        await _delay.WaitAsync(2000, 4000, ct);

        var followBtn = page.Locator(ThreadsSelectors.FollowButton).First;
        if (await followBtn.CountAsync() == 0)
            return false; // already following or doesn't exist

        await followBtn.ClickAsync();
        await _delay.WaitAsync(2000, 4000, ct);
        return true;
    }

    public async Task<int> FollowFromSuggestedAsync(IPage page, int count, CancellationToken ct = default)
    {
        int followed = 0;

        // Search page has suggested profiles
        await page.GotoAsync(ThreadsConstants.SearchUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = ThreadsConstants.PageLoadTimeout });
        await _delay.WaitAsync(2000, 4000, ct);

        var followBtns = page.Locator(ThreadsSelectors.FollowButton);
        int total = await followBtns.CountAsync();

        for (int i = 0; i < total && followed < count && !ct.IsCancellationRequested; i++)
        {
            var btn = followBtns.Nth(i);
            try
            {
                await btn.ScrollIntoViewIfNeededAsync();
                await btn.ClickAsync();
                followed++;
                await _delay.WaitAsync(3000, 8000, ct);
            }
            catch { /* button may have changed state */ }
        }

        return followed;
    }

    // ── Unfollow ──

    public async Task<bool> UnfollowUserAsync(IPage page, string username, CancellationToken ct = default)
    {
        var profileUrl = string.Format(ThreadsConstants.ProfileUrl, username);
        await page.GotoAsync(profileUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = ThreadsConstants.PageLoadTimeout });
        await _delay.WaitAsync(2000, 4000, ct);

        var followingBtn = page.Locator(ThreadsSelectors.FollowingButton).First;
        if (await followingBtn.CountAsync() == 0)
            return false;

        await followingBtn.ClickAsync();
        await _delay.WaitAsync(1000, 2000, ct);

        var confirmBtn = page.Locator(ThreadsSelectors.UnfollowConfirm).First;
        if (await confirmBtn.CountAsync() > 0)
        {
            await confirmBtn.ClickAsync();
            await _delay.WaitAsync(2000, 4000, ct);
        }

        return true;
    }

    // ── Create Post ──

    /// <summary>
    /// Creates a new top-level post. Returns the URL of the newly-created post
    /// on success, or null if the post could not be created or its URL could
    /// not be recovered.
    /// </summary>
    public async Task<string?> CreatePostAsync(IPage page, string text, CancellationToken ct = default)
    {
        var createBtn = page.Locator(ThreadsSelectors.CreateButton).First;
        if (await createBtn.CountAsync() == 0)
            return null;

        await createBtn.ClickAsync();
        await _delay.WaitAsync(1500, 3000, ct);

        var textBox = page.Locator(ThreadsSelectors.PostTextBox).First;
        await textBox.WaitForAsync(new() { Timeout = ThreadsConstants.ElementWaitTimeout });
        await textBox.FillAsync(text);
        await _delay.WaitAsync(1000, 2000, ct);

        var postBtn = page.Locator(ThreadsSelectors.PostSubmitButton).First;
        await postBtn.ClickAsync();
        await _delay.WaitAsync(3000, 6000, ct);

        // Best-effort URL recovery: find the article containing the just-posted
        // text and grab its /post/ link. Works in the typical case where the
        // composer closes back onto the feed/profile and the new post renders.
        return await FindNewlyPostedUrlAsync(page, text, ct);
    }

    private async Task<string?> FindNewlyPostedUrlAsync(IPage page, string postedText, CancellationToken ct)
    {
        // Threads' has-text is very sensitive to length / special chars — use a
        // short snippet of the post that's unlikely to collide with nearby UI.
        var snippet = postedText.Length > 60 ? postedText[..60] : postedText;
        snippet = snippet.Replace("\"", "\\\"");

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            try
            {
                var article = page.Locator($"article:has-text(\"{snippet}\")").First;
                if (await article.CountAsync() > 0)
                {
                    var link = article.Locator("a[href*='/post/']").First;
                    if (await link.CountAsync() > 0)
                    {
                        var href = await link.GetAttributeAsync("href");
                        if (!string.IsNullOrWhiteSpace(href))
                        {
                            return href.StartsWith("http")
                                ? href
                                : $"https://www.threads.net{href}";
                        }
                    }
                }
            }
            catch { /* transient DOM / selector hiccup */ }

            await _delay.WaitAsync(800, 1400, ct);
        }

        return null;
    }

    // ── Repost ──

    public async Task<bool> RepostAsync(IPage page, ILocator postArticle, CancellationToken ct = default)
    {
        var repostBtn = postArticle.Locator(ThreadsSelectors.RepostButton).First;
        if (await repostBtn.CountAsync() == 0)
            return false;

        await repostBtn.ClickAsync();
        await _delay.WaitAsync(1000, 2000, ct);

        var repostOption = page.Locator(ThreadsSelectors.RepostOption).First;
        if (await repostOption.CountAsync() > 0)
        {
            await repostOption.ClickAsync();
            await _delay.WaitAsync(2000, 4000, ct);
            return true;
        }

        return false;
    }

    public async Task<int> RepostFeedPostsAsync(IPage page, int count, CancellationToken ct = default)
    {
        int reposted = 0;

        await ScrollFeedAsync(page, 3, ct);

        var posts = page.Locator(ThreadsSelectors.PostArticle);
        int total = await posts.CountAsync();

        for (int i = 0; i < total && reposted < count && !ct.IsCancellationRequested; i++)
        {
            var post = posts.Nth(i);
            await post.ScrollIntoViewIfNeededAsync();
            await _delay.WaitAsync(500, 1000, ct);

            try
            {
                if (await RepostAsync(page, post, ct))
                {
                    reposted++;
                    await _delay.WaitAsync(4000, 10000, ct);
                }
            }
            catch
            {
                await page.Keyboard.PressAsync("Escape");
                await _delay.WaitAsync(1000, 2000, ct);
            }
        }

        return reposted;
    }

    // ── View Profile ──

    public async Task ViewProfileAsync(IPage page, string username, CancellationToken ct = default)
    {
        var profileUrl = string.Format(ThreadsConstants.ProfileUrl, username);
        await page.GotoAsync(profileUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = ThreadsConstants.PageLoadTimeout });
        await _delay.WaitAsync(3000, 6000, ct);

        // Scroll a bit to simulate reading
        await ScrollFeedAsync(page, 5, ct);
    }

    // ── Search ──

    public async Task<int> SearchAndInteractAsync(IPage page, string keyword, bool interactWithResults, CancellationToken ct = default)
    {
        await page.GotoAsync(ThreadsConstants.SearchUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = ThreadsConstants.PageLoadTimeout });
        await _delay.WaitAsync(1500, 3000, ct);

        var searchInput = page.Locator(ThreadsSelectors.SearchInput).First;
        await searchInput.WaitForAsync(new() { Timeout = ThreadsConstants.ElementWaitTimeout });
        await searchInput.FillAsync(keyword);
        await _delay.WaitAsync(2000, 4000, ct);

        // Press Enter to search
        await searchInput.PressAsync("Enter");
        await _delay.WaitAsync(3000, 5000, ct);

        int interactions = 0;

        if (interactWithResults)
        {
            // Click on search results and like their posts
            var results = page.Locator(ThreadsSelectors.SearchResult);
            int count = Math.Min(await results.CountAsync(), 3);

            for (int i = 0; i < count && !ct.IsCancellationRequested; i++)
            {
                var result = results.Nth(i);
                try
                {
                    await result.ClickAsync();
                    await _delay.WaitAsync(2000, 4000, ct);
                    interactions++;

                    // Navigate back
                    await page.GoBackAsync();
                    await _delay.WaitAsync(1500, 3000, ct);
                }
                catch { break; }
            }
        }

        return interactions;
    }

    // ── Click Random Post ──

    /// <summary>
    /// Clicks on a random post thumbnail/link from current page (feed/search).
    /// Waits for load + scrolls, picks random post link, clicks to interact.
    /// More reliable than opening detail view.
    /// </summary>
    public async Task<bool> ClickRandomPostAsync(IPage page, CancellationToken ct = default)
    {
        // Wait & scroll to load posts
        await WaitForFeedLoadAsync(page, ct);
        await ScrollFeedAsync(page, 2, ct);

        // Cari post links (lebih reliable dari article)
        var postLinks = page.Locator("a[href*='/post/']").First;
        int total = await postLinks.CountAsync();
        if (total == 0)
        {
            // Fallback ke articles
            var posts = page.Locator(ThreadsSelectors.PostArticle);
            total = await posts.CountAsync();
            if (total == 0) return false;
        }

        var rng = new Random();
        int index = rng.Next(0, Math.Min(3, total));
        
        ILocator target;
        if (total > 0)
        {
            // Prioritaskan post links
            target = page.Locator("a[href*='/post/']").Nth(index);
            if (await target.CountAsync() == 0)
                target = page.Locator(ThreadsSelectors.PostArticle).Nth(index);
        }
        else
        {
            target = page.Locator(ThreadsSelectors.PostArticle).Nth(index);
        }

        await target.ScrollIntoViewIfNeededAsync();
        await _delay.WaitAsync(500, 1000, ct);
        await target.ClickAsync();
        await _delay.WaitAsync(2000, 4000, ct);
        return true;
    }

    // ── Helpers ──

    public async Task<bool> IsLoggedInAsync(IPage page)
    {
        var notifications = page.Locator(ThreadsSelectors.NotificationsLink);
        return await notifications.CountAsync() > 0;
    }

    public async Task WaitForFeedLoadAsync(IPage page, CancellationToken ct = default)
    {
        try
        {
            await page.WaitForSelectorAsync(ThreadsSelectors.PostArticle,
                new() { Timeout = ThreadsConstants.PageLoadTimeout });
        }
        catch (TimeoutException)
        {
            // Feed might be empty or slow
        }
    }

    private static string EscapeForTextSelector(string text)
    {
        return text.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
