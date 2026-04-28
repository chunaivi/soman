using Microsoft.Playwright;
using SoMan.Models;
using SoMan.Services.Security;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SoMan.Services.Browser;

using PlaywrightCookie = Microsoft.Playwright.Cookie;

public interface IBrowserManager : IAsyncDisposable
{
    Task InitializeAsync();
    Task<IBrowserContext> CreateContextAsync(Models.Account account, bool forceDesktop = false);
    Task<IPage> OpenAccountPageAsync(Models.Account account, string url = "https://www.threads.net", bool forceDesktop = false);
    Task CloseContextAsync(int accountId);
    Task CloseAllAsync();
    int GetActiveContextCount();
    bool IsContextAlive(int accountId);
    IBrowserContext? GetContext(int accountId);
    IPage? GetPage(int accountId);
    bool CanLaunchMore();
    int LastInjectedCookieCount { get; }
}

public class BrowserManager : IBrowserManager
{
    private IPlaywright? _playwright;
    // Two browsers: one headed, one headless
    private IBrowser? _headlessBrowser;
    private IBrowser? _headedBrowser;
    private readonly ConcurrentDictionary<int, IBrowserContext> _contexts = new();
    private readonly ConcurrentDictionary<int, IPage> _pages = new();
    private readonly IEncryptionService _encryptionService;
    private readonly IResourceMonitor _resourceMonitor;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _initialized;
    public int LastInjectedCookieCount { get; private set; }

    // Mobile viewport (iPhone 14 size)
    private const int MobileWidth = 390;
    private const int MobileHeight = 844;
    // Window chrome adds ~14px width and ~79px height
    private const int WindowChromeW = 14;
    private const int WindowChromeH = 79;
    private int _nextWindowX = 0;

