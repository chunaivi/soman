using SoMan.Models;
using SoMan.Services.Account;
using Microsoft.Playwright;

namespace SoMan.Services.Browser;

public enum SessionStatus
{
    Valid,
    Expired,
    Error
}

public record SessionCheckResult(SessionStatus Status, string Message, string? Username = null);

public interface ISessionValidator
{
    Task<SessionCheckResult> ValidateSessionAsync(Models.Account account);
}

public class SessionValidator : ISessionValidator
{
    private readonly IBrowserManager _browserManager;
    private readonly IAccountService _accountService;

    public SessionValidator(IBrowserManager browserManager, IAccountService accountService)
    {
        _browserManager = browserManager;
        _accountService = accountService;
    }

    public async Task<SessionCheckResult> ValidateSessionAsync(Models.Account account)
    {
        try
        {
            // Use desktop mode for validation — mobile Threads shows login banners even for logged-in users
            var page = await _browserManager.OpenAccountPageAsync(account, "https://www.threads.net", forceDesktop: true);

            // Wait for page to settle
            try
            {
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15000 });
            }
            catch (TimeoutException) { /* continue anyway */ }

            var url = page.Url;

            // Check if redirected to login page
            if (url.Contains("/login") || url.Contains("/accounts/login"))
            {
                await _browserManager.CloseContextAsync(account.Id);
                await _accountService.UpdateStatusAsync(account.Id, AccountStatus.CookiesExpired);
                return new SessionCheckResult(SessionStatus.Expired, "Cookies expired — redirected to login page.");
            }

            // Check for NOT logged in indicators:
            // 1. "Say more with Threads" login popup
            // 2. "Log in or sign up for Threads" sidebar
            // 3. "Continue with Instagram" button
            bool hasLoginPrompt = false;
            try
            {
                var loginIndicator = page.Locator("text='Say more with Threads'")
                    .Or(page.Locator("text='Log in or sign up for Threads'"))
                    .Or(page.Locator("text='Continue with Instagram'"))
                    .Or(page.Locator("text='Log in with username instead'"));
                await loginIndicator.First.WaitForAsync(new() { Timeout = 6000, State = WaitForSelectorState.Visible });
                hasLoginPrompt = true;
            }
            catch (TimeoutException)
            {
                hasLoginPrompt = false;
            }

            if (hasLoginPrompt)
            {
                await _browserManager.CloseContextAsync(account.Id);
                await _accountService.UpdateStatusAsync(account.Id, AccountStatus.CookiesExpired);
                return new SessionCheckResult(SessionStatus.Expired, "Login popup detected — cookies not working. Re-export cookies from your browser.");
            }

            // Positive check: look for logged-in user elements
            // Threads shows a navigation bar with Notifications/Profile icons only when logged in
            bool hasAuthUI = false;
            try
            {
                var authIndicator = page.Locator("[aria-label='Notifications']")
                    .Or(page.Locator("[aria-label='New post']"))
                    .Or(page.Locator("a[href*='/liked']"));
                await authIndicator.First.WaitForAsync(new() { Timeout = 5000, State = WaitForSelectorState.Visible });
                hasAuthUI = true;
            }
            catch (TimeoutException)
            {
                hasAuthUI = false;
            }

            if (!hasAuthUI)
            {
                await _browserManager.CloseContextAsync(account.Id);
                await _accountService.UpdateStatusAsync(account.Id, AccountStatus.CookiesExpired);
                return new SessionCheckResult(SessionStatus.Expired, "No authenticated UI found — session may be expired.");
            }

            // Session is valid
            await _accountService.UpdateStatusAsync(account.Id, AccountStatus.Active);

            if (account.IsHeadless)
            {
                await _browserManager.CloseContextAsync(account.Id);
            }

            return new SessionCheckResult(SessionStatus.Valid, "Session is valid — logged in successfully.", account.Username);
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("Timeout"))
        {
            try { await _browserManager.CloseContextAsync(account.Id); } catch { }
            return new SessionCheckResult(SessionStatus.Error, $"Timeout loading page: {ex.Message}");
        }
        catch (Exception ex)
        {
            try { await _browserManager.CloseContextAsync(account.Id); } catch { }
            return new SessionCheckResult(SessionStatus.Error, $"Error: {ex.Message}");
        }
    }
}
