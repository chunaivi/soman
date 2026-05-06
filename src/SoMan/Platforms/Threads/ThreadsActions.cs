using System.Text;
using System.Text.RegularExpressions;
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

    private static readonly Regex PostButtonTextRegex =
        new(@"^\s*Post\s*$", RegexOptions.Compiled);

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

            // Threads opens the reply composer in a modal dialog. Scope to that dialog
            // so we never grab the page-level search box, the top-of-feed composer, or
            // an unrelated "Post" link from the nav.
            var dialog = page.Locator("[role='dialog']").Last;
            bool hasDialog = false;
            try
            {
                hasDialog = await dialog.CountAsync() > 0 && await dialog.IsVisibleAsync();
            }
            catch { /* dialog may be transitioning */ }
            dbg.Append($"hasDialog={hasDialog}; ");

            ILocator textArea = hasDialog
                ? dialog.Locator("[contenteditable='true'], [role='textbox']").Last
                : page.Locator(ThreadsSelectors.ReplyTextArea).Last;

            await textArea.WaitForAsync(new() { Timeout = ThreadsConstants.ElementWaitTimeout });
            dbg.Append("textAreaReady; ");

            // Click to focus, then type via real keystrokes. FillAsync against Threads'
            // contenteditable composer (Lexical/React-controlled) frequently fails to
            // dispatch the input event that flips the Post button from disabled to
            // enabled — so the button looks active visually but ignores our click.
            await textArea.ClickAsync();
            await _delay.WaitAsync(200, 500, ct);
            await textArea.PressSequentiallyAsync(text, new() { Delay = 30 });
            dbg.Append($"textTyped(len={text.Length}); ");
            await _delay.WaitAsync(800, 1500, ct);

            // Helper: confirm the reply composer actually closed. This is the only
            // reliable success signal — the prior code used InputValueAsync which
            // returns "" for contenteditable, plus a page-wide text search that
            // matched the *typed* text in the still-open composer (false positive).
            async Task<bool> WaitForComposerClosedAsync(int seconds)
            {
                var until = DateTime.UtcNow.AddSeconds(seconds);
                while (DateTime.UtcNow < until && !ct.IsCancellationRequested)
                {
                    try
                    {
                        if (hasDialog)
                        {
                            int dlgCount = await dialog.CountAsync();
                            if (dlgCount == 0) return true;
                            if (!await dialog.IsVisibleAsync()) return true;
                        }
                        else
                        {
                            // No dialog scope: fall back to the textbox visibility / content.
                            var editor = page.Locator(ThreadsSelectors.ReplyTextArea).Last;
                            if (await editor.CountAsync() == 0) return true;
                            if (!await editor.IsVisibleAsync()) return true;
                            // contenteditable: use InnerText, not InputValue.
                            var txt = await editor.InnerTextAsync();
                            if (string.IsNullOrWhiteSpace(txt)) return true;
                        }
                    }
                    catch
                    {
                        // DOM mutated under us — treat as closed.
                        return true;
                    }
                    await _delay.WaitAsync(300, 600, ct);
                }
                return false;
            }

            bool submitted = false;

            // Strategy 1 (PRIMARY): Ctrl+Enter on the focused composer.
            // Threads supports this shortcut and it bypasses every button-click
            // pitfall (wrong element, React onClick not firing, overlay catching
            // the click, aria-disabled mismatch, etc.).
            try
            {
                await textArea.PressAsync("Control+Enter");
                dbg.Append("submit=ctrl_enter_sent; ");
                if (await WaitForComposerClosedAsync(6))
                {
                    dbg.Append("submit=ctrl_enter_ok; ");
                    submitted = true;
                }
            }
            catch (Exception exCe)
            {
                dbg.Append($"submit=ctrl_enter_fail({exCe.Message}); ");
            }

            // Resolve the Post button (used by all click-based fallbacks). Prefer
            // accessible-role lookup, fall back to text-regex match. Scope to the
            // dialog so we never grab nav links or unrelated "Post" elements.
            ILocator scope = hasDialog ? dialog : page.Locator("body");
            ILocator postBtn = scope.GetByRole(AriaRole.Button, new()
            {
                Name = "Post",
                Exact = true
            });
            int byRoleCount = await postBtn.CountAsync();
            dbg.Append($"postBtnByRole={byRoleCount}; ");
            if (byRoleCount == 0)
            {
                postBtn = scope
                    .Locator("button, [role='button']")
                    .Filter(new() { HasTextRegex = PostButtonTextRegex });
                int byTextCount = await postBtn.CountAsync();
                dbg.Append($"postBtnByText={byTextCount}; ");
                if (byTextCount == 0)
                {
                    postBtn = page.Locator(ThreadsSelectors.ReplyPostButton);
                    dbg.Append($"postBtnFallback={await postBtn.CountAsync()}; ");
                }
            }
            postBtn = postBtn.Last;

            // If Ctrl+Enter didn't close the dialog, fall through to button clicks.
            if (!submitted)
            {
                try
                {
                    await postBtn.WaitForAsync(new() { Timeout = ThreadsConstants.ElementWaitTimeout });
                    await postBtn.ScrollIntoViewIfNeededAsync();
                    dbg.Append("postBtnReady; ");
                }
                catch (Exception exReady)
                {
                    dbg.Append($"postBtnReadyFail({exReady.Message}); ");
                }

                // Wait until the Post button is actually enabled (Threads holds
                // aria-disabled='true' until the React input event registers).
                var enabledUntil = DateTime.UtcNow.AddSeconds(6);
                bool isEnabled = false;
                while (DateTime.UtcNow < enabledUntil && !ct.IsCancellationRequested)
                {
                    bool disabled = false;
                    try
                    {
                        var ariaDisabled = await postBtn.GetAttributeAsync("aria-disabled");
                        if (ariaDisabled == "true") disabled = true;
                    }
                    catch { }
                    if (!disabled)
                    {
                        try { if (await postBtn.IsDisabledAsync()) disabled = true; } catch { }
                    }
                    if (!disabled) { isEnabled = true; break; }
                    await _delay.WaitAsync(200, 400, ct);
                }
                dbg.Append($"postBtnEnabled={isEnabled}; ");
            }

            // Strategy 2: normal click
            if (!submitted)
            {
                try
                {
                    await postBtn.ClickAsync(new() { Timeout = 5000 });
                    dbg.Append("click=normal_sent; ");
                    if (await WaitForComposerClosedAsync(5))
                    {
                        dbg.Append("submit=normal_ok; ");
                        submitted = true;
                    }
                }
                catch (Exception ex1)
                {
                    dbg.Append($"click=normal_fail({ex1.Message}); ");
                }
            }

            // Strategy 3: force click
            if (!submitted)
            {
                try
                {
                    await postBtn.ClickAsync(new() { Force = true, Timeout = 5000 });
                    dbg.Append("click=force_sent; ");
                    if (await WaitForComposerClosedAsync(5))
                    {
                        dbg.Append("submit=force_ok; ");
                        submitted = true;
                    }
                }
                catch (Exception ex2)
                {
                    dbg.Append($"click=force_fail({ex2.Message}); ");
                }
            }

            // Strategy 4: click by mouse coordinate at the button's center
            if (!submitted)
            {
                try
                {
                    var box = await postBtn.BoundingBoxAsync();
                    if (box != null)
                    {
                        var cx = box.X + (box.Width / 2);
                        var cy = box.Y + (box.Height / 2);
                        await page.Mouse.ClickAsync(cx, cy);
                        dbg.Append($"click=coord_sent({cx:F1},{cy:F1}); ");
                        if (await WaitForComposerClosedAsync(5))
                        {
                            dbg.Append("submit=coord_ok; ");
                            submitted = true;
                        }
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

            // Strategy 5: invoke .click() in-page via JS — bypasses every Playwright
            // actionability gate and any overlay that swallows the synthetic click.
            if (!submitted)
            {
                try
                {
                    await postBtn.EvaluateAsync<object>("el => el.click()");
                    dbg.Append("click=js_sent; ");
                    if (await WaitForComposerClosedAsync(5))
                    {
                        dbg.Append("submit=js_ok; ");
                        submitted = true;
                    }
                }
                catch (Exception ex4)
                {
                    dbg.Append($"click=js_fail({ex4.Message}); ");
                }
            }

            if (!submitted)
                throw new Exception($"{dbg}post submit not confirmed — composer still open after every strategy");

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

    // ── Quote ──

    /// <summary>
    /// Quotes a post (Repost → Quote menu option) and adds the given commentary.
    /// Navigates to <paramref name="postUrl"/> if the current page is not already there,
    /// finds the target post article, clicks Repost → Quote, types the text and submits.
    /// </summary>
    public async Task<bool> QuotePostAsync(IPage page, string postUrl, string text, CancellationToken ct = default)
    {
        var dbg = new StringBuilder();
        try
        {
            dbg.Append("[Quote] start; ");

            // Navigate to the target post if not already there.
            if (!page.Url.TrimEnd('/').Equals(postUrl.TrimEnd('/')))
            {
                await page.GotoAsync(postUrl, new()
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = ThreadsConstants.PageLoadTimeout
                });
                await _delay.WaitAsync(1500, 3000, ct);
                dbg.Append("navigated; ");
            }

            // The target post is the first article on its own permalink page.
            var article = page.Locator(ThreadsSelectors.PostArticle).First;
            if (await article.CountAsync() == 0)
                throw new Exception($"{dbg}target post article not found");

            // Click the Repost button scoped to that article — same selector Repost uses.
            var repostBtn = article.Locator(ThreadsSelectors.RepostButton).First;
            if (await repostBtn.CountAsync() == 0)
                throw new Exception($"{dbg}repost button not found on article");
            await repostBtn.ClickAsync();
            dbg.Append("repostClicked; ");
            await _delay.WaitAsync(1000, 2000, ct);

            // Repost popup has two options: "Repost" and "Quote". We want Quote.
            var quoteOption = page.Locator(ThreadsSelectors.QuoteOption).First;
            if (await quoteOption.CountAsync() == 0)
                throw new Exception($"{dbg}quote option not found in repost popup");
            await quoteOption.ClickAsync();
            dbg.Append("quoteClicked; ");
            await _delay.WaitAsync(1500, 3000, ct);

            // From here the flow mirrors CommentOnPostAsync's composer-submit stage:
            // dialog opens containing the quoted post + a contenteditable textbox; we
            // type the user's commentary and submit.
            bool submitted = await TypeAndSubmitDialogComposerAsync(page, text, ct, dbg);
            if (!submitted)
                throw new Exception($"{dbg}submit not confirmed — composer still open after every strategy");

            return true;
        }
        catch (Exception ex)
        {
            throw new Exception($"{dbg}error={ex.Message}", ex);
        }
    }

    /// <summary>
    /// Assumes a Threads composer dialog is currently open (e.g. after clicking Reply
    /// on a post, or Quote in the repost popup). Types <paramref name="text"/> into the
    /// dialog's contenteditable composer and submits using the same multi-strategy
    /// fallback chain as <see cref="CommentOnPostAsync"/>. Returns true if the composer
    /// closes (confirmed submission), false otherwise.
    /// </summary>
    private async Task<bool> TypeAndSubmitDialogComposerAsync(
        IPage page, string text, CancellationToken ct, StringBuilder dbg)
    {
        var dialog = page.Locator("[role='dialog']").Last;
        bool hasDialog = false;
        try { hasDialog = await dialog.CountAsync() > 0 && await dialog.IsVisibleAsync(); }
        catch { }
        dbg.Append($"hasDialog={hasDialog}; ");

        ILocator textArea = hasDialog
            ? dialog.Locator("[contenteditable='true'], [role='textbox']").Last
            : page.Locator(ThreadsSelectors.ReplyTextArea).Last;

        await textArea.WaitForAsync(new() { Timeout = ThreadsConstants.ElementWaitTimeout });
        await textArea.ClickAsync();
        await _delay.WaitAsync(200, 500, ct);
        await textArea.PressSequentiallyAsync(text, new() { Delay = 30 });
        dbg.Append($"textTyped(len={text.Length}); ");
        await _delay.WaitAsync(800, 1500, ct);

        async Task<bool> WaitForComposerClosedAsync(int seconds)
        {
            var until = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < until && !ct.IsCancellationRequested)
            {
                try
                {
                    if (hasDialog)
                    {
                        int dlgCount = await dialog.CountAsync();
                        if (dlgCount == 0) return true;
                        if (!await dialog.IsVisibleAsync()) return true;
                    }
                    else
                    {
                        var editor = page.Locator(ThreadsSelectors.ReplyTextArea).Last;
                        if (await editor.CountAsync() == 0) return true;
                        if (!await editor.IsVisibleAsync()) return true;
                        var txt = await editor.InnerTextAsync();
                        if (string.IsNullOrWhiteSpace(txt)) return true;
                    }
                }
                catch { return true; }
                await _delay.WaitAsync(300, 600, ct);
            }
            return false;
        }

        bool submitted = false;

        // Strategy 1: Ctrl+Enter (primary, bypasses all button-click pitfalls)
        try
        {
            await textArea.PressAsync("Control+Enter");
            dbg.Append("submit=ctrl_enter_sent; ");
            if (await WaitForComposerClosedAsync(6)) { submitted = true; dbg.Append("submit=ctrl_enter_ok; "); }
        }
        catch (Exception exCe) { dbg.Append($"submit=ctrl_enter_fail({exCe.Message}); "); }

        // Resolve Post button for click-based fallbacks.
        ILocator scope = hasDialog ? dialog : page.Locator("body");
        ILocator postBtn = scope.GetByRole(AriaRole.Button, new() { Name = "Post", Exact = true });
        if (await postBtn.CountAsync() == 0)
        {
            postBtn = scope.Locator("button, [role='button']").Filter(new() { HasTextRegex = PostButtonTextRegex });
            if (await postBtn.CountAsync() == 0)
                postBtn = page.Locator(ThreadsSelectors.ReplyPostButton);
        }
        postBtn = postBtn.Last;

        if (!submitted)
        {
            try { await postBtn.WaitForAsync(new() { Timeout = ThreadsConstants.ElementWaitTimeout }); await postBtn.ScrollIntoViewIfNeededAsync(); } catch { }

            // Wait for button to become enabled (Threads keeps aria-disabled until input fires).
            var enabledUntil = DateTime.UtcNow.AddSeconds(6);
            while (DateTime.UtcNow < enabledUntil && !ct.IsCancellationRequested)
            {
                bool disabled = false;
                try { if (await postBtn.GetAttributeAsync("aria-disabled") == "true") disabled = true; } catch { }
                if (!disabled) { try { if (await postBtn.IsDisabledAsync()) disabled = true; } catch { } }
                if (!disabled) break;
                await _delay.WaitAsync(200, 400, ct);
            }
        }

        // Strategy 2: normal click
        if (!submitted)
        {
            try
            {
                await postBtn.ClickAsync(new() { Timeout = 5000 });
                if (await WaitForComposerClosedAsync(5)) { submitted = true; dbg.Append("submit=normal_ok; "); }
            }
            catch (Exception ex1) { dbg.Append($"click=normal_fail({ex1.Message}); "); }
        }

        // Strategy 3: force click
        if (!submitted)
        {
            try
            {
                await postBtn.ClickAsync(new() { Force = true, Timeout = 5000 });
                if (await WaitForComposerClosedAsync(5)) { submitted = true; dbg.Append("submit=force_ok; "); }
            }
            catch (Exception ex2) { dbg.Append($"click=force_fail({ex2.Message}); "); }
        }

        // Strategy 4: click at bounding-box center
        if (!submitted)
        {
            try
            {
                var box = await postBtn.BoundingBoxAsync();
                if (box != null)
                {
                    await page.Mouse.ClickAsync(box.X + (box.Width / 2), box.Y + (box.Height / 2));
                    if (await WaitForComposerClosedAsync(5)) { submitted = true; dbg.Append("submit=coord_ok; "); }
                }
            }
            catch (Exception ex3) { dbg.Append($"click=coord_fail({ex3.Message}); "); }
        }

        // Strategy 5: in-page JS click — bypasses Playwright actionability + overlay issues.
        if (!submitted)
        {
            try
            {
                await postBtn.EvaluateAsync<object>("el => el.click()");
                if (await WaitForComposerClosedAsync(5)) { submitted = true; dbg.Append("submit=js_ok; "); }
            }
            catch (Exception ex4) { dbg.Append($"click=js_fail({ex4.Message}); "); }
        }

        return submitted;
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
    /// Clicks on a random post thumbnail/link from the current page (feed/search).
    /// Picks a *real* random post (the previous version always picked the first
    /// because <c>.First</c> on the Locator forced Count() to 1) and uses a
    /// progressive small-step scroll with a hover-then-click cadence so the
    /// interaction looks more human and less like a deterministic bot.
    /// </summary>
    public async Task<bool> ClickRandomPostAsync(IPage page, CancellationToken ct = default)
    {
        await WaitForFeedLoadAsync(page, ct);

        var rng = new Random();

        // Collect unique post hrefs across a few small scrolls. We dedupe on href
        // so the same post in two viewport positions only counts once.
        var seen = new HashSet<string>();
        var hrefs = new List<string>();
        await CollectVisiblePostHrefsAsync(page, seen, hrefs, ct);

        // 2–4 small random scroll hops (200–460px, 300–800ms gap) — much more
        // organic than the previous fixed 2-second WheelAsync loop.
        int hops = rng.Next(2, 5);
        for (int i = 0; i < hops && !ct.IsCancellationRequested && hrefs.Count < 12; i++)
        {
            int delta = rng.Next(200, 460);
            await page.Mouse.WheelAsync(0, delta);
            await _delay.WaitAsync(300, 800, ct);
            await CollectVisiblePostHrefsAsync(page, seen, hrefs, ct);
        }

        // Pick within the first N hrefs we saw — keeps results relevant on a
        // search page while still being non-deterministic.
        if (hrefs.Count > 0)
        {
            int pool = Math.Min(hrefs.Count, 10);
            string chosenHref = hrefs[rng.Next(0, pool)];

            ILocator target = page.Locator($"a[href='{chosenHref}']").First;
            if (await target.CountAsync() == 0)
                target = page.Locator($"a[href*='{EscapePostHrefForCss(chosenHref)}']").First;

            if (await target.CountAsync() > 0)
            {
                await target.ScrollIntoViewIfNeededAsync();
                await _delay.WaitAsync(800, 2200, ct);    // pause to "read" the thumbnail
                try { await target.HoverAsync(); } catch { /* hover is best-effort */ }
                await _delay.WaitAsync(150, 500, ct);
                await target.ClickAsync();
                await _delay.WaitAsync(2000, 4000, ct);
                return true;
            }
        }

        // Fallback: pick a random article container if we couldn't resolve a link.
        var posts = page.Locator(ThreadsSelectors.PostArticle);
        int total = await posts.CountAsync();
        if (total == 0) return false;

        int idx = rng.Next(0, Math.Min(total, 10));
        var post = posts.Nth(idx);
        await post.ScrollIntoViewIfNeededAsync();
        await _delay.WaitAsync(800, 2000, ct);
        try { await post.HoverAsync(); } catch { }
        await _delay.WaitAsync(150, 500, ct);
        await post.ClickAsync();
        await _delay.WaitAsync(2000, 4000, ct);
        return true;
    }

    private static async Task CollectVisiblePostHrefsAsync(
        IPage page, HashSet<string> seen, List<string> hrefs, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;
        var locator = page.Locator("a[href*='/post/']");
        int n = await locator.CountAsync();
        for (int i = 0; i < n; i++)
        {
            var href = await locator.Nth(i).GetAttributeAsync("href");
            if (string.IsNullOrEmpty(href)) continue;
            if (seen.Add(href)) hrefs.Add(href);
        }
    }

    private static string EscapePostHrefForCss(string href)
        => href.Replace("\\", "\\\\").Replace("'", "\\'");

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

}