    public BrowserManager(IEncryptionService encryptionService, IResourceMonitor resourceMonitor)
    {
        _encryptionService = encryptionService;
        _resourceMonitor = resourceMonitor;
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        await _semaphore.WaitAsync();
        try
        {
            if (_initialized) return;
            _playwright = await Playwright.CreateAsync();
            _initialized = true;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public bool CanLaunchMore()
    {
        // Always allow at least 1 browser if none are running
        if (GetActiveContextCount() == 0)
            return true;

        var slots = _resourceMonitor.GetAvailableSlots(true);
        return slots > GetActiveContextCount();
    }

    private async Task<IBrowser> GetOrCreateBrowserAsync(bool headless)
    {
        if (headless)
        {
            _headlessBrowser ??= await _playwright!.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = new[] { "--disable-blink-features=AutomationControlled" }
            });
            return _headlessBrowser;
        }
        else
        {
            _headedBrowser ??= await _playwright!.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = false,
                Args = new[]
                {
                    "--disable-blink-features=AutomationControlled",
                    $"--window-size={MobileWidth + WindowChromeW},{MobileHeight + WindowChromeH}"
                }
            });
            return _headedBrowser;
        }
    }

    public async Task<IBrowserContext> CreateContextAsync(Models.Account account, bool forceDesktop = false)
    {
        if (!_initialized)
            await InitializeAsync();

        // Close existing if any
        if (_contexts.ContainsKey(account.Id))
            await CloseContextAsync(account.Id);

        var browser = await GetOrCreateBrowserAsync(account.IsHeadless);

        BrowserNewContextOptions contextOptions;
        if (forceDesktop)
        {
            contextOptions = new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1280, Height = 720 },
                Locale = "en-US",
                TimezoneId = "Asia/Jakarta",
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"
            };
        }
        else
        {
            contextOptions = new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = MobileWidth, Height = MobileHeight },
                Locale = "en-US",
                TimezoneId = "Asia/Jakarta",
                UserAgent = "Mozilla/5.0 (Linux; Android 14; Pixel 8 Pro) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36",
                IsMobile = true,
                HasTouch = true,
                DeviceScaleFactor = 2
            };
        }

        // Set proxy if configured
        if (account.ProxyConfig != null)
        {
            var proxyServer = account.ProxyConfig.Type switch
            {
                ProxyType.Http => $"http://{account.ProxyConfig.Host}:{account.ProxyConfig.Port}",
                ProxyType.Socks5 => $"socks5://{account.ProxyConfig.Host}:{account.ProxyConfig.Port}",
                _ => null
            };

            if (proxyServer != null)
            {
                contextOptions.Proxy = new Microsoft.Playwright.Proxy
                {
                    Server = proxyServer,
                    Username = account.ProxyConfig.Username,
                    Password = account.ProxyConfig.EncryptedPassword != null
                        ? _encryptionService.Decrypt(account.ProxyConfig.EncryptedPassword)
                        : null
                };
            }
        }

        var context = await browser.NewContextAsync(contextOptions);

        // Inject cookies
        if (!string.IsNullOrEmpty(account.EncryptedCookiesJson))
        {
            var cookiesJson = _encryptionService.Decrypt(account.EncryptedCookiesJson);
            var cookies = ParseCookiesFlexible(cookiesJson);
            if (cookies.Count > 0)
            {
                await context.AddCookiesAsync(cookies);
                LastInjectedCookieCount = cookies.Count;
                foreach (var c in cookies)
                    System.Diagnostics.Debug.WriteLine($"  Cookie: {c.Name} | Domain: {c.Domain} | Secure: {c.Secure} | HttpOnly: {c.HttpOnly}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[BrowserManager] WARNING: 0 cookies parsed for account '{account.Name}'");
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[BrowserManager] WARNING: No encrypted cookies for account '{account.Name}'");
        }

        _contexts.TryAdd(account.Id, context);
        return context;
    }

    public async Task<IPage> OpenAccountPageAsync(Models.Account account, string url = "https://www.threads.net", bool forceDesktop = false)
    {
        var context = await CreateContextAsync(account, forceDesktop);
        var page = await context.NewPageAsync();

        // Anti-detection: override navigator.webdriver
        await page.AddInitScriptAsync(@"
            Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
        ");

        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 });

        // Auto-position headed windows side by side
        if (!account.IsHeadless)
        {
            try
            {
                var screenWidth = (int)System.Windows.SystemParameters.PrimaryScreenWidth;
                var winW = MobileWidth + WindowChromeW;
                // Reset X if it would go off screen
                if (_nextWindowX + winW > screenWidth)
                    _nextWindowX = 0;

                var cdp = await context.NewCDPSessionAsync(page);
                var bounds = new JsonObject
                {
                    ["bounds"] = new JsonObject
                    {
                        ["left"] = _nextWindowX,
                        ["top"] = 0,
                        ["width"] = winW,
                        ["height"] = MobileHeight + WindowChromeH,
                        ["windowState"] = "normal"
                    }
                };
                await cdp.SendAsync("Browser.setWindowBounds",
                    new Dictionary<string, object>
                    {
                        ["windowId"] = (await cdp.SendAsync("Browser.getWindowForTarget")).Value.GetProperty("windowId").GetInt32(),
                        ["bounds"] = new Dictionary<string, object>
                        {
                            ["left"] = _nextWindowX,
                            ["top"] = 0,
                            ["width"] = winW,
                            ["height"] = MobileHeight + WindowChromeH,
                            ["windowState"] = "normal"
                        }
                    });
                _nextWindowX += winW;
            }
            catch { /* positioning is best-effort */ }
        }

        _pages.TryAdd(account.Id, page);
        return page;
    }

    public async Task CloseContextAsync(int accountId)
    {
        _pages.TryRemove(accountId, out _);
        if (_contexts.TryRemove(accountId, out var context))
        {
            await context.CloseAsync();
        }
    }

    public async Task CloseAllAsync()
    {
        _pages.Clear();
        foreach (var kvp in _contexts)
        {
            try { await kvp.Value.CloseAsync(); } catch { }
        }
        _contexts.Clear();
    }

    public int GetActiveContextCount() => _contexts.Count;

    public bool IsContextAlive(int accountId) => _contexts.ContainsKey(accountId);

    public IBrowserContext? GetContext(int accountId)
    {
        _contexts.TryGetValue(accountId, out var context);
        return context;
    }
    public IPage? GetPage(int accountId)
    {
        _pages.TryGetValue(accountId, out var page);
        return page;
    }
    public async ValueTask DisposeAsync()
    {
        await CloseAllAsync();

        if (_headlessBrowser != null)
        {
            await _headlessBrowser.CloseAsync();
            _headlessBrowser = null;
        }
        if (_headedBrowser != null)
        {
            await _headedBrowser.CloseAsync();
            _headedBrowser = null;
        }

        _playwright?.Dispose();
        _playwright = null;
        _initialized = false;
    }
    /// <summary>
    /// Parses cookies from various browser extension export formats
    /// (EditThisCookie, Cookie Editor, Netscape, J2TEAM, etc.)
    /// </summary>
    private List<PlaywrightCookie> ParseCookiesFlexible(string json)
    {
        var result = new List<PlaywrightCookie>();
        JsonArray? arr;
        try
        {
            arr = JsonNode.Parse(json)?.AsArray();
        }
        catch
        {
            return result;
        }

        if (arr == null) return result;

        foreach (var node in arr)
        {
            if (node == null) continue;
            var obj = node.AsObject();

            var name = GetString(obj, "name");
            var value = GetString(obj, "value");
            if (string.IsNullOrEmpty(name)) continue;

            var cookie = new PlaywrightCookie
            {
                Name = name,
                Value = value ?? string.Empty,
                Domain = GetString(obj, "domain") ?? string.Empty,
                Path = GetString(obj, "path") ?? "/",
                Expires = GetFloat(obj, "expires", "expirationDate") ?? -1,
                HttpOnly = GetBool(obj, "httpOnly"),
                Secure = GetBool(obj, "secure"),
                SameSite = ParseSameSite(GetString(obj, "sameSite"))
            };
            result.Add(cookie);
        }
        return result;
    }

    private static string? GetString(JsonObject obj, string key)
    {
        if (obj.TryGetPropertyValue(key, out var node) && node != null)
        {
            try
            {
                var el = node.GetValue<JsonElement>();
                return el.ValueKind switch
                {
                    JsonValueKind.String => el.GetString(),
                    JsonValueKind.Number => el.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => null
                };
            }
            catch { return node.ToString(); }
        }
        return null;
    }

    private static bool GetBool(JsonObject obj, string key)
    {
        if (obj.TryGetPropertyValue(key, out var node) && node != null)
        {
            try
            {
                var el = node.GetValue<JsonElement>();
                if (el.ValueKind == JsonValueKind.True) return true;
                if (el.ValueKind == JsonValueKind.False) return false;
                // Some exporters use 1/0
                if (el.ValueKind == JsonValueKind.Number) return el.GetInt32() != 0;
            }
            catch { }
        }
        return false;
    }

    private static float? GetFloat(JsonObject obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (obj.TryGetPropertyValue(key, out var node) && node != null)
            {
                try
                {
                    var el = node.GetValue<JsonElement>();
                    if (el.ValueKind == JsonValueKind.Number)
                    {
                        if (el.TryGetDouble(out double d))
                            return (float)d;
                    }
                }
                catch { }
            }
        }
        return null;
    }

    private static SameSiteAttribute ParseSameSite(string? value)
    {
        if (string.IsNullOrEmpty(value)) return SameSiteAttribute.None;
        return value.ToLowerInvariant() switch
        {
            "strict" => SameSiteAttribute.Strict,
            "lax" => SameSiteAttribute.Lax,
            "none" => SameSiteAttribute.None,
            "no_restriction" => SameSiteAttribute.None, // Chrome/EditThisCookie format
            "unspecified" => SameSiteAttribute.None,
            _ => SameSiteAttribute.None
        };
    }
}
