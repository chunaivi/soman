namespace SoMan.Platforms.Threads;

/// <summary>
/// CSS/XPath selectors untuk Threads UI.
/// Update file ini jika Threads mengubah layout tanpa mengubah logic.
/// </summary>
public static class ThreadsSelectors
{
    // ── Navigation ──
    public const string SearchLink = "a[href='/search']";
    public const string HomeLink = "a[href='/']";
    public const string NotificationsLink = "[aria-label='Notifications']";
    public const string ProfileLink = "[aria-label='Profile']";
    public const string CreateButton = "[aria-label='Create']";

    // ── Feed & Posts ──
    public const string FeedContainer = "main";
    public const string PostArticle = "article";
    public const string PostContent = "[data-pressable-container='true']";

    // ── Post Actions (inside article) ──
    public const string LikeButton = "[aria-label='Like']";
    public const string UnlikeButton = "[aria-label='Unlike']";
    public const string ReplyButton = "[aria-label='Reply']";
    public const string RepostButton = "[aria-label='Repost']";
    public const string ShareButton = "[aria-label='Share']";
    public const string MoreButton = "[aria-label='More']";

    // ── Repost Popup ──
    public const string RepostOption = "text=Repost";
    public const string QuoteOption = "text=Quote";

    // ── Comment/Reply ──
    public const string ReplyTextArea = "[role='textbox']";
    public const string ReplyPostButton = "span:has-text('Post'), div:has-text('Post'), [role='button']:has-text('Post'), text=Post";

    // ── Follow ──
    public const string FollowButton = "text=Follow";
    public const string FollowingButton = "text=Following";
    public const string UnfollowConfirm = "text=Unfollow";

    // ── Profile ──
    public const string ProfileUsername = "h1";
    public const string ProfileBio = "span[class]";
    public const string ProfileFollowers = "a[href*='followers']";
    public const string ProfileFollowButton = "header >> text=Follow";

    // ── Create Post ──
    public const string PostTextBox = "[role='textbox']";
    public const string PostSubmitButton = "text=Post";
    public const string PostAttachButton = "[aria-label='Attach media']";

    // ── Search ──
    public const string SearchInput = "input[type='search'], input[placeholder*='Search']";
    public const string SearchResult = "[role='listbox'] >> [role='option']";

    // ── Auth Detection ──
    public const string LoginPopupText = "text=Say more with Threads";
    public const string LoginPopupText2 = "text=Log in or sign up for Threads";
    public const string ContinueWithInstagram = "text=Continue with Instagram";

    // ── Loading ──
    public const string Spinner = "[role='progressbar']";
    public const string LoadingIndicator = "svg[aria-label='Loading']";
}
